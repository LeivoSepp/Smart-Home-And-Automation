using HomeModule.Measuring;
using System;
using System.Threading.Tasks;
using HomeModule.Azure;
using System.Linq;
using HomeModule.Models;
using HomeModule.Raspberry;
using System.Device.Gpio;
using System.Text.Json;
using System.IO;
using HomeModule.Helpers;

namespace HomeModule.Schedulers
{
    class HomeTemperature
    {
        internal const string BEDROOM = "Bedroom";
        internal const string LIVING = "Living";
        internal const string PIANO = "Piano";
        internal const string OFFICE = "Office";
        internal const string SAUNA = "Sauna";
        internal const string WARM_WATER = "Warm water";
        internal const string INFLOW_MAIN = "Main inflow";
        internal const string RETURN_MAIN = "Main return";
        internal const string INFLOW_1_FLOOR = "1. Floor Inflow";
        internal const string RETURN_1_FLOOR = "1. Floor Return";
        internal const string INFLOW_2_FLOOR = "2. Floor Inflow";
        internal const string RETURN_2_FLOOR = "2. Floor Return";

        public static SensorReadings ListOfAllSensors;
        public async void ReadTemperature()
        {
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
            while (true)
            {
                var filename = Methods.GetFilePath(CONSTANT.FILENAME_ROOM_TEMPERATURES);
                if (File.Exists(filename))
                {
                    var dataFromFile = await Methods.OpenExistingFile(filename);
                    SensorReadings SetRoomTemps = JsonSerializer.Deserialize<SensorReadings>(dataFromFile);
                    SetTemperatures(SetRoomTemps);
                }
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
                await Shelly.ShellySwitch(isPianoHeatingOn, Shelly.PianoHeating);

                //manage Bedroom heating actuator
                bool isBedroomHeatingOn = ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == BEDROOM).isHeatingRequired;
                await Shelly.ShellySwitch(isBedroomHeatingOn, Shelly.BedroomHeating);

                //manage sauna temperature
                if (ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == SAUNA).isHeatingRequired && TelemetryDataClass.isSaunaOn)
                    Pins.PinWrite(Pins.saunaHeatOutPin, PinValue.Low);
                else
                    Pins.PinWrite(Pins.saunaHeatOutPin, PinValue.High);
                //if sauna extremely hot, then turn off
                if (ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == SAUNA).Temperature > CONSTANT.EXTREME_SAUNA_TEMP) _receiveData.ProcessCommand(CommandNames.TURN_OFF_SAUNA);

                //if all rooms has achieved their target temperature then turn system off
                if (ListOfAllSensors.Temperatures.Where(x => x.isRoom).All(x => !x.isHeatingRequired)) _receiveData.ProcessCommand(CommandNames.TURN_OFF_HEATING);

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
                await Task.Delay(TimeSpan.FromMinutes(1)); //check temperatures every minute
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
        public static void SetTemperatures(SensorReadings sensorReadings)
        {
            foreach (var s in ListOfAllSensors.Temperatures)
            {
                //update room temperatureSET 
                foreach (var n in sensorReadings.Temperatures)
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
