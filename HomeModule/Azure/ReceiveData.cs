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
                var HomeCommands = JsonDocument.Parse(methodRequest.DataAsJson);
                if (HomeCommands.RootElement.TryGetProperty("Ventilation", out JsonElement ventilation) && ventilation.GetBoolean() == !TelemetryDataClass.isVentilationOn)
                {
                    string cmd = ventilation.GetBoolean() ? CommandNames.OPEN_VENT : CommandNames.CLOSE_VENT;
                    command.Add(cmd);
                }
                if (HomeCommands.RootElement.TryGetProperty("Sauna", out JsonElement sauna) && (sauna.GetBoolean() == !TelemetryDataClass.isSaunaOn))
                {
                    string cmd = sauna.GetBoolean() ? CommandNames.TURN_ON_SAUNA : CommandNames.TURN_OFF_SAUNA;
                    command.Add(cmd);
                }
                if (HomeCommands.RootElement.TryGetProperty("Water", out JsonElement water) && water.GetBoolean() == !TelemetryDataClass.isWaterHeatingOn)
                {
                    string cmd = water.GetBoolean() ? CommandNames.TURN_ON_HOTWATERPUMP : CommandNames.TURN_OFF_HOTWATERPUMP;
                    command.Add(cmd);
                }
                if (HomeCommands.RootElement.TryGetProperty("Heating", out JsonElement heating) && heating.GetBoolean() == !TelemetryDataClass.isHeatingOn)
                {
                    string cmd = heating.GetBoolean() ? CommandNames.TURN_ON_HEATING : CommandNames.TURN_OFF_HEATING;
                    command.Add(cmd);
                }
                if (HomeCommands.RootElement.TryGetProperty("NormalTemp", out JsonElement normalTemp) && normalTemp.GetBoolean() == !TelemetryDataClass.isNormalHeating)
                {
                    string cmd = normalTemp.GetBoolean() ? CommandNames.NORMAL_TEMP_COMMAND : CommandNames.REDUCE_TEMP_COMMAND;
                    command.Add(cmd);
                }
                if (HomeCommands.RootElement.TryGetProperty("Vacation", out JsonElement vacation) && vacation.GetBoolean() == !TelemetryDataClass.isHomeInVacation)
                {
                    string cmd = vacation.GetBoolean() ? CommandNames.TURN_ON_VACATION : CommandNames.TURN_OFF_VACATION;
                    command.Add(cmd);
                }
                if (HomeCommands.RootElement.TryGetProperty("Security", out JsonElement security) && security.GetBoolean() == !TelemetryDataClass.isHomeSecured)
                {
                    string cmd = security.GetBoolean() ? CommandNames.TURN_ON_SECURITY : CommandNames.TURN_OFF_SECURITY;
                    command.Add(cmd);
                }
                if (HomeCommands.RootElement.TryGetProperty("isOutsideLightsOn", out JsonElement isoutsideLightsOn) && isoutsideLightsOn.GetBoolean() == !TelemetryDataClass.isOutsideLightsOn)
                {
                    bool setLightsOn = isoutsideLightsOn.GetBoolean();
                    SomeoneAtHome.SetOutsideLightsOn(setLightsOn, setLightsOn); //forcing to turn lights on or off
                }
                if (HomeCommands.RootElement.TryGetProperty("isGarageLightsOn", out JsonElement isgarageLightsOn) && isgarageLightsOn.GetBoolean() == !TelemetryDataClass.isGarageLightsOn)
                {
                    bool isLightsOn = isgarageLightsOn.GetBoolean();
                    SomeoneAtHome.SetGarageLightsOn(isLightsOn);
                }
                if (HomeCommands.RootElement.TryGetProperty("Temperatures", out JsonElement temperatures))
                {
                    //this data is coming from PowerApps
                    SensorReadings SetRoomTemperatures = new SensorReadings
                    {
                        Temperatures = JsonSerializer.Deserialize<List<SensorReading>>(temperatures.GetRawText())
                    };
                    var jsonString = JsonSerializer.Serialize(SetRoomTemperatures);
                    var filename = Methods.GetFilePath(CONSTANT.FILENAME_ROOM_TEMPERATURES);
                    await Methods.SaveStringToLocalFile(filename, jsonString);
                    HomeTemperature.SetTemperatures(SetRoomTemperatures);
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
            //This data goes back directly to PowerApps
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
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task<MethodResponse> HomeEdgeWiFiDevices(MethodRequest methodRequest, object userContext)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            try
            {
                var ParsedWiFiDevices = JsonDocument.Parse(methodRequest.DataAsJson);
                JsonElement jsonElement = ParsedWiFiDevices.RootElement;
                var device = JsonSerializer.Deserialize<Localdevice>(jsonElement.GetRawText());
                WiFiProbes.WiFiDevicesFromPowerApps.Add(new Localdevice()
                {
                    ActiveDuration = device.ActiveDuration,
                    DeviceName = device.DeviceName,
                    DeviceOwner = device.DeviceOwner,
                    DeviceType = device.DeviceType,
                    MacAddress = device.MacAddress
                });
            }
            catch (Exception e)
            {
                Console.WriteLine("WiFI devices: " + e.ToString());
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
            else if (command == CommandNames.TURN_ON_GARAGE_LIGHT)
            {
                TelemetryDataClass.isGarageLightsOn = true;
            }
            else if (command == CommandNames.TURN_OFF_GARAGE_LIGHT)
            {
                TelemetryDataClass.isGarageLightsOn = false;
            }
            else if (command == CommandNames.TURN_ON_OUTSIDE_LIGHT)
            {
                TelemetryDataClass.isOutsideLightsOn = true;
                await Shelly.ShellySwitch(true, Shelly.OutsideLight);
            }
            else if (command == CommandNames.TURN_OFF_OUTSIDE_LIGHT)
            {
                TelemetryDataClass.isOutsideLightsOn = false;
                await Shelly.ShellySwitch(false, Shelly.OutsideLight);
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
            }
            else if (command == CommandNames.TURN_OFF_VACATION)
            {
                TelemetryDataClass.isHomeInVacation = false;
            }
            else if (command == CommandNames.TURN_ON_SECURITY)
            {
                TelemetryDataClass.isHomeSecured = true;
            }
            else if (command == CommandNames.TURN_OFF_SECURITY)
            {
                TelemetryDataClass.isHomeSecured = false;
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
        public static async Task<bool> ShellySwitch(bool turnOn, string ipAddress)
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
            }
            catch (Exception e)
            {
                Console.WriteLine($"Send command to Shelly exception {turnOn} {ipAddress}: {e.Message}");
            }
            return ison;
        }
        public static async void CheckOutsideLightsOnStartup(string ipAddress)
        {
            try
            {
                var http = new HttpClient();
                string url = $"http://{ipAddress}/relay/0";
                HttpResponseMessage response = await http.GetAsync(url);
                var result = response.Content.ReadAsStringAsync();

                //deserialize all content
                var nps = JsonSerializer.Deserialize<JsonElement>(result.Result);
                bool ison = nps.GetProperty("ison").GetBoolean();
                TelemetryDataClass.isOutsideLightsOn = ison;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Check Shelly on startup exception {ipAddress}: {e.Message}");
            }
        }

    }
}
