using System;
using System.Threading.Tasks;
using HomeModule.EnergyPrice;
using System.Collections.Generic;
using HomeModule.Azure;
using System.Linq;
using HomeModule.Helpers;

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
                DateTimeOffset CurrentDateTime = METHOD.DateTimeTZ();

                if (CurrentDateTime.DateTime.Hour == 00 || !_realTimeEnergyPrices.Any()) //run once every day at 00:00 to get energy prices and heating schedule
                {
                    _realTimeEnergyPrices = await _receiveEnergyPrice.QueryEnergyPriceAsync();
                    Console.WriteLine($"Energy price query {CurrentDateTime.DateTime}");
                }
                int HeatingMode = CONSTANT.NORMAL_HEATING;
                bool isHotWaterTime = true;
                //get the current state of heating and hot water
                foreach (var item in _realTimeEnergyPrices)
                {
                    if(item.date.DateTime.Hour == CurrentDateTime.DateTime.Hour)
                    {
                        HeatingMode = item.heat;
                        isHotWaterTime = item.isHotWaterTime;
                        break;
                    }
                }
                //lets control the heating system according to the heating schedule
                string cmdHeat = null;
                if (HeatingMode == CONSTANT.NORMAL_HEATING) cmdHeat = CommandNames.NORMAL_TEMP_COMMAND;
                if (HeatingMode == CONSTANT.REDUCED_HEATING) cmdHeat = CommandNames.REDUCE_TEMP_COMMAND;
                if (HeatingMode == CONSTANT.EVU_STOP) cmdHeat = CommandNames.TURN_OFF_HEATING;
                _receiveData.ProcessCommand(cmdHeat);

                //lets control hot water based on activity time and weekend
                string cmd = isHotWaterTime ? CommandNames.TURN_ON_HOTWATERPUMP : CommandNames.TURN_OFF_HOTWATERPUMP;
                _receiveData.ProcessCommand(cmd);

                int secondsToNextHour = 3600 - (int)CurrentDateTime.DateTime.TimeOfDay.TotalSeconds % 3600;
                await Task.Delay(TimeSpan.FromSeconds(secondsToNextHour));
            }
        }
    }
}
