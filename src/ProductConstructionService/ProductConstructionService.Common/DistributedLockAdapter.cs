// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders.ConfigurationIngestion;

namespace ProductConstructionService.Common;

/// <summary>
/// Adapter that bridges the Maestro.DataProviders IDistributedLockProvider interface
/// with the ProductConstructionService.Common IDistributedLock implementation.
/// </summary>
public class DistributedLockAdapter(IDistributedLock distributedLock) : IDistributedLockProvider
{
    private readonly IDistributedLock _distributedLock = distributedLock;

    public Task<T> ExecuteWithLockAsync<T>(string key, Func<Task<T>> action)
    {
        return _distributedLock.ExecuteWithLockAsync(key, action);
    }
}
