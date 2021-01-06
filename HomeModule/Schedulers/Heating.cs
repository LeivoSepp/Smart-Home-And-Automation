using System;
using System.Threading.Tasks;
using HomeModule.EnergyPrice;
using System.Collections.Generic;
using HomeModule.Azure;

namespace HomeModule.Schedulers
{
    class Heating
    {
        public async void ReduceHeatingSchedulerAsync()
        {
            var _receiveData = new ReceiveData();
            var _receiveEnergyPrice = new ReceiveEnergyPrice();
            List<EnergyPriceClass> _realTimeEnergyPrices = await _receiveEnergyPrice.QueryEnergyPriceAsync(); //run on app startup
            while (true)
            {
                if (Program.DateTimeTZ().DateTime.Hour == 00 || _realTimeEnergyPrices.Count == 0) //run once every day at 00:00 to get energy prices and heating schedule
                {
                    _realTimeEnergyPrices = await _receiveEnergyPrice.QueryEnergyPriceAsync();
                    Console.WriteLine($"Energy price query {Program.DateTimeTZ().DateTime}");
                }
                int HeatingMode = HeatingParams.NORMAL_HEATING;
                bool isHotWaterTime = true;
                //get the current state of heating and hot water
                foreach (var item in _realTimeEnergyPrices)
                {
                    if(item.date.DateTime.Hour == Program.DateTimeTZ().DateTime.Hour)
                    {
                        HeatingMode = item.heat;
                        isHotWaterTime = item.isHotWaterTime;
                        break;
                    }
                }
                //lets control the heating system according to the heating schedule
                switch (HeatingMode)
                {
                    case HeatingParams.NORMAL_HEATING:
                        _receiveData.ProcessCommand(CommandNames.NORMAL_TEMP_COMMAND);
                        break;
                    case HeatingParams.REDUCED_HEATING:
                        _receiveData.ProcessCommand(CommandNames.REDUCE_TEMP_COMMAND);
                        break;
                    case HeatingParams.EVU_STOP:
                        _receiveData.ProcessCommand(CommandNames.TURN_OFF_HEATING);
                        break;
                }
                //lets control hot water based on activity time and weekend
                string cmd = isHotWaterTime ? CommandNames.TURN_ON_HOTWATERPUMP : CommandNames.TURN_OFF_HOTWATERPUMP;
                _receiveData.ProcessCommand(cmd);

                int secondsToNextHour = 3600 - (int)Program.DateTimeTZ().DateTime.TimeOfDay.TotalSeconds % 3600;
                await Task.Delay(TimeSpan.FromSeconds(secondsToNextHour));
            }
        }
    }
}
