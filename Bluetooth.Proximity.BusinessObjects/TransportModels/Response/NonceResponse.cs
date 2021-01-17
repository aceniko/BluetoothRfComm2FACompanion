using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bluetooth.Proximity.BusinessObjects.TransportModels
{
    public class NonceResponse
    {
        [JsonProperty("response_command")]
        public string ResponseCommand { get; set; }

        [JsonProperty("nonce")]
        public string Nonce { get; set; }
    }
}
