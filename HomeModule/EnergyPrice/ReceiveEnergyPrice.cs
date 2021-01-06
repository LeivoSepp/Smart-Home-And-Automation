using HomeModule.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace HomeModule.EnergyPrice
{
    class ReceiveEnergyPrice
    {
        private List<EnergyPriceClass> energyPriceToday;
        private FileOperations fileOperations;
        public async Task<List<EnergyPriceClass>> QueryEnergyPriceAsync()
        {
            fileOperations = new FileOperations();
            string fileToday = $"CurrentEnergyPrice{Program.DateTimeTZ():dd-MM-yyyy}";
            string fileYesterday = $"CurrentEnergyPrice{Program.DateTimeTZ().AddDays(-1):dd-MM-yyyy}";
            fileToday = fileOperations.GetFilePath(fileToday);
            //delete yesterday file, we dont need to collect them
            fileYesterday = fileOperations.GetFilePath(fileYesterday);
            if (File.Exists(fileYesterday)) File.Delete(fileYesterday);

            if (File.Exists(fileToday)) //is there already file with today energy prices
            {
                var dataFromFile = await fileOperations.OpenExistingFile(fileToday);
                energyPriceToday = JsonConvert.DeserializeObject<List<EnergyPriceClass>>(dataFromFile.ToString());
                return energyPriceToday;
            }
            if (!File.Exists(fileToday)) //file with today energy price is missing
            {
                try
                {
                    string MarketPriceToday = Program.DateTimeTZ().ToString("dd.MM.yyyy");
                    energyPriceToday = await GetMarketPrice(MarketPriceToday);
                    var jsonString = JsonConvert.SerializeObject(energyPriceToday);
                    await fileOperations.SaveStringToLocalFile(fileToday, jsonString);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Receive energy price from Cosmos exception: " + e.Message);
                }
            }
            return energyPriceToday;
        }
        private async Task<List<EnergyPriceClass>> GetMarketPrice(string DatePrice)
        {
            string funcUrl = Environment.GetEnvironmentVariable("EnergyPriceFuncURL");
            string funcCode = Environment.GetEnvironmentVariable("EnergyPriceFuncCode");
            //get the energy price from CosmosDB
            var http = new HttpClient();
            string url = funcUrl + DatePrice + "?code=" + funcCode;
            HttpResponseMessage response = await http.GetAsync(url);
            var result = response.Content.ReadAsStringAsync();

            //deserialize all content
            var nps = JsonConvert.DeserializeObject<JObject>(result.Result);
            string dataresult = nps["energyPrices"].ToString();
            var npsOut = JsonConvert.DeserializeObject<List<EnergyPriceClass>>(dataresult);
            return npsOut;
        }
    }
}
