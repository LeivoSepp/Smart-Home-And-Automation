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
        private SendDataAzure _sendListData;
        private readonly METHOD Methods = new METHOD();
        public static List<Localdevice> WiFiDevicesToPowerApps = new List<Localdevice>();
        public static List<Localdevice> WiFiDevicesFromPowerApps = new List<Localdevice>();
        public static bool IsAnyMobileAtHome = false;

        public string username = Environment.GetEnvironmentVariable("KismetUser");
        public string password = Environment.GetEnvironmentVariable("KismetPassword");
        public string urlKismet = Environment.GetEnvironmentVariable("KismetURL");
        public string jsonFields = JsonSerializer.Serialize(KismetField.KismetFields); //serialize kismet fields

        HttpClient http = new HttpClient();

        public async void QueryWiFiProbes()
        {
            var filename = Methods.GetFilePath(CONSTANT.FILENAME_HOME_DEVICES);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}")));

            try
            {
                //open file and -> list of devices from Raspberry if the file exists
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

            await SendMacAddressToCosmos(WiFiDevice.WifiDevices);

            bool isAnyDeviceChanged = true;
            var closeDevices = new List<WiFiDevice>();

            while (true)
            {
                double unixTimestamp = METHOD.DateTimeToUnixTimestamp(DateTime.UtcNow);
                double timeOffset = METHOD.DateTimeTZ().Offset.TotalHours;

                //if there are some items from PowerApps then proceed
                if (WiFiDevicesFromPowerApps.Any())
                {
                    var tempAddList = new List<WiFiDevice>();
                    var tempDeleteList = new List<WiFiDevice>();
                    bool isNewItem = true;
                    //CRUD operations, data is coming from PowerApps
                    foreach (var item in WiFiDevicesFromPowerApps)
                    {
                        foreach (var device in WiFiDevice.WifiDevices)
                        {
                            if (device.MacAddress == item.MacAddress)
                            {
                                //DELETE item (special type=100)
                                if (item.DeviceType == 100)
                                {
                                    tempDeleteList.Add(device);
                                    break;
                                }
                                //UPDATE item
                                device.DeviceName = item.DeviceName;
                                device.DeviceOwner = item.DeviceOwner;
                                device.DeviceType = item.DeviceType;
                                device.ActiveDuration = item.ActiveDuration;
                                device.StatusUnixTime = METHOD.DateTimeToUnixTimestamp(item.StatusFrom);
                                device.SignalType = item.SignalType;
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
                            //ADD item
                            tempAddList.Add(new WiFiDevice
                            (
                                item.MacAddress,
                                item.DeviceName,
                                item.DeviceOwner,
                                item.DeviceType,
                                item.ActiveDuration,
                                METHOD.DateTimeToUnixTimestamp(item.StatusFrom)
                            ));
                        }
                    }
                    WiFiDevicesFromPowerApps.Clear();
                    WiFiDevice.WifiDevices.AddRange(tempAddList.ToArray());
                    WiFiDevice.WifiDevices.RemoveAll(i => tempDeleteList.Any(x => x.MacAddress == i.MacAddress));

                    //send all devices to CosmosDB
                    await SendMacAddressToCosmos(WiFiDevice.WifiDevices);
                }
                //prepare multimac query
                //create a list of the MAC Addresses for multimac query
                List<string> deviceMacs = new List<string>();
                WiFiDevice.WifiDevices.ForEach(x => deviceMacs.Add(x.MacAddress));
                string jsonMacAddresses = JsonSerializer.Serialize(deviceMacs);
                string urlMultiMac = $"{urlKismet}/devices/multimac/devices.json";
                string jsonContentFieldsMac = "json={\"fields\":" + jsonFields + ",\"devices\":" + jsonMacAddresses + "}";
                List<WiFiDevice> KismetKnownDevices = await GetDevices(urlMultiMac, jsonContentFieldsMac);

                //prepare last active devices query
                string urlLastActive = $"{urlKismet}/devices/last-time/{CONSTANT.ACTIVE_DEVICES_IN_LAST}/devices.json";
                string jsonContentFields = "json={\"fields\":" + jsonFields + "}";
                List<WiFiDevice> KismetActiveDevices = await GetDevices(urlLastActive, jsonContentFields);

                //clear devices list.
                WiFiDevicesToPowerApps.Clear();
                //building list of the local devces which are last seen
                foreach (var device in WiFiDevice.WifiDevices)
                {
                    foreach (var kismet in KismetKnownDevices)
                    {
                        if (device.MacAddress == kismet.MacAddress)
                        {
                            device.LastUnixTime = kismet.LastUnixTime;
                            device.LastSignal = kismet.LastSignal;
                            device.SignalType = kismet.SignalType;
                            device.SSID = kismet.SSID;
                            device.WiFiName = kismet.ProbedSSID;
                            if (kismet.SignalType.Contains("Wi-Fi"))
                            {
                                device.AccessPoint = WiFiDevice.WifiDevices.Where(x => x.MacAddress == kismet.LastBSSID).Select(x => x.DeviceName).DefaultIfEmpty("No AP").First();
                                if (string.IsNullOrEmpty(device.WiFiName) || device.WiFiName == "0")
                                {
                                    device.WiFiName = WiFiDevice.WifiDevices.Where(x => x.MacAddress == kismet.LastBSSID).Select(x => x.SSID).DefaultIfEmpty("No AP").First();
                                }
                            }
                            break;
                        }
                    }
                    //checking active devices and setting Active/NonActive statuses. Each device can have individual timewindow
                    device.IsChanged = false; //this is just for debugging, no other reason
                    var durationUntilNotActive = (unixTimestamp - device.LastUnixTime) / 60; //timestamp in minutes
                    device.IsPresent = durationUntilNotActive < device.ActiveDuration;
                    if (device.IsPresent && !device.StatusChange)
                    {
                        device.StatusUnixTime = unixTimestamp;
                        device.StatusChange = true;
                        isAnyDeviceChanged = true;
                        device.IsChanged = true;
                    }
                    if (!device.IsPresent && device.StatusChange)
                    {
                        device.StatusUnixTime = device.LastUnixTime;
                        device.StatusChange = false;
                        isAnyDeviceChanged = true;
                        device.IsChanged = true; 
                    }
                    //following list WiFiDevicesToPowerApps is used to send minimal data to PowerApps (sending only userdevices)
                    //this list will be sent as a response after PowerApps asks something or refreshing it's data
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
                            MacAddress = device.MacAddress,
                            SignalType = device.SignalType
                        });
                    }
                }
                //save the data locally into Raspberry
                var jsonString = JsonSerializer.Serialize(WiFiDevice.WifiDevices);
                await Methods.SaveStringToLocalFile(filename, jsonString);

                //if any mobile phone or watch is present then someone is at home
                IsAnyMobileAtHome = WiFiDevice.WifiDevices.Any(x => x.IsPresent && (x.DeviceType == WiFiDevice.MOBILE || x.DeviceType == WiFiDevice.WATCH || x.DeviceType == WiFiDevice.CAR));

                //for local debugging only show the active/non active devices in console window
                if (isAnyDeviceChanged)
                {
                    var sortedList = WiFiDevice.WifiDevices.OrderByDescending(y => y.IsPresent).ThenBy(w => w.DeviceOwner).ThenByDescending(z => z.AccessPoint).ThenBy(w => w.SignalType).ThenByDescending(x => x.StatusUnixTime).ToList();
                    Console.WriteLine();
                    Console.WriteLine($"All known devices at: {METHOD.DateTimeTZ().DateTime:G}");
                    Console.WriteLine();
                    Console.WriteLine($"   |   From   | Status  |          Device           |        WiFi network      |        AccessPoint       |  SignalType");
                    Console.WriteLine($"   |  ------  | ------- |         --------          |           -----          |           -----          |  --------  ");
                    foreach (var device in sortedList)
                    {
                        if (device.LastUnixTime > 0) //show only my own ever seen LocalUserDevices devices
                        {
                            Console.WriteLine($" {(device.IsChanged ? "1" : " ")} " +
                                $"| {METHOD.UnixTimeStampToDateTime(device.StatusUnixTime).AddHours(timeOffset):T} " +
                                $"| {(device.IsPresent ? "Active " : "Not Act")} " +
                                $"| {device.DeviceName}{"".PadRight(26 - (device.DeviceName.Length > 26 ? 26 : device.DeviceName.Length))}" +
                                $"| {device.WiFiName}{"".PadRight(26 - (!string.IsNullOrEmpty(device.WiFiName) ? (device.WiFiName.Length > 26 ? 26 : device.WiFiName.Length) : 0))}" +
                                $"| {device.AccessPoint}{"".PadRight(26 - (!string.IsNullOrEmpty(device.AccessPoint) ? (device.AccessPoint.Length > 26 ? 26 : device.AccessPoint.Length) : 0))}" +
                                $"| {device.SignalType} ");
                        }
                    }
                    Console.WriteLine();
                    isAnyDeviceChanged = false;
                }

                //removing all known devices from the last seen devices list
                //known devices are all locally registered devices in the list WiFiDevice.WifiDevices
                var tempDelList = new List<WiFiDevice>();
                foreach (var kismet in KismetActiveDevices)
                {
                    foreach (var device in WiFiDevice.WifiDevices)
                    {
                        if (kismet.MacAddress == device.MacAddress || kismet.LastSignal < CONSTANT.SIGNAL_TRESHOLD)
                        {
                            tempDelList.Add(kismet);
                            break;
                        }
                    }
                }
                KismetActiveDevices.RemoveAll(i => tempDelList.Contains(i));

                //adding new members to the close devices list
                if (KismetActiveDevices.Any())
                {

                    var tempAddList = new List<WiFiDevice>();
                    bool isNewItem = true;
                    foreach (var kismet in KismetActiveDevices)
                    {
                        foreach (var device in closeDevices)
                        {
                            if (kismet.MacAddress == device.MacAddress)
                            {
                                device.Count++;
                                device.LastUnixTime = kismet.LastUnixTime;
                                device.WiFiName = kismet.ProbedSSID;
                                if (kismet.SignalType.Contains("Wi-Fi"))
                                {
                                    //get device AccessPoint and WIFI network names from Kismet (if reported by device)
                                    List<WiFiDevice> KismetOneMac = new List<WiFiDevice>();
                                    string urlOneMac = $"{urlKismet}/devices/by-mac/{kismet.MacAddress}/devices.json";
                                    KismetOneMac = await GetDevices(urlOneMac, jsonContentFields);
                                    device.AccessPoint = KismetOneMac.Where(x => x.MacAddress == kismet.LastBSSID).Select(x => x.BaseName).DefaultIfEmpty("No AP").First();
                                    if (string.IsNullOrEmpty(kismet.ProbedSSID) || device.WiFiName == "0")
                                        device.WiFiName = KismetOneMac.Where(x => x.MacAddress == kismet.LastBSSID).Select(x => x.SSID).DefaultIfEmpty("No AP").First();
                                }
                                isNewItem = false;
                                break;
                            }
                            else
                            {
                                isNewItem = true;
                            }
                        }
                        if (isNewItem) tempAddList.Add(kismet);
                    }
                    closeDevices.AddRange(tempAddList.ToArray());

                    closeDevices.RemoveAll(x => (unixTimestamp - x.LastUnixTime) / 60 > 120); //remove all entries older that 2 hour

                    //local debugging: show the devices list in console only if some device has been added
                    if (tempAddList.Any())
                    {
                        var sortedList = closeDevices.OrderBy(x => x.SignalType).ThenByDescending(y => y.LastUnixTime).ToList();
                        Console.WriteLine();
                        Console.WriteLine($"All unknown devices at: {METHOD.DateTimeTZ().DateTime:G}");
                        Console.WriteLine();
                        Console.WriteLine($"dB  | First | Last  |    Mac Address    |Count|  SignalType   |         WiFi network      |         AccessPoint       |    Base Name     | Manufacturer ");
                        Console.WriteLine($" -  | ----  | ----  |    -----------    | --- |  ----------   |          ---------        |          ---------        |    ----------    |  -----------  ");

                        foreach (var device in sortedList)
                        {
                            Console.WriteLine($"{device.LastSignal}{"".PadRight(device.LastSignal < 0 ? 1 : 3)}" +
                                $"| {METHOD.UnixTimeStampToDateTime(device.FirstUnixTime).AddHours(timeOffset):t} " +
                                $"| {METHOD.UnixTimeStampToDateTime(device.LastUnixTime).AddHours(timeOffset):t} " +
                                $"| {device.MacAddress} " +
                                $"| {device.Count:00}  " +
                                $"| {device.SignalType}{"".PadRight(14 - (!string.IsNullOrEmpty(device.SignalType) ? (device.SignalType.Length > 14 ? 14 : device.SignalType.Length) : 0))}" +
                                $"| {device.WiFiName}{"".PadRight(26 - (!string.IsNullOrEmpty(device.WiFiName) ? (device.WiFiName.Length > 26 ? 26 : device.WiFiName.Length) : 0))}" +
                                $"| {device.AccessPoint}{"".PadRight(26 - (!string.IsNullOrEmpty(device.AccessPoint) ? (device.AccessPoint.Length > 26 ? 26 : device.AccessPoint.Length) : 0))}" +
                                $"| {device.BaseName}{"".PadRight(string.IsNullOrEmpty(device.BaseName) ? 19 : 0)}" +
                                $"| {device.Manufacture}");
                        }
                        Console.WriteLine();
                    }
                }
                //it's a good place to check also garage light statuses in every minute
                if (TelemetryDataClass.isGarageLightsOn)
                    TelemetryDataClass.isGarageLightsOn = await Shelly.GetShellyState(Shelly.GarageLight);
                await Task.Delay(TimeSpan.FromSeconds(60)); //check statuses every 10 millisecond
            }
        }
        async Task<List<WiFiDevice>> GetDevices(string url, string jsonContent)
        {
            List<WiFiDevice> devices = new List<WiFiDevice>();
            try
            {
                HttpContent httpContent = new StringContent(jsonContent);
                httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                httpContent.Headers.ContentType.CharSet = "UTF-8";
                HttpResponseMessage responseMsg = await http.PostAsync(url, httpContent);
                var result = responseMsg.Content.ReadAsStringAsync();
                devices = JsonSerializer.Deserialize<List<WiFiDevice>>(result.Result);
            }
            catch (JsonException e)
            {
                Console.WriteLine($"Json exception: {e.Message}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Kismet query error: {e.Message}");
            }
            return devices;
        }
        private async Task<List<WiFiDevice>> GetMacAddressFromCosmos()
        {
            string funcUrl = Environment.GetEnvironmentVariable("GetWifiDevicesFuncURL");
            string funcCode = Environment.GetEnvironmentVariable("WiFiDevicesFuncCode");
            var http = new HttpClient();
            string url = funcUrl + "?code=" + funcCode;
            HttpResponseMessage response = await http.GetAsync(url);
            var result = response.Content.ReadAsStringAsync();

            //deserialize all content
            var nps = JsonSerializer.Deserialize<JsonElement>(result.Result);
            string dataresult = nps.GetProperty("AllWiFiDevices").GetRawText();
            var npsOut = JsonSerializer.Deserialize<List<WiFiDevice>>(dataresult);
            return npsOut;
        }

        async Task SendMacAddressToCosmos(List<WiFiDevice> wiFiDevices)
        {
            _sendListData = new SendDataAzure();
            double timeOffset = METHOD.DateTimeTZ().Offset.TotalHours;

            //prepare the list to send into CosmosDB
            var AllWiFiDevices = new List<Localdevice>();
            wiFiDevices.ForEach(x => AllWiFiDevices.Add(new Localdevice()
            {
                ActiveDuration = x.ActiveDuration,
                DeviceName = x.DeviceName,
                DeviceOwner = x.DeviceOwner,
                DeviceType = x.DeviceType,
                MacAddress = x.MacAddress,
                StatusFrom = METHOD.UnixTimeStampToDateTime(x.StatusUnixTime).AddHours(timeOffset),
                IsPresent = x.IsPresent,
                SignalType = x.SignalType
            }));
            TelemetryDataClass.SourceInfo = $"WiFi Devices";
            //send data to CosmosDB
            var monitorData = new
            {
                DeviceID = "HomeController",
                status = TelemetryDataClass.SourceInfo,
                DateAndTime = METHOD.DateTimeTZ().DateTime,
                AllWiFiDevices
            };
            await _sendListData.PipeMessage(monitorData, Program.IoTHubModuleClient, TelemetryDataClass.SourceInfo, "output");
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
        public string SignalType { get; set; }
        public string AccessPoint { get; set; }
    }
    class WiFiDevice
    {
        public WiFiDevice(string macAddress, string deviceName, string deviceOwner, int deviceType = DEVICE, int activeDuration = 0, double statusUnixTime = 0, bool isPresent = false, bool statusChange = true)
        {
            MacAddress = macAddress;
            ActiveDuration = activeDuration;
            DeviceName = deviceName;
            DeviceOwner = deviceOwner;
            DeviceType = deviceType;
            IsPresent = isPresent;
            StatusChange = statusChange;
            StatusUnixTime = statusUnixTime;
        }

        public int ActiveDuration { get; set; }
        public string DeviceOwner { get; set; }
        public int DeviceType { get; set; }
        public bool IsPresent { get; set; }
        public bool StatusChange { get; set; }
        public double StatusUnixTime { get; set; }
        public int Count { get; set; }
        public string AccessPoint { get; set; } //Connected device name, calculated by LastBSSID
        public string WiFiName { get; set; } //Connected WiFi network name, SSID or ProbedSSID (depending of the device type)
        public string DeviceName { get; set; } //manually set device name
        public bool IsChanged { get; set; } //just for debugging, this will be true if device status changed
        [JsonPropertyName("kismet.device.base.name")]
        public string BaseName { get; set; }
        [JsonPropertyName("kismet.device.base.type")]
        public string SignalType { get; set; }
        [JsonPropertyName("kismet.device.base.macaddr")]
        public string MacAddress { get; set; }
        [JsonPropertyName("dot11.probedssid.ssid")]
        [JsonConverter(typeof(LongToStringJsonConverter))]
        public string ProbedSSID { get; set; } //WiFi device: WiFi network name
        [JsonPropertyName("dot11.device.last_bssid")]
        [JsonConverter(typeof(LongToStringJsonConverter))]
        public string LastBSSID { get; set; } //Any device: connected into this MAC address
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
        public string SSID { get; set; } //WiFi AccessPoint: WiFi network name

        public const int MOBILE = 1;
        public const int NOTEBOOK = 2;
        public const int WATCH = 3;
        public const int OTHER = 4;
        public const int DEVICE = 5;
        public const int CAR = 6;

        //type with a number 100 is reserved for an item deleteting, used in PowerApps

        //this list is used in very first time, later the data is taken from the file from Raspberry.
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
            new WiFiDevice(Environment.GetEnvironmentVariable("naaber1"), "homeWifi naaber", "Unknown"),
            new WiFiDevice(Environment.GetEnvironmentVariable("naaber2"), "homeWifi naaber", "Unknown"),
            new WiFiDevice(Environment.GetEnvironmentVariable("naaber3"), "Naabri Xiaomi", "Unknown"),
            new WiFiDevice(Environment.GetEnvironmentVariable("naaber4"), "Naabri HP printer", "Unknown"),
            new WiFiDevice(Environment.GetEnvironmentVariable("naaber5"), "Naabri Arris", "Unknown"),
            new WiFiDevice(Environment.GetEnvironmentVariable("naaber6"), "Naabri telekas", "Unknown")
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
            "kismet.device.base.name",
            "kismet.device.base.manuf",
            "kismet.device.base.type",
            "kismet.device.base.macaddr",
            "dot11.device/dot11.device.last_bssid",
            "kismet.device.base.last_time",
            "kismet.device.base.first_time",
            "kismet.device.base.signal/kismet.common.signal.last_signal",
            "dot11.device/dot11.device.last_probed_ssid_record/dot11.probedssid.ssid",
            "dot11.device/dot11.device.last_beaconed_ssid_record/dot11.advertisedssid.ssid"
        };
    }
}
