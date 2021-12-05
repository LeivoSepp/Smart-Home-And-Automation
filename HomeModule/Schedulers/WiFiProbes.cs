using HomeModule.Azure;
using HomeModule.Helpers;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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
        public static List<Localdevice> WiFiDevicesWhoIsChanged = new List<Localdevice>();
        public static bool IsAnyMobileAtHome = false;

        public string username = Environment.GetEnvironmentVariable("KismetUser");
        public string password = Environment.GetEnvironmentVariable("KismetPassword");
        public string urlKismet = Environment.GetEnvironmentVariable("KismetURL");
        public string jsonFields = JsonSerializer.Serialize(KismetField.KismetFields); //serialize kismet fields

        readonly HttpClient http = new HttpClient();

        public async void QueryWiFiProbes()
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}")));

            var filename = Methods.GetFilePath(CONSTANT.FILENAME_HOME_DEVICES);
            try
            {
                ////open file and -> list of devices from Raspberry if the file exists
                //if (File.Exists(filename))
                //{
                //    var result = await Methods.OpenExistingFile(filename);
                //    WiFiDevice.WellKnownDevices = JsonSerializer.Deserialize<List<WiFiDevice>>(result).ToList();
                //}
                //else
                //{
                //double unixTimestamp = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                ////following will happen only if there is no file on Raspberry and the list will be loaded from the environment variables
                //WiFiDevice.WellKnownDevices.ForEach(x => x.StatusUnixTime = unixTimestamp);
                //}
                WiFiDevice.WellKnownDevices = await GetMacAddressFromCosmos();
            }
            catch (Exception e)
            {
                Console.WriteLine($"open file {e}");
            }

            //await SendMacAddressToCosmos(WiFiDevice.WellKnownDevices);

            bool isAnyDeviceChanged = true;
            var UnknownDevices = new List<WiFiDevice>();

            while (true)
            {
                double unixTimestamp = METHOD.DateTimeToUnixTimestamp(DateTime.UtcNow);
                double timeOffset = METHOD.DateTimeTZ().Offset.TotalHours;

                //if there are some items from PowerApps then proceed
                if (WiFiDevicesFromPowerApps.Any())
                {
                    //Add, Remove or Change WiFiDevices list according to WiFiDevices From PowerApps
                    WiFiDevice.WellKnownDevices = AddRemoveChangeDevices(WiFiDevicesFromPowerApps, WiFiDevice.WellKnownDevices);
                    WiFiDevicesFromPowerApps.Clear();
                    //send all devices to CosmosDB
                    await SendMacAddressToCosmos(WiFiDevice.WellKnownDevices);
                }
                //prepare multimac query
                //create a list of the MAC Addresses for multimac query
                List<string> deviceMacs = new List<string>();
                WiFiDevice.WellKnownDevices.ForEach(x => deviceMacs.Add(x.MacAddress));
                string jsonMacAddresses = JsonSerializer.Serialize(deviceMacs);
                string urlMultiMac = $"{urlKismet}/devices/multimac/devices.json";
                string jsonContentFieldsMac = "json={\"fields\":" + jsonFields + ",\"devices\":" + jsonMacAddresses + "}";
                List<WiFiDevice> KismetKnownDevices = await GetDevices(urlMultiMac, jsonContentFieldsMac);

                //clear devices list.
                WiFiDevicesToPowerApps.Clear();
                //building list of the local devices which are last seen
                foreach (var knownDevice in WiFiDevice.WellKnownDevices)
                {
                    foreach (var kismet in KismetKnownDevices)
                    {
                        if (knownDevice.MacAddress == kismet.MacAddress)
                        {
                            knownDevice.LastUnixTime = kismet.LastUnixTime;
                            knownDevice.LastSignal = kismet.LastSignal;
                            knownDevice.SignalType = kismet.SignalType;
                            knownDevice.SSID = kismet.SSID;
                            knownDevice.WiFiName = kismet.ProbedSSID;
                            if (kismet.SignalType.Contains("Wi-Fi"))
                            {
                                knownDevice.AccessPoint = WiFiDevice.WellKnownDevices.Where(x => x.MacAddress == kismet.LastBSSID).Select(x => x.DeviceName).DefaultIfEmpty("No AP").First();
                                if (string.IsNullOrEmpty(knownDevice.WiFiName) || knownDevice.WiFiName == "0")
                                {
                                    knownDevice.WiFiName = WiFiDevice.WellKnownDevices.Where(x => x.MacAddress == kismet.LastBSSID).Select(x => x.SSID).DefaultIfEmpty("No AP").First();
                                }
                            }
                            break;
                        }
                    }
                    //checking active devices and setting Active/NonActive statuses. Each device can have individual timewindow
                    knownDevice.IsChanged = false; //this is just for debugging, no other reason
                    var durationUntilNotActive = (unixTimestamp - knownDevice.LastUnixTime) / 60; //timestamp in minutes
                    knownDevice.IsPresent = durationUntilNotActive < knownDevice.ActiveDuration;
                    if (knownDevice.IsPresent && !knownDevice.StatusChange)
                    {
                        knownDevice.StatusUnixTime = unixTimestamp;
                        knownDevice.StatusChange = true;
                        isAnyDeviceChanged = true;
                        knownDevice.IsChanged = true;
                    }
                    if (!knownDevice.IsPresent && knownDevice.StatusChange)
                    {
                        knownDevice.StatusUnixTime = knownDevice.LastUnixTime;
                        knownDevice.StatusChange = false;
                        isAnyDeviceChanged = true;
                        knownDevice.IsChanged = true;
                    }
                    //following list WiFiDevicesToPowerApps is used to send minimal data to PowerApps (sending only userdevices)
                    //this list will be sent as a response after PowerApps asks something or refreshing it's data
                    if (knownDevice.DeviceType != WiFiDevice.DEVICE)
                    {
                        WiFiDevicesToPowerApps.Add(new Localdevice()
                        {
                            DeviceOwner = knownDevice.DeviceOwner,
                            DeviceName = knownDevice.DeviceName,
                            DeviceType = knownDevice.DeviceType,
                            IsPresent = knownDevice.IsPresent,
                            StatusFrom = METHOD.UnixTimeStampToDateTime(knownDevice.StatusUnixTime),
                            ActiveDuration = knownDevice.ActiveDuration,
                            MacAddress = knownDevice.MacAddress,
                            SignalType = knownDevice.SignalType,
                            IsChanged = knownDevice.IsChanged
                        });
                    }
                    //build a list with the names who is expected to track 
                    //this list will be sent through Azure Functions to the owners e-mail
                    if (knownDevice.DeviceType == WiFiDevice.MOBILE || knownDevice.DeviceType == WiFiDevice.WATCH || knownDevice.DeviceType == WiFiDevice.CAR)
                    {
                        //to be sure that every person is listed just once
                        if (!WiFiDevicesWhoIsChanged.Any(x => x.DeviceOwner == knownDevice.DeviceOwner) || !WiFiDevicesWhoIsChanged.Any())
                        {
                            WiFiDevicesWhoIsChanged.Add(new Localdevice()
                            {
                                DeviceOwner = knownDevice.DeviceOwner,
                                DeviceName = knownDevice.DeviceName,
                                IsPresent = knownDevice.IsPresent,
                                StatusFrom = METHOD.UnixTimeStampToDateTime(knownDevice.StatusUnixTime),
                                SignalType = knownDevice.SignalType,
                                IsChanged = knownDevice.IsChanged
                            });
                        }
                    }
                }

                //update the list of the correct statuses, are they home or away
                foreach (var item in WiFiDevicesWhoIsChanged)
                {
                    item.IsChanged = false;
                    foreach (var device in WiFiDevicesToPowerApps)
                    {
                        if (item.DeviceOwner == device.DeviceOwner && (device.DeviceType == WiFiDevice.MOBILE || device.DeviceType == WiFiDevice.WATCH || device.DeviceType == WiFiDevice.CAR))
                        {
                            if (device.IsChanged)
                            {
                                item.IsChanged = device.IsChanged;
                                item.DeviceName = device.DeviceName;
                                item.StatusFrom = device.StatusFrom.AddHours(timeOffset);
                                item.SignalType = device.SignalType;
                                item.IsPresent = device.IsPresent;
                            }
                            //if device is exist and has'nt been changed, then mark it as unchanged and move to next item
                            //if device is not exist (not seen) but has been changed then mark it as unchanged and move to next item = do not notify leaving devices
                            //this is XOR
                            if (device.IsPresent ^ device.IsChanged)
                            {
                                item.IsChanged = false;
                                break;
                            }
                        }
                    }
                }

                //save the data locally into Raspberry
                var jsonString = JsonSerializer.Serialize(WiFiDevice.WellKnownDevices);
                await Methods.SaveStringToLocalFile(filename, jsonString);

                //send an e-mail only if someone has arrived at home or left the home
                if (WiFiDevicesWhoIsChanged.Any(x => x.IsChanged))
                {
                    string status = "Look who is at home:\n\n";

                    WiFiDevicesWhoIsChanged.ForEach(x => status += $"{(x.IsPresent ? x.DeviceOwner + " " + x.DeviceName + " at home from" : x.DeviceOwner + " not seen since")} {x.StatusFrom:HH:mm dd.MM.yyyy} \n ");
                    var x = WiFiDevicesWhoIsChanged.First(x => x.IsChanged);
                    string whoChanged = $"{(x.IsPresent ? x.DeviceOwner + " " + x.DeviceName + " at home " : x.DeviceOwner + " not seen since")} {x.StatusFrom:HH:mm dd.MM.yyyy}";
                    _sendListData = new SendDataAzure();
                    TelemetryDataClass.SourceInfo = $"{whoChanged}";
                    //send data to CosmosDB
                    var monitorData = new
                    {
                        DeviceID = "HomeController",
                        TelemetryDataClass.SourceInfo,
                        status,
                        DateAndTime = METHOD.DateTimeTZ().DateTime,
                        isHomeSecured = true
                    };
                    await _sendListData.PipeMessage(monitorData, Program.IoTHubModuleClient, TelemetryDataClass.SourceInfo, "output");
                }


                //if any mobile phone or watch is present then someone is at home
                IsAnyMobileAtHome = WiFiDevice.WellKnownDevices.Any(x => x.IsPresent && (x.DeviceType == WiFiDevice.MOBILE || x.DeviceType == WiFiDevice.WATCH || x.DeviceType == WiFiDevice.CAR));
                //if security mode automatic and home secured then unsecure home if any known mobile device is seen
                if (IsAnyMobileAtHome && TelemetryDataClass.isHomeSecured && !SomeoneAtHome.IsSecurityManuallyOn) SomeoneAtHome.SomeoneAtHomeChanged();

                #region Known Device Listing, only for debugging

                //for local debugging only show the active/non active devices in console window
                if (isAnyDeviceChanged)
                {
                    isAnyDeviceChanged = false;
                    var sortedList = WiFiDevice.WellKnownDevices.OrderByDescending(y => y.IsPresent).ThenBy(w => w.AccessPoint).ThenBy(z => z.SignalType).ThenBy(x => x.DeviceName).ToList();
                    Console.WriteLine();
                    Console.WriteLine($"All known devices at: {METHOD.DateTimeTZ().DateTime:G}");
                    Console.WriteLine();
                    Console.WriteLine($"   |   From   | Status  |          Device           |        WiFi network      |        AccessPoint       |  SignalType");
                    Console.WriteLine($"   |  ------  | ------- |         --------          |           -----          |           -----          |  --------  ");
                    foreach (var device in sortedList)
                    {
                        if (device.LastUnixTime > 0 && device.SignalType != "Wi-Fi AP" && device.DeviceOwner != "Unknown" && device.DeviceType != WiFiDevice.DEVICE) //show only my own ever seen LocalUserDevices devices
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
                }

                #endregion

                #region Unknown Devices Debugging

                //prepare last active devices query
                string urlLastActive = $"{urlKismet}/devices/last-time/{CONSTANT.ACTIVE_DEVICES_IN_LAST}/devices.json";
                string jsonContentFields = "json={\"fields\":" + jsonFields + "}";
                List<WiFiDevice> KismetActiveDevices = await GetDevices(urlLastActive, jsonContentFields);
                //removing all known devices from the last seen devices list
                //known devices are all locally registered devices in the list WiFiDevice.WifiDevices
                var tempDelList = new List<WiFiDevice>();
                var AllActiveDevices = new List<WiFiDevice>(KismetActiveDevices);
                foreach (var kismet in KismetActiveDevices)
                {
                    foreach (var knownDevice in WiFiDevice.WellKnownDevices)
                    {
                        if (kismet.MacAddress == knownDevice.MacAddress || kismet.LastSignal < CONSTANT.SIGNAL_TRESHOLD || (kismet.CommonName == kismet.MacAddress && kismet.Manufacture == "Unknown"))
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
                        foreach (var unknownDevice in UnknownDevices)
                        {
                            if (kismet.MacAddress == unknownDevice.MacAddress)
                            {
                                unknownDevice.Count++;
                                unknownDevice.LastUnixTime = kismet.LastUnixTime;
                                unknownDevice.WiFiName = kismet.ProbedSSID;
                                if (kismet.SignalType.Contains("Wi-Fi"))
                                {
                                    //get device AccessPoint and WIFI network names from Kismet (if reported by device)
                                    List<WiFiDevice> KismetOneMac = new List<WiFiDevice>();
                                    string urlOneMac = $"{urlKismet}/devices/by-mac/{kismet.MacAddress}/devices.json";
                                    KismetOneMac = await GetDevices(urlOneMac, jsonContentFields);
                                    //have to check KismetOneMac list because sometimes it is empty
                                    if (KismetOneMac.Any())
                                    {
                                        unknownDevice.AccessPoint = AllActiveDevices.Where(x => x.MacAddress == KismetOneMac.First().LastBSSID).Select(x => x.BaseName).DefaultIfEmpty("No AP").First();
                                        if (string.IsNullOrEmpty(kismet.ProbedSSID) || unknownDevice.WiFiName == "0")
                                            unknownDevice.WiFiName = AllActiveDevices.Where(x => x.MacAddress == KismetOneMac.First().LastBSSID).Select(x => x.SSID).DefaultIfEmpty("No AP").First();
                                    }
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
                    UnknownDevices.AddRange(tempAddList.ToArray());

                    UnknownDevices.RemoveAll(x => (unixTimestamp - x.LastUnixTime) / 60 > 120); //remove all entries older that 2 hour

                    //local debugging: show the devices list in console only if some device has been added
                    if (tempAddList.Any() && UnknownDevices.Any())
                    {
                        var sortedList = UnknownDevices.OrderBy(x => x.SignalType).ThenByDescending(y => y.LastUnixTime).ToList();
                        Console.WriteLine();
                        Console.WriteLine($"All unknown devices at: {METHOD.DateTimeTZ().DateTime:G}");
                        Console.WriteLine();
                        Console.WriteLine($"dB  | First | Last  |    Mac Address    |Count |  SignalType   |         WiFi network      |         AccessPoint       |    Common Name     | Manufacturer ");
                        Console.WriteLine($" -  | ----  | ----  |    -----------    | ---  |  ----------   |          ---------        |          ---------        |    -----------     |  -----------  ");

                        foreach (var device in sortedList)
                        {
                            Console.WriteLine($"{device.LastSignal}{"".PadRight(device.LastSignal < 0 ? 1 : 3)}" +
                                $"| {METHOD.UnixTimeStampToDateTime(device.FirstUnixTime).AddHours(timeOffset):t} " +
                                $"| {METHOD.UnixTimeStampToDateTime(device.LastUnixTime).AddHours(timeOffset):t} " +
                                $"| {device.MacAddress} " +
                                $"| {device.Count}{"".PadRight(device.Count < 10 ? 3 : device.Count < 100 ? 2 : 1)} " +
                                $"| {device.SignalType}{"".PadRight(14 - (!string.IsNullOrEmpty(device.SignalType) ? (device.SignalType.Length > 14 ? 14 : device.SignalType.Length) : 0))}" +
                                $"| {device.WiFiName}{"".PadRight(26 - (!string.IsNullOrEmpty(device.WiFiName) ? (device.WiFiName.Length > 26 ? 26 : device.WiFiName.Length) : 0))}" +
                                $"| {device.AccessPoint}{"".PadRight(26 - (!string.IsNullOrEmpty(device.AccessPoint) ? (device.AccessPoint.Length > 26 ? 26 : device.AccessPoint.Length) : 0))}" +
                                $"| {device.CommonName}{"".PadRight(19 - (!string.IsNullOrEmpty(device.CommonName) ? (device.CommonName.Length > 19 ? 19 : device.CommonName.Length) : 0))}" +
                                $"| {device.Manufacture}");
                        }
                        Console.WriteLine();
                    }
                }
                #endregion

                await Task.Delay(TimeSpan.FromSeconds(60)); //check statuses every 10 millisecond
            }
        }

        List<WiFiDevice> AddRemoveChangeDevices(List<Localdevice> devicesFromPowerApps, List<WiFiDevice> localWiFiDevices)
        {
            var tempAddList = new List<WiFiDevice>();
            var tempDeleteList = new List<WiFiDevice>();
            bool isNewItem = true;
            //CRUD operations, data is coming from PowerApps
            foreach (var item in devicesFromPowerApps)
            {
                foreach (var device in localWiFiDevices)
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
            localWiFiDevices.AddRange(tempAddList.ToArray());
            localWiFiDevices.RemoveAll(i => tempDeleteList.Any(x => x.MacAddress == i.MacAddress));
            return localWiFiDevices;
        }

        async Task<List<WiFiDevice>> GetDevices(string url, string jsonContent)
        {
            List<WiFiDevice> devices = new List<WiFiDevice>();
            string debug = "";
            try
            {
                HttpContent httpContent = new StringContent(jsonContent);
                httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded")
                {
                    CharSet = "UTF-8"
                };
                HttpResponseMessage responseMsg = await http.PostAsync(url, httpContent);
                var result = responseMsg.Content.ReadAsStringAsync();
                debug = result.Result;
                devices = JsonSerializer.Deserialize<List<WiFiDevice>>(result.Result);
            }
            catch (JsonException e)
            {
                Console.WriteLine($"Json exception: {e.Message}. All devices {debug}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Kismet query error: {e.Message}.");
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
            var npsOut = JsonSerializer.Deserialize<List<Localdevice>>(result.Result);

            //convert from Localdevice (CosmosDB) to WiFiDevice 
            var AllWiFiDevices = new List<WiFiDevice>();
            npsOut.ForEach(x => AllWiFiDevices.Add(new WiFiDevice(
                x.MacAddress,
                x.DeviceName,
                x.DeviceOwner,
                x.DeviceType,
                x.ActiveDuration,
                METHOD.DateTimeToUnixTimestamp(x.StatusFrom),
                METHOD.DateTimeToUnixTimestamp(x.StatusFrom),
                x.SignalType,
                x.AccessPoint
                )
            ));

            return AllWiFiDevices;
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
                SignalType = x.SignalType,
                AccessPoint = x.AccessPoint,
                IsChanged = x.IsChanged
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
        public bool IsChanged { get; set; } //this will be true if device status changed

    }
    class WiFiDevice
    {
        public WiFiDevice(string macAddress, string deviceName, string deviceOwner, int deviceType = DEVICE, int activeDuration = 0, double statusUnixTime = 0, double lastUnixTime = 0, string signalType = "", string accessPoint = "", bool isPresent = false, bool statusChange = true)
        {
            MacAddress = macAddress;
            ActiveDuration = activeDuration;
            DeviceName = deviceName;
            DeviceOwner = deviceOwner;
            DeviceType = deviceType;
            IsPresent = isPresent;
            StatusChange = statusChange;
            StatusUnixTime = statusUnixTime;
            LastUnixTime = lastUnixTime;
            SignalType = signalType;
            AccessPoint = accessPoint;
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
        public bool IsChanged { get; set; } //this will be true if device status changed
        [JsonPropertyName("kismet.device.base.name")]
        public string BaseName { get; set; }
        [JsonPropertyName("kismet.device.base.commonname")]
        public string CommonName { get; set; }
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
        public static List<WiFiDevice> WellKnownDevices = new List<WiFiDevice>
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
            "kismet.device.base.commonname",
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
