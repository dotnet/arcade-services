// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.Core;

public enum BuildStatus
{
    Unknown,
    Succeeded,
    Failed,
    Cancelled,
    PartiallySucceeded,
}
