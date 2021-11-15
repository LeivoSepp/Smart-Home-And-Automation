namespace GarageModule
{
    using GarageModule.Azure;
    using GarageModule.Sensors;
    using Microsoft.Azure.Devices.Client;
    using System;
    using System.Runtime.Loader;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
        public static ModuleClient IoTHubModuleClient { get; set; }
        private static Garage _temperature;
        private static ReceiveData _receiveData;

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
            AmqpTransportSettings amqpSetting = new AmqpTransportSettings(TransportType.Amqp_Tcp_Only);
            ITransportSettings[] settings = { amqpSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            IoTHubModuleClient = ioTHubModuleClient;
            Console.WriteLine("IoT Hub module client initialized.");

            // Register callback to be called when a message is received by the module
            //await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", PipeMessage, ioTHubModuleClient);

            _temperature = new Garage();
            _temperature.ReadTemperatureAsync();
            _temperature.LedMatrixAsync();

            //Receive IoTHub commands
            _receiveData = new ReceiveData();
            _receiveData.ReceiveCommandsAsync();
        }
    }
}
