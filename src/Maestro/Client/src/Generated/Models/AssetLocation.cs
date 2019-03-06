using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class AssetLocation
    {
        public AssetLocation(int id, AssetLocationType type, string location)
        {
            Id = id;
            Type = type;
            Location = location;
        }

        [JsonProperty("id")]
        public int Id { get; }

        [JsonProperty("location")]
        public string Location { get; }

        [JsonProperty("type")]
        public AssetLocationType Type { get; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (Type == default)
                {
                    return false;
                }
                return true;
            }
        }
    }
}
