// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Maestro.MergePolicies;
using Microsoft.DotNet.DarcLib;
using ProductConstructionService.DependencyFlow.Model;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     Pull request target for batched updaters.
///     Target repository and branch come from the <see cref="BatchedPullRequestUpdaterId"/>;
///     merge policies come from the <see cref="RepositoryBranch"/> entity in the database.
/// </summary>
internal class BatchedPullRequestTarget : IPullRequestTarget
{
    private readonly BatchedPullRequestUpdaterId _id;
    private readonly BuildAssetRegistryContext _context;
    private readonly IMergePolicyBuilder _mergePolicyBuilder;

    public string UpdaterId => _id.Id;

    public BatchedPullRequestTarget(
        BatchedPullRequestUpdaterId id,
        BuildAssetRegistryContext context,
        IMergePolicyBuilder mergePolicyBuilder)
    {
        _id = id;
        _context = context;
        _mergePolicyBuilder = mergePolicyBuilder;
    }

    public Task<(string Repository, string Branch)> GetTargetAsync()
    {
        return Task.FromResult((_id.Repository, _id.Branch));
    }

    public async Task<IReadOnlyList<IMergePolicy>> GetMergePoliciesAsync()
    {
        RepositoryBranch? repositoryBranch = await _context.RepositoryBranches.FindAsync(_id.Repository, _id.Branch);

        return _mergePolicyBuilder.BuildBatchedSubscriptionMergePolicies(SqlBarClient.ToClientModelRepositoryBranch(repositoryBranch
            ?? throw new DarcException($"Repository branch {_id.Repository}/{_id.Branch} doesn't exist in BAR")));
    }

    // For batched subscriptions we don't know which source repo to tag
    public Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr)
    {
        return Task.CompletedTask;
    }

    // For batched subscriptions we don't know which subscriptions are actually a part of the PR,
    // so we can't tell if all of them have been deleted
    public Task<bool> ShouldContinueProcessingAsync()
    {
        return Task.FromResult(true);
    }
}
