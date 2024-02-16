using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class FlowGraph
    {
        public FlowGraph(IImmutableList<Models.FlowRef> flowRefs, IImmutableList<Models.FlowEdge> flowEdges)
        {
            FlowRefs = flowRefs;
            FlowEdges = flowEdges;
        }

        [JsonProperty("flowRefs")]
        public IImmutableList<Models.FlowRef> FlowRefs { get; set; }

        [JsonProperty("flowEdges")]
        public IImmutableList<Models.FlowEdge> FlowEdges { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (FlowRefs == default(IImmutableList<Models.FlowRef>))
                {
                    return false;
                }
                if (FlowEdges == default(IImmutableList<Models.FlowEdge>))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
