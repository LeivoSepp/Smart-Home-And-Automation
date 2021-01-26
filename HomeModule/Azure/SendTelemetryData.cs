using HomeModule.Netatmo;
using System.Threading.Tasks;
using System;
using HomeModule.Helpers;

namespace HomeModule.Azure
{
    class TelemetryDataClass
    {
        public static int RoomHeatingInMinutes { get; set; }
        public static int WaterHeatingInMinutes { get; set; }
        public static int VentilationInMinutes { get; set; } = 0;
        public static bool isHomeSecured { get; set; } = false;
        public static bool isHomeInVacation { get; set; } = false;
        public static bool isVentilationOn { get; set; } = false;
        public static bool isWaterHeatingOn { get; set; } = false;
        public static bool isHeatingOn { get; set; } = false;
        public static bool isNormalHeating { get; set; } = false;
        public static string SourceInfo { get; set; }
        public static bool isSaunaOn { get; set; } = false;
        public static DateTime SaunaStartedTime { get; set; } = new DateTime();
        public static bool isSomeoneAtHome { get; set; }
        public static bool isOutsideLightsOn { get; set; }
        public static bool isGarageLightsOn { get; set; }
        public static bool isHomeDoorOpen { get; set; }

    }
    class SendTelemetryData
    {
        private SendDataAzure _sendListData;
        public async void SendTelemetryEventsAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(10)); //wait 10 seconds for the first initialization to have a Netatmo data present, later it doesnt make sense
            while (true)
            {
                TelemetryDataClass.SourceInfo = "Telemetry 5min before every full hour";
                await SendTelemetryAsync();

                int secondsToNextHour = 3600 - ((int)DateTime.UtcNow.TimeOfDay.TotalSeconds+300) % 3600;
                await Task.Delay(TimeSpan.FromSeconds(secondsToNextHour)); //wait until 5min before the next hour
            }
        }
        public async Task SendTelemetryAsync()
        {
            _sendListData = new SendDataAzure();

            var monitorData = new
            { 
                DeviceID = "HomeController",
                NetatmoDataClass.Co2,
                NetatmoDataClass.Humidity,
                NetatmoDataClass.OutsideHumidity,
                NetatmoDataClass.Temperature,
                NetatmoDataClass.TemperatureOut,
                NetatmoDataClass.Noise,
                NetatmoDataClass.BatteryPercent,
                UtcOffset = METHOD.DateTimeTZ().Offset.Hours,
                DateAndTime = METHOD.DateTimeTZ(),
                TelemetryDataClass.SourceInfo
            };

            await _sendListData.PipeMessage(monitorData, Program.IoTHubModuleClient, TelemetryDataClass.SourceInfo, "outputStream");
        }
    }
}
