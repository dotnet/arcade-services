using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class BuildTime
    {
        public BuildTime(int defaultChannelId, double officialBuildTime, double prBuildTime)
        {
            DefaultChannelId = defaultChannelId;
            OfficialBuildTime = officialBuildTime;
            PrBuildTime = prBuildTime;
        }

        [JsonProperty("defaultChannelId")]
        public int DefaultChannelId { get; set; }

        [JsonProperty("officialBuildTime")]
        public double OfficialBuildTime { get; set; }

        [JsonProperty("prBuildTime")]
        public double PrBuildTime { get; set; }
    }
}
