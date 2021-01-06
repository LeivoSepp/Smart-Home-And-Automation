using HomeModule.Azure;
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
                if (NetatmoDataClass.Co2 > 900 || ManualVentLogic.VENT_ON)
                {
                    _receiveData.ProcessCommand(CommandNames.OPEN_VENT);
                    ManualVentLogic.VENT_ON = false;
                    await Task.Delay(TimeSpan.FromMinutes(60)); //vent turned on at least 60 minutes if manually turned on
                }
                else
                {
                    if(TelemetryDataClass.isVentilationOn) _receiveData.ProcessCommand(CommandNames.CLOSE_VENT);
                    await Task.Delay(TimeSpan.FromMinutes(5)); //check co2 turn on condition every 5 minute
                }
            }
        }
    }
}
