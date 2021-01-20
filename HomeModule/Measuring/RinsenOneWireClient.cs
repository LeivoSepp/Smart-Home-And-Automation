using System;
using Rinsen.IoT.OneWire;
using HomeModule.Models;
using System.Threading.Tasks;
using HomeModule.Schedulers;
using System.Linq;
using HomeModule.Parameters;

namespace HomeModule.Measuring
{
    internal class RinsenOneWireClient
    {
        private readonly DS2482DeviceFactory dS2482DeviceFactory = new DS2482DeviceFactory();
        public async Task<SensorReadings> ReadSensors()
        {
            SensorReadings AllSensors = new SensorReadings();
            AllSensors.Temperatures.Add(new SensorReading("28-FF-8C-BB-70-16-04-6F", HomeTemperature.BEDROOM, HomeParameters.DEFAULT_ROOM_TEMP, true));
            AllSensors.Temperatures.Add(new SensorReading("28-FF-0E-56-70-16-04-F3", HomeTemperature.HOME_OFFICE, HomeParameters.DEFAULT_ROOM_TEMP, true));
            AllSensors.Temperatures.Add(new SensorReading("28-FF-31-80-70-16-05-4E", HomeTemperature.LIVING_ROOM, HomeParameters.DEFAULT_ROOM_TEMP, true));
            AllSensors.Temperatures.Add(new SensorReading("28-FF-83-8D-70-16-05-1F", HomeTemperature.PIANO_LOUNGE, HomeParameters.DEFAULT_ROOM_TEMP, true));
            AllSensors.Temperatures.Add(new SensorReading("28-FF-FD-B6-70-16-04-52", HomeTemperature.SAUNA, HomeParameters.DEFAULT_SAUNA_TEMP, false));
            AllSensors.Temperatures.Add(new SensorReading("28-FF-C2-36-64-16-04-B4", HomeTemperature.WARM_WATER, 0, false));
            AllSensors.Temperatures.Add(new SensorReading("28-F5-FC-A8-0C-00-00-0E", HomeTemperature.INFLOW_MAIN, 0, false));
            AllSensors.Temperatures.Add(new SensorReading("28-99-CA-A8-0C-00-00-A8", HomeTemperature.RETURN_MAIN, 0, false));
            AllSensors.Temperatures.Add(new SensorReading("28-2C-DD-A7-0C-00-00-F3", HomeTemperature.INFLOW_1_FLOOR, 0, false));
            AllSensors.Temperatures.Add(new SensorReading("28-D1-F7-A7-0C-00-00-8E", HomeTemperature.RETURN_1_FLOOR, 0, false));
            AllSensors.Temperatures.Add(new SensorReading("28-FF-15-21-70-16-04-D1", HomeTemperature.INFLOW_2_FLOOR, 0, false));
            AllSensors.Temperatures.Add(new SensorReading("28-0F-2E-A8-0C-00-00-6E", HomeTemperature.RETURN_2_FLOOR, 0, false));

            try
            {
                using (var ds2482_100 = dS2482DeviceFactory.CreateDS2482_100(false, false))
                {
                    await Task.Delay(2000);
                    if (ds2482_100 == null)
                        Console.WriteLine("ds2482 is ZERO");

                    foreach (var device in ds2482_100.GetDevices<DS18B20>())
                    {
                        var reading = new SensorReading
                        {
                            SensorID = device.OneWireAddressString,
                            Temperature = device.GetTemperature()
                        };
                        //update sensor temperature
                        AllSensors.Temperatures.FirstOrDefault(x => x.SensorID == reading.SensorID).Temperature = reading.Temperature;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"{e}");
            }
            return AllSensors;
        }
    }
}
