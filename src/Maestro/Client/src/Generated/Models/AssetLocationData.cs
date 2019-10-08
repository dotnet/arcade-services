using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class AssetLocationData
    {
        public AssetLocationData(LocationType type)
        {
            Type = type;
        }

        [JsonProperty("location")]
        public string Location { get; set; }

        [JsonProperty("type")]
        public LocationType Type { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (Type == default(LocationType))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
