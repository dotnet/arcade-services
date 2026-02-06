// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using Microsoft.DotNet.DarcLib.Models.Darc;

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public class FlowGraph
{
    public static FlowGraph Create(DependencyFlowGraph other)
    {
        return new FlowGraph([.. other.Nodes.Select(FlowRef.Create)], [.. other.Edges.Select(FlowEdge.Create)]);
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
