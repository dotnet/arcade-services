// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class EntityChangesDefaultChannelYaml
    {
        public EntityChangesDefaultChannelYaml()
        {
        }

        [JsonProperty("creations")]
        public List<ClientDefaultChannelYaml> Creations { get; set; }

        [JsonProperty("updates")]
        public List<ClientDefaultChannelYaml> Updates { get; set; }

        [JsonProperty("removals")]
        public List<ClientDefaultChannelYaml> Removals { get; set; }
    }
}
