// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class EntityChangesChannelYaml
    {
        public EntityChangesChannelYaml()
        {
        }

        [JsonProperty("creations")]
        public List<ClientChannelYaml> Creations { get; set; }

        [JsonProperty("updates")]
        public List<ClientChannelYaml> Updates { get; set; }

        [JsonProperty("removals")]
        public List<ClientChannelYaml> Removals { get; set; }
    }
}
