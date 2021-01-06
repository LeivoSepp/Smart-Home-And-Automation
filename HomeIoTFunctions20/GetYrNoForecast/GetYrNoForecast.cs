using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using CoordinateSharp;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HomeIoTFunctions20.GetYrNoForecast
{
    public static class GetYrNoForecast
    {
        //https://github.com/Tronald/CoordinateSharp
        static readonly double Lat = 59.419555;
        static readonly double Lon = 24.703521;
        static DateTime ActualDataUpdated;
        static int offset;

        //timer scheduler to get the forecast from Yr.No
        [FunctionName("GetYrNoForecast")]
        public static async Task Run(
            [TimerTrigger("0 0 */6 * * *"
            )]TimerInfo myTimer, //every 6 hours
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TelemetryData",
                ConnectionStringSetting = "CosmosDBConnection"
                )]
                IAsyncCollector<YrnoTempClass> asyncCollectorYrNo,
            ILogger log)
        {
            var coordinate = new Coordinate(Lat, Lon, DateTime.Now);
            var sunset = (DateTime)coordinate.CelestialInfo.SunSet;
            var sunrise = (DateTime)coordinate.CelestialInfo.SunRise;
            offset = GetEnergyMarketPrice.GetEnergyMarketPrice.DateTimeTZ().Offset.Hours;

            var yrnoList = await getYrnoData();
            var sendData = new YrnoTempClass()
            {
                DateAndTime = GetEnergyMarketPrice.GetEnergyMarketPrice.DateTimeTZ(),
                ActualDataUpdated = ActualDataUpdated,
                Sunrise = sunrise.AddHours(offset),
                Sunset = sunset.AddHours(offset),
                YrnoTemp = yrnoList
            };
            await asyncCollectorYrNo.AddAsync(sendData);
        }
        private static async Task<List<YrnoTemp>> getYrnoData()
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            string url = $"https://api.met.no/weatherapi/locationforecast/2.0/complete?lat={Lat.ToString("0.000000", CultureInfo.InvariantCulture)}&lon={Lon.ToString("0.0000000", CultureInfo.InvariantCulture)}";
            
            HttpResponseMessage response = await http.GetAsync(url);
            var result = response.Content.ReadAsStringAsync();
            Root YrNoRawData = JsonConvert.DeserializeObject<Root>(result.Result);

            ActualDataUpdated = YrNoRawData.properties.meta.updated_at.AddHours(offset);

            var YrnoList = YrNoRawData.properties.timeseries.Where(item => item.time.AddHours(offset) < GetEnergyMarketPrice.GetEnergyMarketPrice.DateTimeTZ().Date.AddDays(2)).Select(item =>
                        new YrnoTemp
                        {
                            from = item.time.AddHours(offset),
                            to = item.time.AddHours(offset+1),
                            temp = item.data.instant.details.air_temperature
                        }).ToList();
            return YrnoList;
        }
    }
    public class YrnoTempClass
    {
        public DateTimeOffset DateAndTime { get; set; }
        public string DeviceID = "Yrno";
        public DateTime ActualDataUpdated { get; set; }
        public DateTime Sunrise { get; set; }
        public DateTime Sunset { get; set; }
        public IList<YrnoTemp> YrnoTemp { get; set; }
    }
    public class YrnoTemp
    {
        public DateTime from { get; set; }
        public DateTime to { get; set; }
        public double temp { get; set; }
    }

    //https://api.met.no/doc/ForecastJSON
    //https://api.met.no/weatherapi/locationforecast/2.0/complete?lat=59.41&lon=24.70
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse); 
    public class Geometry
    {
        public string type { get; set; }
        public List<double> coordinates { get; set; }
    }
    public class Meta
    {
        public DateTime updated_at { get; set; }
    }
    public class Details
    {
        public double air_pressure_at_sea_level { get; set; }
        public double air_temperature { get; set; }
        public double cloud_area_fraction { get; set; }
        public double cloud_area_fraction_high { get; set; }
        public double cloud_area_fraction_low { get; set; }
        public double cloud_area_fraction_medium { get; set; }
        public double dew_point_temperature { get; set; }
        public double fog_area_fraction { get; set; }
        public double relative_humidity { get; set; }
        public double ultraviolet_index_clear_sky { get; set; }
        public double wind_from_direction { get; set; }
        public double wind_speed { get; set; }
    }
    public class Instant
    {
        public Details details { get; set; }
    }
    public class Data
    {
        public Instant instant { get; set; }
    }
    public class Timesery
    {
        public DateTime time { get; set; }
        public Data data { get; set; }
    }
    public class Properties
    {
        public Meta meta { get; set; }
        public List<Timesery> timeseries { get; set; }
    }
    public class Root
    {
        public string type { get; set; }
        public Geometry geometry { get; set; }
        public Properties properties { get; set; }
    }
}
