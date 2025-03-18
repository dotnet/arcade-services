// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ForwardFlowOperation(
    ForwardFlowCommandLineOptions options,
    ILocalGitRepoFactory localGitRepoFactory,
    IVmrInfo vmrInfo,
    IFileSystem fileSystem,
    IProcessManager processManager,
    ILogger<ForwardFlowOperation> logger)
    : CodeFlowOperation(options, logger)
{
    private readonly ForwardFlowCommandLineOptions _options = options;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessManager _processManager = processManager;
    private readonly ILogger<ForwardFlowOperation> _logger = logger;

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        var repoPath = new NativePath(_processManager.FindGitRoot(Environment.CurrentDirectory));
        var repo = _localGitRepoFactory.Create(repoPath);
        var repoSha = await repo.GetShaForRefAsync();

        if (string.IsNullOrEmpty(_options.VmrPath) || _options.VmrPath == repoPath)
        {
            throw new DarcException("Please specify a path to a local clone of the VMR to flow the changed into.");
        }

        if (!_fileSystem.FileExists(_vmrInfo.SourceManifestPath))
        {
            throw new DarcException(
                $"Failed to find {_vmrInfo.SourceManifestPath}! " +
                "Please specify a path to a local clone of the VMR to flow the changed into.");
        }

        _logger.LogInformation(
            "Flowing current repo commit {repoSha} to VMR {targetDirectory}...",
            Commit.GetShortSha(repoSha),
            _options.VmrPath);
    }
}
