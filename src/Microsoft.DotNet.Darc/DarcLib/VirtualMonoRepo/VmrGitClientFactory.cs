// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IGitRepoFactory
{
    IGitRepo CreateClient(string repoUri);
}

public class VmrGitClientFactory : IGitRepoFactory
{
    private readonly IVmrInfo _vmrInfo;
    private readonly RemoteConfiguration _remoteConfiguration;
    private readonly IProcessManager _processManager;
    private readonly ILoggerFactory _loggerFactory;

    public VmrGitClientFactory(
        IVmrInfo vmrInfo,
        RemoteConfiguration remoteConfiguration,
        IProcessManager processManager,
        ILoggerFactory loggerFactory)
    {
        _vmrInfo = vmrInfo;
        _remoteConfiguration = remoteConfiguration;
        _processManager = processManager;
        _loggerFactory = loggerFactory;
    }

    public IGitRepo CreateClient(string repoUri) => GitRepoUrlParser.ParseTypeFromUri(repoUri) switch
    {
        GitRepoType.AzureDevOps => new AzureDevOpsClient(
            _processManager.GitExecutable,
            _remoteConfiguration.AzureDevOpsToken,
            _loggerFactory.CreateLogger<AzureDevOpsClient>(),
            _vmrInfo.TmpPath),

        GitRepoType.GitHub => new GitHubClient(
            _processManager.GitExecutable,
            _remoteConfiguration.GitHubToken,
            _loggerFactory.CreateLogger<GitHubClient>(),
            _vmrInfo.TmpPath,
            // Caching not in use for Darc local client.
            null),

        GitRepoType.Local => new LocalLibGit2Client(_remoteConfiguration, _processManager, _loggerFactory.CreateLogger<LocalGitClient>()),

        _ => throw new ArgumentException("Unknown git repository type", nameof(repoUri)),
    };
}
