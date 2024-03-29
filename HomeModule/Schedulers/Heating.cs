﻿using HomeModule.Azure;
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

            //run on app startup and get the energy prices from Cosmos
            List<EnergyPriceClass> _realTimeEnergyPrices = await _receiveEnergyPrice.QueryEnergyPriceAsync(); 
            while (true)
            {
                DateTimeOffset CurrentDateTime = METHOD.DateTimeTZ();

                //run once every day at 00:00 to get energy prices and heating schedule from Cosmos
                if (CurrentDateTime.DateTime.Hour == 00 || !_realTimeEnergyPrices.Any()) 
                {
                    _realTimeEnergyPrices = await _receiveEnergyPrice.QueryEnergyPriceAsync();
                }
                
                //get the current hour energy price
                var currentHour = _realTimeEnergyPrices.FirstOrDefault(x => x.date.DateTime.Hour == CurrentDateTime.DateTime.Hour);

                //this is used in ReadTemperature scheduler to turn on or off the heating
                TelemetryDataClass.IsHeatingTime = currentHour.heat;

                //this is used in ReadTemperature scheduler to turn on or off the hot water
                TelemetryDataClass.IsHotWaterTime = currentHour.isHotWaterTime;

                //turn off manual heating option in every hour
                TelemetryDataClass.IsHeatingTurnedOnManually = false;
                TelemetryDataClass.IsHeatingTurnedOffManually = false;

                Console.WriteLine($"\nEnergy price {currentHour.price}, hot water time {currentHour.isHotWaterTime.ToString().ToUpper()}, heating time {currentHour.heat.ToString().ToUpper()} at {currentHour.date:g}\n");

                //calculate seconds for the next hour
                int secondsToNextHour = 3600 - (int)CurrentDateTime.DateTime.TimeOfDay.TotalSeconds % 3600;
                await Task.Delay(TimeSpan.FromSeconds(secondsToNextHour));
            }
        }
    }
}
