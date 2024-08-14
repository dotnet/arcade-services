// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.DotNet.DarcLib;

namespace Maestro.Api.Model.v2018_07_16;

public class FlowGraph
{
    public static FlowGraph Create(DependencyFlowGraph other)
    {
        return new FlowGraph(other.Nodes.Select(FlowRef.Create).ToList(), other.Edges.Select(FlowEdge.Create).ToList());
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
