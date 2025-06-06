// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Asset = ProductConstructionService.DependencyFlow.Model.Asset;

namespace ProductConstructionService.DependencyFlow.Tests;

internal abstract class UpdateAssetsPullRequestUpdaterTests : PullRequestUpdaterTests
{
    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);
        services.AddSingleton(MergePolicyEvaluator.Object);
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
}
