using IoTHubTrigger = Microsoft.Azure.WebJobs.EventHubTriggerAttribute;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.EventHubs;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using Newtonsoft.Json.Linq;
using SendGrid.Helpers.Mail;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace HomeIoTFunctions20.IoTHubMessages
{
    public static class IoTHubMessages
    {
        static List<EmailAddress> EmailsTo = new List<EmailAddress>();
        static string SendEmailFrom;
        //This function is listsening IoT Hub messages (actually messages which are coming from HomePI Edge StreamAnalytics or GaragePI directly) 
        //all messages are forwarded to CosmosDB and some of the messages will sent to SendGrid (e-mail)
        //it will not forward old messages, only messages that are current and forward
        [FunctionName("IoTHubMessages")]
        public static async Task Run([IoTHubTrigger("iothubtrigger", Connection = "IoTHubEndpoint", ConsumerGroup = "FunctionGroup")] EventData[] eventMessages,
             [CosmosDB(
                databaseName: "FreeCosmosDB",
                collectionName: "TelemetryData",
                ConnectionStringSetting = "CosmosDBConnection"
                )]
                IAsyncCollector<dynamic> output,
             [SendGrid(ApiKey = "SendGridAPIKey")] IAsyncCollector<SendGridMessage> messageCollector,
             ExecutionContext context,
            ILogger log)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();
            var SendEmailTo = config["SendEmailsTo"];
            SendEmailFrom = config["SendEmailFrom"];
            List<string> emailList = SendEmailTo.Split(",").ToList();
            foreach (var item in emailList)
            {
                EmailsTo.Add(new EmailAddress(item));
            }
            try
            {

                log.LogInformation($"C# IoT Hub trigger function processed a message: {eventMessages.Length}");
                string jsonStr;


                foreach (var eventData in eventMessages)
                {
                    try
                    {
                        if (eventData.SystemProperties.EnqueuedTimeUtc >= DateTime.UtcNow.AddMinutes(-1))
                            jsonStr = Encoding.UTF8.GetString(eventData.Body.Array);
                        else
                            return;

                        if (JToken.Parse(jsonStr) is JObject)
                        {
                            JObject json = JsonConvert.DeserializeObject<JObject>(jsonStr);
                            log.LogInformation($"JObject: {json}");

                            if (json.Value<bool>("isHomeSecured") && json.Value<string>("DeviceID") == "SecurityController")
                                SendEmail(json, messageCollector);
                            //if (json.Value<string>("SourceInfo") == "Someone is at home: True" || json.Value<string>("SourceInfo") == "Someone is at home: False")
                            //    SendEmail(json, messageCollector);


                            await output.AddAsync(json);
                        }
                        else //array, from the Stream Analytics it comes as an Array
                        {
                            JArray json = JsonConvert.DeserializeObject<JArray>(jsonStr);
                            foreach (JObject doc in json)
                            {
                                if (doc.Value<bool>("isHomeSecured"))
                                    SendEmail(doc, messageCollector);

                                log.LogInformation($"JArray: {doc}");
                                await output.AddAsync(doc);
                            }
                        }
                        //log.LogInformation($"events: {eventData.SystemProperties.EnqueuedTimeUtc}");
                        //log.LogInformation($"C# IoT Hub trigger function processed a message: {Encoding.UTF8.GetString(eventData.Body.Array)}");
                    }
                    catch (Exception ex)
                    {
                        log.LogInformation($"Caught exception: {ex.Message}");
                    }
                }
            }
            catch (Exception e)
            {

                log.LogInformation($"Exception when reading messages: {e.Message}");
            }

        }
        public static async void SendEmail(JObject jObject, IAsyncCollector<SendGridMessage> messageCollector)
        {

            string source = jObject.Value<string>("SourceInfo");
            //string status = jObject.Value<string>("status");
            string date = jObject.Value<string>("date");
            string time = jObject.Value<string>("time");
            string subject = $"{source} at {date} {time}";

            var message = new SendGridMessage();
            message.AddTos(EmailsTo);
            message.AddContent("text/plain", $"Hello, \n\nSmart security announcement at {time}, your lovely home recognized an activity: {source} \n\nYour Sweet Home\n{patterns}");
            message.SetFrom(SendEmailFrom);
            message.SetSubject(subject);
            await messageCollector.AddAsync(message);
        }
        static string patterns = @"
    * 
    * PATTERNS
    * 
    1. Entry 1, door closed. Entrance, when the door is closed beforehand, with closing the door afterwards.
    2. Entry 2, door left open. Entrance when the door is closed beforehand, without closing the door afterwards.
    3. Exit 1_1/1_2/1_3, door closed. Exit when the door is closed beforehand, with closing the door afterwards.
    4. Exit 2, door left open. Exit when the door is closed beforehand, without closing the door afterwards.
    5. Entry-Exit 1, door closed. Entrance / exit when the door is opened beforehand, with closing the door afterwards.
    6. Entry-Exit 2, door left open. Entrance / exit when the door is opened beforehand, without closing the door afterwards.
";
    }
}