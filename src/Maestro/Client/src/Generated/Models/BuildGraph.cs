// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class BuildGraph
    {
        public BuildGraph(IImmutableDictionary<string, Build> builds)
        {
            Builds = builds;
        }

        [JsonProperty("builds")]
        public IImmutableDictionary<string, Build> Builds { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (Builds == default(IImmutableDictionary<string, Build>))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
