// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SubscriptionActorService;

public enum SynchronizePullRequestResult
{
    Invalid = 0,
    UnknownPR = 1,
    Completed = 2,
    InProgressCanUpdate = 3,
    InProgressCannotUpdate = 4,
}
