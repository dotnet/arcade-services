// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Helpers;

/// <summary>
/// Token provider that attempts to retrieve a GitHub token from the GitHub CLI ('gh auth token')
/// if no static token is provided. Falls back to the provided token or null if gh CLI is not available.
/// </summary>
internal class GitHubCliTokenProvider : IRemoteTokenProvider
{
    private readonly string? _staticToken;
    private readonly IProcessManager _processManager;
    private readonly ILogger _logger;
    private string? _cachedToken;
    private bool _hasAttemptedGhCli;
#if NET10_0_OR_GREATER
    private readonly System.Threading.Lock _lock = new();
#else
    private readonly object _lock = new();
#endif

    public GitHubCliTokenProvider(IProcessManager processManager, ILogger<GitHubCliTokenProvider> logger, string? staticToken)
    {
        _staticToken = staticToken;
        _processManager = processManager;
        _logger = logger;
        _hasAttemptedGhCli = false;
    }

    public string? GetTokenForRepository(string repoUri)
    {
        lock (_lock)
        {
            // If we have a static token, use it
            if (!string.IsNullOrEmpty(_staticToken))
            {
                return _staticToken;
            }

            // If we've already tried and cached the token, return it
            if (_hasAttemptedGhCli)
            {
                return _cachedToken;
            }

            // Try to get token from GitHub CLI
            _cachedToken = TryGetGitHubTokenFromCliAsync().GetAwaiter().GetResult();
            _hasAttemptedGhCli = true;
            return _cachedToken;
        }
    }

    public Task<string?> GetTokenForRepositoryAsync(string repoUri)
    {
        return Task.FromResult(GetTokenForRepository(repoUri));
    }

    private async Task<string?> TryGetGitHubTokenFromCliAsync()
    {
        try
        {
            var result = await _processManager.Execute("gh", [ "auth", "token" ], timeout: System.TimeSpan.FromSeconds(15));

            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                var token = result.StandardOutput.Trim();
                _logger.LogDebug("Successfully retrieved GitHub token from 'gh auth token'");
                return token;
            }

            _logger.LogDebug("GitHub CLI did not return a valid token. Exit code: {exitCode}", result.ExitCode);
            return null;
        }
        catch (System.Exception ex)
        {
            _logger.LogDebug(ex, "Failed to retrieve GitHub token from 'gh' CLI. This is expected if 'gh' is not installed or not authenticated.");
            return null;
        }
    }
}
