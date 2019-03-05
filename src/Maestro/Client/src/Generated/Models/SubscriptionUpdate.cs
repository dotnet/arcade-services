using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class SubscriptionUpdate
    {
        public SubscriptionUpdate()
        {
        }

        [JsonProperty("channelName")]
        public string ChannelName { get; set; }

        [JsonProperty("sourceRepository")]
        public string SourceRepository { get; set; }

        [JsonProperty("policy")]
        public SubscriptionPolicy Policy { get; set; }

        [JsonProperty("enabled")]
        public bool? Enabled { get; set; }
    }
}
