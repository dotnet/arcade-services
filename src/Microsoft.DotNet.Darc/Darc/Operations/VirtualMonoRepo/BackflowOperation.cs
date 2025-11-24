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

internal class BackflowOperation(
    BackflowCommandLineOptions options,
    IVmrInfo vmrInfo,
    IVmrForwardFlower forwardFlower,
    IVmrBackFlower backFlower,
    IVmrCloneManager vmrCloneManager,
    IVmrDependencyTracker dependencyTracker,
    ILocalGitRepoFactory localGitRepoFactory,
    IDependencyFileManager dependencyFileManager,
    IBasicBarClient barApiClient,
    IProcessManager processManager,
    IFileSystem fileSystem,
    ILogger<BackflowOperation> logger)
    : CodeFlowOperation(options, forwardFlower, backFlower, vmrInfo, vmrCloneManager, dependencyTracker, dependencyFileManager, localGitRepoFactory, barApiClient, fileSystem, logger)
{
    private readonly BackflowCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IProcessManager _processManager = processManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(targetDirectory))
        {
            throw new DarcException("Please specify path to a local repository to flow to");
        }

        var vmrPath = new NativePath(_options.VmrPath ?? _processManager.FindGitRoot(Environment.CurrentDirectory));
        var targetRepoPath = new NativePath(_processManager.FindGitRoot(targetDirectory));

        _vmrInfo.VmrPath = vmrPath;

        var vmr = _localGitRepoFactory.Create(vmrPath);

        var build = await GetOrCreateBuildAsync(vmr, _options.Build);

        await FlowCodeLocallyAsync(
            targetRepoPath,
            isForwardFlow: false,
            build: build,
            subscription: null,
            cancellationToken: cancellationToken);
    }
}
