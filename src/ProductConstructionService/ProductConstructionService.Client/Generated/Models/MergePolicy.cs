// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
{
    public partial class MergePolicy
    {
        public MergePolicy()
        {
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("properties")]
        public IReadOnlyDictionary<string, Newtonsoft.Json.Linq.JToken> Properties { get; set; }
    }
}
