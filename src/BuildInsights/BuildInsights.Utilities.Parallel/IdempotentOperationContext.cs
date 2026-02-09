// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.Utilities.Parallel;

public class IdempotentOperationContext
{
    public void RejectCache()
    {
        CacheResult = false;
    }

    public bool CacheResult { get; private set; } = true;
}
