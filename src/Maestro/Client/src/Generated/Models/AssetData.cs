using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class AssetData
    {
        public AssetData(bool nonShipping)
        {
            NonShipping = nonShipping;
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("nonShipping")]
        public bool NonShipping { get; set; }

        [JsonProperty("locations")]
        public IImmutableList<AssetLocationData> Locations { get; set; }
    }
}
