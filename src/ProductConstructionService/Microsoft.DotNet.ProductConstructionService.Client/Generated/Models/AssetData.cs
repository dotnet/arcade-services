// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
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
        public List<AssetLocationData> Locations { get; set; }
    }
}
