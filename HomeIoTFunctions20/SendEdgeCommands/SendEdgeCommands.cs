using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Configuration;


namespace HomeIoTFunctions20.SendEdgeCommands
{
    public static class SendEdgeCommands
    {
        //this function is called by PowerApps and it is used to send direct commands to Raspberry
        //response message sent back to PowerApps is the detailed data of Raspberry's current state
        [FunctionName("SendEdgeCommands")]
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
            var response = await serviceClient.InvokeDeviceMethodAsync("HomeEdgeDevice", "HomeModule", cloudToDeviceMethod).ConfigureAwait(false);
            var json = response.GetPayloadAsJson();

            return new OkObjectResult($"{json}");

        }
    }
}
