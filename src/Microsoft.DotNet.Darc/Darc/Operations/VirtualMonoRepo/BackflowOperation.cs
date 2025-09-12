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
    IVmrBackFlower backFlower,
    IVmrDependencyTracker dependencyTracker,
    IVmrPatchHandler patchHandler,
    ILocalGitRepoFactory localGitRepoFactory,
    IDependencyFileManager dependencyFileManager,
    IProcessManager processManager,
    IFileSystem fileSystem,
    IBarApiClient barClient,
    ILogger<BackflowOperation> logger)
    : CodeFlowOperation(options, vmrInfo, backFlower, dependencyTracker, patchHandler, dependencyFileManager, localGitRepoFactory, fileSystem, barClient, logger)
{
    private readonly BackflowCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IVmrBackFlower _backFlower = backFlower;
    private readonly IProcessManager _processManager = processManager;

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

        _vmrInfo.VmrPath = new NativePath(_options.VmrPath ?? _processManager.FindGitRoot(Environment.CurrentDirectory));
        var targetRepoPath = new NativePath(_processManager.FindGitRoot(targetDirectory));

        await FlowCodeLocallyAsync(
            async (mapping, repoPath, build, excludedAssets, targetBranch, headBranch)
                => await _backFlower.FlowBackAsync(mapping.Name, repoPath, build, excludedAssets, targetBranch, headBranch, false, cancellationToken),
            targetRepoPath,
            isForwardFlow: false,
            additionalRemotes,
            cancellationToken);
    }

    protected override IEnumerable<string> GetIgnoredFiles(string mapping) => DependencyFileManager.CodeflowDependencyFiles;
}
