// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class EntityChangesChannelYaml
    {
        public EntityChangesChannelYaml()
        {
        }

        [JsonProperty("creations")]
        public IImmutableList<Models.ClientChannelYaml> Creations { get; set; }

        [JsonProperty("updates")]
        public IImmutableList<Models.ClientChannelYaml> Updates { get; set; }

        [JsonProperty("removals")]
        public IImmutableList<Models.ClientChannelYaml> Removals { get; set; }
    }
}
