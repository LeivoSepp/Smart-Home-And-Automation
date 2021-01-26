using GarageModule.Sensors;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace GarageModule.Azure
{
    class ReceiveDataClass
    {
        public static bool IsGarageLightsOn { get; set; }
    }
    class CommandNames
    {
        internal const string TURN_ON_GARAGE_LIGHTS = "Garage Lights On";
        internal const string TURN_OFF_GARAGE_LIGHTS = "Garage Lights Off";
    }

    class ReceiveData
    {
        Garage _sensors = new Garage();
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task<MethodResponse> ControlGarageEdgeDevice(MethodRequest methodRequest, object userContext)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            ReceiveData receiveData = new ReceiveData();
            List<string> command = new List<string>();

            try
            {
                var ReceivedCommand = JObject.Parse(methodRequest.DataAsJson);
                if (ReceivedCommand["isGarageLightsOn"] != null && ((bool)ReceivedCommand["isGarageLightsOn"] == !ReceiveDataClass.IsGarageLightsOn))
                {
                    string cmd = (bool)ReceivedCommand["isGarageLightsOn"] ? CommandNames.TURN_ON_GARAGE_LIGHTS : CommandNames.TURN_OFF_GARAGE_LIGHTS;
                    command.Add(cmd);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Garage device Receive command: " + e.ToString());
            }

            foreach (string cmd in command)
            {
                if (cmd != null)
                    receiveData.ProcessCommand(cmd);
            }

            var reportedProperties = new TwinCollection();
            reportedProperties["isGarageLightsOn"] = ReceiveDataClass.IsGarageLightsOn;
            reportedProperties["Light"] = Garage.CurrentLux;
            reportedProperties["Temperature"] = Garage.Temperature;
            reportedProperties["isGarageDoorOpen"] = Garage.isGarageDoorOpen;

            var response = Encoding.ASCII.GetBytes(reportedProperties.ToJson());
            return new MethodResponse(response, 200);
        }
        public async void ReceiveCommandsAsync()
        {
            ModuleClient ioTHubModuleClient = Program.IoTHubModuleClient;
            // Register callback to be called when a message is received by the module
            await ioTHubModuleClient.SetMethodHandlerAsync("ManagementCommands", ControlGarageEdgeDevice, null);
        }
        public void ProcessCommand(string command)
        {
            if (command == CommandNames.TURN_ON_GARAGE_LIGHTS)
            {
                ReceiveDataClass.IsGarageLightsOn = true;
            }
            else if (command == CommandNames.TURN_OFF_GARAGE_LIGHTS)
            {
                ReceiveDataClass.IsGarageLightsOn = false;
            }
        }
    }
}
