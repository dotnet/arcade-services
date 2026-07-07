// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib;

public interface ICommitSignatureVerifier
{
    Task<bool> VerifyAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default);
}
