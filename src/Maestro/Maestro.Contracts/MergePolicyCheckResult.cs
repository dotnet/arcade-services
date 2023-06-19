// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.Contracts;

public enum MergePolicyCheckResult
{
    NoPolicies = 0,
    PendingPolicies = 1,
    FailedPolicies = 2,
    FailedToMerge = 3,
    Merged = 4,
}
