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

    protected static readonly string AlwaysSucceedMergePolicyName = "AlwaysSucceed";
    protected static readonly string AlwaysSucceedMergePolicyDisplayName = "Always Succeed Merge Policy";

    protected static readonly string AlwaysFailMergePolicyName = "AlwaysFail";
    protected static readonly string AlwaysFailMergePolicyDisplayName = "Always Fail Merge Policy";

    protected static readonly MergePolicyEvaluationResult AlwaysSucceedMergePolicyResult = new(
        MergePolicyEvaluationStatus.DecisiveSuccess,
        "check succeeded :)",
        "yay :)",
        AlwaysSucceedMergePolicyName,
        AlwaysSucceedMergePolicyDisplayName);

    protected static readonly MergePolicyEvaluationResult AlwaysFailMergePolicyResult = new(
        MergePolicyEvaluationStatus.DecisiveFailure,
        "check failed :(",
        "oh no :(",
        AlwaysSucceedMergePolicyName,
        AlwaysSucceedMergePolicyDisplayName);


    protected static readonly MergePolicyEvaluationResult DeprecatedMergePolicyResult = new(
        MergePolicyEvaluationStatus.DecisiveFailure,
        "N/A",
        "This result should never exist after merge policy evaluation",
        DeprecatedMergePolicyName,
        DeprecatedMergePolicyDisplayName);

    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);
        services.TryAddSingleton<IMergePolicyEvaluator, MergePolicyEvaluator>();
        services.AddTransient<IMergePolicyBuilder, AlwaysSucceedMergePolicyBuilder>();
        services.AddTransient<IMergePolicyBuilder, AlwaysFailMergePolicyBuilder>();
        services.AddTransient<IMergePolicyBuilder, DeprecatedMergePolicyBuilder>();
    }

    protected async Task WhenUpdateAssetsAsyncIsCalled(Build forBuild)
    {
        await Execute(
            async context =>
            {
                IPullRequestUpdater updater = CreatePullRequestActor(context);
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

        var alwaysFailMergePolicyDefinition = new MergePolicyDefinition
        {
            Name = AlwaysFailMergePolicyName
        };

        GivenACodeFlowSubscription(
            new SubscriptionPolicy
            {
                Batchable = false,
                UpdateFrequency = UpdateFrequency.EveryBuild,
                MergePolicies = [alwaysFailMergePolicyDefinition]
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

            //todo find a way to inject a real mergepolicyevaluator
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

    protected class AlwaysSucceedMergePolicy : MergePolicy
    {
        public override string Name => AlwaysSucceedMergePolicyName;
        public override string DisplayName => AlwaysSucceedMergePolicyDisplayName;
        public override Task<MergePolicyEvaluationResult> EvaluateAsync(
            PullRequestUpdateSummary pullRequest,
            IRemote remote)
        {
            return Task.FromResult(AlwaysSucceedMergePolicyResult);
        }
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

    protected class DeprecatedMergePolicy : MergePolicy
    {
        public override string Name => DeprecatedMergePolicyName;
        public override string DisplayName => DeprecatedMergePolicyDisplayName;

        public override Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc) => throw new NotImplementedException();
    }

    internal class DeprecatedMergePolicyBuilder : IMergePolicyBuilder
    {
        public string Name => DeprecatedMergePolicyName;
        public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
        {
            IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new DeprecatedMergePolicy() };
            return Task.FromResult(policies);
        }
    }

    internal class AlwaysSucceedMergePolicyBuilder : IMergePolicyBuilder
    {
        public string Name => AlwaysSucceedMergePolicyName;
        public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
        {
            IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new AlwaysSucceedMergePolicy() };
            return Task.FromResult(policies);
        }
    }

    internal class AlwaysFailMergePolicyBuilder : IMergePolicyBuilder
    {
        public string Name => AlwaysFailMergePolicyName;
        public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
        {
            IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new AlwaysFailMergePolicy() };
            return Task.FromResult(policies);
        }
    }
}
