using System.Device.I2c;
using Iot.Device.Mcp23xxx;
using HomeModule.Azure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HomeModule.Schedulers;

namespace HomeModule.MCP
{
    class IRDetector
    {
        public IRDetector(bool irstate, int sensorid, string sensorname)
        {
            irState = irstate;
            sensorID = sensorid;
            sensorName = sensorname;
        }
        public bool irState;
        public int sensorID;
        public string sensorName;
    }
    static class Helpers
    {
        public static List<T> Rotate<T>(this List<T> list, int offset)
        {
            return list.Skip(offset).Concat(list.Take(offset)).ToList();
        }
    }

    internal class HomeSecurity
    {
        public static string alertingSensors = "";
        private SendDataAzure _sendListData;
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

        public async void RegisterMCP23017()
        {
            _sendListData = new SendDataAzure();

            var i2cConnectionSettings = new I2cConnectionSettings(1, 0x20);
            var i2cDevice = I2cDevice.Create(i2cConnectionSettings);

            var mcp23017 = new Mcp23017(i2cDevice);

            mcp23017.WriteByte(Register.IODIR, 0b1111_1111, Port.PortA); //all ports 0-7 are input
            //mcp23017.WriteByte(Register.IODIR, 0b0000_0000, Port.PortB); //all ports 8-15 are output

            List<IRDetector> irDetectors = new List<IRDetector>
            {
                new IRDetector(true, 1, "Front door or sauna"),
                new IRDetector(true, 2, "Esik or piano hall"),
                new IRDetector(true, 3, "Elutuba"),
                new IRDetector(true, 4, "1st floor sleeping room"),
                new IRDetector(true, 5, "2nd floor hall"),
                new IRDetector(true, 6, "2nd floor sleeping room"),
                new IRDetector(true, 7, "Smoke detector"),
                new IRDetector(true, 8, "Vaba 1")
            };

            State state1 = new State { DoorValue = true, IRValue = true }; //door closed, IR passive
            State state2 = new State { DoorValue = false, IRValue = true }; //door open, IR passive
            State state3 = new State { DoorValue = false, IRValue = false }; //door open, IR active
            State state4 = new State { DoorValue = true, IRValue = false }; //door closed, IR active

            /*
             * PATTERNS
             * 
                Entry 1, door closed. Entrance, when the door is closed beforehand, with closing the door afterwards.
                1. Door closed, corridor is empty
                2. Door open, corridor is empty
                3. Door open, human in corridor
                4. Door closed, human in corridor
                5. Door closed, corridor is empty

                Entry 2, door left open. Entrance when the door is closed beforehand, without closing the door afterwards.
                1. Door closed, corridor is empty
                2. Door open, corridor is empty
                3. Door open, human in corridor
                4. Door open, corridor is empty

                Exit 1_1, door closed. Exit when the door is closed beforehand, with closing the door afterwards.
                1. Door closed, corridor is empty
                2. Door closed, human in corridor
                3. Door open, human in corridor
                4. Door open, corridor is empty
                5. Door closed, corridor is empty

                Exit 1_2, door closed. Exit when the door is closed beforehand, with closing the door afterwards.
                1. Door closed, corridor is empty
                2. Door closed, human in corridor
                3. Door open, human in corridor
                4. Door closed, corridor is empty

                Exit 1_3, door closed. Exit when the door is closed beforehand, with closing the door afterwards.
                1. Door closed, corridor is empty
                2. Door closed, human in corridor
                3. Door open, human in corridor
                4. Door closed, human in corridor
                5. Door closed, corridor is empty

                Exit 2, door left open. Exit when the door is closed beforehand, without closing the door afterwards.
                1. Door closed, corridor is empty
                2. Door closed, human in corridor
                3. Door open, human in corridor
                4. Door open, corridor is empty

                Entry-Exit 1, door closed. Entrance / exit when the door is opened beforehand, with closing the door afterwards.
                1. Door open, corridor is empty
                2. Door open, human in corridor
                3. Door closed, human in corridor
                4. Door closed, corridor is empty

                Entry-Exit 2, door left open. Entrance / exit when the door is opened beforehand, without closing the door afterwards.
                1. Door open, corridor is empty
                2. Door open, human in corridor
                3. Door open, corridor is empty

            
            (PDF) Activity Detection in Smart Home Environment.Available from: https://www.researchgate.net/publication/307914206_Activity_Detection_in_Smart_Home_Environment [accessed Oct 08 2018].
            */

            List<State> Pattern1 = new List<State>
            {
                state1,
                state2,
                state3,
                state4,
                state1
            };

            List<State> Pattern2 = new List<State>
            {
                state1,
                state2,
                state3,
                state2
            };

            List<State> Pattern31 = new List<State>
            {
                state1,
                state4,
                state3,
                state2,
                state1
            };

            List<State> Pattern32 = new List<State>
            {
                state1,
                state4,
                state3,
                state1
            };

            List<State> Pattern33 = new List<State>
            {
                state1,
                state4,
                state3,
                state4,
                state1
            };

            List<State> Pattern4 = new List<State>
            {
                state1,
                state4,
                state3,
                state2
            };

            List<State> Pattern5 = new List<State>
            {
                state2,
                state3,
                state4,
                state1
            };

            List<State> Pattern6 = new List<State>
            {
                state2,
                state3,
                state2
            };

            string status = "No pattern";
            int lastSumOfIRDetectors = 1000; //some big number to initialize first message on program startup
            bool _doorValue, _iRValue;
            bool smokeDetector = false;
            List<State> _queue = new List<State>();

            while (true)
            {
                try
                {

                    //read all the bits from PortA
                    byte sensorData = mcp23017.ReadByte(Register.GPIO, Port.PortA);
                    //LEVEL - LOW: IR detector is active, HIGH: IR detector doesnt see anything
                    //fill up the list with the IR sensor data
                    irDetectors.ForEach(x => x.irState = GetBit(sensorData, x.sensorID));

                    //summing all the room sensors together
                    int currentSumOfIRDetectors = 0;
                    irDetectors.ForEach(x => currentSumOfIRDetectors += x.irState ? 1 : 0);
                    //checking if the sensors together has changed by 1
                    //SomeoneAtHome.IsSomeoneMoving = Math.Abs(currentSumOfIRDetectors - lastSumOfIRDetectors) >= 1 &&
                                                            //(currentSumOfIRDetectors != irDetectors.Count);
                    lastSumOfIRDetectors = currentSumOfIRDetectors;

                    //making string of the alerting sensors
                    alertingSensors = null;
                    IEnumerable<IRDetector> alerts = irDetectors.Where(x => !x.irState); //check the alerting sensor
                    foreach (IRDetector sensor in alerts)
                        alertingSensors += $"{sensor.sensorName}, ";

                    _doorValue = irDetectors.First(ir => ir.sensorID == 1).irState;
                    _iRValue = irDetectors.First(ir => ir.sensorID == 2).irState;
                    smokeDetector = irDetectors.First(ir => ir.sensorID == 7).irState;

                    State _state = new State { DoorValue = _doorValue, IRValue = _iRValue };

                    if (_queue.Count > 4)
                    {
                        _queue = new List<State>(Helpers.Rotate(_queue, 1));
                        _queue.RemoveAt(_queue.Count - 1);
                    }

                    State lastItem = (_queue.Count != 0) ? _queue[_queue.Count - 1] : new State { DoorValue = true, IRValue = true }; //if list is empty, then return some unreal value, othervize last item

                    if (_state != lastItem)
                    {
                        _queue.Add(_state);
                        if (_queue.Count > 2) //only check pattern if there are more than 3 events 
                        {
                            if (ContainsPattern(_queue, Pattern1))
                            {
                                status = "Entry 1, closed";
                                _queue.Clear();
                            }
                            if (ContainsPattern(_queue, Pattern2))
                            {
                                status = "Entry 2, open";
                                _queue.Clear();
                            }
                            if (ContainsPattern(_queue, Pattern31))
                            {
                                status = "Exit 1_1, closed";
                                _queue.Clear();
                            }
                            if (ContainsPattern(_queue, Pattern32))
                            {
                                status = "Exit 1_2, closed";
                                _queue.Clear();
                            }
                            if (ContainsPattern(_queue, Pattern33))
                            {
                                status = "Exit 1_3, closed";
                                _queue.Clear();
                            }
                            if (ContainsPattern(_queue, Pattern4))
                            {
                                status = "Exit 2, open";
                                _queue.Clear();
                            }
                            if (ContainsPattern(_queue, Pattern5))
                            {
                                status = "Entry-Exit 1, closed";
                                _queue.Clear();
                            }
                            if (ContainsPattern(_queue, Pattern6))
                            {
                                status = "Entry-Exit 2, open";
                                _queue.Clear();
                            }
                        }
                    }
                    if (status != "No pattern")
                    {
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
                    Console.WriteLine("MCP23017 exception: " + e.Message);
                }
                await Task.Delay(TimeSpan.FromSeconds(1)); //check statuses every 0,5 second
            }
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
        public static bool GetBit(byte b, int bitNumber)
        {
            return (b & (1 << bitNumber - 1)) != 0;
        }
    }
}
