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

internal class ForwardFlowOperation(
        ForwardFlowCommandLineOptions options,
        IVmrForwardFlower forwardFlower,
        IVmrInfo vmrInfo,
        IVmrCloneManager vmrCloneManager,
        IVmrDependencyTracker dependencyTracker,
        IDependencyFileManager dependencyFileManager,
        ILocalGitRepoFactory localGitRepoFactory,
        IBasicBarClient barApiClient,
        IFileSystem fileSystem,
        IProcessManager processManager,
        ILogger<ForwardFlowOperation> logger)
    : CodeFlowOperation(options, vmrInfo, vmrCloneManager, dependencyTracker, dependencyFileManager, localGitRepoFactory, barApiClient, fileSystem, logger)
{
    private readonly ForwardFlowCommandLineOptions _options = options;
    private readonly IVmrForwardFlower _forwardFlower = forwardFlower;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
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

    protected override async Task<bool> FlowCodeAsync(
        ILocalGitRepo productRepo,
        Build build,
        Codeflow currentFlow,
        SourceMapping mapping,
        string headBranch,
        CancellationToken cancellationToken)
    {
        try
        {
            CodeFlowResult result = await _forwardFlower.FlowForwardAsync(
                mapping.Name,
                productRepo.Path,
                build,
                excludedAssets: [], // TODO (https://github.com/dotnet/arcade-services/issues/5313): Fill from subscription
                headBranch,
                headBranch,
                _vmrInfo.VmrPath,
                enableRebase: true,
                forceUpdate: true,
                cancellationToken);

            return result.HadUpdates;
        }
        finally
        {
            // Update target branch's source manifest with the new commit
            var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
            var sourceManifestContent = await vmr.GetFileFromGitAsync(VmrInfo.DefaultRelativeSourceManifestPath, headBranch);
            var sourceManifest = SourceManifest.FromJson(sourceManifestContent!);
            sourceManifest.UpdateVersion(mapping.Name, build.GetRepository(), build.Commit, build.Id);
            _fileSystem.WriteToFile(_vmrInfo.SourceManifestPath, sourceManifest.ToJson());
            await vmr.StageAsync([_vmrInfo.SourceManifestPath], cancellationToken);
        }
    }
}
