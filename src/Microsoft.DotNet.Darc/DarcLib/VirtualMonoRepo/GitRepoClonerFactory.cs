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
    /// <param name="type">Cloner type</param>
    IGitRepoCloner GetCloner(string repoUri, GitClonerType type);
}

public enum GitClonerType
{
    Native,
    LibGit2Sharp,
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

    public IGitRepoCloner GetCloner(string repoUri, GitClonerType type)
    {
        var repoType = GitRepoTypeParser.ParseFromUri(repoUri);

        string? token = repoType switch
        {
            GitRepoType.GitHub => _vmrRemoteConfig.GitHubToken,
            GitRepoType.AzureDevOps => _vmrRemoteConfig.AzureDevOpsToken,
            GitRepoType.Local => null,
            _ => throw new NotImplementedException($"Unsupported repository remote {repoUri}"),
        };

        return type switch
        {
            GitClonerType.LibGit2Sharp => new GitRepoCloner(token, _logger),
            GitClonerType.Native => new GitNativeRepoCloner(_processManager, _logger, token),
            _ => throw new ArgumentException($"Unknown cloner type {type}"),
        };
    }
}
