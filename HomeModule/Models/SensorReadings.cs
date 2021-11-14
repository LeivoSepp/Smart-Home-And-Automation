using System.Collections.Generic;

namespace HomeModule.Models
{
    class SensorReading
    {
        public SensorReading(string sensorid = "", string roomName = "", double temperatureset = 0, bool isroom = false)
        {
            SensorID = sensorid;
            RoomName = roomName;
            TemperatureSET = temperatureset;
            isRoom = isroom;
        }
        public string SensorID { get; set; }
        public string RoomName { get; set; }
        public double Temperature { get; set; }
        public double LastTemperature { get; set; }
        public bool isTrendIncreases { get; set; }
        public double TemperatureSET { get; set; }
        public bool isHeatingRequired { get; set; }
        public bool isRoom { get; set; }
    }
    class SensorReadings
    {
        public List<SensorReading> Temperatures = new List<SensorReading>();
    }
}
