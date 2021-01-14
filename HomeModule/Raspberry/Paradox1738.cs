using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.IO;
using System.Linq;
using HomeModule.Azure;
using System.Threading.Tasks;
using HomeModule.Schedulers;
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

                string Byte1id = DataStream[0].ToString("X2");
                string Event = events.Where(x => x.Byte1 == Byte1id).Select(x => x.EventName).DefaultIfEmpty($"Event?_{Byte1id}").First();
                int EventCategory = events.Where(x => x.Byte1 == Byte1id).Select(x => x.EventCategory).DefaultIfEmpty(DataStream[0]).First();

                string Byte2id = DataStream[1].ToString("X2");
                string Message = Byte2id;

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
                    //save the IRState into zone's list
                    bool IsZoneOpen = false;
                    if (Byte1id == "04") IsZoneOpen = true;
                    //update existing list with the IR statuses and activating/closing time
                    Zones.Where(x => x.Byte2 == Byte2id).Select(x => { x.IsZoneOpen = IsZoneOpen; x.ZoneEventTime = Program.DateTimeTZ(); return x; }).ToList();
                    Zones.Sort((x, y) => DateTimeOffset.Compare(x.ZoneEventTime, y.ZoneEventTime)); //sort the zones by date
                    Message = Zones.Where(x => x.Byte2 == Byte2id).Select(x => $"{x.ZoneName} {(x.IsZoneOpen ? "Open" : "Closed")}").DefaultIfEmpty($"Zone_{Byte2id}").First();

                    //add alerting sensors into list if home secured
                    if (TelemetryDataClass.isHomeSecured)
                    {
                        if (IsZoneOpen)
                        {
                            Zone zone = Zones.FirstOrDefault(x => x.IsZoneOpen);
                            alertingSensors.Add(new Zone() { IsZoneOpen = zone.IsZoneOpen, ZoneName = zone.ZoneName, ZoneEventTime = zone.ZoneEventTime, Byte2 = zone.Byte2 });
                        }
                    }
                    else
                    {
                        alertingSensors.Clear();
                    }
                }
                if (isStatus) Message = PartitionStatuses.Where(x => x.Byte2 == Byte2id).Select(x => x.Name).DefaultIfEmpty($"Status_{Byte2id}").First();
                if (isTrouble) Message = SystemTroubles.Where(x => x.Byte2 == Byte2id).Select(x => x.Name).DefaultIfEmpty($"Trouble_{Byte2id}").First();
                if (isSpecialAlarm) Message = SpecialAlarms.Where(x => x.Byte2 == Byte2id).Select(x => x.Name).DefaultIfEmpty($"SpecialAlarm_{Byte2id}").First();
                if (isSpecialArm) Message = SpecialArms.Where(x => x.Byte2 == Byte2id).Select(x => x.Name).DefaultIfEmpty($"SpecialArm_{Byte2id}").First();
                if (isSpecialDisarm) Message = SpecialDisarms.Where(x => x.Byte2 == Byte2id).Select(x => x.Name).DefaultIfEmpty($"SpecialDisarm_{Byte2id}").First();
                if (isNonReportEvents) Message = NonReportableEvents.Where(x => x.Byte2 == Byte2id).Select(x => x.Name).DefaultIfEmpty($"NonReportEvent_{Byte2id}").First();
                if (isSpecialReport) Message = SpecialReportings.Where(x => x.Byte2 == Byte2id).Select(x => x.Name).DefaultIfEmpty($"SpecialReporting_{Byte2id}").First();
                if (isRemoteControl) Message = $"Remote_{Byte2id}";
                if (isAccessCode) Message = $"AccessCode_{Byte2id}";

                Console.WriteLine($"{Program.DateTimeTZ():HH:mm:ss,ff} {Event}, {Message}");
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
            bool isDoorOpen, isIrOpen;
            bool isSmokeOpen = false;
            bool isQueueCleared = false;
            string status = "No pattern";
            List<State> _queue = new List<State>();

            while (true)
            {
                try
                {
                    //check the last sensor time to calculate is there someone at home
                    Zone LastActiveZone = Zones.Last();
                    var timerInMinutes = TelemetryDataClass.isHomeSecured ? 1 : 60;
                    var DurationUntilHouseIsEmpty = !LastActiveZone.IsZoneOpen ? (Program.DateTimeTZ() - LastActiveZone.ZoneEventTime).TotalMinutes : 0;
                    SomeoneAtHome.IsSomeoneAtHome = DurationUntilHouseIsEmpty < timerInMinutes;

                    Zone doorZone = Zones.First(ir => ir.Byte2 == "11");
                    Zone IrZone = Zones.First(ir => ir.Byte2 == "21");
                    Zone smokeZone = Zones.First(ir => ir.Byte2 == "71");
                    isDoorOpen = doorZone.IsZoneOpen;
                    isIrOpen = IrZone.IsZoneOpen;
                    isSmokeOpen = smokeZone.IsZoneOpen;

                    //if door or IR is closed more that 2 minutes then clear the queue
                    var clearDuration = TimeSpan.FromSeconds(120).TotalSeconds;
                    var durationUntilReset = doorZone.ZoneEventTime > IrZone.ZoneEventTime ?
                        (Program.DateTimeTZ() - doorZone.ZoneEventTime).TotalSeconds :
                        (Program.DateTimeTZ() - IrZone.ZoneEventTime).TotalSeconds;
                    var isClearTime = durationUntilReset % clearDuration >= clearDuration - 1;
                    if (isClearTime && !isDoorOpen && !isQueueCleared)
                    {
                        _queue.Clear();
                        _queue.Add(new State { DoorValue = false, IRValue = false });
                    }
                    isQueueCleared = isClearTime;

                    //save the door and IR statuses for the queue
                    State _state = new State { DoorValue = isDoorOpen, IRValue = isIrOpen };

                    if (_queue.Count > 6)
                    {
                        _queue = new List<State>(Helpers.Rotate(_queue, 1));
                        _queue.RemoveAt(_queue.Count - 1);
                    }

                    //if list is empty, then return both open, otherwise last item
                    State lastItem = (_queue.Count != 0) ? _queue[_queue.Count - 1] : new State { DoorValue = true, IRValue = true };

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

        public static List<Event> events = new List<Event>
        {
            new Event(){Byte1 = "00", EventCategory = Category.ZONE, EventName = "Zone OK"},
            new Event(){Byte1 = "04", EventCategory = Category.ZONE, EventName = "Zone Open"},
            new Event(){Byte1 = "08", EventCategory = Category.STATUS, EventName = "Partition Status"},
            new Event(){Byte1 = "14", EventCategory = Category.NON_REPORT_EVENTS, EventName = "Non-Reportable Events"},
            new Event(){Byte1 = "18", EventCategory = Category.REMOTE_CONTROL, EventName = "Arm/Disarm with Remote Control"},
            new Event(){Byte1 = "1C", EventCategory = Category.REMOTE_CONTROL, EventName = "Button Pressed on Remote (B)"},
            new Event(){Byte1 = "20", EventCategory = Category.REMOTE_CONTROL, EventName = "Button Pressed on Remote (C)"},
            new Event(){Byte1 = "24", EventCategory = Category.REMOTE_CONTROL, EventName = "Button Pressed on Remote (D)"},
            new Event(){Byte1 = "28", EventCategory = Category.ACCESS_CODE, EventName = "Bypass programming"},
            new Event(){Byte1 = "29", EventCategory = Category.ACCESS_CODE, EventName = "Bypass programming"},
            new Event(){Byte1 = "2A", EventCategory = Category.ACCESS_CODE, EventName = "Bypass programming"},
            new Event(){Byte1 = "2B", EventCategory = Category.ACCESS_CODE, EventName = "Bypass programming"},
            new Event(){Byte1 = "2C", EventCategory = Category.ACCESS_CODE, EventName = "User Activated PGM"},
            new Event(){Byte1 = "2D", EventCategory = Category.ACCESS_CODE, EventName = "User Activated PGM"},
            new Event(){Byte1 = "2E", EventCategory = Category.ACCESS_CODE, EventName = "User Activated PGM"},
            new Event(){Byte1 = "2F", EventCategory = Category.ACCESS_CODE, EventName = "User Activated PGM"},
            new Event(){Byte1 = "30", EventCategory = Category.ZONE, EventName = "Zone with delay is breached"},
            new Event(){Byte1 = "34", EventCategory = Category.ACCESS_CODE, EventName = "Arm"},
            new Event(){Byte1 = "35", EventCategory = Category.ACCESS_CODE, EventName = "Arm"},
            new Event(){Byte1 = "36", EventCategory = Category.ACCESS_CODE, EventName = "Arm"},
            new Event(){Byte1 = "37", EventCategory = Category.ACCESS_CODE, EventName = "Arm"},
            new Event(){Byte1 = "38", EventCategory = Category.SPECIAL_ARM, EventName = "Special Arm"},
            new Event(){Byte1 = "3C", EventCategory = Category.ACCESS_CODE, EventName = "Disarm"},
            new Event(){Byte1 = "3D", EventCategory = Category.ACCESS_CODE, EventName = "Disarm"},
            new Event(){Byte1 = "3E", EventCategory = Category.ACCESS_CODE, EventName = "Disarm"},
            new Event(){Byte1 = "3F", EventCategory = Category.ACCESS_CODE, EventName = "Disarm"},
            new Event(){Byte1 = "40", EventCategory = Category.ACCESS_CODE, EventName = "Disarm after Alarm"},
            new Event(){Byte1 = "41", EventCategory = Category.ACCESS_CODE, EventName = "Disarm after Alarm"},
            new Event(){Byte1 = "42", EventCategory = Category.ACCESS_CODE, EventName = "Disarm after Alarm"},
            new Event(){Byte1 = "43", EventCategory = Category.ACCESS_CODE, EventName = "Disarm after Alarm"},
            new Event(){Byte1 = "44", EventCategory = Category.ACCESS_CODE, EventName = "Cancel Alarm"},
            new Event(){Byte1 = "45", EventCategory = Category.ACCESS_CODE, EventName = "Cancel Alarm"},
            new Event(){Byte1 = "46", EventCategory = Category.ACCESS_CODE, EventName = "Cancel Alarm"},
            new Event(){Byte1 = "47", EventCategory = Category.ACCESS_CODE, EventName = "Cancel Alarm"},
            new Event(){Byte1 = "48", EventCategory = Category.SPECIAL_DISARM, EventName = "Special Disarm"},
            new Event(){Byte1 = "4C", EventCategory = Category.ZONE, EventName = "Zone Bypassed on arming"},
            new Event(){Byte1 = "50", EventCategory = Category.ZONE, EventName = "Zone in Alarm"},
            new Event(){Byte1 = "54", EventCategory = Category.ZONE, EventName = "Fire Alarm"},
            new Event(){Byte1 = "58", EventCategory = Category.ZONE, EventName = "Zone Alarm restore"},
            new Event(){Byte1 = "5C", EventCategory = Category.ZONE, EventName = "Fire Alarm restore"},
            new Event(){Byte1 = "60", EventCategory = Category.SPECIAL_ALARM, EventName = "Special alarm"},
            new Event(){Byte1 = "64", EventCategory = Category.ZONE, EventName = "Auto zone shutdown"},
            new Event(){Byte1 = "68", EventCategory = Category.ZONE, EventName = "Zone tamper"},
            new Event(){Byte1 = "6C", EventCategory = Category.ZONE, EventName = "Zone tamper restore"},
            new Event(){Byte1 = "70", EventCategory = Category.TROUBLE, EventName = "System Trouble"},
            new Event(){Byte1 = "74", EventCategory = Category.TROUBLE, EventName = "System Trouble restore"},
            new Event(){Byte1 = "78", EventCategory = Category.SPECIAL_REPORT, EventName = "Special Reporting"},
            new Event(){Byte1 = "7C", EventCategory = Category.ZONE, EventName = "Wireless Transmitter Supervision Loss"},
            new Event(){Byte1 = "80", EventCategory = Category.ZONE, EventName = "Wireless Transmitter Supervision Loss Restore"},
            new Event(){Byte1 = "84", EventCategory = Category.ZONE, EventName = "Arming with a Keyswitch"},
            new Event(){Byte1 = "88", EventCategory = Category.ZONE, EventName = "Disarming with a Keyswitch"},
            new Event(){Byte1 = "8C", EventCategory = Category.ZONE, EventName = "Disarm after Alarm with a Keyswitch"},
            new Event(){Byte1 = "90", EventCategory = Category.ZONE, EventName = "Cancel Alarm with a Keyswitch"},
            new Event(){Byte1 = "94", EventCategory = Category.ZONE, EventName = "Wireless Transmitter Low Battery"},
            new Event(){Byte1 = "98", EventCategory = Category.ZONE, EventName = "Wireless Transmitter Low Battery Restore"}
        };
        public static List<Byte2Data> PartitionStatuses = new List<Byte2Data>
        {
            new Byte2Data(){Byte2 = "01", Name = "System not ready"},
            new Byte2Data(){Byte2 = "11", Name = "System ready"},
            new Byte2Data(){Byte2 = "21", Name = "Steady alarm"},
            new Byte2Data(){Byte2 = "31", Name = "Pulsed alarm"},
            new Byte2Data(){Byte2 = "41", Name = "Pulsed or Steady Alarm"},
            new Byte2Data(){Byte2 = "51", Name = "Alarm in partition restored"},
            new Byte2Data(){Byte2 = "61", Name = "Bell Squawk Activated"},
            new Byte2Data(){Byte2 = "71", Name = "Bell Squawk Deactivated"},
            new Byte2Data(){Byte2 = "81", Name = "Ground start"},
            new Byte2Data(){Byte2 = "91", Name = "Disarm partition"},
            new Byte2Data(){Byte2 = "A1", Name = "Arm partition"},
            new Byte2Data(){Byte2 = "B1", Name = "Entry delay started"}
        };
        public static List<Byte2Data> SystemTroubles = new List<Byte2Data>
        {
            new Byte2Data(){Byte2 = "11", Name = "AC Loss"},
            new Byte2Data(){Byte2 = "21", Name = "Battery Failure"},
            new Byte2Data(){Byte2 = "31", Name = "Auxiliary current overload"},
            new Byte2Data(){Byte2 = "41", Name = "Bell current overload"},
            new Byte2Data(){Byte2 = "51", Name = "Bell disconnected"},
            new Byte2Data(){Byte2 = "61", Name = "Timer Loss"},
            new Byte2Data(){Byte2 = "71", Name = "Fire Loop Trouble"},
            new Byte2Data(){Byte2 = "81", Name = "Future use"},
            new Byte2Data(){Byte2 = "91", Name = "Module Fault"},
            new Byte2Data(){Byte2 = "A1", Name = "Printer Fault"},
            new Byte2Data(){Byte2 = "B1", Name = "Fail to Communicate"}
        };
        public static List<Byte2Data> NonReportableEvents = new List<Byte2Data>
        {
            new Byte2Data(){Byte2 = "01", Name = "Telephone Line Trouble"},
            new Byte2Data(){Byte2 = "11", Name = "Reset smoke detectors"},
            new Byte2Data(){Byte2 = "21", Name = "Instant arming"},
            new Byte2Data(){Byte2 = "31", Name = "Stay arming"},
            new Byte2Data(){Byte2 = "41", Name = "Force arming"},
            new Byte2Data(){Byte2 = "51", Name = "Fast Exit (Force & Regular Only)"},
            new Byte2Data(){Byte2 = "61", Name = "PC Fail to Communicate"},
            new Byte2Data(){Byte2 = "71", Name = "Midnight"}
        };
        public static List<Byte2Data> SpecialAlarms = new List<Byte2Data>
        {
            new Byte2Data(){Byte2 = "01", Name = "Emergency, keys [1] [3]"},
            new Byte2Data(){Byte2 = "11", Name = "Auxiliary, keys [4] [6]"},
            new Byte2Data(){Byte2 = "21", Name = "Fire, keys [7] [9]"},
            new Byte2Data(){Byte2 = "31", Name = "Recent closing"},
            new Byte2Data(){Byte2 = "41", Name = "Auto Zone Shutdown"},
            new Byte2Data(){Byte2 = "51", Name = "Duress alarm"},
            new Byte2Data(){Byte2 = "61", Name = "Keypad lockout"}
        };
        public static List<Byte2Data> SpecialReportings = new List<Byte2Data>
        {
            new Byte2Data(){Byte2 = "01", Name = "System power up"},
            new Byte2Data(){Byte2 = "11", Name = "Test report"},
            new Byte2Data(){Byte2 = "21", Name = "WinLoad Software Access"},
            new Byte2Data(){Byte2 = "31", Name = "WinLoad Software Access finished"},
            new Byte2Data(){Byte2 = "41", Name = "Installer enters programming mode"},
            new Byte2Data(){Byte2 = "51", Name = "Installer exits programming mode"}
        };
        public static List<Byte2Data> SpecialDisarms = new List<Byte2Data>
        {
            new Byte2Data(){Byte2 = "01", Name = "Cancel Auto Arm (timed/no movement)"},
            new Byte2Data(){Byte2 = "11", Name = "Disarm with WinLoad Software"},
            new Byte2Data(){Byte2 = "21", Name = "Disarm after alarm with WinLoad Software"},
            new Byte2Data(){Byte2 = "31", Name = "Cancel Alarm with WinLoad Software"}
        };
        public static List<Byte2Data> SpecialArms = new List<Byte2Data>
        {
            new Byte2Data(){Byte2 = "01", Name = "Auto arming (timed/no movement)"},
            new Byte2Data(){Byte2 = "11", Name = "Late to Close (Auto-Arming failed)"},
            new Byte2Data(){Byte2 = "21", Name = "No Movement Auto-Arming"},
            new Byte2Data(){Byte2 = "31", Name = "Partial Arming (Stay, Force, Instant, Bypass)"},
            new Byte2Data(){Byte2 = "41", Name = "One-Touch Arming"},
            new Byte2Data(){Byte2 = "51", Name = "Arm with WinLoad Software"},
            new Byte2Data(){Byte2 = "71", Name = "Closing Delinquency"}
        };
        public static List<Zone> Zones = new List<Zone>
        {
            new Zone(){Byte2 = "11", IsZoneOpen=false, ZoneName = "DOOR"},
            new Zone(){Byte2 = "21", IsZoneOpen=false, ZoneName = "ENTRY",},
            new Zone(){Byte2 = "31", IsZoneOpen=false, ZoneName = "LIVING ROOM"},
            new Zone(){Byte2 = "41", IsZoneOpen=false, ZoneName = "OFFICE"},
            new Zone(){Byte2 = "51", IsZoneOpen=false, ZoneName = "HALL"},
            new Zone(){Byte2 = "61", IsZoneOpen=false, ZoneName = "BEDROOM"},
            new Zone(){Byte2 = "71", IsZoneOpen=false, ZoneName = "FIRE"},
            new Zone(){Byte2 = "81", IsZoneOpen=false, ZoneName = "TECHNO"},
            new Zone(){Byte2 = "91", IsZoneOpen=false, ZoneName = "PIANO"}
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
        public string Byte1 { get; set; }
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
        public string Byte2 { get; set; }
        public string ZoneName { get; set; }
        public bool IsZoneOpen { get; set; }
        public DateTimeOffset ZoneEventTime { get; set; }
    }
    class Byte2Data
    {
        public string Byte2 { get; set; }
        public string Name { get; set; }
    }
}
