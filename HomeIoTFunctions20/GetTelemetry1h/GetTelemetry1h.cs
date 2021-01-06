using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Documents;
using System.Collections.Generic;

namespace HomeIoTFunctions20.GetTelemetry1h
{
    public static class GetTelemetry1h
    {
        //this is used by PowerApps to draw the nice graph from different elements
        [FunctionName("GetTelemetry1h")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TelemetryData",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT TOP 24 c.DateAndTime, c.time, c.Ventilation1h, c.HomeHeating1h, c.WaterHeating1h, " +
                            "ROUND(c.Co21h) as Co21h, ROUND(c.Noise1h) as Noise1h, ROUND(c.Humidity1h) as Humidity1h, ROUND(c.HumidityOut1h) as HumidityOut1h, ROUND(c.Temperature1h) as Temperature1h, " +
                            "ROUND(c.TemperatureOut1h) as TemperatureOut1h, " +
                            "ROUND(c.BatteryPercent1h) as BatteryPercent1h " +
                            "FROM c WHERE c.Co21h <> null ORDER BY c._ts DESC"
                )]
            IEnumerable<Document> input,
            ILogger log)
        {
            log.LogInformation($"Result: {input} ");
            string msg = "{\"data\":" + JsonConvert.SerializeObject(input) + "}";
            var output = JsonConvert.DeserializeObject(msg);
            return new OkObjectResult($"{output}");
        }
    }
}
