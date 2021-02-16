using CoordinateSharp;
using HomeModule.Azure;
using HomeModule.Helpers;
using System;
using System.Threading.Tasks;

namespace HomeModule.Schedulers
{
    public static class SomeoneAtHome
    {
        //https://github.com/Tronald/CoordinateSharp
        static readonly double Lat = 59.419555;
        static readonly double Lon = 24.703521;

        //this has a state if someone is at home
        public static bool IsSomeoneAtHome
        {
            get { return _isSomeoneAtHome; }
            set
            {
                if (_isSomeoneAtHome != value)
                {
                    _isSomeoneAtHome = value;
                    SomeoneAtHomeChanged();
                }
            }
        }
        private static bool _isSomeoneAtHome;

        public static bool LightsManuallyOnOff = false;
        public static bool IsSecurityManuallyOn = false;

        public static void SomeoneAtHomeChanged()
        {
            ReceiveData _receiveData = new ReceiveData();
            DateTimeOffset CurrentDateTime = METHOD.DateTimeTZ();

            //if security button is pushed from PowerApps then no automatic security change
            //if vacation mode is pushed from PowerApps, then security is back in automatic mode
            //if automatic mode, then secure home if nobody is at home and unsecure if some known mobile is at home
            if (!IsSecurityManuallyOn)
            {
                string cmd = WiFiProbes.IsAnyMobileAtHome ? CommandNames.TURN_OFF_SECURITY : CommandNames.TURN_ON_SECURITY;
                _receiveData.ProcessCommand(cmd);
            }
            TelemetryDataClass.isSomeoneAtHome = IsSomeoneAtHome;
            Console.WriteLine($"{(IsSecurityManuallyOn ? "Manual security mode." : "Automatic security mode.")} {(IsSomeoneAtHome ? "Someone at home" : "Nobody is home")} {CurrentDateTime.DateTime:G}");
        }

        public static async void CheckLightStatuses()
        {
            DateTime dateTime = METHOD.DateTimeTZ().DateTime;
            while (true)
            {
                DateTime CurrentDateTime = METHOD.DateTimeTZ().DateTime;
                bool isLightsTime = IsDarkTime() && !IsSleepTime();
                //following is checking if one has pushed the button from the PowerApp, then lights are on for 10 minutes
                var durationToForceLights = LightsManuallyOnOff ? (CurrentDateTime - dateTime).TotalMinutes : CONSTANT.OUTSIDE_LIGHTS_MANUAL_DURATION;
                var isLightNotForced = durationToForceLights >= CONSTANT.OUTSIDE_LIGHTS_MANUAL_DURATION;
                if (isLightNotForced)
                {
                    LightsManuallyOnOff = false;
                    dateTime = CurrentDateTime;
                    //execute shelly lights only if needed, not in every minute :-)
                    if ((isLightsTime && !TelemetryDataClass.isOutsideLightsOn) || (!isLightsTime && TelemetryDataClass.isOutsideLightsOn))
                        TelemetryDataClass.isOutsideLightsOn = await Shelly.SetShellySwitch(isLightsTime, Shelly.OutsideLight, nameof(Shelly.OutsideLight));
                }
                await Task.Delay(TimeSpan.FromMinutes(1)); //check statuses every 1 minutes
            }
        }
        private static bool IsDarkTime()
        {
            var coordinate = new Coordinate(Lat, Lon, DateTime.Now);
            var sunset = (DateTime)coordinate.CelestialInfo.SunSet;
            var sunrise = (DateTime)coordinate.CelestialInfo.SunRise;
            bool isDarkTime = METHOD.TimeBetween(METHOD.DateTimeTZ().ToUniversalTime(), sunset.TimeOfDay, sunrise.TimeOfDay);
            return isDarkTime;
        }
        public static bool IsSleepTime()
        {
            TimeSpan SleepTimeStart = TimeSpan.Parse(CONSTANT.SLEEP_TIME);
            TimeSpan SleepTimeEnd = TimeSpan.Parse(CONSTANT.WAKEUP_TIME);
            bool isSleepTime = METHOD.TimeBetween(METHOD.DateTimeTZ(), SleepTimeStart, SleepTimeEnd);
            return isSleepTime;
        }
    }
}
