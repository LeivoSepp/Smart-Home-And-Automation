using HomeModule.Azure;
using HomeModule.Models;
using HomeModule.Parameters;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace HomeModule.Schedulers
{
    class SaunaHeating
    {
        private ReceiveData _receiveData = new ReceiveData();
        private FileOperations fileOperations = new FileOperations();
        public async void CheckHeatingTime()
        {
            while (true)
            {
                if (TelemetryDataClass.SaunaStartedTime == DateTime.MinValue)
                {
                    var filename = fileOperations.GetFilePath(HomeParameters.FILENAME_SAUNA_TIME);
                    if (File.Exists(filename)) //this mean that sauna has been started and system has suddenly restarted/updated
                    {
                        var result = await fileOperations.OpenExistingFile(filename);
                        if (DateTime.TryParseExact(result, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var SaunaStartedTime))
                        {
                            TelemetryDataClass.SaunaStartedTime = SaunaStartedTime;
                        }
                    }
                }
                if (TelemetryDataClass.SaunaStartedTime != DateTime.MinValue)
                {
                    int TotalTimeSaunaHeatedInMinutes = (int)(Program.DateTimeTZ().DateTime - TelemetryDataClass.SaunaStartedTime).TotalMinutes;
                    string cmd = CommandNames.NO_COMMAND;
                    //turn saun on if it has heating time but not turned on
                    if (TotalTimeSaunaHeatedInMinutes < HomeParameters.MAX_SAUNA_HEATING_TIME && !TelemetryDataClass.isSaunaOn)
                        cmd = CommandNames.TURN_ON_SAUNA;
                    //turn sauna off if the time is over
                    if (TotalTimeSaunaHeatedInMinutes > HomeParameters.MAX_SAUNA_HEATING_TIME)
                        cmd = CommandNames.TURN_OFF_SAUNA;
                    _receiveData.ProcessCommand(cmd);
                }
                await Task.Delay(TimeSpan.FromMinutes(1)); //check every minute
            }
        }
    }
}
