// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.Data.Models;
using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.Model;

namespace ProductConstructionService.DependencyFlow.Tests;
internal class MergePolicyEvaluationTests : PullRequestUpdaterTests
{
    protected static readonly string DeprecatedMergePolicyName = "Deprecated";
    protected static readonly string DeprecatedMergePolicyDisplayName = "Deprecated Merge Policy";

    protected static readonly string AlwaysFailMergePolicyName = "AlwaysFail";
    protected static readonly string AlwaysFailMergePolicyDisplayName = "Always Fail Merge Policy";

    protected static readonly MergePolicyEvaluationResult AlwaysFailMergePolicyResult = new(
        MergePolicyEvaluationStatus.DecisiveFailure,
        "check failed :(",
        "oh no :(",
        AlwaysFailMergePolicyName,
        AlwaysFailMergePolicyDisplayName);

    protected static readonly MergePolicyEvaluationResult DeprecatedMergePolicyResult = new(
        MergePolicyEvaluationStatus.DecisiveFailure,
        "N/A",
        "This result should never exist after merge policy evaluation",
        DeprecatedMergePolicyName,
        DeprecatedMergePolicyDisplayName);

    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);
        services.Replace(ServiceDescriptor.Singleton<IMergePolicyBuilder, TestMergePolicyBuilder>());
    }

    protected async Task WhenUpdateAssetsAsyncIsCalled(Build forBuild)
    {
        await Execute(
            async context =>
            {
                IPullRequestUpdater updater = CreatePullRequestActor(context, isCodeflow: true);
                await updater.UpdateAssetsAsync(
                    Subscription.Id,
                    Subscription.SourceEnabled ? SubscriptionType.DependenciesAndSources : SubscriptionType.Dependencies,
                    forBuild.Id,
                    applyNewestOnly: false);
            });
    }

    [Test]
    public async Task TestPRUpdaterWithMergePolicyEvaluation()
    {
        GivenATestChannel();

        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild,
            });

        Build oldBuild = GivenANewBuild(true);
        Build newBuild = GivenANewBuild(true);
        newBuild.Commit = "sha123456";

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true, willFlowNewBuild: true, mockMergePolicyEvaluator: false, sourceRepoNotified: true))
        {
            ExpectPrMetadataToBeUpdated();

            var mergePolicyEvaluationResults = new MergePolicyEvaluationResults(
                [ AlwaysFailMergePolicyResult, DeprecatedMergePolicyResult ],
                InProgressPrHeadBranchSha);

            SetState(Subscription, mergePolicyEvaluationResults);

            await WhenUpdateAssetsAsyncIsCalled(newBuild);

            var expectedMergePolicyEvaluationResults = new MergePolicyEvaluationResults(
                [ AlwaysFailMergePolicyResult ],
                InProgressPrHeadBranchSha);

            ThenShouldHaveCachedMergePolicyResults(expectedMergePolicyEvaluationResults);

            ThenShouldHaveInProgressPullRequestState(newBuild, sourceRepoNotified: true);
            AndCodeShouldHaveBeenFlownForward(newBuild);
            AndShouldHavePullRequestCheckReminder();

            VerifyCachedMergePolicyResults();
        }
    }

    private void VerifyCachedMergePolicyResults()
    {
        Cache.Data.Where(pair => pair.Value is MergePolicyEvaluationResults).Should().BeEquivalentTo(ExpectedEvaluationResultCacheState);
    }

    protected class AlwaysFailMergePolicy : MergePolicy
    {
        public override string Name => AlwaysFailMergePolicyName;
        public override string DisplayName => AlwaysFailMergePolicyDisplayName;
        public override Task<MergePolicyEvaluationResult> EvaluateAsync(
            PullRequestUpdateSummary pullRequest,
            IRemote remote)
        {
            return Task.FromResult(AlwaysFailMergePolicyResult);
        }
    }

    internal class TestMergePolicyBuilder : IMergePolicyBuilder
    {
        public IReadOnlyList<IMergePolicy> BuildBatchedSubscriptionMergePolicies(
            Microsoft.DotNet.ProductConstructionService.Client.Models.RepositoryBranch? repositoryBranch)
            => [new AlwaysFailMergePolicy()];

        public IReadOnlyList<IMergePolicy> BuildNonBatchedSubscriptionMergePolicies(
            Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription subscription)
            => [new AlwaysFailMergePolicy()];
    }
}
