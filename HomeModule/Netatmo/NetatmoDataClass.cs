using System;

namespace HomeModule.Netatmo
{
    class NetatmoDataClass
    {
        public static DateTime dateTime { get; set; }
        public static int Co2 { get; set; }
        public static int Humidity { get; set; }
        public static int Noise { get; set; }
        public static double Temperature { get; set; }
        public static string TempTrend { get; set; }
        public static double TemperatureOut { get; set; }
        public static int OutsideHumidity { get; set; }
        public static int Battery { get; set; }
        public static int BatteryPercent { get; set; }
    }
}
