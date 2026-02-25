// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Microsoft.DotNet.GitHub.Authentication;

namespace BuildInsights.Api.GitHub;

public class GitHubTokenProvider(IGitHubTokenProvider tokenProvider) : IRemoteTokenProvider
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
