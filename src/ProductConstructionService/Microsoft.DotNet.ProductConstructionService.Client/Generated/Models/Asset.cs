// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class Asset
    {
        public Asset(int id, int buildId, bool nonShipping, string name, string version, List<AssetLocation> locations)
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
        public List<AssetLocation> Locations { get; }
    }
}
