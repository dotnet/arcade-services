// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class EntityChangesDefaultChannelYaml
    {
        public EntityChangesDefaultChannelYaml()
        {
        }

        [JsonProperty("creations")]
        public IImmutableList<Models.ClientDefaultChannelYaml> Creations { get; set; }

        [JsonProperty("updates")]
        public IImmutableList<Models.ClientDefaultChannelYaml> Updates { get; set; }

        [JsonProperty("removals")]
        public IImmutableList<Models.ClientDefaultChannelYaml> Removals { get; set; }
    }
}
