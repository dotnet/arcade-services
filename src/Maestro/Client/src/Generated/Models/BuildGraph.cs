using System;
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
        public IImmutableDictionary<string, Build> Builds { get; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (Builds == null)
                {
                    return false;
                }
                return true;
            }
        }
    }
}
