// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace Microsoft.DotNet.DarcLib.Models.Darc;

public class DependencyFlowEdge
{
    public DependencyFlowEdge(DependencyFlowNode from, DependencyFlowNode to, Subscription subscription)
    {
        Subscription = subscription;
        From = from;
        To = to;
        OnLongestBuildPath = false;
        BackEdge = false;
    }

    // An edge is associated with a subscription
    public readonly Subscription Subscription;
    public readonly DependencyFlowNode From;
    public readonly DependencyFlowNode To;
    /// <summary>
    ///     True if the edge is part of a cycle, false if the edge is not, null if cycles have not been computed
    /// </summary>
    public bool? PartOfCycle { get; set; }
    public bool BackEdge { get; set; }
    public bool OnLongestBuildPath { get; set; }
    /// <summary>
    ///   True if all assets that "To" node depends on are tooling dependencies.
    /// </summary>
    public bool IsToolingOnly { get; set; }
}
