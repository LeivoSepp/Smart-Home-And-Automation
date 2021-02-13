using GarageModule.Sensors;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using System.Text;
using System.Threading.Tasks;

namespace GarageModule.Azure
{
    class ReceiveData
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        //This method is called out from Azure Function
        //Azure Function is called out from PowerApps
        private async Task<MethodResponse> GetGarageStatus(MethodRequest methodRequest, object userContext)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            var reportedProperties = new TwinCollection();
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
            await ioTHubModuleClient.SetMethodHandlerAsync("GetGarageStatus", GetGarageStatus, null);
        }
    }
}
