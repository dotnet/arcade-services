// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     A <see cref="PullRequestActorImplementation" /> that reads its Merge Policies and Target information from a
///     non-batched subscription object
/// </summary>
internal class NonBatchedPullRequestActor : PullRequestActor
{
    public NonBatchedPullRequestActor(NonBatchedPullRequestActorId id)
        : base(id)
    {
    }

    protected override Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions() => throw new NotImplementedException();
    protected override Task<(string repository, string branch)> GetTargetAsync() => throw new NotImplementedException();
}
