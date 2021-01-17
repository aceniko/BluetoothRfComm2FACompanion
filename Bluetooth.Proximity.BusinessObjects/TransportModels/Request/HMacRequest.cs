using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bluetooth.Proximity.BusinessObjects.TransportModels
{
    public class HMacRequest
    {
        [JsonProperty("request_command")]
        public string RequestCommand { get; set; }

        [JsonProperty("srv_nonce")]
        public string SrvNonce { get; set; }
        [JsonProperty("sess_nonce")]
        public string SessNonce { get; set; }
        [JsonProperty("dev_nonce")]
        public string DevNonce { get; set; }
    }
}
