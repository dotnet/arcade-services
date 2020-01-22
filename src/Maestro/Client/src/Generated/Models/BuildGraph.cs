using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class BuildGraph
    {
        public BuildGraph(IImmutableDictionary<string, Models.Build> builds)
        {
            Builds = builds;
        }

        [JsonProperty("builds")]
        public IImmutableDictionary<string, Models.Build> Builds { get; }

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
