﻿using HomeModule.Helpers;
using Microsoft.Azure.Devices.Client;
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeModule.Azure
{
    class SendDataAzure
    {
        static int counter;
        public async Task<MessageResponse> PipeMessage(object inputdata, ModuleClient userContext, string SourceInfo, string output)
        {
            int counterValue = Interlocked.Increment(ref counter);

            if (!(userContext is ModuleClient moduleClient))
            {
                throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
            }
            var messageJson = JsonSerializer.Serialize(inputdata);
            var message = new Message(Encoding.ASCII.GetBytes(messageJson));
            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);

            if (!string.IsNullOrEmpty(messageString))
            {
                //the following piece of code is necessary only if using Twin Desired/Reported properties
                //this desired/reported properties are not used at the moment in my code
                var pipeMessage = new Message(messageBytes);
                foreach (var prop in message.Properties)
                {
                    pipeMessage.Properties.Add(prop.Key, prop.Value);
                }
                await moduleClient.SendEventAsync(output, pipeMessage);
                Console.WriteLine($"\nAzure IoT Hub message: {counterValue}. {SourceInfo}: {METHOD.DateTimeTZ().DateTime}");
            }
            return MessageResponse.Completed;
        }
    }
}
