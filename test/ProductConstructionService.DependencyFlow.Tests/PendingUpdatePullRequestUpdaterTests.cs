// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Maestro.DataProviders;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using BuildDTO = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;

namespace ProductConstructionService.DependencyFlow.Tests;

internal abstract class PendingUpdatePullRequestUpdaterTests : PullRequestUpdaterTests
{
    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);
        services.AddSingleton(MergePolicyEvaluator.Object);
    }

    protected async Task WhenProcessPendingUpdatesAsyncIsCalled(
        Build forBuild,
        bool isCodeFlow = false,
        bool applyNewestOnly = false,
        bool forceUpdate = false,
        bool shouldGetUpdates = false)
    {
        await Execute(
            async context =>
            {
                if (shouldGetUpdates)
                { 
                    DarcRemotes[TargetRepo].Setup(r => r.GetUpdatedDependencyFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<DependencyDetail>>(), It.IsAny<UnixPath>()))
                        .ReturnsAsync([new GitFile("path", "content")]);
                }
                BuildDTO buildDTO = SqlBarClient.ToClientModelBuild(forBuild);
                IPullRequestUpdater updater = CreatePullRequestActor(context);
                await updater.ProcessPendingUpdatesAsync(CreateSubscriptionUpdate(forBuild, isCodeFlow), applyNewestOnly, forceUpdate, buildDTO);
            });
    }

    protected void GivenAPendingUpdateReminder(Build forBuild, bool isCodeFlow = false)
    {
        SetExpectedReminder(Subscription, CreateSubscriptionUpdate(forBuild, isCodeFlow));
    }

    protected void AndPendingUpdates(Build forBuild, bool isCodeFlow = false)
    {
        AfterDbUpdateActions.Add(
            () =>
            {
                var update = CreateSubscriptionUpdate(forBuild, isCodeFlow);
                SetExpectedReminder(Subscription, update);
            });
    }
}
