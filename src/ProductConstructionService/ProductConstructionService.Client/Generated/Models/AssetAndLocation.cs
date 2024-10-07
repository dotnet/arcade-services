// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
{
    public partial class AssetAndLocation
    {
        public AssetAndLocation(int assetId, Models.LocationType locationType)
        {
            AssetId = assetId;
            LocationType = locationType;
        }

        [JsonProperty("assetId")]
        public int AssetId { get; set; }

        [JsonProperty("location")]
        public string Location { get; set; }

        [JsonProperty("locationType")]
        public Models.LocationType LocationType { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (LocationType == default)
                {
                    return false;
                }
                return true;
            }
        }
    }
}
