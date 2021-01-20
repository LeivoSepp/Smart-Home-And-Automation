using HomeModule.Azure;
using HomeModule.Netatmo;
using HomeModule.Parameters;
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
                if (NetatmoDataClass.Co2 > HomeParameters.CO2_LEVEL_TO_CHECK || ManualVentLogic.VENT_ON)
                {
                    _receiveData.ProcessCommand(CommandNames.OPEN_VENT);
                    ManualVentLogic.VENT_ON = false;
                    await Task.Delay(TimeSpan.FromMinutes(HomeParameters.TIMER_MINUTES_VENT_ON)); //vent turned on at least 60 minutes if manually turned on
                }
                else
                {
                    if(TelemetryDataClass.isVentilationOn) _receiveData.ProcessCommand(CommandNames.CLOSE_VENT);
                    await Task.Delay(TimeSpan.FromMinutes(HomeParameters.TIMER_MINUTES_CHECK_CO2)); //check co2 turn on condition every 5 minute
                }
            }
        }
    }
}
