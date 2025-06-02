// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Maestro.DataProviders;
using ProductConstructionService.DependencyFlow.WorkItems;
using BuildDTO = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;

namespace ProductConstructionService.DependencyFlow.Tests;

internal abstract class PendingUpdatePullRequestUpdaterTests : PullRequestUpdaterTests
{
    protected async Task WhenProcessPendingUpdatesAsyncIsCalled(
        Build forBuild,
        bool isCodeFlow = false,
        bool applyNewestOnly = false,
        bool forceUpdate = false)
    {
        await Execute(
            async context =>
            {
                BuildDTO buildDTO = SqlBarClient.ToClientModelBuild(forBuild);
                IPullRequestUpdater updater = CreatePullRequestActor(context);
                await updater.ProcessPendingUpdatesAsync(CreateSubscriptionUpdate(forBuild, isCodeFlow), applyNewestOnly, forceUpdate, buildDTO);
            });
    }

    protected void GivenAPendingUpdateReminder(Build forBuild, bool isCodeFlow = false)
    {
        SetExpectedReminder(Subscription, CreateSubscriptionUpdate(forBuild, isCodeFlow));
    }

    protected void AndNoPendingUpdates()
    {
        RemoveExpectedState<SubscriptionUpdateWorkItem>(Subscription);
        RemoveState<SubscriptionUpdateWorkItem>(Subscription);
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
