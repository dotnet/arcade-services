// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.DependencyFlow.Model;

public enum PullRequestStatus
{
    Invalid = 0,
    Completed = 2,
    InProgressCanUpdate = 3,
    InProgressCannotUpdate = 4,
}
