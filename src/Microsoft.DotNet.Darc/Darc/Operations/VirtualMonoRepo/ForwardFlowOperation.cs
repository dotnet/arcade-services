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
        IVmrForwardFlower forwardFlower,
        IVmrBackFlower backFlower,
        IVmrInfo vmrInfo,
        IVmrCloneManager vmrCloneManager,
        IRepositoryCloneManager cloneManager,
        IVmrDependencyTracker dependencyTracker,
        IDependencyFileManager dependencyFileManager,
        ILocalGitRepoFactory localGitRepoFactory,
        IBasicBarClient barApiClient,
        IFileSystem fileSystem,
        IProcessManager processManager,
        ILogger<ForwardFlowOperation> logger)
    : CodeFlowOperation(options, forwardFlower, backFlower, vmrInfo, vmrCloneManager, dependencyTracker, dependencyFileManager, localGitRepoFactory, barApiClient, fileSystem, logger)
{
    private readonly ForwardFlowCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IRepositoryCloneManager _cloneManager = cloneManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IProcessManager _processManager = processManager;

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? sourceDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        var sourceRepoPath = new NativePath(_processManager.FindGitRoot(Environment.CurrentDirectory));

        if (string.IsNullOrEmpty(_options.VmrPath) || _options.VmrPath == sourceRepoPath)
        {
            throw new DarcException("Please specify a path to a local clone of the VMR to flow the changed into.");
        }

        var sourceRepo = _localGitRepoFactory.Create(sourceRepoPath);

        var build = await GetOrCreateBuildAsync(sourceRepo, _options.Build);
        _vmrInfo.VmrPath = new NativePath(_options.VmrPath);

        await _cloneManager.RegisterCloneAsync(sourceRepo.Path);

        await FlowCodeLocallyAsync(
            sourceRepoPath,
            isForwardFlow: true,
            build: build,
            subscription: null,
            cancellationToken: cancellationToken);
    }
}
