// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class FlowGraph
    {
        public FlowGraph(List<FlowRef> flowRefs, List<FlowEdge> flowEdges)
        {
            FlowRefs = flowRefs;
            FlowEdges = flowEdges;
        }

        [JsonProperty("flowRefs")]
        public List<FlowRef> FlowRefs { get; set; }

        [JsonProperty("flowEdges")]
        public List<FlowEdge> FlowEdges { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (FlowRefs == default(List<FlowRef>))
                {
                    return false;
                }
                if (FlowEdges == default(List<FlowEdge>))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
