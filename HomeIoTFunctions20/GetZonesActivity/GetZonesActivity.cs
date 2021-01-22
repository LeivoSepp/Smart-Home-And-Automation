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
                SqlQuery = "SELECT c.ZoneName, c.DateStart, c.TimeStart, c.TimeEnd, c.IsHomeSecured FROM Zones f JOIN c IN f.alertingSensors WHERE f.DateAndTime > {Date} AND f.DateAndTime < DateTimeAdd('d', 1, {Date}) ORDER BY f._ts DESC"
                )]
                JArray output,
            ILogger log)
        {
            //{Date} = 2021-04-23

            return new OkObjectResult($"{output}");
        }
    }
}
