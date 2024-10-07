// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
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
                if (Type == default)
                {
                    return false;
                }
                return true;
            }
        }
    }
}
