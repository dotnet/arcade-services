// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common.Cache;
using Maestro.Common.Telemetry;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow;

internal class BatchedDependencyPullRequestUpdater : DependencyPullRequestUpdater
{
    private readonly BatchedPullRequestUpdaterId _id;

    public BatchedDependencyPullRequestUpdater(
        BatchedPullRequestUpdaterId id,
        IMergePolicyEvaluator mergePolicyEvaluator,
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        IPullRequestUpdaterFactory updaterFactory,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IPullRequestBuilder pullRequestBuilder,
        IRedisCacheFactory cacheFactory,
        IReminderManagerFactory reminderManagerFactory,
        ISqlBarClient sqlClient,
        ITelemetryRecorder telemetryRecorder,
        ILogger<BatchedDependencyPullRequestUpdater> logger,
        ICommentCollector commentCollector,
        IPullRequestCommenter pullRequestCommenter)
        : base(id, mergePolicyEvaluator, context, remoteFactory, updaterFactory, coherencyUpdateResolver, pullRequestBuilder, cacheFactory, reminderManagerFactory, sqlClient, telemetryRecorder, logger, commentCollector, pullRequestCommenter)
    {
        _id = id;
    }

    protected override Task<(string repository, string branch)> GetTargetAsync()
    {
        return Task.FromResult((_id.Repository, _id.Branch));
    }

    protected override async Task<IReadOnlyList<MergePolicyDefinition>> GetMergePolicyDefinitions()
    {
        RepositoryBranch? repositoryBranch = await _context.RepositoryBranches.FindAsync(_id.Repository, _id.Branch);
        return repositoryBranch?.PolicyObject?.MergePolicies ?? [];
    }

    protected override Task TagSourceRepositoryGitHubContactsIfPossibleAsync(InProgressPullRequest pr) => Task.CompletedTask;
}
