// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.PullRequestUpdaters;

internal class BatchedCodeFlowPullRequestUpdater : CodeFlowPullRequestUpdater
{
    private readonly BatchedPullRequestUpdaterId _id;
    private readonly BuildAssetRegistryContext _context;

    public BatchedCodeFlowPullRequestUpdater(
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
            ILocalLibGit2Client gitClient,
            IVmrInfo vmrInfo,
            IPcsVmrForwardFlower vmrForwardFlower,
            IPcsVmrBackFlower vmrBackFlower,
            ITelemetryRecorder telemetryRecorder,
            ILogger<BatchedDependencyPullRequestUpdater> logger,
            ICommentCollector commentCollector,
            IPullRequestCommenter pullRequestCommenter,
            IFeatureFlagService featureFlagService)
        : base(
            id,
            mergePolicyEvaluator,
            context,
            remoteFactory,
            updaterFactory,
            coherencyUpdateResolver,
            pullRequestBuilder,
            cacheFactory,
            reminderManagerFactory,
            sqlClient,
            gitClient,
            vmrInfo,
            vmrForwardFlower,
            vmrBackFlower,
            telemetryRecorder,
            logger,
            commentCollector,
            pullRequestCommenter,
            featureFlagService)
    {
        _id = id;
        _context = context;
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
}
