using CoordinateSharp;
using HomeModule.Azure;
using HomeModule.MCP;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HomeModule.Schedulers
{
    public static class SomeoneAtHome
    {
        //https://github.com/Tronald/CoordinateSharp
        static readonly double Lat = 59.419555;
        static readonly double Lon = 24.703521;

        //this has a state if someone is at home
        private static bool IsSomeoneAtHome
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
                        OnGateOpening();
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

        private static SendTelemetryData _sendTelemetryData = new SendTelemetryData();
        private static SendDataAzure _sendListData = new SendDataAzure();
        private static Stopwatch stopwatchHome = new Stopwatch();
        private static Stopwatch stopwatchGate = new Stopwatch();

        private static async void OnGateOpening()
        {
            var timerInMinutes = 5; //5 minutes window
            var timerInterval = TimeSpan.FromMinutes(timerInMinutes).TotalSeconds;
            stopwatchGate.Restart();

            while (true)
            {
                var te = stopwatchGate.Elapsed.TotalSeconds;
                IsGateOpen = te <= timerInterval;
                if (!IsGateOpen) break;
                await Task.Delay(TimeSpan.FromSeconds(1)); //check the stopper statuses every second
            }
        }
        private static void OnGateOpened()
        {
            //with Gate the Garage lights are going on for 5 minutes
            SetGarageLightsOn(IsGateOpen);
            //outside lights forced for 30 min
            SetOutsideLightsOn(IsGateOpen, true);
        }

        //this will be fired every time when someone moves at home
        public static async void OnSomeoneMovingAtHome()
        {
            //if home is secured, then check data in every minute otherwise in every 60 minutes
            var timerInMinutes = TelemetryDataClass.isHomeSecured ? 1 : 60;
            var timerInterval = TimeSpan.FromMinutes(timerInMinutes).TotalSeconds;
            stopwatchHome.Restart();
            while (true)
            {
                var te = stopwatchHome.Elapsed.TotalSeconds;
                IsSomeoneAtHome = te <= timerInterval;
                if (!IsSomeoneAtHome) break;
                await Task.Delay(TimeSpan.FromSeconds(1)); //check the stopper statuses every second
            }
        }
        private static async void SomeoneAtHomeChanged()
        {
            //turn on-off outside lights based someone is at home or not
            SetOutsideLightsOn(IsSomeoneAtHome);

            //send this alert only if home is not secured and either started moving or stopped moving
            if (!TelemetryDataClass.isHomeSecured)
            {
                TelemetryDataClass.isSomeoneAtHome = IsSomeoneAtHome;
                TelemetryDataClass.SourceInfo = $"Someone is at home: {IsSomeoneAtHome}";
                var monitorData = new
                {
                    DeviceID = "HomeController",
                    TelemetryDataClass.SourceInfo,
                    UtcOffset = Program.DateTimeTZ().Offset.Hours,
                    DateAndTime = Program.DateTimeTZ().DateTime,
                };
                Console.WriteLine($"Send data, not secured");
                //await _sendListData.PipeMessage(monitorData, Program.IoTHubModuleClient, TelemetryDataClass.SourceInfo);
            }

            //run the alert only if home is secured and someone is moving
            if (TelemetryDataClass.isHomeSecured && IsSomeoneAtHome)
            {
                bool forceOutsideLightsOn = true;
                SetOutsideLightsOn(true, forceOutsideLightsOn); //forcing outside lights ON
                TelemetryDataClass.SourceInfo = $"Home secured, someone moving {HomeSecurity.alertingSensors}";
                var monitorData = new
                {
                    DeviceID = "SecurityController",
                    TelemetryDataClass.SourceInfo,
                    TelemetryDataClass.isHomeSecured,
                    UtcOffset = Program.DateTimeTZ().Offset.Hours,
                    DateAndTime = Program.DateTimeTZ().DateTime,
                    date = Program.DateTimeTZ().ToString("dd.MM"),
                    time = Program.DateTimeTZ().ToString("HH:mm"),
                    status = HomeSecurity.alertingSensors
                };
                Console.WriteLine($"Send data, secured");
                await _sendListData.PipeMessage(monitorData, Program.IoTHubModuleClient, TelemetryDataClass.SourceInfo);
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
            Console.WriteLine($"Outside lights are {(TelemetryDataClass.isOutsideLightsOn ? "on" : "off")} {Program.DateTimeTZ().DateTime:dd.MM HH:mm}");
        }
        public static void SetGarageLightsOn(bool isLightsOn = true)
        {
            bool isDarkTime = IsDarkTime();
            var _receiveData = new ReceiveData();
            string cmd = isLightsOn && isDarkTime ? CommandNames.TURN_ON_GARAGE_LIGHT : CommandNames.TURN_OFF_GARAGE_LIGHT;
            _receiveData.ProcessCommand(cmd);
            Console.WriteLine($"Garage lights are {(TelemetryDataClass.isGarageLightsOn ? "on" : "off")} {Program.DateTimeTZ().DateTime:dd.MM HH:mm}");
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
                if (IsManuallyTurnedOn)
                {
                    SetOutsideLightsOn();
                    await Task.Delay(TimeSpan.FromMinutes(30)); //check statuses every 30 minutes
                    IsManuallyTurnedOn = false;
                }
                if (IsManuallyTurnedOff)
                {
                    SetOutsideLightsOn(false);
                    await Task.Delay(TimeSpan.FromMinutes(30)); //check statuses every 30 minutes
                    IsManuallyTurnedOff = false;
                }
                //check the day/night conditions to turn lights on/off
                if (!IsManuallyTurnedOn && !IsManuallyTurnedOff)
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
            bool isDarkTime = HeatingParams.TimeBetween(Program.DateTimeTZ().ToUniversalTime(), sunset.TimeOfDay, sunrise.TimeOfDay);
            return isDarkTime;
        }
        public static bool IsSleepTime()
        {
            TimeSpan SleepTimeStart = TimeSpan.Parse("00:00");
            TimeSpan SleepTimeEnd = TimeSpan.Parse("07:00");
            bool isSleepTime = HeatingParams.TimeBetween(Program.DateTimeTZ(), SleepTimeStart, SleepTimeEnd);
            return isSleepTime;
        }
    }
}
