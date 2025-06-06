// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.DotNet.DarcLib;
using Maestro.Data.Models;
using NUnit.Framework;


namespace ProductConstructionService.DependencyFlow.Tests;
internal class MergePolicyEvaluationTests : PullRequestUpdaterTests
{
    internal static string DeprecatedMergePolicyName = "Deprecated";
    internal static string DeprecatedMergePolicyDisplayName = "Deprecated Merge Policy";

    internal static string AlwaysSucceedMergePolicyName = "AlwaysSucceed";
    internal static string AlwaysSucceedMergePolicyDisplayName = "Always Succeed Merge Policy";

    internal static string AlwaysFailMergePolicyName = "AlwaysFail";
    internal static string AlwaysFailMergePolicyDisplayName = "Always Fail Merge Policy";

    MergePolicyEvaluationResult AlwaysSucceedMergePolicyResult = new MergePolicyEvaluationResult(
        MergePolicyEvaluationStatus.DecisiveFailure,
        "check succeeded :)",
        "yay :)",
        AlwaysSucceedMergePolicyName,
        AlwaysSucceedMergePolicyDisplayName);

    MergePolicyEvaluationResult AlwaysFailMergePolicyResult = new MergePolicyEvaluationResult(
        MergePolicyEvaluationStatus.DecisiveFailure,
        "check failed :(",
        "oh no :(",
        AlwaysSucceedMergePolicyName,
        AlwaysSucceedMergePolicyDisplayName);


    MergePolicyEvaluationResult DeprecatedMergePolicyResult = new MergePolicyEvaluationResult(
        MergePolicyEvaluationStatus.DecisiveFailure,
        "This text should never be returned",
        "This merge policy is deprecated and cannot be evaluated",
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
                Subscription.Id.ToString(),
                new List<MergePolicyEvaluationResult> { AlwaysFailMergePolicyResult, DeprecatedMergePolicyResult }.AsReadOnly(),
                newBuild.Commit);

            SetState(Subscription, mergePolicyEvaluationResults);

            //todo find a way to inject a real mergepolicyevaluator
            await WhenUpdateAssetsAsyncIsCalled(newBuild);

            MergePolicyEvaluationResults expectedMergePolicyEvaluationResults = new MergePolicyEvaluationResults(
                Subscription.Id.ToString(),
                new List<MergePolicyEvaluationResult> { AlwaysFailMergePolicyResult }.AsReadOnly(),
                newBuild.Commit);

            ThenShouldHaveCachedMergePolicyResults(expectedMergePolicyEvaluationResults);

            ThenShouldHaveInProgressPullRequestState(newBuild);
            AndCodeShouldHaveBeenFlownForward(newBuild);
            AndShouldHavePullRequestCheckReminder();
        }
    }

    class AlwaysSucceedMergePolicy : MergePolicy
    {
        public override string Name => AlwaysSucceedMergePolicyName;
        public override string DisplayName => AlwaysSucceedMergePolicyDisplayName;
        public override Task<MergePolicyEvaluationResult> EvaluateAsync(
            PullRequestUpdateSummary pullRequest,
            IRemote remote)
        {
            return Task.FromResult(new MergePolicyEvaluationResult(
                MergePolicyEvaluationStatus.DecisiveSuccess,
                "Success",
                "",
                Name,
                DisplayName));
        }
    }

    class AlwaysFailMergePolicy : MergePolicy
    {
        public override string Name => AlwaysFailMergePolicyName;
        public override string DisplayName => AlwaysFailMergePolicyDisplayName;
        public override Task<MergePolicyEvaluationResult> EvaluateAsync(
            PullRequestUpdateSummary pullRequest,
            IRemote remote)
        {
            return Task.FromResult(new MergePolicyEvaluationResult(
                MergePolicyEvaluationStatus.DecisiveSuccess,
                "Failure",
                "",
                Name,
                DisplayName));
        }
    }

    internal class DeprecatedMergePolicy : MergePolicy
    {
        public override string Name => DeprecatedMergePolicyName;
        public override string DisplayName => DeprecatedMergePolicyDisplayName;

        public override Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc) => throw new NotImplementedException();
    }

    public class DeprecatedMergePolicyBuilder : IMergePolicyBuilder
    {
        public string Name => DeprecatedMergePolicyName;
        public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
        {
            IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new DeprecatedMergePolicy() };
            return Task.FromResult(policies);
        }
    }

    public class AlwaysSucceedMergePolicyBuilder : IMergePolicyBuilder
    {
        public string Name => AlwaysSucceedMergePolicyName;
        public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
        {
            IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new AlwaysSucceedMergePolicy() };
            return Task.FromResult(policies);
        }
    }

    public class AlwaysFailMergePolicyBuilder : IMergePolicyBuilder
    {
        public string Name => AlwaysFailMergePolicyName;
        public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
        {
            IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new AlwaysFailMergePolicy() };
            return Task.FromResult(policies);
        }
    }
}
