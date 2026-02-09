// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public enum BuildResult
{
    None,
    Succeeded,
    Failed,
    Canceled,
    PartiallySucceeded,
}
