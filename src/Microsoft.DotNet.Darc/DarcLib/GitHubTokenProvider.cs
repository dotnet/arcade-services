﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.GitHub.Authentication;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public class GitHubTokenProvider : IRemoteTokenProvider
{
    private readonly IGitHubTokenProvider _tokenProvider;

    public GitHubTokenProvider(IGitHubTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    public string? GetTokenForRepository(string repoUri)
    {
        return _tokenProvider.GetTokenForRepository(repoUri).GetAwaiter().GetResult();
    }

    public async Task<string?> GetTokenForRepositoryAsync(string repoUri)
    {
        return await _tokenProvider.GetTokenForRepository(repoUri);
    }
}
