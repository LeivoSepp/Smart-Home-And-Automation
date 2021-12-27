using HomeModule.Azure;
using HomeModule.Helpers;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace HomeModule.Schedulers
{
    class SaunaHeating
    {
        private ReceiveData _receiveData = new ReceiveData();
        private readonly METHOD Methods = new METHOD();
        public async void CheckHeatingTime()
        {
            while (true)
            {
                if (TelemetryDataClass.SaunaStartedTime == DateTime.MinValue)
                {
                    var filename = Methods.GetFilePath(CONSTANT.FILENAME_SAUNA_TIME);
                    if (File.Exists(filename)) //this mean that sauna has been started and system has suddenly restarted/updated
                    {
                        var result = await Methods.OpenExistingFile(filename);
                        if (DateTime.TryParseExact(result, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var SaunaStartedTime))
                        {
                            TelemetryDataClass.SaunaStartedTime = SaunaStartedTime;
                        }
                    }
                }
                if (TelemetryDataClass.SaunaStartedTime != DateTime.MinValue)
                {
                    int TotalTimeSaunaHeatedInMinutes = (int)(METHOD.DateTimeTZ().DateTime - TelemetryDataClass.SaunaStartedTime).TotalMinutes;
                    //turn saun on if it has heating time but not turned on
                    if (TotalTimeSaunaHeatedInMinutes < CONSTANT.MAX_SAUNA_HEATING_TIME && !TelemetryDataClass.isSaunaOn)
                        _receiveData.ProcessCommand(CommandNames.TURN_ON_SAUNA);
                    //turn sauna off if the time is over
                    if (TotalTimeSaunaHeatedInMinutes > CONSTANT.MAX_SAUNA_HEATING_TIME && TelemetryDataClass.isSaunaOn)
                        _receiveData.ProcessCommand(CommandNames.TURN_OFF_SAUNA);
                }
                await Task.Delay(TimeSpan.FromMinutes(1)); //check every minute
            }
        }
    }
}
