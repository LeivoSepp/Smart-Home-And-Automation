using IoTHubTrigger = Microsoft.Azure.WebJobs.EventHubTriggerAttribute;
using Microsoft.Azure.WebJobs;
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
using Azure.Messaging.EventHubs;

namespace HomeIoTFunctions20.IoTHubMessages
{
    public static class IoTHubMessages
    {
        static List<EmailAddress> EmailsTo;
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
            EmailsTo = new List<EmailAddress>();
            List<string> emailList = SendEmailTo.Split(",").ToList();
            foreach (var item in emailList)
            {
                EmailsTo.Add(new EmailAddress(item));
            }
            log.LogInformation($"C# IoT Hub trigger function processed a message: {eventMessages.Length}");
            string jsonStr;
            foreach (var eventData in eventMessages)
            {
                try
                {
                    if (eventData.EnqueuedTime >= DateTime.UtcNow.AddMinutes(-1))
                        jsonStr = Encoding.UTF8.GetString(eventData.EventBody.ToArray());
                    else
                        return;

                    if (JToken.Parse(jsonStr) is JObject)
                    {
                        JObject json = JsonConvert.DeserializeObject<JObject>(jsonStr);
                        log.LogInformation($"JObject: {json}");

                        if (json.Value<bool>("isHomeSecured"))
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
                }
                catch (Exception ex)
                {
                    log.LogInformation($"Caught exception: {ex.Message}");
                }
            }
        }
        public static async void SendEmail(JObject jObject, IAsyncCollector<SendGridMessage> messageCollector)
        {
            string source = jObject.Value<string>("SourceInfo");
            string status = jObject.Value<string>("status");
            string date = jObject.Value<string>("date");
            string time = jObject.Value<string>("time");
            string subject = $"{source} at {date} {time}";

            var message = new SendGridMessage();
            message.AddTos(EmailsTo);
            message.AddContent("text/plain", $"Hello, \n\nSmart security announcement at {time}, your lovely home recognized an activity.\n {status} \n\nYour Sweet Home");
            message.SetFrom(SendEmailFrom);
            message.SetSubject(subject);
            await messageCollector.AddAsync(message);
        }
    }
}