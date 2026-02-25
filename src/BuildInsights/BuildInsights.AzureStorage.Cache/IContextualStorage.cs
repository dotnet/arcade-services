// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BuildInsights.AzureStorage.Cache;

public interface IContextualStorage
{
    Task PutAsync(string name, Stream data, CancellationToken cancellationToken);
    Task<Stream?> TryGetAsync(string name, CancellationToken cancellationToken);
    void SetContext(string path);
}
