// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Helpers;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.Model;
internal class TargetRepoDependencyUpdates
{
    public required Dictionary<UnixPath, TargetRepoDirectoryDependencyUpdates> DirectoryUpdates { get; set; }
    public required SubscriptionUpdateWorkItem Update { get; set; }

}
