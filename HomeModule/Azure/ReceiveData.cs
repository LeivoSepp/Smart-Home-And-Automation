﻿using Microsoft.Azure.Devices.Client;
using System.Text;
using HomeModule.Schedulers;
using HomeModule.Raspberry;
using System;
using System.Device.Gpio;
using Microsoft.Azure.Devices.Shared;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using HomeModule.Netatmo;
using System.Net.Http;
using Newtonsoft.Json;
using HomeModule.Models;
using System.IO;

namespace HomeModule.Azure
{
    class ReceiveData
    {
        private SendTelemetryData _sendDataAzure;
        private readonly FileOperations fileOperations = new FileOperations();
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task<MethodResponse> HomeEdgeDeviceManagement(MethodRequest methodRequest, object userContext)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            ReceiveData receiveData = new ReceiveData();
            List<string> command = new List<string>();
            try
            {
                var HomeCommands = JObject.Parse(methodRequest.DataAsJson);
                if (HomeCommands["Ventilation"] != null && ((bool)HomeCommands["Ventilation"] == !TelemetryDataClass.isVentilationOn))
                {
                    string cmd = (bool)HomeCommands["Ventilation"] ? CommandNames.OPEN_VENT : CommandNames.CLOSE_VENT;
                    command.Add(cmd);
                }
                if (HomeCommands["Sauna"] != null && ((bool)HomeCommands["Sauna"] == !TelemetryDataClass.isSaunaOn))
                {
                    string cmd = (bool)HomeCommands["Sauna"] ? CommandNames.TURN_ON_SAUNA : CommandNames.TURN_OFF_SAUNA;
                    command.Add(cmd);
                }
                if (HomeCommands["Water"] != null && ((bool)HomeCommands["Water"] == !TelemetryDataClass.isWaterHeatingOn))
                {
                    string cmd = (bool)HomeCommands["Water"] ? CommandNames.TURN_ON_HOTWATERPUMP : CommandNames.TURN_OFF_HOTWATERPUMP;
                    command.Add(cmd);
                }
                if (HomeCommands["Heating"] != null && ((bool)HomeCommands["Heating"] == !TelemetryDataClass.isHeatingOn))
                {
                    string cmd = (bool)HomeCommands["Heating"] ? CommandNames.TURN_ON_HEATING : CommandNames.TURN_OFF_HEATING;
                    command.Add(cmd);
                }
                if (HomeCommands["NormalTemp"] != null && ((bool)HomeCommands["NormalTemp"] == !TelemetryDataClass.isNormalHeating))
                {
                    string cmd = (bool)HomeCommands["NormalTemp"] ? CommandNames.NORMAL_TEMP_COMMAND : CommandNames.REDUCE_TEMP_COMMAND;
                    command.Add(cmd);
                }
                if (HomeCommands["Vacation"] != null && ((bool)HomeCommands["Vacation"] == !TelemetryDataClass.isHomeInVacation))
                {
                    string cmd = (bool)HomeCommands["Vacation"] ? CommandNames.TURN_ON_VACATION : CommandNames.TURN_OFF_VACATION;
                    command.Add(cmd);
                }
                if (HomeCommands["Security"] != null && ((bool)HomeCommands["Security"] == !TelemetryDataClass.isHomeSecured))
                {
                    string cmd = (bool)HomeCommands["Security"] ? CommandNames.TURN_ON_SECURITY : CommandNames.TURN_OFF_SECURITY;
                    command.Add(cmd);
                }
                if (HomeCommands["isOutsideLightsOn"] != null && ((bool)HomeCommands["isOutsideLightsOn"] == !TelemetryDataClass.isOutsideLightsOn))
                {
                    bool setLightsOn = (bool)HomeCommands["isOutsideLightsOn"];
                    SomeoneAtHome.SetOutsideLightsOn(setLightsOn, setLightsOn, !setLightsOn); //forcing to turn lights on or off
                }
                if (HomeCommands["isGarageLightsOn"] != null && ((bool)HomeCommands["isGarageLightsOn"] == !TelemetryDataClass.isGarageLightsOn))
                {
                    bool isLightsOn = (bool)HomeCommands["isGarageLightsOn"];
                    SomeoneAtHome.SetGarageLightsOn(isLightsOn);
                }
                if (HomeCommands["Temperatures"] != null)
                {
                    //this data is coming from PowerApps
                    SensorReadings SetRoomTemperatures = new SensorReadings
                    {
                        Temperatures = JsonConvert.DeserializeObject<List<SensorReading>>(HomeCommands["Temperatures"].ToString())
                    };
                    var jsonString = JsonConvert.SerializeObject(SetRoomTemperatures);
                    var filename = fileOperations.GetFilePath("temperatureSET");
                    await fileOperations.SaveStringToLocalFile(filename, jsonString);
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
            reportedProperties["Temperatures"] = HomeTemperature.ListOfAllSensors.Temperatures;

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
        static DateTime dateTimeVentilation = Program.DateTimeTZ().DateTime;
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
                var filename = fileOperations.GetFilePath("SaunaStartedTime");
                if (TelemetryDataClass.SaunaStartedTime == DateTime.MinValue) //if sauna hasnt been started before
                {
                    DateTime SaunaStartedTime = Program.DateTimeTZ().DateTime;
                    await fileOperations.SaveStringToLocalFile(filename, SaunaStartedTime.ToString("dd.MM.yyyy HH:mm"));
                    TelemetryDataClass.SaunaStartedTime = SaunaStartedTime;
                }
            }
            else if (command == CommandNames.TURN_OFF_SAUNA)
            {
                var filename = fileOperations.GetFilePath("SaunaStartedTime");
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
            {                TelemetryDataClass.isHomeInVacation = false;
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
                dateTimeVentilation = Program.DateTimeTZ().DateTime;
            }
            else if (command == CommandNames.CLOSE_VENT)
            {
                Pins.PinWrite(Pins.ventOutPin, PinValue.Low);
                ManualVentLogic.VENT_ON = false;
                TelemetryDataClass.VentilationInMinutes = (int)(Program.DateTimeTZ().DateTime - dateTimeVentilation).TotalMinutes;
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
        public static string PianoLoungeHeating = Environment.GetEnvironmentVariable("PianoHeatingShellyIP");
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
                var nps = JsonConvert.DeserializeObject<JObject>(result.Result);
                ison = (bool)nps["ison"];
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
                var nps = JsonConvert.DeserializeObject<JObject>(result.Result);
                bool ison = (bool)nps["ison"];
                TelemetryDataClass.isOutsideLightsOn = ison;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Check Shelly on startup exception {ipAddress}: {e.Message}");
            }
        }

    }
}
