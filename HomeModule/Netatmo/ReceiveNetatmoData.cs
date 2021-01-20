using HomeModule.Parameters;
using Netatmo.Net;
using Netatmo.Net.Model;
using System;
using System.Threading.Tasks;

// https://github.com/tebben/Netatmo-API-DOTNET

namespace HomeModule.Netatmo
{
    class ReceiveNetatmoData
    {
        static readonly string netatmoClientId = Environment.GetEnvironmentVariable("netatmoClientId");
        static readonly string netatmoClientSecret = Environment.GetEnvironmentVariable("netatmoClientSecret");
        static readonly string netatmoUsername = Environment.GetEnvironmentVariable("netatmoUsername");
        static readonly string netatmoPassword = Environment.GetEnvironmentVariable("netatmoPassword");
        static readonly string deviceIDIndoor = Environment.GetEnvironmentVariable("deviceIDIndoor");
        static readonly string deviceIDOutdoor = Environment.GetEnvironmentVariable("deviceIDOutdoor");

        readonly NetatmoApi _api = new NetatmoApi(netatmoClientId, netatmoClientSecret);
        private async void ApiLoginSuccessful(object sender)
        {
            while (true)
            {
                try
                {
                    var data = await _api.GetStationsData();
                    if (data.Success)
                    {
                        //DashboardData: Last data measured per device(NB: DashboardData field is not returned when the device is unreachable)
                        bool isInsideAccessible = data.Result.Data.Devices[0].DashboardData != null;
                        Module OutsideModule = data.Result.Data.Devices[0].Modules[0];
                        bool isOutsideAccessible = data.Result.Data.Devices[0].Modules[0].DashboardData != null;

                        if (isInsideAccessible)
                        {
                            DashboardData InsideDevice = data.Result.Data.Devices[0].DashboardData;
                            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(InsideDevice.TimeUtc);
                            DateTime dateTime = dateTimeOffset.UtcDateTime.ToLocalTime();

                            NetatmoDataClass.dateTime = dateTime;
                            NetatmoDataClass.Co2 = (int)InsideDevice.CO2;
                            NetatmoDataClass.Humidity = (int)InsideDevice.Humidity;
                            NetatmoDataClass.Noise = (int)InsideDevice.Noise;
                            NetatmoDataClass.Temperature = Math.Round(InsideDevice.Temperature, 1);

                            NetatmoDataClass.Battery = OutsideModule.BatteryVp;
                            NetatmoDataClass.BatteryPercent = OutsideModule.BatteryPercent;
                        }
                        if (isOutsideAccessible)
                        {
                            DashboardData OutsideDevice = OutsideModule.DashboardData;
                            NetatmoDataClass.TempTrend = OutsideDevice.TempTrend;
                            NetatmoDataClass.TemperatureOut = Math.Round(OutsideDevice.Temperature, 1);
                            NetatmoDataClass.OutsideHumidity = (int)OutsideDevice.Humidity;
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Netatmo exception thrown: " + e.ToString());
                }
                await Task.Delay(TimeSpan.FromMinutes(HomeParameters.CHECK_NETATMO_IN_MINUTES)); //check every 10 min
            }
        }
        public void ReceiveData()
        {
            _api.LoginSuccessful += ApiLoginSuccessful;
            _api.Login(netatmoUsername, netatmoPassword, new[] { NetatmoScope.read_station });
        }

    }
}
