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

namespace HomeIoTFunctions20.GetZonesActivity
{
    public static class GetZonesActivity
    {
        [FunctionName("GetZonesActivity")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "GetZonesActivity/{Date}")] HttpRequest req,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TelemetryData",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT c.ZoneName, c.ZoneEmptyDetectTime as ZoneStart, c.ZoneEventTime as ZoneEnd, c.IsHomeSecured FROM Zones f JOIN c IN f.alertingSensors WHERE f.DateAndTime > {Date} ORDER BY f._ts DESC"
                )]
                JArray input,
            ILogger log)
        {
            //{Date} = "08/23/2019"

            return new OkObjectResult($"{input}");
        }
    }
}
