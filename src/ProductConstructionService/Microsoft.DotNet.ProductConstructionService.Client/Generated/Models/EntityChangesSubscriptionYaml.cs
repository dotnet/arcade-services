// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class EntityChangesSubscriptionYaml
    {
        public EntityChangesSubscriptionYaml()
        {
        }

        [JsonProperty("creations")]
        public List<ClientSubscriptionYaml> Creations { get; set; }

        [JsonProperty("updates")]
        public List<ClientSubscriptionYaml> Updates { get; set; }

        [JsonProperty("removals")]
        public List<ClientSubscriptionYaml> Removals { get; set; }
    }
}
