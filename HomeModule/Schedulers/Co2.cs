using HomeModule.Azure;
using HomeModule.Helpers;
using HomeModule.Netatmo;
using System;
using System.Threading.Tasks;

namespace HomeModule.Schedulers
{
    class ManualVentLogic
    {
        public static bool VENT_ON = false;
    }
    class Co2
    {
        private ReceiveData _receiveData;
        public async void CheckCo2Async()
        {
            _receiveData = new ReceiveData();
            while (true)
            {
                if (NetatmoDataClass.Co2 > CONSTANT.CO2_LEVEL_TO_CHECK || ManualVentLogic.VENT_ON)
                {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Run(() => _receiveData.ProcessCommand(CommandNames.OPEN_VENT));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    ManualVentLogic.VENT_ON = false;
                    await Task.Delay(TimeSpan.FromMinutes(CONSTANT.TIMER_MINUTES_VENT_ON)); //vent turned on at least 60 minutes if manually turned on
                }
                else
                {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    if (TelemetryDataClass.isVentilationOn) Task.Run(() => _receiveData.ProcessCommand(CommandNames.CLOSE_VENT));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    await Task.Delay(TimeSpan.FromMinutes(CONSTANT.TIMER_MINUTES_CHECK_CO2)); //check co2 turn on condition every 5 minute
                }
            }
        }
    }
}
