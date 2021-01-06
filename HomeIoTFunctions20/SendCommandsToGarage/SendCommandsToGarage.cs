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
using Microsoft.Azure.Devices;

namespace HomeIoTFunctions20.SendCommandsToGarage
{
    public static class SendCommandsToGarage
    {
        //this function is called by PowerApps to send direct commands to GaragePI
        //response message sent back to PowerApps is the statuses of GaragePI

        [FunctionName("SendCommandsToGarage")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ExecutionContext context,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            //this piece of code needed to take the connection string from local file. This is for debugging
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = config["IoTHubConnectionString"];

            var serviceClient = ServiceClient.CreateFromConnectionString(connectionString);
            var cloudToDeviceMethod = new CloudToDeviceMethod("ManagementCommands")
            {
                ConnectionTimeout = TimeSpan.FromSeconds(5),
                ResponseTimeout = TimeSpan.FromSeconds(5)
            };

            cloudToDeviceMethod.SetPayloadJson(requestBody);
            var response = await serviceClient.InvokeDeviceMethodAsync("GarageEdgeDevice", "GarageModule", cloudToDeviceMethod).ConfigureAwait(false);
            var json = response.GetPayloadAsJson();

            return new OkObjectResult($"{json}");
        }
    }
}
