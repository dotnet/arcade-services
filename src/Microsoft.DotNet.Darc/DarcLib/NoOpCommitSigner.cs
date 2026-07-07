// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib;

public sealed class NoOpCommitSigner : ICommitSigner
{
    public Task<CommitSigningConfiguration> GetConfigurationAsync(string repoPath, CancellationToken cancellationToken = default)
        => Task.FromResult(new CommitSigningConfiguration());
}
