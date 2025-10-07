// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
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

internal class ForwardFlowOperation(
        ForwardFlowCommandLineOptions options,
        IVmrForwardFlower codeFlower,
        IVmrInfo vmrInfo,
        IVmrDependencyTracker dependencyTracker,
        IDependencyFileManager dependencyFileManager,
        ILocalGitRepoFactory localGitRepoFactory,
        IBasicBarClient barApiClient,
        IFileSystem fileSystem,
        IProcessManager processManager,
        ILogger<ForwardFlowOperation> logger)
    : CodeFlowOperation(options, vmrInfo, codeFlower, dependencyTracker, dependencyFileManager, localGitRepoFactory, barApiClient, fileSystem, logger)
{
    private readonly ForwardFlowCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker = dependencyTracker;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IFileSystem _fileSystem = fileSystem;
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

        await FlowCodeLocallyAsync(
            sourceRepoPath,
            isForwardFlow: true,
            additionalRemotes,
            cancellationToken);
    }

    protected override IEnumerable<string> GetIgnoredFiles(string mapping) =>
    [
        VmrInfo.DefaultRelativeSourceManifestPath,
        .. DependencyFileManager.CodeflowDependencyFiles
            .Select(f => VmrInfo.GetRelativeRepoSourcesPath(mapping) / f)
    ];

    protected override async Task UpdateToolsetAndDependenciesAsync(
        SourceMapping mapping,
        LastFlows lastFlows,
        Codeflow currentFlow,
        ILocalGitRepo sourceRepo,
        ILocalGitRepo targetRepo,
        Build build,
        string branch,
        CancellationToken cancellationToken)
    {
        // Update source manifest
        var sourceManifest = SourceManifest.FromFile(_vmrInfo.SourceManifestPath);
        var version = sourceManifest.GetRepoVersion(mapping.Name) as IVersionedSourceComponent
            ?? throw new DarcException($"Failed to find repo version for {mapping.Name} in source manifest at {_vmrInfo.SourceManifestPath}");

        _dependencyTracker.UpdateDependencyVersion(new(
            mapping,
            version.RemoteUri,
            currentFlow.SourceSha,
            Parent: null,
            OfficialBuildId: null,
            BarId: version.BarId));

        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);

        var targetPath = _vmrInfo.GetRepoSourcesPath(mapping) / DarcLib.Constants.CommonScriptFilesPath;

        // Copy eng/common
        try
        {
            _fileSystem.DeleteDirectory(targetPath, recursive: true);
        }
        catch { }

        // TODO: Handle file permissions (if devs run this on Windows)
        _fileSystem.CopyDirectory(
             sourceRepo.Path / DarcLib.Constants.CommonScriptFilesPath,
            targetPath,
            true);

        await vmr.StageAsync([targetPath, _vmrInfo.SourceManifestPath], cancellationToken);
    }
}
