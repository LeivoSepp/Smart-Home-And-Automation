using CoordinateSharp;
using HomeModule.Azure;
using HomeModule.Helpers;
using HomeModule.Raspberry;
using System;
using System.Diagnostics;
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

        //this will be fired every time when the gates will open/close
        public static bool IsGateOpening
        {
            get { return _isGateOpening; }
            set
            {
                if (_isGateOpening != value)
                {
                    _isGateOpening = value;
                    //fire only if gate start opening
                    if (_isGateOpening)
                    {
                        SetGateTimerInterval();
                    }
                }
            }
        }
        private static bool _isGateOpening = false; //REMOVE THIS FALSE IF YOU PUT GARAGE TO WORK

        //this will be fired at the beginning of each gate opening for 10 minutes
        private static bool IsGateOpen
        {
            get { return _isGateOpen; }
            set
            {
                if (_isGateOpen != value)
                {
                    _isGateOpen = value;
                    OnGateOpened();
                }
            }
        }
        private static bool _isGateOpen;

        private static bool IsManuallyTurnedOn = false;
        private static bool IsManuallyTurnedOff = false;
        private static double timerIntervalGate;
        private static SendDataAzure _sendListData = new SendDataAzure();
        private static Stopwatch stopwatchGate = new Stopwatch();

        private static void OnGateOpened()
        {
            //with Gate the Garage lights are going on for 5 minutes
            SetGarageLightsOn(IsGateOpen);
            //outside lights forced for 30 min
            SetOutsideLightsOn(IsGateOpen, true);
        }
        //this will be fired every time when someone opens the gate
        private static void SetGateTimerInterval()
        {
            var timerInMinutes = 5; //5 minutes window
            timerIntervalGate = TimeSpan.FromMinutes(timerInMinutes).TotalSeconds;
            stopwatchGate.Restart();
        }
        public static async void CheckSomeoneMoving()
        {
            stopwatchGate.Start();
            while (true)
            {
                var elapsedSecGate = stopwatchGate.Elapsed.TotalSeconds;
                IsGateOpen = elapsedSecGate <= timerIntervalGate;
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
        private static async void SomeoneAtHomeChanged()
        {
            DateTimeOffset CurrentDateTime = METHOD.DateTimeTZ();

            //send this message only if home is not secured and either started moving or stopped moving
            if (!TelemetryDataClass.isHomeSecured)
            {
                SetOutsideLightsOn(IsSomeoneAtHome);

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
                Console.WriteLine("someone at home");
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
                bool forceOutsideLightsOn = true;
                SetOutsideLightsOn(true, forceOutsideLightsOn); //forcing outside lights ON

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
        public static void SetOutsideLightsOn(bool setLightsOn = true, bool isForcedToTurnOn = false, bool isForcedToTurnOff = false)
        {
            IsManuallyTurnedOn = isForcedToTurnOn;
            IsManuallyTurnedOff = isForcedToTurnOff;
            bool isSleepTime = IsSleepTime();
            bool isDarkTime = IsDarkTime();
            bool isLightsTime = isDarkTime && !isSleepTime;
            var _receiveData = new ReceiveData();
            string cmd = ((setLightsOn && isLightsTime) || IsManuallyTurnedOn) && !IsManuallyTurnedOff ? CommandNames.TURN_ON_OUTSIDE_LIGHT : CommandNames.TURN_OFF_OUTSIDE_LIGHT;
            _receiveData.ProcessCommand(cmd);
            Console.WriteLine($"Outside lights are {(TelemetryDataClass.isOutsideLightsOn ? "on" : "off")} {METHOD.DateTimeTZ().DateTime:dd.MM HH:mm:ss}");
        }
        public static async void SetGarageLightsOn(bool isLightsOn = true)
        {
            TelemetryDataClass.isGarageLightsOn = await Shelly.SetShellySwitch(isLightsOn, Shelly.GarageLight);
            Console.WriteLine($"Garage lights are {(TelemetryDataClass.isGarageLightsOn ? "on" : "off")} {METHOD.DateTimeTZ().DateTime:dd.MM HH:mm}");
        }

        //turn lights off is it's already DayTime but people are moving constantly around
        //turn lights on, if it's DarkTime and people are moving constantly, so SomeoneAtHome event is not fired
        public static async void CheckLightStatuses()
        {
            while (true)
            {
                bool isSleepTime = IsSleepTime();
                bool isDarkTime = IsDarkTime();
                bool isLightsTime = isDarkTime && !isSleepTime;
                bool isNotLightsTime = !isLightsTime;
                bool isLightsAreOn = TelemetryDataClass.isGarageLightsOn || TelemetryDataClass.isOutsideLightsOn;

                //the following manual modes are comes if one pushes the button from the app
                if (IsManuallyTurnedOn || IsManuallyTurnedOff)
                {
                    await Task.Delay(TimeSpan.FromMinutes(CONSTANT.OUTSIDE_LIGHTS_MANUAL_DURATION)); //wait here for 30 minutes, lights are on or off
                    IsManuallyTurnedOn = IsManuallyTurnedOff = false;
                }
                else
                {
                    //during day time and night time turn off the lights if they are suddenly on
                    if (isNotLightsTime && isLightsAreOn)
                    {
                        SetOutsideLightsOn(false);
                        SetGarageLightsOn(false);
                    }
                    //during dark time if someone is at home but lights are off, turn them on
                    if (isLightsTime && IsSomeoneAtHome && !TelemetryDataClass.isOutsideLightsOn)
                    {
                        SetOutsideLightsOn();
                    }
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
