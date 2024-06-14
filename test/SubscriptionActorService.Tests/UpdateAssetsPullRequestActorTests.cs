// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Threading.Tasks;
using Maestro.Contracts;
using Maestro.Data.Models;

using Asset = Maestro.Contracts.Asset;

namespace SubscriptionActorService.Tests;

internal abstract class UpdateAssetsPullRequestActorTests : PullRequestActorTests
{
    protected async Task WhenUpdateAssetsAsyncIsCalled(Build forBuild)
    {
        await Execute(
            async context =>
            {
                PullRequestActor actor = CreateActor(context);
                await actor.Implementation!.UpdateAssetsAsync(
                    Subscription.Id,
                    Subscription.SourceEnabled ? SubscriptionType.DependenciesAndSources : SubscriptionType.Dependencies,
                    forBuild.Id,
                    SourceRepo,
                    forBuild.Commit,
                    forBuild.Assets.Select(
                            a => new Asset
                            {
                                Name = a.Name,
                                Version = a.Version
                            })
                        .ToList());
            });
    }
}
