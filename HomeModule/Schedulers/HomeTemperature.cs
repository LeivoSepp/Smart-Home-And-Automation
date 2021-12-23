using HomeModule.Azure;
using HomeModule.Helpers;
using HomeModule.Measuring;
using HomeModule.Models;
using HomeModule.Raspberry;
using System;
using System.Collections.Generic;
using System.Device.Gpio;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RobootikaCOM.NetCore.Devices;


namespace HomeModule.Schedulers
{
    class HomeTemperature
    {
        internal const string LIVING = "Living";
        internal const string OFFICE = "Office";
        internal const string PIANO = "Piano";
        internal const string BEDROOM = "Bedroom";
        internal const string SAUNA = "Sauna";
        internal const string WARM_WATER = "Warm water";
        internal const string INFLOW_MAIN = "Main inflow";
        internal const string RETURN_MAIN = "Main return";
        internal const string INFLOW_1_FLOOR = "1. Floor Inflow";
        internal const string RETURN_1_FLOOR = "1. Floor Return";
        internal const string INFLOW_2_FLOOR = "2. Floor Inflow";
        internal const string RETURN_2_FLOOR = "2. Floor Return";

        public static SensorReadings ListOfAllSensors;

        private readonly HT16K33 driver = new HT16K33(new byte[] { 0x71, 0x73 }, HT16K33.Rotate.D180); //LED matrix
        public async void ReadTemperature()
        {
            bool isReadTemperatureStarted = false;
            var _receiveData = new ReceiveData();
            var _sensorsClient = new RinsenOneWireClient();
            var _sendListData = new SendDataAzure();
            var Methods = new METHOD();
            //currentSumOfTempDeltas is some bigger number than the delta (0,5) is used to determine temperature changes
            double SumOfTemperatureDeltas = 10;
            //initiate the list with the temps and names
            ListOfAllSensors = await _sensorsClient.ReadSensors();
            //fill out LastTemperatures and initial Temperature trend which is initially always TRUE
            ListOfAllSensors = UpdateSensorsTrendAndLastTemp(ListOfAllSensors, ListOfAllSensors);

            //Start LED matrix
            LedMatrixAsync();

            var filename = Methods.GetFilePath(CONSTANT.FILENAME_ROOM_TEMPERATURES);
            if (File.Exists(filename))
            {
                var dataFromFile = await Methods.OpenExistingFile(filename);
                List<SensorReading> SetRoomTemps = JsonSerializer.Deserialize<List<SensorReading>>(dataFromFile);
                SetTemperatures(SetRoomTemps);
            }
            while (true)
            {
                //get a new sensor readings and then update
                SensorReadings sensorReadings = await _sensorsClient.ReadSensors();
                ListOfAllSensors = UpdateSensorsTrendAndLastTemp(ListOfAllSensors, sensorReadings);

                //summing all the room temperature changes together add new deltas until it bigger that 4 degrees
                SumOfTemperatureDeltas += ListOfAllSensors.Temperatures.Where(x => x.isRoom).Sum(x => Math.Abs(x.Temperature - x.LastTemperature));

                //manage Livingroom heating actuator
                if (ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == LIVING).isHeatingRequired)
                    Pins.PinWrite(Pins.livingRoomHeatControlOut, PinValue.High);
                else
                    Pins.PinWrite(Pins.livingRoomHeatControlOut, PinValue.Low);

                //manage Office heating actuator
                if (ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == OFFICE).isHeatingRequired)
                    Pins.PinWrite(Pins.homeOfficeHeatControlOut, PinValue.High);
                else
                    Pins.PinWrite(Pins.homeOfficeHeatControlOut, PinValue.Low);

                //manage Piano heating actuator
                bool isPianoHeatingOn = ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == PIANO).isHeatingRequired;
                await Shelly.SetShellySwitch(isPianoHeatingOn, Shelly.PianoHeating, nameof(Shelly.PianoHeating));

                //manage Bedroom heating actuator
                bool isBedroomHeatingOn = ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == BEDROOM).isHeatingRequired;
                await Shelly.SetShellySwitch(isBedroomHeatingOn, Shelly.BedroomHeating, nameof(Shelly.BedroomHeating));

                //manage sauna temperature
                if (ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == SAUNA).isHeatingRequired && TelemetryDataClass.isSaunaOn)
                    Pins.PinWrite(Pins.saunaHeatOutPin, PinValue.Low);
                else
                    Pins.PinWrite(Pins.saunaHeatOutPin, PinValue.High);

                //if sauna extremely hot, then turn off
                if (ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == SAUNA).Temperature > CONSTANT.EXTREME_SAUNA_TEMP)
                    _receiveData.ProcessCommand(CommandNames.TURN_OFF_SAUNA);

                //if hotwater time and hot water is below 40 then turn on heating and set "heatingRequired"
                if (TelemetryDataClass.isWaterHeatingOn && ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == WARM_WATER).Temperature < CONSTANT.MIN_WATER_TEMP)
                    TelemetryDataClass.isHotWaterRequired = true;
                else
                    TelemetryDataClass.isHotWaterRequired = false;

                //if all rooms has achieved their target temperature then turn system off
                if (TelemetryDataClass.isHeatingTime && !ListOfAllSensors.Temperatures.Where(x => x.isRoom).All(x => !x.isHeatingRequired))
                    TelemetryDataClass.isHeatingRequired = true;
                else
                    TelemetryDataClass.isHeatingRequired = false;

                //turn off heating if there is no demand for heating and hot water 
                if (!TelemetryDataClass.isHeatingRequired && !TelemetryDataClass.isHotWaterRequired && TelemetryDataClass.isHeatingOn)
                    _receiveData.ProcessCommand(CommandNames.TURN_OFF_HEATING);
                
                //reduced heating will be turned on if there is demand for hot water 
                if (TelemetryDataClass.isHotWaterRequired && !TelemetryDataClass.isHeatingOn)
                    _receiveData.ProcessCommand(CommandNames.TURN_ON_HEATING);

                //normal heating will be turned on if there is demand for hot water or heating
                if (TelemetryDataClass.isHeatingRequired && !TelemetryDataClass.isHeatingOn)
                    _receiveData.ProcessCommand(CommandNames.NORMAL_TEMP_COMMAND);

                //if all room temperatures together has changed more that 3 degrees then send it out to database
                if (Math.Abs(SumOfTemperatureDeltas) > 4)
                {
                    TelemetryDataClass.SourceInfo = $"Room temp changed {SumOfTemperatureDeltas:0.0}";
                    var monitorData = new
                    {
                        DeviceID = "RoomTemperatures",
                        UtcOffset = METHOD.DateTimeTZ().Offset.Hours,
                        DateAndTime = METHOD.DateTimeTZ().DateTime,
                        time = METHOD.DateTimeTZ().ToString("HH:mm"),
                        TelemetryDataClass.SourceInfo,
                        ListOfAllSensors.Temperatures
                    };
                    await _sendListData.PipeMessage(monitorData, Program.IoTHubModuleClient, TelemetryDataClass.SourceInfo, "output");
                    SumOfTemperatureDeltas = 0; //resetting to start summing up again
                }
                //started message for debugging
                if (!isReadTemperatureStarted)
                {
                    Console.WriteLine($"ReadTemperature() started");
                    isReadTemperatureStarted = true;
                }

                await Task.Delay(TimeSpan.FromMinutes(1)); //check temperatures every minute
            }
        }
        public async void LedMatrixAsync()
        {
            LED8x8Matrix matrix = new LED8x8Matrix(driver);
            while (true)
            {
                int SaunaTemp = Convert.ToInt32(ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == SAUNA).Temperature);
                string SaunaStarted = "";
                if (TelemetryDataClass.isSaunaOn)
                    SaunaStarted = $"   Alates {TelemetryDataClass.SaunaStartedTime:HH:mm}";
                string message = $"{SaunaStarted}  saun {(TelemetryDataClass.isSaunaOn ? "sees" : "off" )}  {SaunaTemp}'";
                matrix.ScrollStringInFromRight(message, 70);
                await Task.Delay(TimeSpan.FromSeconds(2)); //scroll every 2 sec
            }
        }

        private SensorReadings UpdateSensorsTrendAndLastTemp(SensorReadings listOfRooms, SensorReadings sensorReadings)
        {
            foreach (var s in listOfRooms.Temperatures)
            {
                //update room temperature trends and values
                foreach (var n in sensorReadings.Temperatures)
                {
                    if (s.RoomName == n.RoomName)
                    {
                        s.isHeatingRequired = (s.TemperatureSET - n.Temperature) > 0;
                        s.isTrendIncreases = (n.Temperature - s.Temperature) > 0;
                        s.LastTemperature = s.Temperature;
                        s.Temperature = n.Temperature;
                        break;
                    }
                }
            }
            return listOfRooms;
        }
        public static void SetTemperatures(List<SensorReading> temperatures)
        {
            foreach (var s in ListOfAllSensors.Temperatures)
            {
                //update room temperatureSET 
                foreach (var n in temperatures)
                {
                    if (s.RoomName == n.RoomName)
                    {
                        s.TemperatureSET = n.TemperatureSET;
                        break;
                    }
                }
            }
        }
    }
}
