using HomeModule.Azure;
using System;
using System.Threading.Tasks;
using System.Device.Gpio;
using System.Collections.Generic;

namespace HomeModule.Raspberry
{
    class Pins
    {
        private SendDataAzure _sendListData;
        private static readonly ReceiveData _receiveData = new ReceiveData();
        public static GpioController gpio = new GpioController(PinNumberingScheme.Logical);

        //OUTPUT pins
        public const int waterOutPin = 5;
        public const int ventOutPin = 6;
        public const int normalTempOutPin = 22;
        public const int heatOnOutPin = 27;
        public const int floorPumpOutPin = 21;
        public const int saunaHeatOutPin = 20;
        public const int homeOfficeHeatControlOut = 19;
        public const int livingRoomHeatControlOut = 26;

        //public const int greenLedPin = 47;
        //public const int redLedPin = 35;

        //INPUT pins
        public const int btnWaterPin = 13;
        public const int btnVentPin = 4;
        public const int btnNormalTempPin = 18;
        public const int btnHeatOnPin = 17;
        public const int roomHeatInPin = 25;
        public const int waterHeatInPin = 23;
        public const int SaunaDoorInPin = 16;

        private static bool IsRoomHeatPinOn
        {
            get { return _isRoomHeatPinOn; }
            set
            {
                if (_isRoomHeatPinOn != value)
                {
                    _isRoomHeatPinOn = value;
                    if (_isRoomHeatPinOn)
                        Pin_HeatingRisingAsync();
                    else
                        Pin_HeatingFallingAsync();
                    IsRoomHeatingOn = _isRoomHeatPinOn && !IsWaterHeatPinOn;
                    IsWaterHeatingOn = _isRoomHeatPinOn && IsWaterHeatPinOn;
                }
            }
        }
        private static bool _isRoomHeatPinOn;

        private static bool IsWaterHeatPinOn
        {
            get { return _isWaterHeatPinOn; }
            set
            {
                if (_isWaterHeatPinOn != value)
                {
                    _isWaterHeatPinOn = value;
                    if (_isWaterHeatPinOn)
                        Pin_WaterRisingAsync();
                    else
                        Pin_WaterFallingAsync();
                    IsRoomHeatingOn = IsRoomHeatPinOn && !_isWaterHeatPinOn;
                    IsWaterHeatingOn = IsRoomHeatPinOn && _isWaterHeatPinOn;
                }
            }
        }
        private static bool _isWaterHeatPinOn;

        public static bool IsSaunaDoorOpen
        {
            get { return _isSaunaDoorOpen; }
            set
            {
                if (_isSaunaDoorOpen != value)
                {
                    _isSaunaDoorOpen = value;
                }
            }
        }
        private static bool _isSaunaDoorOpen;

        public static bool IsRoomHeatingOn;
        public static bool IsWaterHeatingOn;

        private static Dictionary<int, PinValue> pinStates = new Dictionary<int, PinValue>()
            {
                {waterOutPin, PinValue.High},
                {ventOutPin, PinValue.High},
                {normalTempOutPin, PinValue.High},
                {heatOnOutPin, PinValue.High},
                {floorPumpOutPin, PinValue.High},
                {saunaHeatOutPin, PinValue.High},
                {homeOfficeHeatControlOut, PinValue.High},
                {livingRoomHeatControlOut, PinValue.High}
            };
        public static void PinWrite(int key, PinValue value)
        {
            gpio.Write(key, value);
            pinStates[key] = value;
        }
        public static PinValue PinRead(int key)
        {
            return pinStates[key];
        }
        public void ConnectGpio()
        {
            gpio.OpenPin(waterOutPin, PinMode.Output);
            gpio.OpenPin(ventOutPin, PinMode.Output);
            gpio.OpenPin(normalTempOutPin, PinMode.Output);
            gpio.OpenPin(heatOnOutPin, PinMode.Output);
            gpio.OpenPin(floorPumpOutPin, PinMode.Output);
            gpio.OpenPin(saunaHeatOutPin, PinMode.Output);
            gpio.OpenPin(homeOfficeHeatControlOut, PinMode.Output);
            gpio.OpenPin(livingRoomHeatControlOut, PinMode.Output);

            PinWrite(waterOutPin, PinValue.Low);
            PinWrite(ventOutPin, PinValue.Low);
            PinWrite(normalTempOutPin, PinValue.Low);
            PinWrite(heatOnOutPin, PinValue.Low);
            PinWrite(floorPumpOutPin, PinValue.Low);
            PinWrite(homeOfficeHeatControlOut, PinValue.High);
            PinWrite(livingRoomHeatControlOut, PinValue.High);
            PinWrite(saunaHeatOutPin, PinValue.High);

            //summertime: waterheat is always high "waterheat mode", LED is on
            //otherwise - roomheat pin is high when heating.
            //waterheat pin is high when water water-LED is on on the system (kraaniga märk)
            gpio.OpenPin(btnWaterPin, PinMode.InputPullUp); //PullUp - kõik PIN-d is High by default, kontrollitud!
            gpio.OpenPin(btnVentPin, PinMode.InputPullUp);
            gpio.OpenPin(btnNormalTempPin, PinMode.InputPullUp);
            gpio.OpenPin(btnHeatOnPin, PinMode.InputPullUp);
            gpio.OpenPin(roomHeatInPin, PinMode.InputPullUp);
            gpio.OpenPin(waterHeatInPin, PinMode.InputPullUp);
            gpio.OpenPin(SaunaDoorInPin, PinMode.InputPullUp);

            //kui on sama sisendi peale falling ja rising - siis hakkab segast peksma.
            //isegi siis peksab segast kui on roomheat ja waterheat mõlemad ainult falling
        }

        public async void LoopGpioPins()
        {
            //this loop will cycle in 70ms to check indefinitely all the pins
            //it's a better solution than using event handlers
            while (true)
            {
                //if water LED is ON by default, then water has been raised
                IsWaterHeatPinOn = (bool)gpio.Read(waterHeatInPin);
                await Task.Delay(TimeSpan.FromMilliseconds(10));

                //if heating LED is ON by default, then heating has been raised
                IsRoomHeatPinOn = (bool)gpio.Read(roomHeatInPin);
                await Task.Delay(TimeSpan.FromMilliseconds(10));

                //is sauna door open/closed on program sartup
                IsSaunaDoorOpen = (bool)gpio.Read(SaunaDoorInPin);
                await Task.Delay(TimeSpan.FromMilliseconds(10));

                //check the buttons
                SetButtonPressed((bool)gpio.Read(btnWaterPin), btnWaterPin);
                await Task.Delay(TimeSpan.FromMilliseconds(10));
                SetButtonPressed((bool)gpio.Read(btnVentPin), btnVentPin);
                await Task.Delay(TimeSpan.FromMilliseconds(10));
                SetButtonPressed((bool)gpio.Read(btnNormalTempPin), btnNormalTempPin);
                await Task.Delay(TimeSpan.FromMilliseconds(10));
                SetButtonPressed((bool)gpio.Read(btnHeatOnPin), btnHeatOnPin);
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }
        }

        private void SetButtonPressed(bool value, int button)
        {
            if (_isButtonPressed != value)
            {
                _isButtonPressed = value;
                if (!_isButtonPressed) Btn_ValueFalling(button);
            }
        }
        private bool _isButtonPressed;

        //button commands
        private void Btn_ValueFalling(int PressedButton)
        {
            string onCommand, offCommand;
            int ledValuePin;
            switch (PressedButton)
            {
                case btnWaterPin:
                    onCommand = CommandNames.TURN_ON_HOTWATERPUMP;
                    offCommand = CommandNames.TURN_OFF_HOTWATERPUMP;
                    ledValuePin = waterOutPin;
                    break;
                case btnVentPin:
                    onCommand = CommandNames.OPEN_VENT;
                    offCommand = CommandNames.CLOSE_VENT;
                    ledValuePin = ventOutPin;
                    break;
                case btnNormalTempPin:
                    onCommand = CommandNames.NORMAL_TEMP_COMMAND;
                    offCommand = CommandNames.REDUCE_TEMP_COMMAND;
                    ledValuePin = normalTempOutPin;
                    break;
                case btnHeatOnPin:
                    onCommand = CommandNames.TURN_ON_HEATING;
                    offCommand = CommandNames.TURN_OFF_HEATING;
                    ledValuePin = heatOnOutPin;
                    break;
                default:
                    onCommand = null;
                    offCommand = null;
                    ledValuePin = 0;
                    break;
            }

            var ledValue = PinRead(ledValuePin);
            while (!(bool)gpio.Read(PressedButton)) ; //loop is running until button pressed

            string command = ((bool)ledValue) ? offCommand : onCommand;
            _receiveData.ProcessCommand(command);
        }
        public async Task SendData()
        {
            _sendListData = new SendDataAzure();
            var monitorData = new
            {
                DeviceID = "HomeController",
                status = TelemetryDataClass.SourceInfo,
                TelemetryDataClass.RoomHeatingInMinutes,
                TelemetryDataClass.WaterHeatingInMinutes,
                TelemetryDataClass.VentilationInMinutes,
                UtcOffset = Program.DateTimeTZ().Offset.Hours,
                DateAndTime = Program.DateTimeTZ().DateTime,
            };
            await _sendListData.PipeMessage(monitorData, Program.IoTHubModuleClient, TelemetryDataClass.SourceInfo);
            return;
        }
        static DateTime dateTimeRoomHeat = Program.DateTimeTZ().DateTime;
        static DateTime dateTimeWaterHeat = Program.DateTimeTZ().DateTime;
        static bool IsWaterJustFinished = false;
        //below is a bunch of logic which you probaly wont understand later
        //this piece of code is working based on ground-heating system LED-s events
        private static async void Pin_WaterRisingAsync() //winter only
        {
            var _sendData = new Pins();
            if (IsRoomHeatPinOn) //if roomheating LED was on, stop roomheating
            {
                TelemetryDataClass.RoomHeatingInMinutes = (int)(Program.DateTimeTZ().DateTime - dateTimeRoomHeat).TotalMinutes;
                TelemetryDataClass.SourceInfo = $"5. Roomheating {TelemetryDataClass.RoomHeatingInMinutes} min, Start Waterheating";
                await _sendData.SendData();
                TelemetryDataClass.RoomHeatingInMinutes = 0;
                dateTimeWaterHeat = Program.DateTimeTZ().DateTime; //start waterheating
            }
        }
        private static async void Pin_WaterFallingAsync() //winter only
        {
            IsWaterJustFinished = true;
            var _sendData = new Pins();
            TelemetryDataClass.WaterHeatingInMinutes = (int)(Program.DateTimeTZ().DateTime - dateTimeWaterHeat).TotalMinutes;
            TelemetryDataClass.SourceInfo = $"7. Waterheating {TelemetryDataClass.WaterHeatingInMinutes} min";
            await _sendData.SendData();
            TelemetryDataClass.WaterHeatingInMinutes = 0;

            await Task.Delay(TimeSpan.FromSeconds(3)); //wait for a 3 seconds for the roomheat LED, is it turning on or off?
            IsWaterJustFinished = false;
            //waterheating just finished and roomheating will continue nonstop
            if (IsRoomHeatPinOn) dateTimeRoomHeat = Program.DateTimeTZ().DateTime; //start roomheat
        }
        private static void Pin_HeatingRisingAsync() //winter and summer
        {
            if (!IsWaterHeatPinOn) dateTimeRoomHeat = Program.DateTimeTZ().DateTime; //start roomheat
            if (IsWaterHeatPinOn) dateTimeWaterHeat = Program.DateTimeTZ().DateTime; //start waterheat
        }
        private static async void Pin_HeatingFallingAsync() //winter and summer
        {
            var _sendData = new Pins();
            if (!IsWaterJustFinished && !IsWaterHeatPinOn) //if waterheating LED is off AND the waterheating counter isnt started then stop roomheating
            {
                TelemetryDataClass.RoomHeatingInMinutes = (int)(Program.DateTimeTZ().DateTime - dateTimeRoomHeat).TotalMinutes;
                TelemetryDataClass.SourceInfo = $"3. Roomheating {TelemetryDataClass.RoomHeatingInMinutes} min";
                await _sendData.SendData();
                TelemetryDataClass.RoomHeatingInMinutes = 0;
            }

            await Task.Delay(TimeSpan.FromSeconds(3)); //wait for a 3 seconds, maybe the waterheating just finished and it is already off?
            if (IsWaterHeatPinOn) //heating system in WATERHEATING mode, waterheating LED always on, this is for summer time
            {
                TelemetryDataClass.WaterHeatingInMinutes = (int)(Program.DateTimeTZ().DateTime - dateTimeWaterHeat).TotalMinutes;
                TelemetryDataClass.SourceInfo = $"4. Waterheating {TelemetryDataClass.WaterHeatingInMinutes} min";
                await _sendData.SendData();
                TelemetryDataClass.WaterHeatingInMinutes = 0;
            }
        }
    }
}
