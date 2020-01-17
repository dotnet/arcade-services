using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.DotNet.DarcLib;

namespace Maestro.Web.Api.v2018_07_16.Models
{
    public class FlowGraph
    {
        public static FlowGraph Create(DependencyFlowGraph other)
        {
            return new FlowGraph(other.Nodes.Select(n => FlowRef.Create(n)).ToList(), other.Edges.Select(e => FlowEdge.Create(e)).ToList());
        }
        public FlowGraph(List<FlowRef> flowRefs, List<FlowEdge> flowEdges)
        {
            FlowRefs = flowRefs;
            FlowEdges = flowEdges;
        }

        [Required]
        public List<FlowRef> FlowRefs { get; }
        [Required]
        public List<FlowEdge> FlowEdges { get; }
    }
}
