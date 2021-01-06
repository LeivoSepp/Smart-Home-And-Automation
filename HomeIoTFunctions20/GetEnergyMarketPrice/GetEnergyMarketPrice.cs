using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HomeIoTFunctions20.GetYrNoForecast;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HomeIoTFunctions20.GetEnergyMarketPrice
{
    public static class GetEnergyMarketPrice
    {
        internal static string MLapiKey;
        internal static string EnergyPriceURL;
        internal static string MachineLearningURL;
        internal const int NORMAL_HEATING = 1; //heating rooms an warm water
        internal const int REDUCED_HEATING = 2; //heating reduced 3 degrees
        internal const int EVU_STOP = 3; //heating off at all
        internal static string[] onTimer = { "06:00", "16:00", "20:00" }; //hot wwater start time
        internal static string[] offTimer = { "07:00", "17:00", "23:00" }; //hot water end time

        //timer scheduler to get the energy market price once in a day, calculate heating schedule and save it to CosmosDB
        [FunctionName("GetEnergyMarketPrice")]
        public static async Task Run(
        [TimerTrigger("0 0 13 * * *")] TimerInfo myTimer, //every day at 14 (trigger if fired based UTC)
                                       //[TimerTrigger("0 */2 * * * *")] TimerInfo myTimer,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TelemetryData",
                ConnectionStringSetting = "CosmosDBConnection"
                )]
                IAsyncCollector<EnergyPriceClass> asyncCollectorEnergyPrice,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TelemetryData",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT TOP 1 * FROM c WHERE c.DeviceID = 'Yrno' ORDER BY c._ts DESC"
                )]
                IEnumerable<YrnoTempClass> input,
            ExecutionContext context,
            ILogger log)
        {
            //this piece of code needed to take the connection string from local file. This is for debugging
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            MLapiKey = config["MachineLearningAPIKey"];
            EnergyPriceURL = config["EnergyPriceURL"];
            MachineLearningURL = config["MachineLearningURL"];

            //deserialize only Rows and only with needed attributes defined by Row and Column class
            var energyRawData = await GetMarketPrice();
            var energyData = ExtractTheData(energyRawData);

            //adding three consequences day into database, the third day has only one item.
            await CalculateAndSendEnergyPriceToCosmos(input, 0, energyData, asyncCollectorEnergyPrice);
            await CalculateAndSendEnergyPriceToCosmos(input, 1, energyData, asyncCollectorEnergyPrice);
            await CalculateAndSendEnergyPriceToCosmos(input, 2, energyData, asyncCollectorEnergyPrice);
        }
        private static async Task CalculateAndSendEnergyPriceToCosmos(IEnumerable<YrnoTempClass> input, byte daysToAdd, List<EnergyPrice> energyData, IAsyncCollector<EnergyPriceClass> asyncCollectorEnergyPrice)
        {
            int hoursToHeat = await getHoursToHeatInDay(input.First(), DateTimeTZ().DateTime.AddDays(daysToAdd));
            var energyPrice = CalculateHeatingTime(energyData, daysToAdd, hoursToHeat);
            if (energyPrice.Count > 0)
            {
                var energyPriceUpdated = updateEnergyPrice(energyPrice);
                await asyncCollectorEnergyPrice.AddAsync(energyPriceUpdated);
            }
        }
        private static List<EnergyPrice> ExtractTheData(RootData energyDataRaw)
        {
            var energyData = new List<EnergyPrice>();
            //extract the raw data and put it into class that is designed for heating schedule
            foreach (var row in energyDataRaw.data.Rows)
            {
                if (!row.IsExtraRow)
                {
                    DateTimeOffset time = row.StartTime;
                    foreach (var column in row.Columns)
                    {
                        //date from column and time from row
                        string datetime = $"{column.Name} {time:HH:mm}";
                        if (column.Index < 3) //filter out last three days: Index = 0, 1, 2
                            energyData.Add(new EnergyPrice
                            {
                                date = DateTimeOffset.ParseExact(datetime, "dd-MM-yyyy HH:mm", null).AddHours(1),
                                price = double.Parse(column.Value, CultureInfo.GetCultureInfo("et-EE")),
                                time = time.AddHours(1).ToString("HH:mm")
                            });
                    }
                }
            }
            return energyData;
        }
        private static async Task<RootData> GetMarketPrice(string endDate = "")
        {
            //get the energy market price data and if needed historical data then add endDate=21-11-2020 
            //endDate is not used this time as the json has already data from the whole week
            var http = new HttpClient();
            string url = EnergyPriceURL + endDate;
            HttpResponseMessage response = await http.GetAsync(url);
            var result = response.Content.ReadAsStringAsync();
            var output = JsonConvert.DeserializeObject<RootData>(result.Result);
            return output;
        }
        private static async Task<int> getHoursToHeatInDay(YrnoTempClass input, DateTimeOffset forecastDate)
        {
            int hoursInDay = 0;
            if (input.YrnoTemp.FirstOrDefault(m => m.from.Day == forecastDate.Day) != null)
            {
                int avgTomorrowTemperature = (int)input.YrnoTemp
                    .Where(m => m.from.Day == forecastDate.Day)
                    .Average(t => t.temp);
                //get the data from AI
                hoursInDay = await GetHeatingHours(avgTomorrowTemperature.ToString());
            }
            return hoursInDay;
            //int amplifier = 4;
            //sbyte output = Convert.ToSByte(11 - avgTomorrowTemperature - (5 - avgTomorrowTemperature) / amplifier);
        }
        private static async Task<sbyte> GetHeatingHours(string tempForecast)
        {

            using var client = new HttpClient();
            var scoreRequest = new
            {
                Inputs = new Dictionary<string, List<Dictionary<string, string>>>() {
                    {
                        "input1",
                        new List<Dictionary<string, string>>(){new Dictionary<string, string>(){
                               {
                                  "tempout", tempForecast
                               },
                            }
                        }
                    },
                },
            };

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MLapiKey);
            client.BaseAddress = new Uri(MachineLearningURL);

            HttpResponseMessage response = await client.PostAsJsonAsync("", scoreRequest);
            sbyte hours = 5; //if we will get an error from AI webservice
            if (response.IsSuccessStatusCode)
            {
                var result = response.Content.ReadAsStringAsync();
                var nps = JsonConvert.DeserializeObject<JObject>(result.Result);
                //{"Results":{"output1":[{"hours":"5"}]}}
                var dataresult = nps["Results"]["output1"][0]["hours"].ToString();
                sbyte.TryParse(dataresult, out hours);
            }
            return hours;
        }
        private static List<EnergyPrice> CalculateHeatingTime(List<EnergyPrice> energyPricesTwoDays, byte days, int hoursToHeatInDay)
        {
            List<EnergyPrice> energyPrices = new List<EnergyPrice>();
            //filter out one day energy price
            var dayToFilter = DateTimeTZ().AddDays(days).Day;
            energyPrices.AddRange(energyPricesTwoDays.Where(x => x.date.Day == dayToFilter));

            energyPrices.Sort((x, y) => x.price.CompareTo(y.price)); //sort by energy price

            for (int i = 0; i < energyPrices.Count; i++)
            {
                bool isItPassiveTime = IsItInsidePassiveTime(energyPrices[i].date);
                if (i < hoursToHeatInDay) //all cheap hours marked as heating hours
                    energyPrices[i].heat = NORMAL_HEATING;
                else if (isItPassiveTime) //if it is daytime or night time (and not heating time), then full stop for heating
                    energyPrices[i].heat = EVU_STOP;
                else //if it is inside active time (early morning and evening), then warm water
                    energyPrices[i].heat = REDUCED_HEATING;
                energyPrices[i].isHotWaterTime = !isItPassiveTime; //set separate parameter for hotwater
            }
            energyPrices.Sort((x, y) => x.date.CompareTo(y.date)); //sort by time of day
            return energyPrices;
        }
        private static bool IsItInsidePassiveTime(DateTimeOffset date)
        {
            bool isWakeUpTime = !IsSleepTime(date);
            bool isWeekend = IsWeekend(date);
            bool result = true;
            for (int i = 0; i < onTimer.Length; i++)
            {
                bool isTimeBetween = TimeBetween(date, TimeSpan.Parse(onTimer[i]), TimeSpan.Parse(offTimer[i]));
                if (isTimeBetween || (isWeekend && isWakeUpTime))
                {
                    result = false;
                    break;
                }
            }
            return result;
        }
        private static bool IsWeekend(DateTimeOffset date)
        {
            return new[] { DayOfWeek.Sunday, DayOfWeek.Saturday }.Contains(date.DayOfWeek);
        }
        private static bool TimeBetween(DateTimeOffset datetime, TimeSpan start, TimeSpan end)
        {
            TimeSpan now = datetime.TimeOfDay;
            // see if start comes before end
            if (start < end)
                return start <= now && now <= end;
            // start is after end, so do the inverse comparison
            return !(end < now && now < start);
        }
        public static DateTimeOffset DateTimeTZ()
        {
            TimeZoneInfo eet = TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
            TimeSpan timeSpan = eet.GetUtcOffset(DateTime.UtcNow);
            DateTimeOffset LocalTimeTZ = new DateTimeOffset(DateTime.UtcNow).ToOffset(timeSpan);
            return LocalTimeTZ;
        }
        public static bool IsSleepTime(DateTimeOffset datetime)
        {
            TimeSpan SleepTimeStart = TimeSpan.Parse("00:00");
            TimeSpan SleepTimeEnd = TimeSpan.Parse("07:00");
            bool isSleepTime = TimeBetween(datetime, SleepTimeStart, SleepTimeEnd);
            return isSleepTime;
        }
        private static EnergyPriceClass updateEnergyPrice(List<EnergyPrice> energyPrices)
        {
            //update values heatOn, heatReduced and heatOff to make nice graphs in PowerApps
            energyPrices.ToList().ForEach(c =>
            {
                c.heatOn = c.heat == 1 ? c.heat * (int)c.price : 0;
                c.heatReduced = c.heat == 2 ? c.heat * (int)c.price / 2 : 0;
                c.heatOff = c.heat == 3 ? c.heat * (int)c.price / 3 : 0;
            });
            EnergyPriceClass monitorData = new EnergyPriceClass()
            {
                UtcOffset = DateTimeTZ().Offset.Hours,
                DateAndTime = DateTimeTZ(),
                dateEnergyPrice = energyPrices.First().date.ToString("dd.MM.yyyy"),
                energyPrices = energyPrices
            };
            return monitorData;
        }
    }
    public class RootData
    {
        public Data data { get; set; }
    }
    public class Data
    {
        public List<Row> Rows { get; set; }
    }
    public class Column
    {
        public string Value { get; set; }
        public string Name { get; set; }
        public int Index { get; set; }
    }
    public class Row
    {
        public List<Column> Columns { get; set; }
        public DateTimeOffset StartTime { get; set; }
        public bool IsExtraRow { get; set; }
    }
    public class EnergyPriceClass
    {
        public string DeviceID = "EnergyData";
        public string SourceInfo = "Energy price Function";
        public int UtcOffset { get; set; }
        public DateTimeOffset DateAndTime { get; set; }
        public string dateEnergyPrice { get; set; }
        public IList<EnergyPrice> energyPrices { get; set; }
    }
    public class EnergyPrice
    {
        public DateTimeOffset date { get; set; }
        public double price { get; set; }
        public string time { get; set; }
        public int heat { get; set; }
        public int heatOn { get; set; }
        public int heatReduced { get; set; }
        public int heatOff { get; set; }
        public bool isHotWaterTime { get; set; }
    }
}
