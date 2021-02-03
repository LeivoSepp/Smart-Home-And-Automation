using HomeModule.Azure;
using HomeModule.Helpers;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HomeModule.Schedulers
{
    class WiFiProbes
    {
        async Task SendMacAddressToCosmos()
        {
            //create a list of the MAC Addresses for multimac query
            deviceMacs.Clear();
            WiFiDevice.WifiDevices.ForEach(x => deviceMacs.Add(x.MacAddress));
            //send data back to CosmosDB
            var AllWiFiDevices = new List<Localdevice>();
            WiFiDevice.WifiDevices.ForEach(x => AllWiFiDevices.Add(new Localdevice()
            {
                ActiveDuration = x.ActiveDuration,
                DeviceName = x.DeviceName,
                DeviceOwner = x.DeviceOwner,
                DeviceType = x.DeviceType,
                MacAddress = x.MacAddress,
                StatusFrom = METHOD.UnixTimeStampToDateTime(x.StatusUnixTime),
                IsPresent = x.IsPresent
            }));
            _sendListData = new SendDataAzure();
            TelemetryDataClass.SourceInfo = $"WiFi Devices";
            var monitorData = new
            {
                DeviceID = "HomeController",
                status = TelemetryDataClass.SourceInfo,
                DateAndTime = METHOD.DateTimeTZ().DateTime,
                AllWiFiDevices
            };
            await _sendListData.PipeMessage(monitorData, Program.IoTHubModuleClient, TelemetryDataClass.SourceInfo, "output");
        }

        List<string> deviceMacs = new List<string>();
        private SendDataAzure _sendListData;
        private readonly METHOD Methods = new METHOD();
        public static List<Localdevice> WiFiDevicesToPowerApps = new List<Localdevice>();
        public static List<Localdevice> WiFiDevicesFromPowerApps = new List<Localdevice>();
        public static bool IsAnyMobileAtHome = false;

        public async void QueryWiFiProbes()
        {
            string username = Environment.GetEnvironmentVariable("KismetUser");
            string password = Environment.GetEnvironmentVariable("KismetPassword");
            string urlKismet = Environment.GetEnvironmentVariable("KismetURL");
            var filename = Methods.GetFilePath(CONSTANT.FILENAME_HOME_DEVICES);

            var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}")));

            try
            {
                //open file and -> list of deices from Raspberry if it exists
                if (File.Exists(filename))
                {
                    var result = await Methods.OpenExistingFile(filename);
                    WiFiDevice.WifiDevices = JsonSerializer.Deserialize<List<WiFiDevice>>(result).ToList();
                }
                else
                {
                    double unixTimestamp = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                    //following will happen only if there is no file on Raspberry and the list will be loaded from the environment variables
                    WiFiDevice.WifiDevices.ForEach(x => x.StatusUnixTime = unixTimestamp);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"open file {e}");
            }

            await SendMacAddressToCosmos();

            string jsonFields = JsonSerializer.Serialize(KismetField.KismetFields); //serialize kismet fields
            string jsonDevices = JsonSerializer.Serialize(deviceMacs); //serialize mac addresses

            //prepare last active devices query
            int lastActive = CONSTANT.LAST_ACTIVE_DEVICES;
            string urlLastActive = $"{urlKismet}/devices/last-time/{lastActive}/devices.json";
            string contentFields = "json={\"fields\":" + jsonFields + "}";
            HttpContent httpContentLastActive = new StringContent(contentFields);
            httpContentLastActive.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            httpContentLastActive.Headers.ContentType.CharSet = "UTF-8";

            //prepare multimac query
            string urlDevices = $"{urlKismet}/devices/multimac/devices.json";
            string contentDevices = "json={\"fields\":" + jsonFields + ",\"devices\":" + jsonDevices + "}";
            HttpContent httpContentDevices = new StringContent(contentDevices);
            httpContentDevices.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            httpContentDevices.Headers.ContentType.CharSet = "UTF-8";

            bool showHeaders = true;
            var closeDevices = new List<WiFiDevice>();

            while (true)
            {
                double unixTimestamp = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                double timeOffset = METHOD.DateTimeTZ().Offset.TotalHours;

                //get data from PowerApps
                if (WiFiDevicesFromPowerApps.Any())
                {
                    var tempAddList = new List<WiFiDevice>();
                    var tempDeleteList = new List<WiFiDevice>();
                    bool isNewItem = true;
                    //CRUD operations
                    foreach (var item in WiFiDevicesFromPowerApps)
                    {
                        foreach (var device in WiFiDevice.WifiDevices)
                        {
                            if (device.MacAddress == item.MacAddress)
                            {
                                //if powerapps will send an item with type=100 then this item should be deleted
                                if (item.DeviceType == 100)
                                {
                                    tempDeleteList.Add(device);
                                    break;
                                }
                                //update item
                                device.DeviceName = item.DeviceName;
                                device.DeviceOwner = item.DeviceOwner;
                                device.DeviceType = item.DeviceType;
                                device.ActiveDuration = item.ActiveDuration;
                                isNewItem = false;
                                break;
                            }
                            else
                            {
                                isNewItem = true;
                            }
                        }
                        if (isNewItem)
                        {
                            //add time
                            tempAddList.Add(new WiFiDevice
                            (
                                item.MacAddress,
                                item.DeviceName,
                                item.DeviceOwner,
                                item.DeviceType,
                                item.ActiveDuration
                            ));
                        }
                    }
                    WiFiDevicesFromPowerApps.Clear();
                    WiFiDevice.WifiDevices.AddRange(tempAddList.ToArray());
                    WiFiDevice.WifiDevices.RemoveAll(i => tempDeleteList.Any(x => x.MacAddress == i.MacAddress));

                    //send list to CosmosDB
                    await SendMacAddressToCosmos();
                }
                List<WiFiDevice> WifiKnownDevices = new List<WiFiDevice>();
                List<WiFiDevice> WiFiActiveDevices = new List<WiFiDevice>();
                try
                {
                    //execute multimac query
                    HttpResponseMessage responseDevices = await http.PostAsync(urlDevices, httpContentDevices);
                    var resultDevices = responseDevices.Content.ReadAsStringAsync();
                    WifiKnownDevices = JsonSerializer.Deserialize<List<WiFiDevice>>(resultDevices.Result);

                    //execute last active devices query
                    HttpResponseMessage responseLastActive = await http.PostAsync(urlLastActive, httpContentLastActive);
                    var resultLastActive = responseLastActive.Content.ReadAsStringAsync();
                    WiFiActiveDevices = JsonSerializer.Deserialize<List<WiFiDevice>>(resultLastActive.Result);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Post queries to Kismet {e}");
                }

                //clear devices list. This will be sent to PowerApps
                WiFiDevicesToPowerApps.Clear(); 
                //building list of the local devce which is last seen
                foreach (var device in WiFiDevice.WifiDevices)
                {
                    foreach (var probe in WifiKnownDevices)
                    {
                        if (device.MacAddress == probe.MacAddress)
                        {
                            device.LastUnixTime = probe.LastUnixTime;
                            device.LastSignal = probe.LastSignal;
                            break;
                        }
                    }
                    //checking active devices and setting statuses
                    var durationUntilNotActive = (unixTimestamp - device.LastUnixTime) / 60; //timestamp in minutes
                    device.IsPresent = durationUntilNotActive < device.ActiveDuration;
                    if (device.IsPresent && !device.StatusChange)
                    {
                        device.StatusUnixTime = unixTimestamp;
                        device.StatusChange = true;
                        showHeaders = true;
                    }
                    if (!device.IsPresent && device.StatusChange)
                    {
                        device.StatusUnixTime = device.LastUnixTime;
                        device.StatusChange = false;
                        showHeaders = true;
                    }
                    //following list is used to send minimal data to PowerApps, send only userdevices
                    //this list is sent as a response when PowerApps asks something
                    if (device.DeviceType != WiFiDevice.DEVICE)
                    {
                        WiFiDevicesToPowerApps.Add(new Localdevice()
                        {
                            DeviceOwner = device.DeviceOwner,
                            DeviceName = device.DeviceName,
                            DeviceType = device.DeviceType,
                            IsPresent = device.IsPresent,
                            StatusFrom = METHOD.UnixTimeStampToDateTime(device.StatusUnixTime),
                            ActiveDuration = device.ActiveDuration,
                            MacAddress = device.MacAddress
                        });
                    }
                }
                //save the data locally into Raspberry
                var jsonString = JsonSerializer.Serialize(WiFiDevice.WifiDevices);
                await Methods.SaveStringToLocalFile(filename, jsonString);

                //if any mobile phone is present then someone is at home
                IsAnyMobileAtHome = WiFiDevice.WifiDevices.Any(x => x.IsPresent && (x.DeviceType == WiFiDevice.MOBILE || x.DeviceType == WiFiDevice.WATCH));

                if (showHeaders) //for local debugging
                {
                    var sortedList = WiFiDevice.WifiDevices.OrderByDescending(y => y.IsPresent).ThenByDescending(x => x.StatusUnixTime).ToList();

                    Console.WriteLine($"   From   | Status  | Device");
                    Console.WriteLine($" -------- | ------- | ----- ");
                    foreach (var device in sortedList)
                    {
                        if (device.LastUnixTime > 0 && device.DeviceType != WiFiDevice.DEVICE) //show only ever seen LocalUserDevices devices
                        {
                            Console.WriteLine($" {METHOD.UnixTimeStampToDateTime(device.StatusUnixTime).AddHours(timeOffset):T} | {(device.IsPresent ? "Active " : "Not Act")} | {device.DeviceName}");
                        }
                    }
                    Console.WriteLine($"");
                    showHeaders = false;
                }

                //removing all known devices from the last seen devices list
                var tempDelList = new List<WiFiDevice>();
                foreach (var activeDevice in WiFiActiveDevices)
                {
                    foreach (var wifiDevice in WiFiDevice.WifiDevices)
                    {
                        if (activeDevice.MacAddress == wifiDevice.MacAddress || activeDevice.LastSignal < CONSTANT.SIGNAL_TRESHOLD)
                        {
                            tempDelList.Add(activeDevice);
                            break;
                        }
                    }
                }
                WiFiActiveDevices.RemoveAll(i => tempDelList.Contains(i));

                //adding new members to the close devices list
                if (WiFiActiveDevices.Any())
                {
                    var tempAddList = new List<WiFiDevice>();
                    bool isNewItem = true;
                    foreach (var device in WiFiActiveDevices)
                    {
                        foreach (var item in closeDevices)
                        {
                            if (device.MacAddress == item.MacAddress)
                            {
                                item.Count++;
                                item.LastUnixTime = device.LastUnixTime;
                                isNewItem = false;
                                break;
                            }
                            else
                            {
                                isNewItem = true;
                            }
                        }
                        if (isNewItem) tempAddList.Add(device);
                    }
                    closeDevices.AddRange(tempAddList.ToArray());

                    closeDevices.RemoveAll(x => (unixTimestamp - x.LastUnixTime) / 60 > 60); //remove older that 1 hour
                    var sortedList = closeDevices.OrderBy(x => x.SignalType).ThenByDescending(y => y.LastUnixTime).ToList();

                    if (tempAddList.Any())
                    {
                        Console.WriteLine($"dB  | First | Last  |    Mac Address     |Count| SignalType  | Manufacturer | SSID");
                        Console.WriteLine($" -  | ----  | ----  |    -----------     | --- | ----------  | -----------  | ----");

                        foreach (var probe in sortedList)
                        {
                            Console.WriteLine($"{probe.LastSignal:00} | {METHOD.UnixTimeStampToDateTime(probe.FirstUnixTime).AddHours(timeOffset):t} | {METHOD.UnixTimeStampToDateTime(probe.LastUnixTime).AddHours(timeOffset):t} | {probe.MacAddress} | {probe.Count}  | {probe.SignalType}  | {probe.Manufacture}  |{(string.IsNullOrEmpty(probe.ProbedSSID) || probe.ProbedSSID == "0" ? probe.SSID : probe.ProbedSSID)}");
                        }
                        Console.WriteLine();
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(60)); //check statuses every 10 millisecond
            }
        }
    }
    class Localdevice
    {
        public string MacAddress { get; set; }
        public string DeviceName { get; set; }
        public string DeviceOwner { get; set; }
        public int DeviceType { get; set; }
        public bool IsPresent { get; set; }
        [JsonConverter(typeof(DateTimeConverterUsingDateTimeParse))]
        public DateTime StatusFrom { get; set; }
        public int ActiveDuration { get; set; }
    }
    class WiFiDevice
    {
        public WiFiDevice(string macAddress, string deviceName, string deviceOwner, int deviceType = DEVICE, int activeDuration = 0, bool isPresent = false, bool statusChange = true)
        {
            MacAddress = macAddress;
            ActiveDuration = activeDuration;
            DeviceName = deviceName;
            DeviceOwner = deviceOwner;
            DeviceType = deviceType;
            IsPresent = isPresent;
            StatusChange = statusChange;
        }

        public int ActiveDuration { get; set; }
        public string LastConnectedDevice { get; set; }
        public string DeviceOwner { get; set; }
        public int DeviceType { get; set; }
        public bool IsPresent { get; set; }
        public bool StatusChange { get; set; }
        public double StatusUnixTime { get; set; }
        public int Count { get; set; }
        //[JsonPropertyName("kismet.device.base.commonname")]
        public string DeviceName { get; set; }
        [JsonPropertyName("kismet.device.base.type")]
        public string SignalType { get; set; }
        [JsonPropertyName("kismet.device.base.macaddr")]
        public string MacAddress { get; set; }
        [JsonPropertyName("dot11.probedssid.ssid")]
        [JsonConverter(typeof(LongToStringJsonConverter))]
        public string ProbedSSID { get; set; }
        [JsonPropertyName("dot11.device.last_bssid")]
        [JsonConverter(typeof(LongToStringJsonConverter))]
        public string LastBSSID { get; set; }
        [JsonPropertyName("kismet.common.signal.last_signal")]
        public int LastSignal { get; set; }
        [JsonPropertyName("kismet.device.base.last_time")]
        public double LastUnixTime { get; set; }
        [JsonPropertyName("kismet.device.base.first_time")]
        public double FirstUnixTime { get; set; }
        [JsonPropertyName("kismet.device.base.manuf")]
        [JsonConverter(typeof(LongToStringJsonConverter))]
        public string Manufacture { get; set; }
        [JsonPropertyName("dot11.advertisedssid.ssid")]
        [JsonConverter(typeof(LongToStringJsonConverter))]
        public string SSID { get; set; }

        public const int MOBILE = 1;
        public const int NOTEBOOK = 2;
        public const int WATCH = 3;
        public const int OTHER = 4;
        public const int DEVICE = 5;

        public static List<WiFiDevice> WifiDevices = new List<WiFiDevice>
        {
            new WiFiDevice(Environment.GetEnvironmentVariable("leivotelo"), "Leivo telo", "Leivo", MOBILE, CONSTANT.MOBILE_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("kajatelo"), "Kaja telo", "Kaja", MOBILE, CONSTANT.MOBILE_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("kreetetelo"), "Kreete telo", "Kreete", MOBILE, CONSTANT.MOBILE_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("lauratelo"), "Laura telo", "Laura", MOBILE, CONSTANT.MOBILE_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("ramsestelo"), "Ramses telo", "Ramses", MOBILE, CONSTANT.MOBILE_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("leivolap"), "Leivo lap", "Leivo", NOTEBOOK, CONSTANT.NOTEBOOK_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("kajalap"), "Kaja lap", "Kaja", NOTEBOOK, CONSTANT.NOTEBOOK_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("surfacelap"), "Surface lap", "Leivo", NOTEBOOK, CONSTANT.NOTEBOOK_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("kreetelap"), "Kreete lap Huuhkaja", "Kreete", NOTEBOOK, CONSTANT.NOTEBOOK_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("lauralap"), "Laura lap", "Laura", NOTEBOOK, CONSTANT.NOTEBOOK_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("ramseslap"), "Ramses lap Valhalla", "Ramses", NOTEBOOK, CONSTANT.NOTEBOOK_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("mummilap"), "Mummi lap", "Mumm", NOTEBOOK, CONSTANT.NOTEBOOK_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("garminkaal"), "Garmin kaal", "Kodu", OTHER, CONSTANT.OTHER_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("fenix6"), "Fenix6", "Leivo", WATCH, CONSTANT.WATCH_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("venu"), "Venu", "Kaja", WATCH, CONSTANT.WATCH_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("edge1000"), "Garmin Edge1000", "Leivo", WATCH, CONSTANT.WATCH_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("tolmutriin"), "Tolmutriinu", "Kodu", OTHER, CONSTANT.OTHER_DURATION),
            new WiFiDevice(Environment.GetEnvironmentVariable("e4200"), "Linksys E4200", "Kodu"),
            new WiFiDevice(Environment.GetEnvironmentVariable("e900"), "Linksys E900", "Kodu"),
            new WiFiDevice(Environment.GetEnvironmentVariable("shellytuled"), "Shelly tuled", "Kodu"),
            new WiFiDevice(Environment.GetEnvironmentVariable("shellypiano"), "Shelly piano", "Kodu"),
            new WiFiDevice(Environment.GetEnvironmentVariable("netatmo"), "Netatmo", "Kodu"),
            new WiFiDevice(Environment.GetEnvironmentVariable("garminwifi"), "Garmin Wifi", "Kodu"),
            new WiFiDevice(Environment.GetEnvironmentVariable("huawei4g"), "Huawei 4G", "Kodu"),
            new WiFiDevice(Environment.GetEnvironmentVariable("huaweilan"), "Huawei LAN", "Kodu"),
            new WiFiDevice(Environment.GetEnvironmentVariable("huaweilan2"), "Huawei LAN", "Kodu"),
            new WiFiDevice(Environment.GetEnvironmentVariable("naaber1"), "homeWifi naaber", "Naaber"),
            new WiFiDevice(Environment.GetEnvironmentVariable("naaber2"), "homeWifi naaber", "Naaber"),
            new WiFiDevice(Environment.GetEnvironmentVariable("naaber3"), "Naabri Xiaomi", "Naaber"),
            new WiFiDevice(Environment.GetEnvironmentVariable("naaber4"), "Naabri HP printer", "Naaber"),
            new WiFiDevice(Environment.GetEnvironmentVariable("naaber5"), "Naabri Arris", "Naaber"),
            new WiFiDevice(Environment.GetEnvironmentVariable("naaber6"), "Naabri telekas", "Naaber")
         };
    }
    class DeviceOwner
    {
        public string Owner { get; set; }
        public List<WiFiDevice> WiFiDevices { get; set; }

        public DeviceOwner(string owner, List<WiFiDevice> wiFis)
        {
            Owner = owner;
            WiFiDevices = wiFis;
        }
        public static List<DeviceOwner> DeviceOwners = new List<DeviceOwner>
        {
            new DeviceOwner("Leivo",
                new List<WiFiDevice>{
                    new WiFiDevice(Environment.GetEnvironmentVariable("leivotelo"), "Leivo telo", "Leivo", WiFiDevice.MOBILE, CONSTANT.MOBILE_DURATION),
                    new WiFiDevice(Environment.GetEnvironmentVariable("leivolap"), "Leivo lap", "Leivo", WiFiDevice.NOTEBOOK, CONSTANT.NOTEBOOK_DURATION),
                    new WiFiDevice(Environment.GetEnvironmentVariable("surfacelap"), "Surface lap", "Leivo", WiFiDevice.NOTEBOOK, CONSTANT.NOTEBOOK_DURATION),
                    new WiFiDevice(Environment.GetEnvironmentVariable("fenix6"), "Fenix6", "Leivo", WiFiDevice.WATCH, CONSTANT.WATCH_DURATION)
                }),
            new DeviceOwner("Kaja",
                new List<WiFiDevice>
                {
                    new WiFiDevice(Environment.GetEnvironmentVariable("kajatelo"), "Kaja telo", "Kaja", WiFiDevice.MOBILE, CONSTANT.MOBILE_DURATION),
                    new WiFiDevice(Environment.GetEnvironmentVariable("kajalap"), "Kaja lap", "Kaja", WiFiDevice.NOTEBOOK, CONSTANT.NOTEBOOK_DURATION),
                    new WiFiDevice(Environment.GetEnvironmentVariable("venu"), "Venu", "Kaja", WiFiDevice.WATCH, CONSTANT.WATCH_DURATION)
                })
        };
    }
    public class LongToStringJsonConverter : JsonConverter<string>
    {
        // https://geeks.ms/jorge/2020/03/18/cannot-get-the-value-of-a-token-type-number-as-a-string-con-system-text-json/
        public LongToStringJsonConverter() { }

        public override string Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number &&
                type == typeof(String))
                return reader.GetString();

            var span = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;

            if (Utf8Parser.TryParse(span, out long number, out var bytesConsumed) && span.Length == bytesConsumed)
                return number.ToString();

            var data = reader.GetString();

            throw new InvalidOperationException($"'{data}' is not a correct expected value!")
            {
                Source = "LongToStringJsonConverter"
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
    public class DateTimeConverterUsingDateTimeParse : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Debug.Assert(typeToConvert == typeof(DateTime));
            if (DateTime.TryParse(reader.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime value))
            {
                return value;
            }
            return new DateTime();
            //throw new FormatException();
            //return DateTime.Parse(reader.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
    class KismetField
    {
        public static List<string> KismetFields = new List<string>
        {
            ("kismet.device.base.manuf"),
            ("kismet.device.base.type"),
            ("kismet.device.base.macaddr"),
            ("dot11.device/dot11.device.last_bssid"),
            ("kismet.device.base.last_time"),
            ("kismet.device.base.first_time"),
            ("kismet.device.base.signal/kismet.common.signal.last_signal"),
            ("dot11.device/dot11.device.last_probed_ssid_record/dot11.probedssid.ssid"),
            ("dot11.device/dot11.device.last_beaconed_ssid_record/dot11.advertisedssid.ssid")
        };
    }
}
