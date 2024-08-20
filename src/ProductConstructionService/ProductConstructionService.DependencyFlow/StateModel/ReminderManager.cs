// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.DependencyFlow.StateModel;

internal interface IReminderManager
{
    Task TryRegisterReminderAsync(string reminderName, PullRequestActorId actorId, TimeSpan visibilityTimeout);

    Task TryUnregisterReminderAsync(string reminderName);
}
