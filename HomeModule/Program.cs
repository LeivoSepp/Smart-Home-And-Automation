namespace HomeModule
{
    using HomeModule.Azure;
    using HomeModule.Netatmo;
    using HomeModule.Raspberry;
    using HomeModule.Schedulers;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using System;
    using System.Runtime.Loader;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
        public static ModuleClient IoTHubModuleClient { get; set; }

        private static Pins _raspberryPins;
        private static ReceiveNetatmoData _receiveNetatmoData;
        private static ReceiveData _receiveData;
        private static Co2 _co2Scheduler;
        private static Heating _heatingScheduler;
        private static HomeTemperature _homeTemperature;
        private static SaunaHeating _saunaHeating;
        private static SendTelemetryData _sendData;
        private static Paradox1738 _paradox1738;
        private static WiFiProbes _wiFiProbes;

        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {
            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            IoTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await IoTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

            // Register callback to be called when a message is received by the module
            //await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", PipeMessage, ioTHubModuleClient);

            //initialize Raspberry and start scheduler
            _raspberryPins = new Pins();
            _raspberryPins.ConnectGpio();
            _raspberryPins.LoopGpioPins();

            //start Paradox security scheduler
            _paradox1738 = new Paradox1738();
            _paradox1738.ParadoxSecurity();
            _paradox1738.IRSensorsReading();

            //read from ome temperature sensors
            _homeTemperature = new HomeTemperature();
            _homeTemperature.ReadTemperature();

            //Receive Netatmo data
            _receiveNetatmoData = new ReceiveNetatmoData();
            _receiveNetatmoData.ReceiveData();

            //Starting schedulers
            _co2Scheduler = new Co2();
            _co2Scheduler.CheckCo2Async();

            //start saune scheduler
            _saunaHeating = new SaunaHeating();
            _saunaHeating.CheckHeatingTime();

            //start heating scheduler
            _heatingScheduler = new Heating();
            _heatingScheduler.ReduceHeatingSchedulerAsync();

            //query WiFiProbes
            _wiFiProbes = new WiFiProbes();
            _wiFiProbes.QueryWiFiProbes();

            //shelly's
            TelemetryDataClass.isOutsideLightsOn = await Shelly.GetShellyState(Shelly.OutsideLight);
            SomeoneAtHome.CheckLightStatuses();

            //Send data to IoTHub
            _sendData = new SendTelemetryData();
            _sendData.SendTelemetryEventsAsync();

            //Receive IoTHub commands
            _receiveData = new ReceiveData();
            _receiveData.ReceiveCommandsAsync();
        }
    }
}
