using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.IO;
using System.Linq;
using HomeModule.Azure;
using System.Threading.Tasks;
using HomeModule.Schedulers;
using HomeModule.Parameters;
using System.Security.Cryptography.X509Certificates;

namespace HomeModule.Raspberry
{
    class Paradox1738
    {
        static SerialPort _serialPort;
        private SendDataAzure _sendListData;
        public static List<Zone> alertingSensors = new List<Zone>();

        public async void ParadoxSecurity()
        {
            string ComPort = "/dev/ttyAMA0";
            int baudrate = 9600;
            _serialPort = new SerialPort(ComPort, baudrate);
            try
            {
                _serialPort.Open();
            }
            catch (IOException ex)
            {
                Console.WriteLine($"{ex}");
            }
            byte[] DataStream = new byte[4];
            byte index = 0;
            while (true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1)); //check statuses every millisecond
                //Spectra message output is always 4 bytes
                try
                {
                    if (_serialPort.BytesToRead < 4)
                    {
                        index = 0;
                        while (index < 4)
                        {
                            DataStream[index++] = (byte)_serialPort.ReadByte();
                        }
                    }
                }
                catch (Exception e) { Console.WriteLine($"Timeout {e}"); }

                int EventId = DataStream[0] >> 2;
                int CategoryId = ((DataStream[0] & 3) << 4) + (DataStream[1] >> 4);

                string Event = events.Where(x => x.EventId == EventId).Select(x => x.EventName).DefaultIfEmpty($"Event_{EventId}").First();
                int EventCategory = events.Where(x => x.EventId == EventId).Select(x => x.EventCategory).DefaultIfEmpty(EventId).First();

                string Message = CategoryId.ToString();

                bool isZoneEvent = EventCategory == Category.ZONE;
                bool isStatus = EventCategory == Category.STATUS;
                bool isTrouble = EventCategory == Category.TROUBLE;
                bool isAccessCode = EventCategory == Category.ACCESS_CODE;
                bool isSpecialAlarm = EventCategory == Category.SPECIAL_ALARM;
                bool isSpecialArm = EventCategory == Category.SPECIAL_ARM;
                bool isSpecialDisarm = EventCategory == Category.SPECIAL_DISARM;
                bool isNonReportEvents = EventCategory == Category.NON_REPORT_EVENTS;
                bool isSpecialReport = EventCategory == Category.SPECIAL_REPORT;
                bool isRemoteControl = EventCategory == Category.REMOTE_CONTROL;

                if (isZoneEvent)
                {
                    bool IsZoneOpen = false;
                    if (EventId == 1) IsZoneOpen = true;
                    //update existing list with the IR statuses and activating/closing time
                    Zones.Where(x => x.ZoneId == CategoryId).Select(x => { x.IsZoneOpen = IsZoneOpen; x.IsHomeSecured = TelemetryDataClass.isHomeSecured; x.ZoneEventTime = Program.DateTimeTZ(); return x; }).ToList();
                    Message = Zones.Where(x => x.ZoneId == CategoryId).Select(x => $"{x.ZoneName}").DefaultIfEmpty($"Zone_{CategoryId}").First();
                }
                if (isStatus) Message = PartitionStatuses.Where(x => x.CategoryId == CategoryId).Select(x => x.Name).DefaultIfEmpty($"Status_{CategoryId}").First();
                if (isTrouble) Message = SystemTroubles.Where(x => x.CategoryId == CategoryId).Select(x => x.Name).DefaultIfEmpty($"Trouble_{CategoryId}").First();
                if (isSpecialAlarm) Message = SpecialAlarms.Where(x => x.CategoryId == CategoryId).Select(x => x.Name).DefaultIfEmpty($"SpecialAlarm_{CategoryId}").First();
                if (isSpecialArm) Message = SpecialArms.Where(x => x.CategoryId == CategoryId).Select(x => x.Name).DefaultIfEmpty($"SpecialArm_{CategoryId}").First();
                if (isSpecialDisarm) Message = SpecialDisarms.Where(x => x.CategoryId == CategoryId).Select(x => x.Name).DefaultIfEmpty($"SpecialDisarm_{CategoryId}").First();
                if (isNonReportEvents) Message = NonReportableEvents.Where(x => x.CategoryId == CategoryId).Select(x => x.Name).DefaultIfEmpty($"NonReportEvent_{CategoryId}").First();
                if (isSpecialReport) Message = SpecialReportings.Where(x => x.CategoryId == CategoryId).Select(x => x.Name).DefaultIfEmpty($"SpecialReporting_{CategoryId}").First();
                if (isRemoteControl) Message = $"Remote_{CategoryId}";
                if (isAccessCode) Message = GetAccessCode(CategoryId);

                if (!(isStatus && (CategoryId == 0 || CategoryId == 1)) && !(EventId == 0 || EventId == 1)) //not show System Ready/Not ready messages and zone open/close messages.
                {
                    Console.WriteLine($"{Program.DateTimeTZ():HH:mm:ss,ff} {Event}, {Message}");
                }
            }
        }

        public async void IRSensorsReading()
        {
            _sendListData = new SendDataAzure();
            State NONE = new State { DoorValue = false, IRValue = false }; //door closed, IR passive
            State DOOR = new State { DoorValue = true, IRValue = false }; //door open, IR passive
            State ALL = new State { DoorValue = true, IRValue = true }; //door open, IR active
            State IR = new State { DoorValue = false, IRValue = true }; //door closed, IR active
            List<State> Entry = new List<State> { NONE, DOOR, ALL, IR, NONE };
            List<State> Entry2 = new List<State> { NONE, DOOR, ALL, DOOR, NONE };
            List<State> Exit = new List<State> { NONE, IR, NONE, DOOR, ALL, IR, NONE };
            List<State> Exit1 = new List<State> { NONE, IR, NONE, DOOR, ALL, DOOR, NONE };
            List<State> Exit2 = new List<State> { NONE, IR, ALL, DOOR, ALL, IR, NONE };
            List<State> Exit3 = new List<State> { NONE, IR, ALL, IR, NONE };
            List<State> Exit4 = new List<State> { NONE, IR, ALL, DOOR, NONE };
            List<State> DoorOpenClose = new List<State> { NONE, DOOR, NONE };
            List<State> RepeatDoorAll = new List<State> { DOOR, ALL }; //repeat
            List<State> RepeatAllDoor = new List<State> { ALL, DOOR }; //repeat
            List<State> RepeatIRAll = new List<State> { IR, ALL }; //repeat
            List<State> _queue = new List<State>
            {
                new State { DoorValue = false, IRValue = false }
            };

            while (true)
            {
                try
                {
                    //check the last zone event time to report is there anybody at home
                    Zone LastActiveZone = Zones.Last();
                    var timerInMinutes = TelemetryDataClass.isHomeSecured ? HomeParameters.TIMER_MINUTES_WHEN_SECURED_HOME_EMPTY : HomeParameters.TIMER_MINUTES_WHEN_HOME_EMPTY;
                    var DurationUntilHouseIsEmpty = !LastActiveZone.IsZoneOpen ? (Program.DateTimeTZ() - LastActiveZone.ZoneEventTime).TotalMinutes : 0;
                    SomeoneAtHome.IsSomeoneAtHome = DurationUntilHouseIsEmpty < timerInMinutes;

                    //check each zone in 2 minutes window to report the zone active time
                    foreach (var zone in Zones)
                    {
                        var durationUntilZoneIsEmpty = !zone.IsZoneOpen ? (Program.DateTimeTZ() - zone.ZoneEventTime).TotalSeconds : 0;
                        zone.IsZoneEmpty = durationUntilZoneIsEmpty > HomeParameters.TIMER_SECONDS_WHEN_ZONE_EMPTY;
                        if (zone.IsZoneEmpty && zone.ZoneEmptyDetectTime != DateTime.MinValue)
                        {
                            //add alerting sensors into list
                            alertingSensors.Add(new Zone(zone.ZoneId, zone.ZoneEmptyDetectTime, zone.ZoneEventTime, zone.ZoneName, zone.IsZoneOpen, zone.IsZoneEmpty, zone.IsHomeSecured));
                            Console.WriteLine($"{zone.ZoneEmptyDetectTime:dd.MM} {zone.ZoneEmptyDetectTime:t} - {zone.ZoneEventTime:t} {(zone.IsHomeSecured ? "SECURED" : null)} {zone.ZoneName} listCount:{alertingSensors.Count}");
                            zone.ZoneEmptyDetectTime = new DateTime();
                        }
                        if (!zone.IsZoneEmpty && zone.ZoneEmptyDetectTime == DateTime.MinValue)
                        {
                            zone.ZoneEmptyDetectTime = Program.DateTimeTZ();
                        }
                    }
                    //maximum is 1000 items in alertingSensor list
                    if (alertingSensors.Count >= HomeParameters.MAX_ITEMS_IN_ALERTING_LIST) alertingSensors.RemoveAt(0);

                    Zone doorZone = Zones.First(ir => ir.ZoneId == 1);
                    Zone IrZone = Zones.First(ir => ir.ZoneId == 2);
                    Zone smokeZone = Zones.First(ir => ir.ZoneId == 7);
                    bool isDoorOpen = doorZone.IsZoneOpen;
                    bool isIrOpen = IrZone.IsZoneOpen;
                    bool isSmokeOpen = smokeZone.IsZoneOpen;

                    //if door or IR is closed more that 2 minutes then clear the queue
                    var LastActive = doorZone.ZoneEventTime > IrZone.ZoneEventTime ? doorZone.ZoneEventTime : IrZone.ZoneEventTime;
                    var durationUntilReset = _queue.Count > 1 ? (Program.DateTimeTZ() - LastActive).TotalSeconds : 0;
                    bool isClearTime = durationUntilReset > HomeParameters.TIMER_SECONDS_CLEAR_DOOR_QUEUE;
                    if (isClearTime && !isDoorOpen)
                    {
                        _queue.Clear();
                        _queue.Add(new State { DoorValue = false, IRValue = false });
                        Console.WriteLine($"{Program.DateTimeTZ():T} queue cleared");
                    }

                    //save the door and IR statuses for the queue
                    State _state = new State { DoorValue = isDoorOpen, IRValue = isIrOpen };

                    if (_queue.Count > 6)
                    {
                        _queue = new List<State>(Helpers.Rotate(_queue, 1));
                        _queue.RemoveAt(_queue.Count - 1);
                    }

                    //if list is empty, then return both open, otherwise last item
                    State lastItem = (_queue.Count != 0) ? _queue[_queue.Count - 1] : new State { DoorValue = true, IRValue = true };
                    string status = "No pattern";

                    if (_state != lastItem)
                    {
                        _queue.Add(_state);
                        if (RemoveDuplicate(_queue, RepeatDoorAll)) Console.WriteLine($"Door-All duplicate removed");
                        if (RemoveDuplicate(_queue, RepeatAllDoor)) Console.WriteLine($"All-Door duplicate removed");
                        if (RemoveDuplicate(_queue, RepeatIRAll)) Console.WriteLine($"IR-All duplicate removed");

                        if (_queue.Count > 2) //only check pattern if there are more than 3 events in queue 
                        {
                            if (ContainsPattern(_queue, Entry)) status = "Entry";
                            if (ContainsPattern(_queue, Entry2)) status = "Entry2";
                            if (ContainsPattern(_queue, Exit)) status = "Exit";
                            if (ContainsPattern(_queue, Exit1)) status = "Exit1";
                            if (ContainsPattern(_queue, Exit2)) status = "Exit2";
                            if (ContainsPattern(_queue, Exit3)) status = "Exit3";
                            if (ContainsPattern(_queue, Exit4)) status = "Exit4";
                            if (ContainsPattern(_queue, DoorOpenClose)) status = "No En/Ex, open-closed";
                        }
                    }
                    if (status != "No pattern")
                    {
                        _queue.Clear(); //clear queue and pattern
                        _queue.Add(new State { DoorValue = false, IRValue = false }); //add first all-closed pattern

                        TelemetryDataClass.SourceInfo = $"Door: {status}";
                        var monitorData = new
                        {
                            DeviceID = "SecurityController",
                            status = TelemetryDataClass.SourceInfo,
                            TelemetryDataClass.isHomeSecured,
                            UtcOffset = Program.DateTimeTZ().Offset.Hours,
                            date = Program.DateTimeTZ().ToString("dd.MM"),
                            time = Program.DateTimeTZ().ToString("HH:mm"),
                            DateAndTime = Program.DateTimeTZ().DateTime
                        };
                        await _sendListData.PipeMessage(monitorData, Program.IoTHubModuleClient, TelemetryDataClass.SourceInfo);
                        status = "No pattern";
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Paradox exception: {e}");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(10)); //check statuses every 10 millisecond
            }
        }
        bool RemoveDuplicate(List<State> list, List<State> pattern)
        {
            List<int> MatchStartAt = new List<int>();
            for (int i = 0; i < list.Count - (pattern.Count - 1); i++)
            {
                var match = true;
                for (int j = 0; j < pattern.Count; j++)
                    if (list[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                if (match) MatchStartAt.Add(i);
            }
            //duplicated pattern found, lets remove that
            if (MatchStartAt.Count > 1)
            {
                list.RemoveRange(MatchStartAt[1], pattern.Count);
                return true;
            }
            return false;
        }

        bool ContainsPattern(List<State> list, List<State> pattern)
        {
            for (int i = 0; i < list.Count - (pattern.Count - 1); i++)
            {
                var match = true;
                for (int j = 0; j < pattern.Count; j++)
                    if (list[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                if (match) return true;
            }
            return false;
        }
#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
        private struct State
#pragma warning restore CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
        {
            public static bool operator ==(State c1, State c2)
            {
                return c1.Equals(c2);
            }
            public static bool operator !=(State c1, State c2)
            {
                return !c1.Equals(c2);
            }
            public bool Equals(State c1, State c2)
            {
                return c1.Equals(c2);
            }
            //public override bool Equals(object obj)
            //{
            //    return true;
            //}
            public override int GetHashCode()
            {
                return 0;
            }
            public bool DoorValue;
            public bool IRValue;
        }

        public string GetAccessCode(int code)
        {
            string AccessCode = code < 10 ? $"User Code 00{code}" : $"User Code 0{code}";
            if (code == 1) AccessCode = "Master code";
            if (code == 2) AccessCode = "Master Code 1";
            if (code == 3) AccessCode = "Master Code 2";
            if (code == 48) AccessCode = "Duress Code";
            return AccessCode;
        }

        public static List<Event> events = new List<Event>
        {
            new Event(){EventId = 0, EventCategory = Category.ZONE, EventName = "Zone OK"},
            new Event(){EventId = 1, EventCategory = Category.ZONE, EventName = "Zone Open"},
            new Event(){EventId = 2, EventCategory = Category.STATUS, EventName = "Partition Status"},
            new Event(){EventId = 5, EventCategory = Category.NON_REPORT_EVENTS, EventName = "Non-Reportable Events"},
            new Event(){EventId = 6, EventCategory = Category.REMOTE_CONTROL, EventName = "Arm/Disarm with Remote Control"},
            new Event(){EventId = 7, EventCategory = Category.REMOTE_CONTROL, EventName = "Button Pressed on Remote (B)"},
            new Event(){EventId = 8, EventCategory = Category.REMOTE_CONTROL, EventName = "Button Pressed on Remote (C)"},
            new Event(){EventId = 9, EventCategory = Category.REMOTE_CONTROL, EventName = "Button Pressed on Remote (D)"},
            new Event(){EventId = 10, EventCategory = Category.ACCESS_CODE, EventName = "Bypass programming"},
            new Event(){EventId = 11, EventCategory = Category.ACCESS_CODE, EventName = "User Activated PGM"},
            new Event(){EventId = 12, EventCategory = Category.ZONE, EventName = "Zone with delay is breached"},
            new Event(){EventId = 13, EventCategory = Category.ACCESS_CODE, EventName = "Arm"},
            new Event(){EventId = 14, EventCategory = Category.SPECIAL_ARM, EventName = "Special Arm"},
            new Event(){EventId = 15, EventCategory = Category.ACCESS_CODE, EventName = "Disarm"},
            new Event(){EventId = 16, EventCategory = Category.ACCESS_CODE, EventName = "Disarm after Alarm"},
            new Event(){EventId = 17, EventCategory = Category.ACCESS_CODE, EventName = "Cancel Alarm"},
            new Event(){EventId = 18, EventCategory = Category.SPECIAL_DISARM, EventName = "Special Disarm"},
            new Event(){EventId = 19, EventCategory = Category.ZONE, EventName = "Zone Bypassed on arming"},
            new Event(){EventId = 20, EventCategory = Category.ZONE, EventName = "Zone in Alarm"},
            new Event(){EventId = 21, EventCategory = Category.ZONE, EventName = "Fire Alarm"},
            new Event(){EventId = 22, EventCategory = Category.ZONE, EventName = "Zone Alarm restore"},
            new Event(){EventId = 23, EventCategory = Category.ZONE, EventName = "Fire Alarm restore"},
            new Event(){EventId = 24, EventCategory = Category.SPECIAL_ALARM, EventName = "Special alarm"},
            new Event(){EventId = 25, EventCategory = Category.ZONE, EventName = "Auto zone shutdown"},
            new Event(){EventId = 26, EventCategory = Category.ZONE, EventName = "Zone tamper"},
            new Event(){EventId = 27, EventCategory = Category.ZONE, EventName = "Zone tamper restore"},
            new Event(){EventId = 28, EventCategory = Category.TROUBLE, EventName = "System Trouble"},
            new Event(){EventId = 29, EventCategory = Category.TROUBLE, EventName = "System Trouble restore"},
            new Event(){EventId = 30, EventCategory = Category.SPECIAL_REPORT, EventName = "Special Reporting"},
            new Event(){EventId = 31, EventCategory = Category.ZONE, EventName = "Wireless Transmitter Supervision Loss"},
            new Event(){EventId = 32, EventCategory = Category.ZONE, EventName = "Wireless Transmitter Supervision Loss Restore"},
            new Event(){EventId = 33, EventCategory = Category.ZONE, EventName = "Arming with a Keyswitch"},
            new Event(){EventId = 34, EventCategory = Category.ZONE, EventName = "Disarming with a Keyswitch"},
            new Event(){EventId = 35, EventCategory = Category.ZONE, EventName = "Disarm after Alarm with a Keyswitch"},
            new Event(){EventId = 36, EventCategory = Category.ZONE, EventName = "Cancel Alarm with a Keyswitch"},
            new Event(){EventId = 37, EventCategory = Category.ZONE, EventName = "Wireless Transmitter Low Battery"},
            new Event(){EventId = 38, EventCategory = Category.ZONE, EventName = "Wireless Transmitter Low Battery Restore"}
        };
        public static List<Byte2Data> PartitionStatuses = new List<Byte2Data>
        {
            new Byte2Data(){CategoryId = 0, Name = "System not ready"},
            new Byte2Data(){CategoryId = 1, Name = "System ready"},
            new Byte2Data(){CategoryId = 2, Name = "Steady alarm"},
            new Byte2Data(){CategoryId = 3, Name = "Pulsed alarm"},
            new Byte2Data(){CategoryId = 4, Name = "Pulsed or Steady Alarm"},
            new Byte2Data(){CategoryId = 5, Name = "Alarm in partition restored"},
            new Byte2Data(){CategoryId = 6, Name = "Bell Squawk Activated"},
            new Byte2Data(){CategoryId = 7, Name = "Bell Squawk Deactivated"},
            new Byte2Data(){CategoryId = 8, Name = "Ground start"},
            new Byte2Data(){CategoryId = 9, Name = "Disarm partition"},
            new Byte2Data(){CategoryId = 10, Name = "Arm partition"},
            new Byte2Data(){CategoryId = 11, Name = "Entry delay started"}
        };
        public static List<Byte2Data> SystemTroubles = new List<Byte2Data>
        {
            new Byte2Data(){CategoryId =  1, Name = "AC Loss"},
            new Byte2Data(){CategoryId =  2, Name = "Battery Failure"},
            new Byte2Data(){CategoryId =  3, Name = "Auxiliary current overload"},
            new Byte2Data(){CategoryId =  4, Name = "Bell current overload"},
            new Byte2Data(){CategoryId =  5, Name = "Bell disconnected"},
            new Byte2Data(){CategoryId =  6, Name = "Timer Loss"},
            new Byte2Data(){CategoryId =  7, Name = "Fire Loop Trouble"},
            new Byte2Data(){CategoryId =  8, Name = "Future use"},
            new Byte2Data(){CategoryId =  9, Name = "Module Fault"},
            new Byte2Data(){CategoryId = 10, Name = "Printer Fault"},
            new Byte2Data(){CategoryId = 11, Name = "Fail to Communicate"}
        };
        public static List<Byte2Data> NonReportableEvents = new List<Byte2Data>
        {
            new Byte2Data(){CategoryId =  0, Name = "Telephone Line Trouble"},
            new Byte2Data(){CategoryId =  1, Name = "Reset smoke detectors"},
            new Byte2Data(){CategoryId =  2, Name = "Instant arming"},
            new Byte2Data(){CategoryId =  3, Name = "Stay arming"},
            new Byte2Data(){CategoryId =  4, Name = "Force arming"},
            new Byte2Data(){CategoryId =  5, Name = "Fast Exit (Force & Regular Only)"},
            new Byte2Data(){CategoryId =  6, Name = "PC Fail to Communicate"},
            new Byte2Data(){CategoryId =  7, Name = "Midnight"}
        };
        public static List<Byte2Data> SpecialAlarms = new List<Byte2Data>
        {
            new Byte2Data(){CategoryId =  0, Name = "Emergency, keys [1] [3]"},
            new Byte2Data(){CategoryId =  1, Name = "Auxiliary, keys [4] [6]"},
            new Byte2Data(){CategoryId =  2, Name = "Fire, keys [7] [9]"},
            new Byte2Data(){CategoryId =  3, Name = "Recent closing"},
            new Byte2Data(){CategoryId =  4, Name = "Auto Zone Shutdown"},
            new Byte2Data(){CategoryId =  5, Name = "Duress alarm"},
            new Byte2Data(){CategoryId =  6, Name = "Keypad lockout"}
        };
        public static List<Byte2Data> SpecialReportings = new List<Byte2Data>
        {
            new Byte2Data(){CategoryId =  0, Name = "System power up"},
            new Byte2Data(){CategoryId =  1, Name = "Test report"},
            new Byte2Data(){CategoryId =  2, Name = "WinLoad Software Access"},
            new Byte2Data(){CategoryId =  3, Name = "WinLoad Software Access finished"},
            new Byte2Data(){CategoryId =  4, Name = "Installer enters programming mode"},
            new Byte2Data(){CategoryId =  5, Name = "Installer exits programming mode"}
        };
        public static List<Byte2Data> SpecialDisarms = new List<Byte2Data>
        {
            new Byte2Data(){CategoryId =  0, Name = "Cancel Auto Arm (timed/no movement)"},
            new Byte2Data(){CategoryId =  1, Name = "Disarm with WinLoad Software"},
            new Byte2Data(){CategoryId =  2, Name = "Disarm after alarm with WinLoad Software"},
            new Byte2Data(){CategoryId =  3, Name = "Cancel Alarm with WinLoad Software"}
        };
        public static List<Byte2Data> SpecialArms = new List<Byte2Data>
        {
            new Byte2Data(){CategoryId =  0, Name = "Auto arming (timed/no movement)"},
            new Byte2Data(){CategoryId =  1, Name = "Late to Close (Auto-Arming failed)"},
            new Byte2Data(){CategoryId =  2, Name = "No Movement Auto-Arming"},
            new Byte2Data(){CategoryId =  3, Name = "Partial Arming (Stay, Force, Instant, Bypass)"},
            new Byte2Data(){CategoryId =  4, Name = "One-Touch Arming"},
            new Byte2Data(){CategoryId =  5, Name = "Arm with WinLoad Software"},
            new Byte2Data(){CategoryId =  7, Name = "Closing Delinquency"}
        };
        public static List<Zone> Zones = new List<Zone>
        {
            new Zone(1, new DateTime(), new DateTime(), "DOOR"),
            new Zone(2, new DateTime(), new DateTime(), "ENTRY"),
            new Zone(3, new DateTime(), new DateTime(), "LIVING ROOM"),
            new Zone(4, new DateTime(), new DateTime(), "OFFICE"),
            new Zone(5, new DateTime(), new DateTime(), "HALL"),
            new Zone(6, new DateTime(), new DateTime(), "BEDROOM"),
            new Zone(7, new DateTime(), new DateTime(), "FIRE"),
            new Zone(8, new DateTime(), new DateTime(), "TECHNO"),
            new Zone(9, new DateTime(), new DateTime(), "PIANO")
         };
    }
    static class Helpers
    {
        public static List<T> Rotate<T>(this List<T> list, int offset)
        {
            return list.Skip(offset).Concat(list.Take(offset)).ToList();
        }
    }
    class Event
    {
        public int EventId { get; set; }
        public string EventName { get; set; }
        public int EventCategory { get; set; }
    }
    class Category
    {
        public const int ZONE = 1;
        public const int STATUS = 2;
        public const int TROUBLE = 3;
        public const int ACCESS_CODE = 4;
        public const int SPECIAL_ALARM = 5;
        public const int SPECIAL_ARM = 6;
        public const int SPECIAL_DISARM = 7;
        public const int NON_REPORT_EVENTS = 8;
        public const int SPECIAL_REPORT = 9;
        public const int REMOTE_CONTROL = 10;
    }
    class Zone
    {
        public Zone(int zoneId, DateTimeOffset zoneEmptyDetectTime, DateTimeOffset zoneEventTime, string zoneName, bool isZoneOpen = false, bool isZoneEmpty = true, bool isHomeSecured = false)
        {
            ZoneId = zoneId;
            IsZoneOpen = isZoneOpen;
            IsZoneEmpty = isZoneEmpty;
            IsHomeSecured = isHomeSecured;
            ZoneEmptyDetectTime = zoneEmptyDetectTime;
            ZoneEventTime = zoneEventTime;
            ZoneName = zoneName;
        }
        public int ZoneId { get; set; }
        public string ZoneName { get; set; }
        public bool IsZoneOpen { get; set; }
        public DateTimeOffset ZoneEventTime { get; set; }
        public DateTimeOffset ZoneEmptyDetectTime { get; set; }
        public bool IsZoneEmpty { get; set; }
        public bool IsHomeSecured { get; set; }
    }
    class Byte2Data
    {
        public int CategoryId { get; set; }
        public string Name { get; set; }
    }
}
