using CoordinateSharp;
using HomeModule.Azure;
using HomeModule.Helpers;
using HomeModule.Raspberry;
using System;
using System.Linq;
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
        private static SendDataAzure _sendListData = new SendDataAzure();

        private static async void SomeoneAtHomeChanged()
        {
            DateTimeOffset CurrentDateTime = METHOD.DateTimeTZ();

            //send this message only if home is not secured and either started moving or stopped moving
            if (!TelemetryDataClass.isHomeSecured)
            {
                TelemetryDataClass.isSomeoneAtHome = IsSomeoneAtHome;
                TelemetryDataClass.SourceInfo = $"Someone is at home: {IsSomeoneAtHome}";
                var monitorData = new
                {
                    DeviceID = "HomeController",
                    TelemetryDataClass.SourceInfo,
                    UtcOffset = CurrentDateTime.Offset.Hours,
                    DateAndTime = CurrentDateTime.DateTime,
                };
                //await _sendListData.PipeMessage(monitorData, Program.IoTHubModuleClient, TelemetryDataClass.SourceInfo);
                Console.WriteLine($"{(IsSomeoneAtHome ? "Someone at home" : "Nobody is home" )} {CurrentDateTime.DateTime:G}");
            }

            //check if the last zone was added into list during the home was secured
            bool isLastZoneSecured = false;
            string lastZoneName = null;
            if (Paradox1738.alertingSensors.Any())
            {
                Paradox1738.alertingSensors.Reverse();
                AlertingZone LastZone = Paradox1738.alertingSensors.First();
                lastZoneName = LastZone.ZoneName;
                isLastZoneSecured = LastZone.IsHomeSecured;
            }

            //run the alert only if home is secured and there are some alerting zone
            if (TelemetryDataClass.isHomeSecured && isLastZoneSecured)
            {
                //forcing outside lights ON
                LightsManuallyOnOff = true;
                TelemetryDataClass.isOutsideLightsOn = await Shelly.SetShellySwitch(true, Shelly.OutsideLight, nameof(Shelly.OutsideLight));

                TelemetryDataClass.SourceInfo = $"Home secured {lastZoneName}";
                string sensorsOpen = "Look where someone is moving:\n\n";
                foreach (var zone in Paradox1738.alertingSensors)
                {
                    //create a string with all zones for an e-mail
                    if (zone.IsHomeSecured) sensorsOpen += $" {zone.ZoneName} {zone.TimeStart} - {zone.TimeEnd}\n";
                }
                var monitorData = new
                {
                    DeviceID = "SecurityController",
                    TelemetryDataClass.SourceInfo,
                    TelemetryDataClass.isHomeSecured,
                    UtcOffset = CurrentDateTime.Offset.Hours,
                    DateAndTime = CurrentDateTime.DateTime,
                    date = CurrentDateTime.ToString("dd.MM"),
                    time = CurrentDateTime.ToString("HH:mm"),
                    status = sensorsOpen
                };
                await _sendListData.PipeMessage(monitorData, Program.IoTHubModuleClient, TelemetryDataClass.SourceInfo, "output");
                Paradox1738.alertingSensors.ForEach(x => Console.WriteLine($"{x.ZoneName} {x.TimeStart} - {x.TimeEnd} {(x.IsHomeSecured ? "SECURED" : null)}"));
                //Paradox1738.alertingSensors.RemoveAll(x => x.IsHomeSecured); //remove all reported zones
            }
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
                if(isLightNotForced)
                {
                    LightsManuallyOnOff = false;
                    dateTime = CurrentDateTime;
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
