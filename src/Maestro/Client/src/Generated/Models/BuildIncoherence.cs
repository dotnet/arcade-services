using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class BuildIncoherence
    {
        public BuildIncoherence()
        {
        }

        [JsonProperty("incoherentDeps")]
        public IImmutableList<Models.IncoherentDependency> IncoherentDeps { get; set; }

        [JsonProperty("incoherentNodes")]
        public IImmutableList<Models.IncoherentNode> IncoherentNodes { get; set; }
    }
}
