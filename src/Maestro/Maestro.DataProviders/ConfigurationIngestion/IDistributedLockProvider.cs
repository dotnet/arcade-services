// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;

namespace Maestro.DataProviders.ConfigurationIngestion;

/// <summary>
/// Provides distributed locking functionality for configuration ingestion operations.
/// </summary>
public interface IDistributedLockProvider
{
    /// <summary>
    /// Executes the specified action within a distributed lock, and return the result.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the action.</typeparam>
    /// <param name="key">The unique identifier for the lock.</param>
    /// <param name="action">The asynchronous delegate to execute while the lock is held. Cannot be null.</param>
    /// <returns>A task that represents the asynchronous operation. The task completes when the action has finished executing
    /// under the lock.</returns>
    Task<T> ExecuteWithLockAsync<T>(string key, Func<Task<T>> action);
}
