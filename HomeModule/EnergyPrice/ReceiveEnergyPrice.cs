using HomeModule.Helpers;
using System.Text.Json;
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
        private METHOD Methods;
        public async Task<List<EnergyPriceClass>> QueryEnergyPriceAsync()
        {
            Methods = new METHOD();
            string fileToday = $"CurrentEnergyPrice{METHOD.DateTimeTZ():dd-MM-yyyy}";
            string fileYesterday = $"CurrentEnergyPrice{METHOD.DateTimeTZ().AddDays(-1):dd-MM-yyyy}";
            fileToday = Methods.GetFilePath(fileToday);
            //delete yesterday file, we dont need to collect them
            fileYesterday = Methods.GetFilePath(fileYesterday);
            if (File.Exists(fileYesterday)) File.Delete(fileYesterday);

            if (File.Exists(fileToday)) //is there already file with today energy prices
            {
                var dataFromFile = await Methods.OpenExistingFile(fileToday);
                energyPriceToday = JsonSerializer.Deserialize<List<EnergyPriceClass>>(dataFromFile.ToString());
            }
            if (!File.Exists(fileToday)) //file with today energy price is missing
            {
                try
                {
                    string MarketPriceToday = METHOD.DateTimeTZ().ToString("dd.MM.yyyy");
                    energyPriceToday = await GetMarketPrice(MarketPriceToday);
                    var jsonString = JsonSerializer.Serialize(energyPriceToday);
                    await Methods.SaveStringToLocalFile(fileToday, jsonString);
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
            var nps = JsonSerializer.Deserialize<JsonElement>(result.Result);
            string dataresult = nps.GetProperty("energyPrices").GetRawText();
            var npsOut = JsonSerializer.Deserialize<List<EnergyPriceClass>>(dataresult);
            return npsOut;
        }
    }
}
