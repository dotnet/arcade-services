// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class ConfigurationIngestResult
    {
        public ConfigurationIngestResult(int added, int updated, int removed)
        {
            Added = added;
            Updated = updated;
            Removed = removed;
        }

        [JsonProperty("added")]
        public int Added { get; set; }

        [JsonProperty("updated")]
        public int Updated { get; set; }

        [JsonProperty("removed")]
        public int Removed { get; set; }
    }
}
