using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using Microsoft.Azure.Documents;


namespace HomeIoTFunctions20.GetDoorLogs
{
    public static class GetDoorLogs
    {
        //this is very badly designed query
        //used by powerapps to get the home door logs and garage door logs
        [FunctionName("GetDoorLogs")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "GetDoorLogs/{DoorType}")] HttpRequest req,
            [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TelemetryData",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT TOP 30 c.DateAndTime, c.date, c.time, c.status, c.isHomeSecured, c.GarageDoorOpenTime, c.isGarageDoorOpen, c.DeviceID  " +
                            "FROM c " +
                            "WHERE ((c.isHomeSecured = true AND (c.DoorOpenInSeconds > 0  OR (c.isGarageDoorOpen = true AND c.DeviceID != {DoorType}) )) OR c.DeviceID = 'SecurityController') " +
                            "AND (c.DeviceID = {DoorType} OR c.DeviceID = 'GaragePI')  ORDER BY c._ts DESC"
                )]
                IEnumerable<Document> input, 
            ILogger log)
        {
            string strInput = JsonConvert.SerializeObject(input);
            strInput = strInput.Substring(1, strInput.Length - 2);

            string msg = "{\"data\":[" + strInput + "]}";
            var output = JsonConvert.DeserializeObject(msg);

            //{DoorType} = SecurityController / GaragePI

            return new OkObjectResult(output);
        }
    }
}
