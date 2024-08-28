// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.Tests;

internal abstract class PendingUpdatePullRequestActorTests : PullRequestActorTests
{
    protected async Task WhenProcessPendingUpdatesAsyncIsCalled(Build forBuild, bool isCodeFlow = false)
    {
        await Execute(
            async context =>
            {
                IPullRequestActor actor = CreatePullRequestActor(context);
                await actor.ProcessPendingUpdatesAsync(CreateSubscriptionUpdate(forBuild, isCodeFlow));
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
