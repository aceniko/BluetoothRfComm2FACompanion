using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bluetooth.Proximity.BusinessObjects.TransportModels
{
    public class RegisterDeviceResponse
    {
        [JsonProperty("response_command")]
        public string ResponseCommand { get; set; }
        [JsonProperty("device_key")]
        public string DeviceKey { get; set; }
        [JsonProperty("auth_key")]
        public string AuthKey { get; set; }
        [JsonProperty("device_name")]
        public string DeviceName { get; set; }
        [JsonProperty("device_model")]
        public string DeviceModel { get; set; }
    }
}
