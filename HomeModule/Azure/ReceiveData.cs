using Microsoft.Azure.Devices.Client;
using System.Text;
using System.Text.Json;
using HomeModule.Schedulers;
using HomeModule.Raspberry;
using System;
using System.Device.Gpio;
using Microsoft.Azure.Devices.Shared;
using System.Threading.Tasks;
using System.Collections.Generic;
using HomeModule.Netatmo;
using System.Net.Http;
using HomeModule.Models;
using System.IO;
using HomeModule.Helpers;
using System.Linq;

namespace HomeModule.Azure
{
    class ReceiveData
    {
        private SendTelemetryData _sendDataAzure;
        private readonly METHOD Methods = new METHOD();
        private async Task<MethodResponse> HomeEdgeDeviceManagement(MethodRequest methodRequest, object userContext)
        {
            ReceiveData receiveData = new ReceiveData();
            List<string> command = new List<string>();
            try
            {
                var HomeCommands = JsonDocument.Parse(methodRequest.DataAsJson).RootElement;
                if (HomeCommands.TryGetProperty("Ventilation", out JsonElement ventilation) && ventilation.GetBoolean() == !TelemetryDataClass.isVentilationOn)
                {
                    string cmd = ventilation.GetBoolean() ? CommandNames.OPEN_VENT : CommandNames.CLOSE_VENT;
                    command.Add(cmd);
                }
                if (HomeCommands.TryGetProperty("Sauna", out JsonElement sauna) && (sauna.GetBoolean() == !TelemetryDataClass.isSaunaOn))
                {
                    string cmd = sauna.GetBoolean() ? CommandNames.TURN_ON_SAUNA : CommandNames.TURN_OFF_SAUNA;
                    command.Add(cmd);
                }
                if (HomeCommands.TryGetProperty("Water", out JsonElement water) && water.GetBoolean() == !TelemetryDataClass.isWaterHeatingOn)
                {
                    string cmd = water.GetBoolean() ? CommandNames.TURN_ON_HOTWATERPUMP : CommandNames.TURN_OFF_HOTWATERPUMP;
                    command.Add(cmd);
                }
                if (HomeCommands.TryGetProperty("Heating", out JsonElement heating) && heating.GetBoolean() == !TelemetryDataClass.isHeatingOn)
                {
                    string cmd = heating.GetBoolean() ? CommandNames.TURN_ON_HEATING : CommandNames.TURN_OFF_HEATING;
                    command.Add(cmd);
                }
                if (HomeCommands.TryGetProperty("NormalTemp", out JsonElement normalTemp) && normalTemp.GetBoolean() == !TelemetryDataClass.isNormalHeating)
                {
                    string cmd = normalTemp.GetBoolean() ? CommandNames.NORMAL_TEMP_COMMAND : CommandNames.REDUCE_TEMP_COMMAND;
                    command.Add(cmd);
                }
                if (HomeCommands.TryGetProperty("Vacation", out JsonElement vacation) && vacation.GetBoolean() == !TelemetryDataClass.isHomeInVacation)
                {
                    string cmd = vacation.GetBoolean() ? CommandNames.TURN_ON_VACATION : CommandNames.TURN_OFF_VACATION;
                    command.Add(cmd);
                }
                if (HomeCommands.TryGetProperty("Security", out JsonElement security) && security.GetBoolean() == !TelemetryDataClass.isHomeSecured)
                {
                    string cmd = security.GetBoolean() ? CommandNames.TURN_ON_SECURITY : CommandNames.TURN_OFF_SECURITY;
                    SomeoneAtHome.IsSecurityManuallyOn = security.GetBoolean(); //override automatic security until next someone at home event
                    command.Add(cmd);
                }
                if (HomeCommands.TryGetProperty("Temperatures", out JsonElement temperatures))
                {
                    //this data is coming from PowerApps
                    var Temperatures = JsonSerializer.Deserialize<List<SensorReading>>(temperatures.GetRawText());
                    string jsonString = JsonSerializer.Serialize(Temperatures);
                    var filename = Methods.GetFilePath(CONSTANT.FILENAME_ROOM_TEMPERATURES);
                    await Methods.SaveStringToLocalFile(filename, jsonString);
                    HomeTemperature.SetTemperatures(Temperatures);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Home device management: " + e.ToString());
            }

            foreach (string cmd in command)
            {
                if (cmd != null)
                    receiveData.ProcessCommand(cmd);
            }

            string SaunaStartedTime = TelemetryDataClass.SaunaStartedTime == DateTime.MinValue ? "--:--" : TelemetryDataClass.SaunaStartedTime.ToString("HH:mm");
            //This data goes back directly to PowerApps through Azure Functions
            var reportedProperties = new TwinCollection();
            reportedProperties["Ventilation"] = TelemetryDataClass.isVentilationOn;
            reportedProperties["Water"] = TelemetryDataClass.isWaterHeatingOn;
            reportedProperties["Heating"] = TelemetryDataClass.isHeatingOn;
            reportedProperties["NormalTemp"] = TelemetryDataClass.isNormalHeating;
            reportedProperties["Vacation"] = TelemetryDataClass.isHomeInVacation;
            reportedProperties["Security"] = TelemetryDataClass.isHomeSecured;
            reportedProperties["Co2"] = NetatmoDataClass.Co2;
            reportedProperties["Temperature"] = NetatmoDataClass.Temperature;
            reportedProperties["Humidity"] = NetatmoDataClass.Humidity;
            reportedProperties["TemperatureOut"] = NetatmoDataClass.TemperatureOut;
            reportedProperties["BatteryPercent"] = NetatmoDataClass.BatteryPercent;
            reportedProperties["isRoomHeatingNow"] = Pins.IsRoomHeatingOn;
            reportedProperties["isWaterHeatingNow"] = Pins.IsWaterHeatingOn;
            reportedProperties["Sauna"] = TelemetryDataClass.isSaunaOn;
            reportedProperties["isSaunaDoorOpen"] = Pins.IsSaunaDoorOpen;
            reportedProperties["SaunaStartedTime"] = SaunaStartedTime;
            reportedProperties["isSaunaHeatingNow"] = !(bool)Pins.PinRead(Pins.saunaHeatOutPin);
            reportedProperties["isSomeoneAtHome"] = TelemetryDataClass.isSomeoneAtHome;
            reportedProperties["isOutsideLightsOn"] = TelemetryDataClass.isOutsideLightsOn;
            reportedProperties["isGarageLightsOn"] = TelemetryDataClass.isGarageLightsOn;
            reportedProperties["isHomeDoorOpen"] = TelemetryDataClass.isHomeDoorOpen;
            reportedProperties["Temperatures"] = HomeTemperature.ListOfAllSensors.Temperatures;
            reportedProperties["Localdevices"] = WiFiProbes.WiFiDevicesToPowerApps;

            var response = Encoding.ASCII.GetBytes(reportedProperties.ToJson());
            return new MethodResponse(response, 200);
        }

        public async void ReceiveCommandsAsync()
        {
            ModuleClient ioTHubModuleClient = Program.IoTHubModuleClient;
            //// Read the value from the module twin's desired properties
            //var moduleTwin = await ioTHubModuleClient.GetTwinAsync();
            //await OnDesiredPropertiesUpdate(moduleTwin.Properties.Desired, ioTHubModuleClient);

            //// Attach a callback for updates to the module twin's desired properties.
            //await ioTHubModuleClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertiesUpdate, ioTHubModuleClient);

            //// Register callback to be called when a message is received by the module
            await ioTHubModuleClient.SetMethodHandlerAsync("ManagementCommands", HomeEdgeDeviceManagement, null);
            await ioTHubModuleClient.SetMethodHandlerAsync("SetWiFiMacAddress", HomeEdgeWiFiDevices, null);
            await ioTHubModuleClient.SetMethodHandlerAsync("SetLights", SetLights, null);
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        //PowerApps is sending data to Azure Function and this Function is calling out this method
        private async Task<MethodResponse> HomeEdgeWiFiDevices(MethodRequest methodRequest, object userContext)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            try
            {
                JsonElement jsonElement = JsonDocument.Parse(methodRequest.DataAsJson).RootElement;
                var device = JsonSerializer.Deserialize<Localdevice>(jsonElement.GetRawText());
                WiFiProbes.WiFiDevicesFromPowerApps.Add(new Localdevice()
                {
                    ActiveDuration = device.ActiveDuration,
                    DeviceName = device.DeviceName,
                    DeviceOwner = device.DeviceOwner,
                    DeviceType = device.DeviceType,
                    MacAddress = device.MacAddress,
                    StatusFrom = device.StatusFrom,
                    SignalType = device.SignalType
                });
            }
            catch (Exception e)
            {
                Console.WriteLine("WiFI devices: " + e.ToString());
            }
            var response = Encoding.ASCII.GetBytes("Ok");
            return new MethodResponse(response, 200);
        }
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        //this method is called out by Azure Function. 
        //1. Shelly Door/Gate sensor activates Azure Function
        //2. Shelly Garage Spotlight activates Azure Function (this doesnt work !??)
        private async Task<MethodResponse> SetLights(MethodRequest methodRequest, object userContext)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            try
            {
                JsonElement jsonElement = JsonDocument.Parse(methodRequest.DataAsJson).RootElement;
                jsonElement.TryGetProperty("LightName", out JsonElement lightName);
                jsonElement.TryGetProperty("TurnOnLights", out JsonElement TurnOnLights);

                if (lightName.GetString() == "garage")
                    TelemetryDataClass.isGarageLightsOn = await Shelly.SetShellySwitch(TurnOnLights.GetBoolean(), Shelly.GarageLight, nameof(Shelly.GarageLight));
                if (lightName.GetString() == "outside")
                {
                    TelemetryDataClass.isOutsideLightsOn = await Shelly.SetShellySwitch(TurnOnLights.GetBoolean(), Shelly.OutsideLight, nameof(Shelly.OutsideLight));
                    SomeoneAtHome.LightsManuallyOnOff = true; //force lights on/off for 30 minutes
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Garage Light: " + e.ToString());
            }
            var response = Encoding.ASCII.GetBytes("Ok");
            return new MethodResponse(response, 200);
        }

        async Task OnDesiredPropertiesUpdate(TwinCollection desiredProperties, object userContext)
        {
            //desired property only for security mode and vacation mode
            //it is useful on the situation for example app/device restart to set the correct state
            string command = null;
            _sendDataAzure = new SendTelemetryData();
            var reportedProperties = new TwinCollection();
            try
            {
                //check if the property is not null AND only proceed if the desired state is different from the reported state
                if (desiredProperties["isHomeInVacation"] != null && ((bool)desiredProperties["isHomeInVacation"] == !TelemetryDataClass.isHomeInVacation))
                {
                    command = (bool)desiredProperties["isHomeInVacation"] ? CommandNames.TURN_ON_VACATION : CommandNames.TURN_OFF_VACATION;
                    ProcessCommand(command);
                    reportedProperties["isHomeInVacation"] = TelemetryDataClass.isHomeInVacation;
                }
                if (desiredProperties["isHomeSecured"] != null && ((bool)desiredProperties["isHomeSecured"] == !TelemetryDataClass.isHomeSecured))
                {
                    command = (bool)desiredProperties["isHomeSecured"] ? CommandNames.TURN_ON_SECURITY : CommandNames.TURN_OFF_SECURITY;
                    ProcessCommand(command);
                    reportedProperties["isHomeSecured"] = TelemetryDataClass.isHomeSecured;
                }
            }
            catch (AggregateException ex)
            {
                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine("Error when receiving desired property: {0}", exception);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error when receiving desired property: {0}", ex.Message);
            }

            if (command != null)
            {
                TelemetryDataClass.SourceInfo = "Refresh data after desired state update";
                await _sendDataAzure.SendTelemetryAsync(); //this is needed to refresh data on CosmosDB and in PowerApps after desired state change

                //reporting back properties
                var moduleClient = (ModuleClient)userContext;
                //var patch = new TwinCollection($"{{ " + $"\"isHomeInVacation\":{isHomeInVacation.ToString().ToLower()},\"isHomeSecured\": {isHomeSecured.ToString().ToLower()},\"isVentOpen\": {isVentOpen.ToString().ToLower()},\"isNormalTemp\": {isNormalTemp.ToString().ToLower()}, \"isHotWater\": {isHotWater.ToString().ToLower()}, \"isHeating\": {isHeating.ToString().ToLower()}}}");
                await moduleClient.UpdateReportedPropertiesAsync(reportedProperties); // Just report back last desired property.
                Console.WriteLine(reportedProperties);
            }
        }
        static DateTime dateTimeVentilation = METHOD.DateTimeTZ().DateTime;
        public async void ProcessCommand(string command)
        {
            if (command == CommandNames.NO_COMMAND)
            {
                //_startStopLogic.testing(Pins.redLedPin);
            }
            else if (command == CommandNames.TURN_ON_SAUNA && !Pins.IsSaunaDoorOpen)
            {
                Pins.PinWrite(Pins.saunaHeatOutPin, PinValue.Low);
                TelemetryDataClass.isSaunaOn = true;
                var filename = Methods.GetFilePath(CONSTANT.FILENAME_SAUNA_TIME);
                if (TelemetryDataClass.SaunaStartedTime == DateTime.MinValue) //if sauna hasnt been started before
                {
                    DateTime SaunaStartedTime = METHOD.DateTimeTZ().DateTime;
                    await Methods.SaveStringToLocalFile(filename, SaunaStartedTime.ToString("dd.MM.yyyy HH:mm"));
                    TelemetryDataClass.SaunaStartedTime = SaunaStartedTime;
                }
            }
            else if (command == CommandNames.TURN_OFF_SAUNA)
            {
                var filename = Methods.GetFilePath(CONSTANT.FILENAME_SAUNA_TIME);
                File.Delete(filename);
                Pins.PinWrite(Pins.saunaHeatOutPin, PinValue.High);
                TelemetryDataClass.isSaunaOn = false;
                TelemetryDataClass.SaunaStartedTime = new DateTime();
            }
            else if (command == CommandNames.TURN_ON_VACATION)
            {
                TelemetryDataClass.isHomeInVacation = true;
                ProcessCommand(CommandNames.TURN_OFF_HEATING);
                ProcessCommand(CommandNames.TURN_OFF_SAUNA);
                ProcessCommand(CommandNames.CLOSE_VENT);
                Console.WriteLine($"Vacation mode on at {METHOD.DateTimeTZ().DateTime:G}");
            }
            else if (command == CommandNames.TURN_OFF_VACATION)
            {
                TelemetryDataClass.isHomeInVacation = false;
                Console.WriteLine($"Vacation mode on at {METHOD.DateTimeTZ().DateTime:G}");
            }
            else if (command == CommandNames.TURN_ON_SECURITY)
            {
                TelemetryDataClass.isHomeSecured = true;
                Console.WriteLine($"Home is secured at: {METHOD.DateTimeTZ().DateTime:G}");
            }
            else if (command == CommandNames.TURN_OFF_SECURITY)
            {
                TelemetryDataClass.isHomeSecured = false;
                Console.WriteLine($"Home is at normal state at: {METHOD.DateTimeTZ().DateTime:G}");
            }
            else if (command == CommandNames.OPEN_VENT)
            {
                Pins.PinWrite(Pins.ventOutPin, PinValue.High);
                ManualVentLogic.VENT_ON = true; //to enable 30min ventilation, same behavior as Co2 over 900
                TelemetryDataClass.isVentilationOn = true;
                dateTimeVentilation = METHOD.DateTimeTZ().DateTime;
            }
            else if (command == CommandNames.CLOSE_VENT)
            {
                Pins.PinWrite(Pins.ventOutPin, PinValue.Low);
                ManualVentLogic.VENT_ON = false;
                TelemetryDataClass.VentilationInMinutes = (int)(METHOD.DateTimeTZ().DateTime - dateTimeVentilation).TotalMinutes;
                TelemetryDataClass.SourceInfo = "Ventilation turned off";
                var _sendData = new Pins();
                await _sendData.SendData();
                TelemetryDataClass.VentilationInMinutes = 0;
                TelemetryDataClass.isVentilationOn = false;
            }
            else if (command == CommandNames.NORMAL_TEMP_COMMAND)
            {
                if (!TelemetryDataClass.isHomeInVacation)
                {
                    ProcessCommand(CommandNames.TURN_ON_HEATING);
                    Pins.PinWrite(Pins.normalTempOutPin, PinValue.High);
                    Pins.PinWrite(Pins.floorPumpOutPin, PinValue.High);
                    TelemetryDataClass.isNormalHeating = true;
                }
                else
                {
                    ProcessCommand(CommandNames.REDUCE_TEMP_COMMAND);
                    ProcessCommand(CommandNames.TURN_ON_HEATING);
                }
            }
            else if (command == CommandNames.REDUCE_TEMP_COMMAND)
            {
                while (Pins.IsRoomHeatingOn || Pins.IsWaterHeatingOn) ;
                Pins.PinWrite(Pins.normalTempOutPin, PinValue.Low);
                Pins.PinWrite(Pins.floorPumpOutPin, PinValue.Low);
                TelemetryDataClass.isNormalHeating = false;
            }
            else if (command == CommandNames.TURN_ON_HOTWATERPUMP && !TelemetryDataClass.isHomeInVacation)
            {
                Pins.PinWrite(Pins.waterOutPin, PinValue.High);
                ProcessCommand(CommandNames.TURN_ON_HEATING);
                TelemetryDataClass.isWaterHeatingOn = true;
            }
            else if (command == CommandNames.TURN_OFF_HOTWATERPUMP)
            {
                Pins.PinWrite(Pins.waterOutPin, PinValue.Low);
                TelemetryDataClass.isWaterHeatingOn = false;
            }
            else if (command == CommandNames.TURN_ON_HEATING && !TelemetryDataClass.isHomeInVacation)
            {
                Pins.PinWrite(Pins.heatOnOutPin, PinValue.High);
                TelemetryDataClass.isHeatingOn = true;
            }
            else if (command == CommandNames.TURN_OFF_HEATING)
            {
                while (Pins.IsRoomHeatingOn || Pins.IsWaterHeatingOn) ;
                Pins.PinWrite(Pins.heatOnOutPin, PinValue.Low);
                Pins.PinWrite(Pins.floorPumpOutPin, PinValue.Low);
                ProcessCommand(CommandNames.REDUCE_TEMP_COMMAND);
                ProcessCommand(CommandNames.TURN_OFF_HOTWATERPUMP);
                TelemetryDataClass.isHeatingOn = false;
            }
        }
    }
    public class Shelly
    {
        public static string OutsideLight = Environment.GetEnvironmentVariable("OutsideLightShellyIP");
        public static string PianoHeating = Environment.GetEnvironmentVariable("PianoHeatingShellyIP");
        public static string EntryLight = Environment.GetEnvironmentVariable("EntryLightsShellyIP");
        public static string BedroomHeating = Environment.GetEnvironmentVariable("BedroomHeatingShellyIP");
        public static string GarageLight = Environment.GetEnvironmentVariable("GarageLightShellyIP");

        public static async Task<bool> SetShellySwitch(bool turnOn, string ipAddress, string shellyName)
        {
            string TurnOnCommand = turnOn ? "on" : "off";
            bool ison = false;
            try
            {
                var http = new HttpClient();
                string url = $"http://{ipAddress}/relay/0?turn={TurnOnCommand}";
                HttpResponseMessage response = await http.GetAsync(url);
                var result = response.Content.ReadAsStringAsync();

                //deserialize all content
                var nps = JsonSerializer.Deserialize<JsonElement>(result.Result);
                ison = nps.GetProperty("ison").GetBoolean();

                if(shellyName == "OutsideLight" || shellyName == "GarageLight")
                    Console.WriteLine($"Shelly {shellyName} {(turnOn ? "turned on" : "turned off")} {METHOD.DateTimeTZ().DateTime:T}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Send command to Shelly exception {shellyName} {turnOn}: {e.Message}");
            }
            return ison;
        }
        public static async Task<bool> GetShellyState(string ipAddress)
        {
            bool isOn = false;
            try
            {
                var http = new HttpClient();
                string url = $"http://{ipAddress}/relay/0";
                HttpResponseMessage response = await http.GetAsync(url);
                var result = response.Content.ReadAsStringAsync();
                //deserialize all content
                var nps = JsonSerializer.Deserialize<JsonElement>(result.Result);
                isOn = nps.GetProperty("ison").GetBoolean();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Shelly check exception {ipAddress}: {e.Message}");
            }
            return isOn;
        }
    }
}
