using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class Asset
    {
        public Asset(int id, int buildId, bool nonShipping, string name, string version, IImmutableList<Models.AssetLocation> locations)
        {
            Id = id;
            BuildId = buildId;
            NonShipping = nonShipping;
            Name = name;
            Version = version;
            Locations = locations;
        }

        [JsonProperty("id")]
        public int Id { get; }

        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("version")]
        public string Version { get; }

        [JsonProperty("buildId")]
        public int BuildId { get; set; }

        [JsonProperty("nonShipping")]
        public bool NonShipping { get; set; }

        [JsonProperty("locations")]
        public IImmutableList<Models.AssetLocation> Locations { get; }
    }
}
