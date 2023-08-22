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
    private const string GitAuthUser = "dn-bot";

    private readonly IProcessManager _processManager;
    private readonly ILogger _logger;
    private readonly string? _token;

    public GitNativeRepoCloner(IProcessManager processManager, ILogger logger, string? token)
    {
        _processManager = processManager;
        _logger = logger;
        _token = token;
    }

    public Task CloneAsync(string repoUri, string? commit, string targetDirectory, bool checkoutSubmodules, string? gitDirectory)
        => CloneAsync(
            repoUri,
            commit,
            targetDirectory,
            checkoutSubmodules ? CheckoutType.CheckoutWithSubmodules : CheckoutType.CheckoutWithoutSubmodules,
            gitDirectory);

    public Task CloneAsync(string repoUri, string targetDirectory, string? gitDirectory)
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
        string[]? redactedValues = null;

        if (!string.IsNullOrEmpty(_token))
        {
            var encodedToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{GitAuthUser}:{_token}"));
            args.Add("-c");
            args.Add($"http.extraheader=Authorization: Basic {encodedToken}");
            redactedValues = new string[] { encodedToken };
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

        var result = await _processManager.Execute(_processManager.GitExecutable, args, redactedStrings: redactedValues);
        result.ThrowIfFailed($"Failed to clone {repoUri} to {targetDirectory}");

        if (commit != null)
        {
            result = await _processManager.ExecuteGit(targetDirectory, "checkout", commit);
            result.ThrowIfFailed($"Failed to check out {commit} in {targetDirectory}");
        }
    }
}
