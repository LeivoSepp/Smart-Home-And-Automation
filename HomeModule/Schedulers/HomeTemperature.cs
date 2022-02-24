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

        private readonly HT16K33 driver = new HT16K33(new byte[] { 0x70, 0x73 }, HT16K33.Rotate.D180); //LED matrix
        public async void ReadTemperature()
        {
            bool isReadTemperatureStarted = false;
            var _receiveData = new ReceiveData();
            var _sensorsClient = new RinsenOneWireClient();
            var _sendListData = new SendDataAzure();
            var Methods = new METHOD();

            //this bool and int are used to run hotwater pump at least 3 minutes
            bool isThreeMinutesPassed = true;
            int countMinutesToThree = 0;

            //currentSumOfTempDeltas is some big number to determine temperature changes at startup to generate send data
            double SumOfTemperatureDeltas = 10;
            //initiate the list with the temps and names
            ListOfAllSensors = await _sensorsClient.ReadSensors();
            //fill out LastTemperatures and initial Temperature trend which is initially always TRUE
            ListOfAllSensors = UpdateSensorsTrendAndLastTemp(ListOfAllSensors, ListOfAllSensors);

            //Start LED matrix
            LedMatrixAsync();

            //save the room temperatures into Raspberry to have this data after reboot or app update
            var filename = Methods.GetFilePath(CONSTANT.FILENAME_ROOM_TEMPERATURES);
            if (File.Exists(filename))
            {
                var dataFromFile = await Methods.OpenExistingFile(filename);
                List<SensorReading> SetRoomTemps = JsonSerializer.Deserialize<List<SensorReading>>(dataFromFile);
                SetTemperatures(SetRoomTemps);
            }

            //run in every minute to check temperatures
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

                //manage Piano heating actuator - this works through Shelly
                bool isPianoHeatingOn = ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == PIANO).isHeatingRequired;
                await Shelly.SetShellySwitch(isPianoHeatingOn, Shelly.PianoHeating, nameof(Shelly.PianoHeating));

                //manage Bedroom heating actuator - this works through Shelly
                bool isBedroomHeatingOn = ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == BEDROOM).isHeatingRequired;
                await Shelly.SetShellySwitch(isBedroomHeatingOn, Shelly.BedroomHeating, nameof(Shelly.BedroomHeating));

                //manage sauna temperature
                var sauna = ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == SAUNA);
                if (sauna.isHeatingRequired && TelemetryDataClass.isSaunaOn)
                    Pins.PinWrite(Pins.saunaHeatOutPin, PinValue.Low);
                else
                    Pins.PinWrite(Pins.saunaHeatOutPin, PinValue.High);

                //if sauna extremely hot, then turn off
                if (sauna.Temperature > CONSTANT.EXTREME_SAUNA_TEMP)
                    _receiveData.ProcessCommand(CommandNames.TURN_OFF_SAUNA);

                //if there is time to make hotwater or hot water turned on manually and
                //hot water temp is low or pump hasnt been run 3 minutes then run hotwater pump
                double waterTemperature = ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == WARM_WATER).Temperature;
                bool isWaterTempVeryLow = waterTemperature < CONSTANT.MIN_WATER_TEMP;
                if ((TelemetryDataClass.IsHotWaterTime || TelemetryDataClass.isWaterHeatingOn) && (isWaterTempVeryLow || !isThreeMinutesPassed) && !TelemetryDataClass.isHomeInVacation)
                {
                    if (countMinutesToThree < 3)
                    {
                        countMinutesToThree++;
                        isThreeMinutesPassed = false;
                        //turn on hotwater pump only if it wasnt tuned on
                        if (!TelemetryDataClass.isWaterHeatingOn) _receiveData.ProcessCommand(CommandNames.TURN_ON_HOTWATERPUMP);
                        Console.WriteLine($"{countMinutesToThree}, {waterTemperature}");
                    }
                    else
                    {
                        countMinutesToThree = 0;
                        isThreeMinutesPassed = true;
                        //enable waterheating if water temperature still low after 3 minutes
                        TelemetryDataClass.IsHotWaterRequired = isWaterTempVeryLow;
                    }
                }
                else
                {
                    TelemetryDataClass.IsHotWaterRequired = false;
                }

                //turn hotwaterpump on if the waterheating is working
                if (!TelemetryDataClass.isWaterHeatingOn && Pins.IsWaterHeatingOn) 
                    _receiveData.ProcessCommand(CommandNames.TURN_ON_HOTWATERPUMP);

                //turn on heating is there is really need for hot water, this is checked during 5 minutes with hotwter pump
                if (!TelemetryDataClass.isHeatingOn && TelemetryDataClass.IsHotWaterRequired) 
                    _receiveData.ProcessCommand(CommandNames.TURN_ON_HEATING);

                //if it is hetaing time or heating switched on-off manually and some room requires heating then heating required
                if ((TelemetryDataClass.IsHeatingTime || TelemetryDataClass.IsHeatingTurnedOnManually) && !TelemetryDataClass.IsHeatingTurnedOffManually && !ListOfAllSensors.Temperatures.Where(x => x.isRoom).All(x => !x.isHeatingRequired))
                    TelemetryDataClass.IsHeatingRequired = true;
                else
                    TelemetryDataClass.IsHeatingRequired = false;

                //normal heating will be turned on if there is demand for heating - only during no-vacation
                if (TelemetryDataClass.IsHeatingRequired && !TelemetryDataClass.isNormalHeating && !TelemetryDataClass.isHomeInVacation)
                    _receiveData.ProcessCommand(CommandNames.NORMAL_TEMP_COMMAND);

                //reduced heating will be turned on if there is demand for heating - only during vacation time
                if (TelemetryDataClass.IsHeatingRequired && !TelemetryDataClass.isHeatingOn && TelemetryDataClass.isHomeInVacation)
                    _receiveData.ProcessCommand(CommandNames.TURN_ON_HEATING);

                //turn off hotwater pump if pump itself is running but waterheating is already stopped
                if (TelemetryDataClass.isWaterHeatingOn && !Pins.IsWaterHeatingOn && !TelemetryDataClass.IsHotWaterRequired && isThreeMinutesPassed)
                    _receiveData.ProcessCommand(CommandNames.TURN_OFF_HOTWATERPUMP);

                //turn off heating (EVU_STOP) if there is no demand for heating and hot water 
                if (!TelemetryDataClass.IsHeatingRequired && !TelemetryDataClass.IsHotWaterRequired && TelemetryDataClass.isHeatingOn)
                    _receiveData.ProcessCommand(CommandNames.TURN_OFF_HEATING);

                //if all room temperatures together has changed more that 4 degrees then send it out to CosmosDB
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
                    Console.WriteLine($"All Temperatures: {METHOD.DateTimeTZ().DateTime} {Pins.FlowTemperatures()}\n");
                    SumOfTemperatureDeltas = 0; //resetting to start summing up again
                }
                //message for debugging
                if (!isReadTemperatureStarted)
                {
                    Console.WriteLine($"ReadTemperature() started");
                    isReadTemperatureStarted = true;
                }
                //check temperatures every 60 sec
                await Task.Delay(TimeSpan.FromSeconds(60)); 
            }
        }
        //scroll the sauna information in Led-matrix
        public async void LedMatrixAsync()
        {
            LED8x8Matrix matrix = new LED8x8Matrix(driver);
            while (true)
            {
                //get the sauna temp
                int SaunaTemp = Convert.ToInt32(ListOfAllSensors.Temperatures.FirstOrDefault(x => x.RoomName == SAUNA).Temperature);
                string SaunaStarted = "";
                if (TelemetryDataClass.isSaunaOn)
                    SaunaStarted = $"   Alates {TelemetryDataClass.SaunaStartedTime:HH:mm}";
                string message = $"{SaunaStarted}  saun {(TelemetryDataClass.isSaunaOn ? "sees" : "off")}  {SaunaTemp}'";
                matrix.ScrollStringInFromRight(message, 70);
                await Task.Delay(TimeSpan.FromSeconds(2)); //scroll every 2 sec
            }
        }

        private SensorReadings UpdateSensorsTrendAndLastTemp(SensorReadings listOfRooms, SensorReadings sensorReadings)
        {
            //reduce target room temperatures by 3 degrees
            int reducedTemp = TelemetryDataClass.isHomeInVacation ? CONSTANT.REDUCED_TEMP : 0;
            foreach (var s in listOfRooms.Temperatures)
            {
                //update room temperature trends and values
                foreach (var n in sensorReadings.Temperatures)
                {
                    if (s.RoomName == n.RoomName)
                    {
                        //trend will be calculated over two measuring cycle (2 minutes)
                        s.isHeatingRequired = s.TemperatureSET - reducedTemp > n.Temperature;
                        s.isTrendIncreases = n.Temperature > s.LastTemperature;
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
