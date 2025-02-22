// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class BuildIncoherence
    {
        public BuildIncoherence()
        {
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("repository")]
        public string Repository { get; set; }

        [JsonProperty("commit")]
        public string Commit { get; set; }
    }
}
