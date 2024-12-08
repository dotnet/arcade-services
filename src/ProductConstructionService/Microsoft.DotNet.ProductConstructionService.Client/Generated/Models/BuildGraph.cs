// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class BuildGraph
    {
        public BuildGraph(Dictionary<string, Build> builds)
        {
            Builds = builds;
        }

        [JsonProperty("builds")]
        public Dictionary<string, Build> Builds { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (Builds == default(Dictionary<string, Build>))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
