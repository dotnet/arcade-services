// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// Class that clones repos by calling out to the native git executable.
/// Does not uses Libgit2Sharp which is quite memory hungry when dealing with large repos.
/// </summary>
public class GitNativeRepoCloner : IGitRepoCloner
{
    private readonly RemoteConfiguration _remoteConfiguration;
    private readonly IProcessManager _processManager;
    private readonly ILogger _logger;

    public GitNativeRepoCloner(RemoteConfiguration remoteConfiguration, IProcessManager processManager, ILogger logger)
    {
        _remoteConfiguration = remoteConfiguration;
        _processManager = processManager;
        _logger = logger;
    }

    public Task CloneAsync(string repoUri, string? commit, string targetDirectory, bool checkoutSubmodules, string? gitDirectory)
        => CloneAsync(
            repoUri,
            commit,
            targetDirectory,
            checkoutSubmodules ? CheckoutType.CheckoutWithSubmodules : CheckoutType.CheckoutWithoutSubmodules,
            gitDirectory);

    public Task CloneNoCheckoutAsync(string repoUri, string targetDirectory, string? gitDirectory)
        => CloneAsync(repoUri, null, targetDirectory, CheckoutType.NoCheckout, gitDirectory);

    private async Task CloneAsync(
        string repoUri,
        string? commit,
        string targetDirectory,
        CheckoutType checkoutType,
        string? gitDirectory)
    {
        _logger.LogInformation("Cloning {repoUri} to {targetDirectory}", repoUri, targetDirectory);

        var args = new List<string>();
        var envVars = new Dictionary<string, string>
        {
            { "GIT_TERMINAL_PROMPT", "0" }
        };

        string? token = _remoteConfiguration.GetTokenForUri(repoUri);

        if (!string.IsNullOrEmpty(token))
        {
            const string ENV_VAR_NAME = "GIT_REMOTE_PAT";
            args.Add($"--config-env=http.extraheader={ENV_VAR_NAME}");
            envVars[ENV_VAR_NAME] = GetAuthorizationHeaderArgument(token);
        }

        if (gitDirectory != null)
        {
            args.Add("--git-dir");
            args.Add(gitDirectory);
        }

        args.Add("clone");
        args.Add("-q");

        if (checkoutType == CheckoutType.NoCheckout || commit != null)
        {
            args.Add("--no-checkout");
        }
        else if (checkoutType == CheckoutType.CheckoutWithSubmodules)
        {
            args.Add("--recurse-submodules");
        }

        args.Add(repoUri);
        args.Add(targetDirectory);

        var result = await _processManager.ExecuteGit(Environment.CurrentDirectory, args, envVariables: envVars);
        result.ThrowIfFailed($"Failed to clone {repoUri} to {targetDirectory}");

        if (commit != null)
        {
            result = await _processManager.ExecuteGit(targetDirectory, "checkout", commit);
            result.ThrowIfFailed($"Failed to check out {commit} in {targetDirectory}");
        }
    }

    public static string GetAuthorizationHeaderArgument(string token)
    {
        var encodedToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Constants.GitHubBotUserName}:{token}"));
        return $"Authorization: Basic {encodedToken}";
    }
}
