using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using Microsoft.Azure.Documents;
using Newtonsoft.Json.Linq;


namespace HomeIoTFunctions20.GetDoorLogs
{
    public static class GetDoorLogs
    {
        //this is very badly designed query
        //used by powerapps to get the home door logs and garage door logs
        [FunctionName("GetDoorLogs")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "GetDoorLogs/{Date}")] HttpRequest req,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TelemetryData",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT c.date, c.time, c.status, c.door  FROM c WHERE c.DeviceID = 'SecurityController' AND c.door <> '' AND c.DateAndTime > {Date} AND c.DateAndTime < DateTimeAdd('d', 1, {Date}) ORDER BY c._ts DESC"
                )]
                JArray output,
            ILogger log)
        {
            //{Date} = 2021-04-23

            return new OkObjectResult(output);
        }
    }
}
