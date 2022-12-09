// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Helpers;

public class GitFileManagerFactory : IGitFileManagerFactory
{
    private readonly IVmrInfo _vmrInfo;
    private readonly VmrRemoteConfiguration _remoteConfiguration;
    private readonly IProcessManager _processManager;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly ILogger<GitFileManagerFactory> _logger;

    public GitFileManagerFactory(
        IVmrInfo vmrInfo,
        VmrRemoteConfiguration remoteConfiguration,
        IProcessManager processManager,
        IVersionDetailsParser versionDetailsParser,
        ILogger<GitFileManagerFactory> logger)
    {
        _vmrInfo = vmrInfo;
        _remoteConfiguration = remoteConfiguration;
        _processManager = processManager;
        _versionDetailsParser = versionDetailsParser;
        _logger = logger;
    }

    public IGitFileManager Create(string repoUri)
        => new GitFileManager(CreateGitRepo(repoUri), _versionDetailsParser, _logger);

    private IGitRepo CreateGitRepo(string repoUri) => GitRepoTypeParser.ParseFromUri(repoUri) switch
    {
        GitRepoType.AzureDevOps => new AzureDevOpsClient(
            _processManager.GitExecutable,
            _remoteConfiguration.AzureDevOpsToken,
            _logger,
            _vmrInfo.TmpPath),

        GitRepoType.GitHub => new GitHubClient(
            _processManager.GitExecutable,
            _remoteConfiguration.GitHubToken,
            _logger,
            _vmrInfo.TmpPath,
            // Caching not in use for Darc local client.
            null),

        GitRepoType.Local => new LocalGitClient(_processManager.GitExecutable, _logger),
        _ => throw new ArgumentException("Unknown git repository type", nameof(repoUri)),
    };
}
