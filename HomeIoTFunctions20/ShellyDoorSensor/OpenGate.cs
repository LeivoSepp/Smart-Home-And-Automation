using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;

namespace HomeIoTFunctions20.ShellyDoorSensor
{
    public static class OpenGate
    {
        [FunctionName("OpenGate")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TelemetryData",
                ConnectionStringSetting = "CosmosDBConnection"
                )]
                IAsyncCollector<dynamic> output,
            ExecutionContext context,
            ILogger log)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var sendData = new 
            {
                DeviceID = "Shelly",
                DateAndTime = GetEnergyMarketPrice.GetEnergyMarketPrice.DateTimeTZ(),
                isGateOpen = true
            };
            await output.AddAsync(sendData);

            return new OkObjectResult("OK");
        }
    }
}
