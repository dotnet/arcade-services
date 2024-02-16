using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class AssetLocation
    {
        public AssetLocation(int id, Models.LocationType type, string location)
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
        public Models.LocationType Type { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (Type == default(Models.LocationType))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
