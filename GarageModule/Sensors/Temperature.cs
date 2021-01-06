﻿using GarageModule.Azure;
using System;
using System.Threading.Tasks;
using System.Device.Gpio;
using Newtonsoft.Json;
using Microsoft.Azure.Devices.Client;
using System.Text;
using RobootikaCOM.NetCore.Devices;

namespace GarageModule.Sensors
{
    class Garage
    {
        private GpioController gpio = new GpioController(PinNumberingScheme.Logical);
        private const int GarageDoorInPin = 26;

        private MPL115A2 MPL115A2Sensor = new MPL115A2(); //temperature sensor
        private TSL2561 TSL2561Sensor = new TSL2561(); //light and pressure sensor

        private HT16K33 driver = new HT16K33(new byte[] { 0x70 }, HT16K33.Rotate.None); //LED matrix
        private SendDataAzure _sendListData;

        public static double CurrentLux { get; set; }
        public static int Temperature { get; set; }
        public static bool isSomeoneInGarage { get; set; } = false;
        public int DoorOpenInSeconds { get; set; } = 0;
        public bool IsDoorOpen { get; set; } = false;
        public string status { get; set; }
        public bool isHomeSecured { get; set; } = true;
        public DateTime dateTimeDoorOpen { get; set; }
        public string Lux, Temp;
        public static string Message { get; set; } = "";
        public DateTimeOffset DateTimeTZ()
        {
            TimeZoneInfo eet = TimeZoneInfo.FindSystemTimeZoneById("EET");
            TimeSpan timeSpan = eet.GetUtcOffset(System.DateTime.UtcNow);
            DateTimeOffset LocalTimeTZ = new DateTimeOffset(System.DateTime.UtcNow).ToOffset(timeSpan);
            return LocalTimeTZ;
        }

        public async void ReadTemperatureAsync()
        {

            TSL2561Sensor.SetTiming(true, TSL2561.INTEGRATIONTIME_402MS);
            
            TimeSpan myDateResult;
            int lastTemperature = 100; //some big number to enable initial message when program is starting
            gpio.OpenPin(GarageDoorInPin, PinMode.InputPullUp);

            while (true)
            {
                try
                {
                    Lux = Temp = "";
                    //double pressure = MPL115A2Sensor.getPressure(); //kPa
                    CurrentLux = Math.Round(TSL2561Sensor.GetLux(), 1);
                    Lux = $"Lux: {CurrentLux}"; //this for logging
                    Temperature = (int)Math.Round(MPL115A2Sensor.getTemperature(), 0);
                    Temp = $"Temp: {Temperature}"; //this for logging

                    status = "No info";

                    IsDoorOpen = (bool)gpio.Read(GarageDoorInPin);

                    //send data out only if temperature has changed more than 1 degrees
                    if (Math.Abs(Temperature - lastTemperature) > 1)
                    {
                        dateTimeDoorOpen = DateTimeTZ().DateTime;
                        isHomeSecured = false;
                        await SendDataAsync();
                        isHomeSecured = true;
                        lastTemperature = Temperature;
                    }

                    //if there is more light or button released, then the door is open
                    if (CurrentLux > 0.5 || IsDoorOpen)
                    {
                        if (!isSomeoneInGarage)
                        {
                            isSomeoneInGarage = true;
                            dateTimeDoorOpen = DateTimeTZ().DateTime;
                            status = "Garage door open";
                            //send data once when the door opened
                            await SendDataAsync();
                        }
                    }
                    else //if there is no light AND button pressed, then the door is closed
                    {
                        if (isSomeoneInGarage)
                        {
                            isSomeoneInGarage = false;
                            myDateResult = DateTimeTZ().DateTime - dateTimeDoorOpen;
                            DoorOpenInSeconds = (int)myDateResult.TotalSeconds;
                            status = "Garage door closed";
                            //send data once when the door was closed
                            await SendDataAsync();
                        }
                        DoorOpenInSeconds = 0;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Garage exception: {e} {Lux}, {Temp} {DateTimeTZ():dd.MM HH:mm}");
                }
                await Task.Delay(TimeSpan.FromSeconds(1)); //read every 1sec
                //10 seconds doesnt raise exceptions
                //5 seconds raise an exception
                //rather set try-catch
            }
        }
        public async Task SendDataAsync()
        {
            _sendListData = new SendDataAzure();
            var monitorData = new
            {
                DeviceID = "GaragePI",
                isGarageDoorOpen = isSomeoneInGarage,
                GarageDoorOpenTime = dateTimeDoorOpen.ToString("HH:mm"),
                DoorOpenInSeconds,
                status,
                isHomeSecured,
                date = DateTimeTZ().ToString("dd.MM"),
                time = DateTimeTZ().ToString("HH:mm"),
                UtcOffset = DateTimeTZ().Offset.Hours,
                DateAndTime = DateTimeTZ().DateTime
            };
            var messageJson = JsonConvert.SerializeObject(monitorData);
            var IoTmessage = new Message(Encoding.ASCII.GetBytes(messageJson));
            await _sendListData.PipeMessage(IoTmessage, Program.IoTHubModuleClient, $"{Message} light: {(ReceiveDataClass.IsGarageLightsOn ? "On" : "Off")}");
        }
        public async void LedMatrixAsync()
        {
            LED8x8Matrix matrix = new LED8x8Matrix(driver);
            while (true)
            {
                Message = $" temp:{Temperature} lux: {CurrentLux} door: {(isSomeoneInGarage ? "Open" : "Closed")} {DateTimeTZ():dd.MM HH:mm}";
                matrix.ScrollStringInFromRight(Message, 70);
                await Task.Delay(TimeSpan.FromSeconds(2)); //scroll every 2 sec
            }
        }
    }
}