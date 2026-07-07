// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public interface ICommitSigner
{
    Task<CommitSigningConfiguration> GetConfigurationAsync(string repoPath, CancellationToken cancellationToken = default);
}

public sealed class CommitSigningConfiguration
{
    public bool Enabled { get; init; }

    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
}
