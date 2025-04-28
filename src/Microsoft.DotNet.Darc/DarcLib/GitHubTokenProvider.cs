﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Maestro.Common;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public class GitHubTokenProvider(GitHub.Authentication.IGitHubTokenProvider tokenProvider) : IRemoteTokenProvider
{
    public string? GetTokenForRepository(string repoUri)
    {
        try
        {
            return tokenProvider.GetTokenForRepository(repoUri).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> GetTokenForRepositoryAsync(string repoUri)
    {
        try
        {
            return await tokenProvider.GetTokenForRepository(repoUri);
        }
        catch
        {
            return null;
        }
    }
}
