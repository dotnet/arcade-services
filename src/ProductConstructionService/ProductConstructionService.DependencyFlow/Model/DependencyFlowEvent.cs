// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.DependencyFlow.Model;

public enum DependencyFlowEventType
{
    Fired = 0,
    Created = 1,
    Updated = 2,
    Completed = 3,
}

public enum DependencyFlowEventReason
{
    New = 0,
    AutomaticallyMerged = 1,
    ManuallyMerged = 2,
    ManuallyClosed = 3,
    FailedUpdate = 4,
    NothingToDo = 5,
}
