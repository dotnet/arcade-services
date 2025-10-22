// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProductConstructionService.DependencyFlow.Model;

namespace ProductConstructionService.DependencyFlow.Tests;

internal abstract class UpdateAssetsPullRequestUpdaterTests : PullRequestUpdaterTests
{
    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);
        services.AddSingleton(MergePolicyEvaluator.Object);
    }

    protected async Task WhenUpdateAssetsAsyncIsCalled(Build forBuild, bool shouldGetUpdates = false)
    {
        await Execute(
            async context =>
            {
                if (shouldGetUpdates)
                {
                    DarcRemotes[TargetRepo].Setup(r => r.GetUpdatesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<DependencyDetail>>(), It.IsAny<UnixPath>()))
                        .ReturnsAsync([new GitFile("path", "content")]);
                }

                IPullRequestUpdater updater = CreatePullRequestActor(context);
                await updater.UpdateAssetsAsync(
                    Subscription.Id,
                    Subscription.SourceEnabled ? SubscriptionType.DependenciesAndSources : SubscriptionType.Dependencies,
                    forBuild.Id,
                    applyNewestOnly: false);
            });
    }
}
