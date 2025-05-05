// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.Tests;

internal abstract class PendingUpdatePullRequestUpdaterTests : PullRequestUpdaterTests
{
    protected async Task WhenProcessPendingUpdatesAsyncIsCalled(
        Build forBuild,
        bool isCodeFlow = false,
        bool forceApply = true)
    {
        await Execute(
            async context =>
            {
                IPullRequestUpdater updater = CreatePullRequestActor(context);
                await updater.ProcessPendingUpdatesAsync(CreateSubscriptionUpdate(forBuild, isCodeFlow), forceApply, null);
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
