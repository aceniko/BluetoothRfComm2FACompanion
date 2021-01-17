using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bluetooth.Proximity.BusinessObjects.TransportModels
{
    public class NonceRequest
    {
        [JsonProperty("request_command")]
        public string RequestCommand { get; set; }

    }
}
