using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class AssetLocationData
    {
        public AssetLocationData(AssetLocationDataType type)
        {
            Type = type;
        }

        [JsonProperty("location")]
        public string Location { get; set; }

        [JsonProperty("type")]
        public AssetLocationDataType Type { get; set; }

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
