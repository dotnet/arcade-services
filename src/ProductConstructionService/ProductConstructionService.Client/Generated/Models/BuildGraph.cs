// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
{
    public partial class BuildGraph
    {
        public BuildGraph(IImmutableDictionary<string, Models.Build> builds)
        {
            Builds = builds;
        }

        [JsonProperty("builds")]
        public IImmutableDictionary<string, Models.Build> Builds { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (Builds == default(IImmutableDictionary<string, Models.Build>))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
