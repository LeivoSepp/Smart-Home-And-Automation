using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace HomeIoTFunctions20.GetEnergyPrice
{
    public static class GetEnergyPrice
    {
        //used by Raspberry to get the energy price and heating schedule from CosmosDB.
        //used by PowerApps to get the today/tomorrow energy price and heating schedule from CosmosDB.
        [FunctionName("GetEnergyPrice")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "GetEnergyPrice/{Date}")] HttpRequest req,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TelemetryData",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT TOP 1 c.energyPrices FROM c WHERE c.DeviceID = 'EnergyData' AND c.dateEnergyPrice = {Date} ORDER BY c._ts DESC"
                )]
                JArray input,
            ILogger log)
        {
            //{Date} = "08/23/2019" "28.12.2021"

            return new OkObjectResult($"{input.First}");
        }
    }
}
