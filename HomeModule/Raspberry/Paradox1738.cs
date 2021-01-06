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
        public static string alertingSensors = "";

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

                string EventID = DataStream[0].ToString("X2");
                string Event = events.Where(x => x.Data == EventID).Select(x => x.EventName).DefaultIfEmpty($"NoName {EventID}").First();
                int EventCategory = events.Where(x => x.Data == EventID).Select(x => x.EventCategory).DefaultIfEmpty(DataStream[0]).First();

                string MessageID = DataStream[1].ToString("X2");
                string Message = MessageID;

                bool isZoneAction = EventCategory == Category.ZONE;
                bool isUserAction = EventCategory == Category.USER;
                bool isTrouble = EventCategory == Category.TROUBLE;
                bool isStatus = EventCategory == Category.STATUS;

                if (isZoneAction)
                {
                    //save the IRState into zone's list
                    bool IsZoneOpen = false;
                    if (EventID == "04") IsZoneOpen = true;
                    zones.Where(x => x.Data == MessageID).Select(x => { x.IsZoneOpen = IsZoneOpen; return x; }).ToList();
                    Message = zones.Where(x => x.Data == MessageID).Select(x => $"{x.ZoneName} {(x.IsZoneOpen ? "Open" : "Closed")}").DefaultIfEmpty($"NoName {MessageID}").First();
                    Console.Write($"{Program.DateTimeTZ().DateTime:HH:mm:ss,ff} {Message}");
                    Console.WriteLine();
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
            List<State> RepeatDoor = new List<State> { DOOR, ALL }; //repeat
            List<State> RepeatIR = new List<State> { IR, ALL }; //repeat
            bool _doorValue, _iRValue;
            bool smokeDetector = false;
            string status = "No pattern";
            List<State> _queue = new List<State>();

            while (true)
            {
                try
                {
                    if (zones.Any(x => x.IsZoneOpen)) SomeoneAtHome.OnSomeoneMovingAtHome();
                    //making string of the alerting sensors
                    alertingSensors = null;
                    zones.ForEach(x => { if (x.IsZoneOpen) { alertingSensors += $"{x.ZoneName}, "; } });

                    _doorValue = zones.First(ir => ir.Data == "11").IsZoneOpen;
                    _iRValue = zones.First(ir => ir.Data == "21").IsZoneOpen;
                    smokeDetector = zones.First(ir => ir.Data == "71").IsZoneOpen;

                    //save the door and IR statuses to put them into queue
                    State _state = new State { DoorValue = _doorValue, IRValue = _iRValue };

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
                        if (RemoveDuplicate(_queue, RepeatDoor)) Console.WriteLine($"Door duplicate removed");
                        if (RemoveDuplicate(_queue, RepeatIR)) Console.WriteLine($"IR duplicate removed");

                        if (_queue.Count > 2) //only check pattern if there are more than 3 events 
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
                        _queue.Add(NONE); //add first all-closed pattern

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
            new Event(){Data = "00", EventCategory = Category.ZONE, EventName = "Zone Closed"},
            new Event(){Data = "04", EventCategory = Category.ZONE, EventName = "Zone Open"},
            new Event(){Data = "08", EventCategory = Category.STATUS, EventName = "Status"},
            new Event(){Data = "34", EventCategory = Category.USER, EventName = "Arming"},
            new Event(){Data = "3C", EventCategory = Category.USER, EventName = "Disarming"},
            new Event(){Data = "40", EventCategory = Category.USER, EventName = "Disarming after Alarm"},
            new Event(){Data = "50", EventCategory = Category.ZONE, EventName = "Zone in Alarm"},
            new Event(){Data = "58", EventCategory = Category.ZONE, EventName = "Zone Alarm restore"},
            new Event(){Data = "70", EventCategory = Category.TROUBLE, EventName = "Trouble fail"},
            new Event(){Data = "74", EventCategory = Category.TROUBLE, EventName = "Trouble back to normal"}
        };
        public static List<Status> statuses = new List<Status>
        {
            new Status(){Data = "01", StatusMessage = "Zones open"},
            new Status(){Data = "11", StatusMessage = "Zones closed"},
            new Status(){Data = "21", StatusMessage = "Alarm21/Bell"},
            new Status(){Data = "41", StatusMessage = "Alarm41/Bell"},
            new Status(){Data = "51", StatusMessage = "Alarm occurred during arm"},
            new Status(){Data = "61", StatusMessage = "ArmCode61"},
            new Status(){Data = "71", StatusMessage = "ArmCode71"},
            new Status(){Data = "91", StatusMessage = "Disarmed"},
            new Status(){Data = "A1", StatusMessage = "Armed"},
            new Status(){Data = "B1", StatusMessage = "Entry delay started"},
        };
        public static List<Trouble> troubles = new List<Trouble>
        {
            new Trouble(){Data = "21", TroubleName = "Battery"},
            new Trouble(){Data = "51", TroubleName = "Bell"}
        };
        public static List<Zone> zones = new List<Zone>
        {
            new Zone(){Data = "11", IsZoneOpen=false, ZoneName = "DOOR"},
            new Zone(){Data = "21", IsZoneOpen=false, ZoneName = "ENTRY/PIANO",},
            new Zone(){Data = "31", IsZoneOpen=false, ZoneName = "LIVING ROOM"},
            new Zone(){Data = "41", IsZoneOpen=false, ZoneName = "OFFICE"},
            new Zone(){Data = "51", IsZoneOpen=false, ZoneName = "HALL"},
            new Zone(){Data = "61", IsZoneOpen=false, ZoneName = "BEDROOM"},
            new Zone(){Data = "71", IsZoneOpen=false, ZoneName = "FIRE"},
            new Zone(){Data = "81", IsZoneOpen=false, ZoneName = "TECHNO",}
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
        public string Data { get; set; }
        public string EventName { get; set; }
        public int EventCategory { get; set; }
    }
    class Category
    {
        public const int ZONE = 1;
        public const int STATUS = 2;
        public const int TROUBLE = 3;
        public const int USER = 4;
    }
    class Zone
    {
        public string Data { get; set; }
        public string ZoneName { get; set; }
        public bool IsZoneOpen { get; set; }
    }
    class Trouble
    {
        public string Data { get; set; }
        public string TroubleName { get; set; }
    }
    class Status
    {
        public string Data { get; set; }
        public string StatusMessage { get; set; }
    }
}
