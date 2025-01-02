// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Asset = ProductConstructionService.DependencyFlow.Model.Asset;

namespace ProductConstructionService.DependencyFlow.Tests;

internal abstract class UpdateAssetsPullRequestUpdaterTests : PullRequestUpdaterTests
{
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
                    SourceRepo,
                    forBuild.Commit,
                    forBuild.Assets
                        .Select(a => new Asset
                        {
                            Name = a.Name,
                            Version = a.Version
                        })
                        .ToList());
            });
    }
}
