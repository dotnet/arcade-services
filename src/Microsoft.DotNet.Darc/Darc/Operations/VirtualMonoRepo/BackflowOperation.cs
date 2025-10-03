// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class BackflowOperation(
    BackflowCommandLineOptions options,
    IVmrInfo vmrInfo,
    IVmrBackFlower backFlower,
    IBackflowConflictResolver backflowConflictResolver,
    IVmrDependencyTracker dependencyTracker,
    ILocalGitRepoFactory localGitRepoFactory,
    IDependencyFileManager dependencyFileManager,
    IBasicBarClient barApiClient,
    IProcessManager processManager,
    IFileSystem fileSystem,
    ILogger<BackflowOperation> logger)
    : CodeFlowOperation(options, vmrInfo, backFlower, dependencyTracker, dependencyFileManager, localGitRepoFactory, barApiClient, fileSystem, logger)
{
    private readonly BackflowCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IBackflowConflictResolver _backflowConflictResolver = backflowConflictResolver;
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
            targetRepoPath,
            isForwardFlow: false,
            additionalRemotes,
            cancellationToken);
    }

    protected override IEnumerable<string> GetIgnoredFiles(string mapping) => DependencyFileManager.CodeflowDependencyFiles;

    protected override async Task UpdateToolsetAndDependenciesAsync(
        SourceMapping mapping,
        LastFlows lastFlows,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        Build build,
        string branch,
        CancellationToken cancellationToken)
    {
        await _backflowConflictResolver.TryMergingBranchAndUpdateDependencies(
            mapping,
            lastFlows,
            (Backflow)currentFlow,
            targetRepo,
            build,
            branch,
            branch,
            [],
            headBranchExisted: true,
            rebase: true,
            cancellationToken);
    }
}
