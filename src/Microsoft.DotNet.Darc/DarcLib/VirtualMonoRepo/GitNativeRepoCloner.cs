﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// Class that clones repos by calling out to the native git executable.
/// Does not uses Libgit2Sharp which is quite memory hungry when dealing with large repos.
/// </summary>
public class GitNativeRepoCloner : IGitRepoCloner
{
    private readonly IProcessManager _processManager;
    private readonly ILogger _logger;

    public GitNativeRepoCloner(IProcessManager processManager, ILogger logger)
    {
        _processManager = processManager;
        _logger = logger;
    }

    public Task Clone(string repoUri, string? commit, string targetDirectory, bool checkoutSubmodules, string? gitDirectory)
        => Clone(
            repoUri,
            commit,
            targetDirectory,
            checkoutSubmodules ? CheckoutType.CheckoutWithSubmodules : CheckoutType.CheckoutWithoutSubmodules,
            gitDirectory);

    public Task Clone(string repoUri, string targetDirectory, string? gitDirectory)
        => Clone(repoUri, null, targetDirectory, CheckoutType.NoCheckout, gitDirectory);

    private async Task Clone(
        string repoUri,
        string? commit,
        string targetDirectory,
        CheckoutType checkoutType,
        string? gitDirectory)
    {
        _logger.LogInformation("Cloning {repoUri} to {targetDirectory}", repoUri, targetDirectory);

        var args = new List<string>();

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

        args.Add("--");
        args.Add(repoUri);
        args.Add(targetDirectory);

        var result = await _processManager.Execute(_processManager.GitExecutable, args);
        result.ThrowIfFailed($"Failed to clone {repoUri} to {targetDirectory}");

        if (commit != null)
        {
            result = await _processManager.ExecuteGit(targetDirectory, "checkout", commit);
            result.ThrowIfFailed($"Failed to check out {commit} in {targetDirectory}");
        }
    }
}
