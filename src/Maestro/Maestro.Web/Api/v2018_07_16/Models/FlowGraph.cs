using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Maestro.Web.Api.v2018_07_16.Models
{
    public class FlowGraph
    {
        public static FlowGraph Create(IEnumerable<FlowRef> flowRefs, List<FlowEdge> flowEdges)
        {
            return new FlowGraph(flowRefs.ToDictionary(f => f.DefaultChannelId, f => f), flowEdges);
        }
        public FlowGraph(IDictionary<int, FlowRef> flowRefs, List<FlowEdge> flowEdges)
        {
            FlowRefs = flowRefs;
            FlowEdges = flowEdges;
        }

        [Required]
        public IDictionary<int, FlowRef> FlowRefs { get; }
        [Required]
        public List<FlowEdge> FlowEdges { get; }
    }
}
