// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

public interface IDarcVmrForwardFlower
{
    /// <summary>
    /// Flows forward the code from a local clone of a repo to a local clone of the VMR.
    /// </summary>
    Task FlowForwardAsync(
        NativePath repoPath,
        string mappingName,
        CodeFlowParameters flowOptions);
}

internal class DarcVmrForwardFlower : VmrForwardFlower, IDarcVmrForwardFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrCodeFlower> _logger;

    public DarcVmrForwardFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            ICodeFlowVmrUpdater vmrUpdater,
            IVmrDependencyTracker dependencyTracker,
            IVmrCloneManager vmrCloneManager,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IVmrPatchHandler patchHandler,
            IProcessManager processManager,
            IFileSystem fileSystem,
            IBasicBarClient barClient,
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, vmrUpdater, dependencyTracker, vmrCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, processManager, fileSystem, barClient, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _localGitRepoFactory = localGitRepoFactory;
        _patchHandler = patchHandler;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task FlowForwardAsync(
        NativePath repoPath,
        string mappingName,
        CodeFlowParameters flowOptions)
    {
        var sourceRepo = _localGitRepoFactory.Create(repoPath);
        var sourceSha = await sourceRepo.GetShaForRefAsync();

        _logger.LogInformation(
            "Flowing current repo commit {repoSha} to VMR {targetDirectory}...",
            Commit.GetShortSha(sourceSha),
            _vmrInfo.VmrPath);

        await _dependencyTracker.RefreshMetadata();
        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        ISourceComponent repoVersion = _sourceManifest.GetRepoVersion(mapping.Name);

        var remotes = new[] { mapping.DefaultRemote, repoVersion.RemoteUri, repoPath }
            .Distinct()
            .OrderRemotesByLocalPublicOther()
            .ToList();

        if (await TryApplyChangesDirectly(flowOptions, sourceRepo, mapping, repoVersion))
        {
            // We were able to apply the delta directly to the VMR
            return;
        }

        // If there are conflicts, we need to perform a full code flow

    }

    private async Task<bool> TryApplyChangesDirectly(CodeFlowParameters flowOptions, ILocalGitRepo sourceRepo, SourceMapping mapping, ISourceComponent repoVersion)
    {
        // Exclude dependency file changes
        mapping = mapping with
        {
            Exclude = [.. mapping.Exclude, .. DependencyFileManager.DependencyFiles],
        };

        // First try to apply the changes directly to the VMR
        var patches = await _patchHandler.CreatePatches(
            mapping,
            sourceRepo,
            repoVersion.CommitSha,
            DarcLib.Constants.HEAD,
            _vmrInfo.TmpPath,
            _vmrInfo.TmpPath,
            CancellationToken.None);

        if (!patches.Any(patch => new FileInfo(patch.Path).Length != 0))
        {
            _logger.LogInformation("No changes to flow found.");
            return false;
        }

        try
        {
            var targetDir = _vmrInfo.GetRepoSourcesPath(mapping);
            foreach (var patch in patches)
            {
                await _patchHandler.ApplyPatch(patch, targetDir, flowOptions.DiscardPatches);
            }
        }
        catch (PatchApplicationFailedException)
        {
            _logger.LogWarning(
                "Failed to apply patches repo changes cleanly to VMR." +
                " A dedicated branch will be created.");
            return false;
        }
        finally
        {
            if (flowOptions.DiscardPatches)
            {
                foreach (var patch in patches)
                {
                    if (_fileSystem.FileExists(patch.Path))
                    {
                        _fileSystem.DeleteFile(patch.Path);
                    }
                }
            }
        }

        _logger.LogInformation("File changes staged at {vmrPath}", _vmrInfo.VmrPath);

        return true;
    }

    protected override bool ShouldResetVmr => false;
}
