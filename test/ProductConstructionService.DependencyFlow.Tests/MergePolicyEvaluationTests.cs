// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.DotNet.DarcLib;


namespace ProductConstructionService.DependencyFlow.Tests;
internal class MergePolicyEvaluationTests : PullRequestUpdaterTests
{
    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);
        services.TryAddSingleton<IMergePolicyEvaluator, MergePolicyEvaluator>();
    }




    class AlwaysSucceedMergePolicy : MergePolicy
    {
        public override string Name => "AlwaysSucceed";
        public override string DisplayName => "Always Succeed Merge Policy";
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
        class AlwaysFailMergePolicy : MergePolicy
        {
            public override string Name => "AlwaysFail";
            public override string DisplayName => "Always Fail Merge Policy";
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
    }
}
