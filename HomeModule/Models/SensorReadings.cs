using System.Collections.Generic;

namespace HomeModule.Models
{
    class SensorReading
    {
        public SensorReading(string sensorid="", string sensorname="", double temperatureset=0, bool isroom = false)
        {
            SensorID = sensorid;
            RoomName = sensorname;
            TemperatureSET = temperatureset;
            isRoom = isroom;
        }
        public string SensorID;
        public string RoomName;
        public double Temperature;
        public double LastTemperature;
        public bool isTrendIncreases;
        public double TemperatureSET;
        public bool isHeatingRequired;
        public bool isRoom;
    }
    class SensorReadings
    {
        public List<SensorReading> Temperatures = new List<SensorReading>();
    }
}
