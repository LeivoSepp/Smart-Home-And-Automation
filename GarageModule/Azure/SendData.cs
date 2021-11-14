﻿using Microsoft.Azure.Devices.Client;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GarageModule.Azure
{
    class SendDataAzure
    {
        static int counter;

        public async Task<MessageResponse> PipeMessage(Message IoTmessage, ModuleClient moduleClient, string message)
        {
            int counterValue = Interlocked.Increment(ref counter);

            await moduleClient.SendEventAsync("output1", IoTmessage);
            Console.WriteLine($"IoT Hub message: {counterValue}, {message}");
            return MessageResponse.Completed;
        }
    }
}
