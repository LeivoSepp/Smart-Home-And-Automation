using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace HomeIoTFunctions20.GetWiFiDevices
{
    public static class GetWiFiDevices
    {
        [FunctionName("GetWiFiDevices")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TelemetryData",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT TOP 1 c.AllWiFiDevices FROM c WHERE c.status = 'WiFi Devices' ORDER BY c._ts DESC"
                )]
                JArray output,
            ILogger log)
        {
            //{Date} = 2021-04-23

            return new OkObjectResult(output.First()["AllWiFiDevices"]);
        }
    }
}
