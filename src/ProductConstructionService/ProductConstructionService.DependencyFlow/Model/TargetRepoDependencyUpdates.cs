// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.MergePolicies;
using Microsoft.DotNet.DarcLib.Helpers;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.Model;

internal class TargetRepoDependencyUpdates
{
    public required NullSafeUnixPathDictionary<TargetRepoDirectoryDependencyUpdates> DirectoryUpdates { get; set; }
    public required SubscriptionUpdateWorkItem SubscriptionUpdate { get; set; }
    public bool CoherencyCheckSuccessful => DirectoryUpdates.Values.All(v => v.CoherencyCheckSuccessful);

    public List<CoherencyErrorDetails> GetAgregatedCoherencyErrors()
        => DirectoryUpdates.Values.SelectMany(update => update.CoherencyErrors ?? [])
            .ToList();

}
