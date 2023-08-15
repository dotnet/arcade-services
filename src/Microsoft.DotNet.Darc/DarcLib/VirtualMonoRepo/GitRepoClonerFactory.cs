// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using System;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IGitRepoClonerFactory
{
    /// <summary>
    /// Create a cloner for the given repo URL.
    /// </summary>
    /// <param name="repoUri">URI of the repo</param>
    /// <param name="libgit2sharpCloner"></param>
    /// <returns></returns>
    IGitRepoCloner GetCloner(string repoUri, bool libgit2sharpCloner = false);
}

public class GitRepoClonerFactory : IGitRepoClonerFactory
{
    private readonly VmrRemoteConfiguration _vmrRemoteConfig;
    private readonly IProcessManager _processManager;
    private readonly ILogger _logger;

    public GitRepoClonerFactory(VmrRemoteConfiguration vmrRemoteConfig, IProcessManager processManager, ILogger logger)
    {
        _vmrRemoteConfig = vmrRemoteConfig;
        _processManager = processManager;
        _logger = logger;
    }

    public IGitRepoCloner GetCloner(string repoUri, bool libgit2sharpCloner = false)
    {
        var repoType = GitRepoTypeParser.ParseFromUri(repoUri);

        string? token = repoType switch
        {
            GitRepoType.GitHub => _vmrRemoteConfig.GitHubToken,
            GitRepoType.AzureDevOps => _vmrRemoteConfig.AzureDevOpsToken,
            GitRepoType.Local => null,
            _ => throw new NotImplementedException($"Unsupported repository remote {repoUri}"),
        };

        return libgit2sharpCloner
            ? new GitRepoCloner(token, _logger)
            : new GitNativeRepoCloner(_processManager, _logger, token);
    }
}
