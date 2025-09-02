// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.MergePolicies;
using Microsoft.DotNet.DarcLib.Models.Darc;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.Model;

internal class TargetRepoDependencyUpdate
{
    public bool CoherencyCheckSuccessful { get; set; } = true;
    public List<CoherencyErrorDetails>? CoherencyErrors { get; set; }
    public List<(SubscriptionUpdateWorkItem update, List<DependencyUpdate> deps)> RequiredUpdates { get; set; } = [];
}
