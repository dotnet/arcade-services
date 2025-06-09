// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.DotNet.DarcLib;
using Maestro.Data.Models;
using NUnit.Framework;
using FluentAssertions;

namespace ProductConstructionService.DependencyFlow.Tests;
internal class MergePolicyEvaluationTests : PullRequestUpdaterTests
{
    protected static string DeprecatedMergePolicyName = "Deprecated";
    protected static string DeprecatedMergePolicyDisplayName = "Deprecated Merge Policy";

    protected static string AlwaysSucceedMergePolicyName = "AlwaysSucceed";
    protected static string AlwaysSucceedMergePolicyDisplayName = "Always Succeed Merge Policy";

    protected static string AlwaysFailMergePolicyName = "AlwaysFail";
    protected static string AlwaysFailMergePolicyDisplayName = "Always Fail Merge Policy";

    protected static MergePolicyEvaluationResult AlwaysSucceedMergePolicyResult = new MergePolicyEvaluationResult(
        MergePolicyEvaluationStatus.DecisiveSuccess,
        "check succeeded :)",
        "yay :)",
        AlwaysSucceedMergePolicyName,
        AlwaysSucceedMergePolicyDisplayName);

    protected static MergePolicyEvaluationResult AlwaysFailMergePolicyResult = new MergePolicyEvaluationResult(
        MergePolicyEvaluationStatus.DecisiveFailure,
        "check failed :(",
        "oh no :(",
        AlwaysSucceedMergePolicyName,
        AlwaysSucceedMergePolicyDisplayName);


    protected static MergePolicyEvaluationResult DeprecatedMergePolicyResult = new MergePolicyEvaluationResult(
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

        var alwaysFailMergePolicyDefinition = new MergePolicyDefinition();
        alwaysFailMergePolicyDefinition.Name = AlwaysFailMergePolicyName;

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

        using (WithExistingCodeFlowPullRequest(oldBuild, canUpdate: true, willFlowNewBuild: true, mockMergePolicyEvaluator: false))
        {
            ExpectPrMetadataToBeUpdated();

            MergePolicyEvaluationResults mergePolicyEvaluationResults = new MergePolicyEvaluationResults(
                new List<MergePolicyEvaluationResult> { AlwaysFailMergePolicyResult, DeprecatedMergePolicyResult }.AsReadOnly(),
                newBuild.Commit);

            SetState(Subscription, mergePolicyEvaluationResults);

            //todo find a way to inject a real mergepolicyevaluator
            await WhenUpdateAssetsAsyncIsCalled(newBuild);

            MergePolicyEvaluationResults expectedMergePolicyEvaluationResults = new MergePolicyEvaluationResults(
                new List<MergePolicyEvaluationResult> { AlwaysFailMergePolicyResult }.AsReadOnly(),
                newBuild.Commit);

            ThenShouldHaveCachedMergePolicyResults(expectedMergePolicyEvaluationResults);

            ThenShouldHaveInProgressPullRequestState(newBuild);
            AndCodeShouldHaveBeenFlownForward(newBuild);
            AndShouldHavePullRequestCheckReminder();

            VerifyCachedMergePolicyResults(expectedMergePolicyEvaluationResults);
        }
    }

    private void VerifyCachedMergePolicyResults(MergePolicyEvaluationResults expectedMergePolicyEvaluationResults)
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
