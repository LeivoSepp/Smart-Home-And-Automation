using HomeModule.Measuring;
using System;
using System.Threading.Tasks;
using HomeModule.Azure;
using System.Linq;
using HomeModule.Models;
using HomeModule.Raspberry;
using System.Device.Gpio;
using Newtonsoft.Json;
using System.IO;

namespace HomeModule.Schedulers
{
    class HomeTemperature
    {
        internal const string BEDROOM = "Bedroom";
        internal const string LIVING_ROOM = "Living Room";
        internal const string PIANO_LOUNGE = "Piano Lounge";
        internal const string HOME_OFFICE = "Home Office";
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
            var fileOperations = new FileOperations();
            //currentSumOfTempDeltas is some bigger number than the delta (0,5) is used to determine temperature changes
            double SumOfTemperatureDeltas = 10;
            //initiate the list with the temps and names
            ListOfAllSensors = await _sensorsClient.ReadSensors();
            //fill out LastTemperatures and initial Temperature trend which is initially always TRUE
            ListOfAllSensors = UpdateSensorsTrendAndLastTemp(ListOfAllSensors, ListOfAllSensors);
            while (true)
            {
                var filename = fileOperations.GetFilePath("temperatureSET");
                if (File.Exists(filename))
                {
                    var dataFromFile = await fileOperations.OpenExistingFile(filename);
                    SensorReadings SetRoomTemps = JsonConvert.DeserializeObject<SensorReadings>(dataFromFile);
                    SetTemperatures(SetRoomTemps);
                }
                //get a new sensor readings and then update
                SensorReadings sensorReadings = await _sensorsClient.ReadSensors();
                ListOfAllSensors = UpdateSensorsTrendAndLastTemp(ListOfAllSensors, sensorReadings);

                //summing all the room temperature changes together add new deltas until it bigger that 4 degrees
                SumOfTemperatureDeltas += ListOfAllSensors.Temperatures.Where(x => x.isRoom).Sum(x => Math.Abs(x.Temperature - x.LastTemperature));

                //turn on-off room heating actuators
                if (ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == LIVING_ROOM).isHeatingRequired)
                    Pins.PinWrite(Pins.livingRoomHeatControlOut, PinValue.High);
                else
                    Pins.PinWrite(Pins.livingRoomHeatControlOut, PinValue.Low);
                if (ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == HOME_OFFICE).isHeatingRequired)
                    Pins.PinWrite(Pins.homeOfficeHeatControlOut, PinValue.High);
                else
                    Pins.PinWrite(Pins.homeOfficeHeatControlOut, PinValue.Low);

                bool isShellyOn = ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == PIANO_LOUNGE).isHeatingRequired;
                await Shelly.ShellySwitch(isShellyOn, Shelly.PianoLoungeHeating);

                //manage sauna temperature
                if (ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == SAUNA).isHeatingRequired && TelemetryDataClass.isSaunaOn)
                    Pins.PinWrite(Pins.saunaHeatOutPin, PinValue.Low);
                else
                    Pins.PinWrite(Pins.saunaHeatOutPin, PinValue.High);
                //if sauna extremely hot, then turn off
                if (ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == SAUNA).Temperature > 110) _receiveData.ProcessCommand(CommandNames.TURN_OFF_SAUNA);

                //if all rooms has achieved their target temperature then turn system off
                if (ListOfAllSensors.Temperatures.Where(x => x.isRoom).All(x => !x.isHeatingRequired)) _receiveData.ProcessCommand(CommandNames.TURN_OFF_HEATING);

                //if all room temperatures together has changed more that 3 degrees then send it out to database
                if (Math.Abs(SumOfTemperatureDeltas) > 4)
                {
                    TelemetryDataClass.SourceInfo = $"Room temp changed {SumOfTemperatureDeltas:0.0}.";
                    var monitorData = new
                    {
                        DeviceID = "RoomTemperatures",
                        UtcOffset = Program.DateTimeTZ().Offset.Hours,
                        DateAndTime = Program.DateTimeTZ().DateTime,
                        time = Program.DateTimeTZ().ToString("HH:mm"),
                        TelemetryDataClass.SourceInfo,
                        ListOfAllSensors.Temperatures
                    };
                    await _sendListData.PipeMessage(monitorData, Program.IoTHubModuleClient, TelemetryDataClass.SourceInfo);
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
