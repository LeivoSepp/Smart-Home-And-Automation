using HomeModule.Helpers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace HomeModule.Schedulers
{
    class WiFiProbes
    {
        public static List<WiFiDevice> LocalDevices = new List<WiFiDevice>();
        public async void QueryWiFiProbes()
        {
            string username = Environment.GetEnvironmentVariable("KismetUser");
            string password = Environment.GetEnvironmentVariable("KismetPassword");
            string urlKismet = Environment.GetEnvironmentVariable("KismetURL");

            var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}")));

            //create a list of the MAC Addresses for multimac query
            List<string> deviceMacs = new List<string>();
            WiFiDevice.WifiDevices.ForEach(x => deviceMacs.Add(x.MacAddress));

            //create a list which has only known mobile-notebook-watches
            LocalDevices = WiFiDevice.WifiDevices.Where(x => x.DeviceType != WiFiDevice.DEVICE).ToList();
            LocalDevices.ForEach(x => x.StatusFrom = METHOD.DateTimeTZ().DateTime);

            string jsonFields = System.Text.Json.JsonSerializer.Serialize(KismetField.KismetFields); //serialize kismet fields
            string jsonDevices = System.Text.Json.JsonSerializer.Serialize(deviceMacs); //serialize mac addresses

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
            var temporary = new List<WiFiDevice>();

            while (true)
            {
                DateTime CurrentDateTime = METHOD.DateTimeTZ().DateTime;

                //execute multimac query
                HttpResponseMessage responseDevices = await http.PostAsync(urlDevices, httpContentDevices);
                var resultDevices = responseDevices.Content.ReadAsStringAsync();
                var WifiDevices = JsonConvert.DeserializeObject<List<WiFiDevice>>(resultDevices.Result);

                //execute last active devices query
                HttpResponseMessage responseLastActive = await http.PostAsync(urlLastActive, httpContentLastActive);
                var resultLastActive = responseLastActive.Content.ReadAsStringAsync();
                var WifiLastActive = JsonConvert.DeserializeObject<List<WiFiDevice>>(resultLastActive.Result);

                //building list of the local devce which is last seen
                foreach (var device in LocalDevices)
                {
                    foreach (var probe in WifiDevices)
                    {
                        if (device.MacAddress == probe.MacAddress)
                        {
                            device.LastSeen = METHOD.UnixTimeStampToDateTime(probe.LastTime);
                            device.LastSignal = probe.LastSignal;
                            break;
                        }
                    }
                    //checking active devices and setting statuses
                    var durationUntilNotActive = (CurrentDateTime - device.LastSeen).TotalMinutes;
                    device.IsPresent = durationUntilNotActive < device.ActiveDuration;
                    if (device.IsPresent && !device.StatusChange)
                    {
                        device.StatusFrom = CurrentDateTime;
                        device.StatusChange = true;
                        showHeaders = true;
                    }
                    if (!device.IsPresent && device.StatusChange)
                    {
                        device.StatusFrom = device.LastSeen;
                        device.StatusChange = false;
                        showHeaders = true;
                    }
                }

                if (showHeaders) //for local debugging
                {
                    Console.WriteLine($"   From   | Status  | Device");
                    Console.WriteLine($" -------- | ------- | ----- ");
                    foreach (var device in LocalDevices)
                    {
                        if (device.LastSeen != DateTime.MinValue) //dont show ever seen devices
                        {
                            Console.WriteLine($" {device.StatusFrom:T} | {(device.IsPresent ? "Active " : "Not Act")} | {device.DeviceName} ");
                        }
                    }
                    Console.WriteLine($"");
                    showHeaders = false;
                }

                //removing all known devices from the last seen devices list
                temporary.Clear();
                foreach (var probe in WifiLastActive)
                {
                    foreach (var Wifi in WiFiDevice.WifiDevices)
                    {
                        if (probe.MacAddress == Wifi.MacAddress || probe.LastSignal < CONSTANT.SIGNAL_TRESHOLD)
                        {
                            temporary.Add(probe);
                            break;
                        }
                    }
                }
                WifiLastActive.RemoveAll(i => temporary.Contains(i));

                //adding new members to the close devices list
                if (WifiLastActive.Any())
                {
                    temporary.Clear();
                    bool isNewItem = true;
                    foreach (var probe in WifiLastActive)
                    {
                        foreach (var item in closeDevices)
                        {
                            if (probe.MacAddress == item.MacAddress)
                            {
                                item.Count++;
                                isNewItem = false;
                                break;
                            }
                            else
                            {
                                isNewItem = true;
                            }
                        }
                        if (isNewItem) temporary.Add(probe);
                    }
                    closeDevices.AddRange(temporary);
                    var sortedList = closeDevices.OrderByDescending(x => x.Count).ToList();

                    if (temporary.Any())
                    {
                        Console.WriteLine($"dB  | Last  |    Mac Address    |Count| SSID");
                        Console.WriteLine($" -  | ----  |    -----------    | --- | ----");

                        foreach (var probe in sortedList)
                        {
                            Console.WriteLine($"{probe.LastSignal} | {METHOD.UnixTimeStampToDateTime(probe.LastTime):t} | {probe.MacAddress} | {probe.Count}  | {(string.IsNullOrEmpty(probe.ProbedSSID) || probe.ProbedSSID == "0" ? probe.SSID : probe.ProbedSSID)}");
                        }
                        Console.WriteLine();
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(60)); //check statuses every 10 millisecond
            }
        }
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
        public DateTime StatusFrom { get; set; }
        public DateTime LastSeen { get; set; }
        public int Count { get; set; }
        //[JsonProperty("kismet.device.base.commonname")]
        public string DeviceName { get; set; }
        [JsonProperty("kismet.device.base.macaddr")]
        public string MacAddress { get; set; }
        [JsonProperty("dot11.probedssid.ssid")]
        public string ProbedSSID { get; set; }
        [JsonProperty("dot11.device.last_bssid")]
        public string LastBSSID { get; set; }
        [JsonProperty("kismet.common.signal.last_signal")]
        public int LastSignal { get; set; }
        [JsonProperty("kismet.device.base.last_time")]
        public double LastTime { get; set; }
        [JsonProperty("dot11.advertisedssid.ssid")]
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
    class KismetField
    {
        public static List<string> KismetFields = new List<string>
        {
            ("kismet.device.base.macaddr"),
            ("dot11.device/dot11.device.last_bssid"),
            ("kismet.device.base.last_time"),
            ("kismet.device.base.signal/kismet.common.signal.last_signal"),
            ("dot11.device/dot11.device.last_probed_ssid_record/dot11.probedssid.ssid"),
            ("dot11.device/dot11.device.last_beaconed_ssid_record/dot11.advertisedssid.ssid")
        };
    }
}
