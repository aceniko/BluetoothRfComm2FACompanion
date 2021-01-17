using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bluetooth.Proximity.BusinessObjects.TransportModels
{
    public class HMacResponse
    {
        [JsonProperty("response_command")]
        public string ResponseCommand { get; set; }

        [JsonProperty("hmac_dk")]
        public string HmacDk { get; set; }
        
        [JsonProperty("hmac_sk")]
        public string HmacSk { get; set; }
    }
}
