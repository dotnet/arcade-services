// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
{
    public partial class BuildGraph
    {
        public BuildGraph(IReadOnlyDictionary<string, Build> builds)
        {
            Builds = builds;
        }

        [JsonProperty("builds")]
        public IReadOnlyDictionary<string, Build> Builds { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (Builds == default(IReadOnlyDictionary<string, Build>))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
