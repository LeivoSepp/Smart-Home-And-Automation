using HomeModule.Azure;
using HomeModule.EnergyPrice;
using HomeModule.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeModule.Schedulers
{
    class Heating
    {
        public async void ReduceHeatingSchedulerAsync()
        {
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
                    if (item.date.DateTime.Hour == CurrentDateTime.DateTime.Hour)
                    {
                        HeatingMode = item.heat;
                        isHotWaterTime = item.isHotWaterTime;
                        break;
                    }
                }
                ////turn manual heating off in every hour
                //TelemetryDataClass.isNormalHeatingManual = false;

                ////turn manual hot water off in every hour
                //TelemetryDataClass.isHotWaterManual = false;

                //this is used in ReadTemperature scheduler to turn on or off the heating
                TelemetryDataClass.isHeatingTime = HeatingMode == CONSTANT.NORMAL_HEATING ? true : false;

                //this is used in ReadTemperature scheduler to turn on or off the hot water
                TelemetryDataClass.isHotWaterTime = isHotWaterTime;

                int secondsToNextHour = 3600 - (int)CurrentDateTime.DateTime.TimeOfDay.TotalSeconds % 3600;
                await Task.Delay(TimeSpan.FromSeconds(secondsToNextHour));
            }
        }
    }
}
