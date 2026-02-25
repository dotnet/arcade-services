// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BuildInsights.AzureStorage.Cache;

internal abstract class BaseContextualStorage : IContextualStorage
{
    protected string PathContext { get; private set; } = null!;

    public void SetContext(string path)
    {
        if (IsInitialized())
        {
            throw new InvalidOperationException("Initialize cannot be called more than once");
        }

        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("Value cannot be null or empty.", nameof(path));
        }

        PathContext = path;
    }

    public Task PutAsync(string name, Stream data, CancellationToken cancellationToken)
    {
        CheckInitialized();

        return PutAsync(PathContext, name, data, cancellationToken);
    }

    protected abstract Task PutAsync(string root, string name, Stream data, CancellationToken cancellationToken);

    public Task<Stream?> TryGetAsync(string name, CancellationToken cancellationToken)
    {
        CheckInitialized();
        return TryGetAsync(PathContext, name, cancellationToken);
    }

    protected abstract  Task<Stream?> TryGetAsync(string root, string name, CancellationToken cancellationToken);

    private void CheckInitialized()
    {
        if (!IsInitialized())
        {
            throw new InvalidOperationException("Initialize must be called before getting/putting data");
        }
    }

    private bool IsInitialized()
    {
        return !string.IsNullOrEmpty(PathContext);
    }
}
