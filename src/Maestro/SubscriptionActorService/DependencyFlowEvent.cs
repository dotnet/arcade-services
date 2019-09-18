// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SubscriptionActorService
{
    public enum DependencyFlowEventType
    {
        Created = 0,
        Updated = 1,
        Completed = 2,
    }

    public enum DependencyFlowEventReason
    {
        New = 0,
        AutomaticallyMerged = 1,
        ManuallyMerged = 2,
        ManuallyClosed = 3,
        FailedUpdate = 4,
    }
}
