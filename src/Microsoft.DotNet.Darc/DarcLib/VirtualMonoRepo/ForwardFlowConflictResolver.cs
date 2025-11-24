// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IForwardFlowConflictResolver
{
    /// <summary>
    /// Tries to resolve well-known conflicts that can occur during a code flow operation.
    /// The conflicts can happen when backward a forward flow PRs get merged out of order.
    /// This can be shown on the following schema (the order of events is numbered):
    /// 
    ///     repo                   VMR
    ///       O────────────────────►O
    ///       │  2.                 │ 1.
    ///       │   O◄────────────────O- - ┐
    ///       │   │            4.   │
    ///     3.O───┼────────────►O   │    │
    ///       │   │             │   │
    ///       │ ┌─┘             │   │    │
    ///       │ │               │   │
    ///     5.O◄┘               └──►O 6. │
    ///       │                 7.  │    O (actual branch for 7. is based on top of 1.)
    ///       |────────────────►O   │
    ///       │                 └──►O 8.
    ///       │                     │
    ///
    /// The conflict arises in step 8. and is caused by the fact that:
    ///   - When the forward flow PR branch is being opened in 7., the last sync (from the point of view of 5.) is from 1.
    ///   - This means that the PR branch will be based on 1. (the real PR branch is the "actual 7.")
    ///   - This means that when 6. merged, VMR's source-manifest.json got updated with the SHA of the 3.
    ///   - So the source-manifest in 6. contains the SHA of 3.
    ///   - The forward flow PR branch contains the SHA of 5.
    ///   - So the source-manifest file conflicts on the SHA (3. vs 5.)
    ///   - However, if only the version files are in conflict, we can try merging 6. into 7. and resolve the conflict.
    ///   - This is because basically we know we want to set the version files to point at 5.
    /// </summary>
    /// <returns>Conflicted files (if any)</returns>
    Task<IReadOnlyCollection<UnixPath>> TryMergingBranch(
        string mappingName,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        string headBranch,
        string branchToMerge,
        ForwardFlow currentFlow,
        LastFlows lastFlows,
        bool enableRebase,
        CancellationToken cancellationToken);

    Task MergeDependenciesAsync(
        string mappingName,
        ILocalGitRepo sourceRepo,
        string targetBranch,
        string repoComparisonSha,
        string vmrComparisonSha,
        ForwardFlow currentFlow,
        CancellationToken cancellationToken);
}

public class ForwardFlowConflictResolver : CodeFlowConflictResolver, IForwardFlowConflictResolver
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ForwardFlowConflictResolver> _logger;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly IJsonFileMerger _jsonFileMerger;
    private readonly IVersionDetailsFileMerger _versionDetailsFileMerger;
    private readonly IVersionDetailsParser _versionDetailsParser;

    public ForwardFlowConflictResolver(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrPatchHandler patchHandler,
        ILocalGitRepoFactory localGitRepoFactory,
        IDependencyFileManager dependencyFileManager,
        IJsonFileMerger jsonFileMerger,
        IVersionDetailsFileMerger versionDetailsFileMerger,
        IVersionDetailsParser versionDetailsParser,
        IFileSystem fileSystem,
        ILogger<ForwardFlowConflictResolver> logger)
        : base(vmrInfo, patchHandler, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _fileSystem = fileSystem;
        _logger = logger;
        _localGitRepoFactory = localGitRepoFactory;
        _dependencyFileManager = dependencyFileManager;
        _jsonFileMerger = jsonFileMerger;
        _versionDetailsFileMerger = versionDetailsFileMerger;
        _versionDetailsParser = versionDetailsParser;
    }

    public async Task<IReadOnlyCollection<UnixPath>> TryMergingBranch(
        string mappingName,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        string headBranch,
        string branchToMerge,
        ForwardFlow currentFlow,
        LastFlows lastFlows,
        bool enableRebase,
        CancellationToken cancellationToken)
    {
        var conflictedFiles = enableRebase
            ? []
            : await TryMergingBranch(vmr, headBranch, branchToMerge, cancellationToken);

        if (conflictedFiles.Count != 0 && await TryResolvingConflicts(
            mappingName,
            vmr,
            sourceRepo,
            conflictedFiles,
            currentFlow,
            lastFlows.CrossingFlow,
            cancellationToken))
        {
            _logger.LogInformation("Successfully resolved file conflicts between branches {headBranch} and {headBranch}",
                branchToMerge,
                headBranch);
            try
            {
                conflictedFiles = [];
                await vmr.CommitAsync(
                    $"Merge branch {branchToMerge} into {headBranch}",
                    allowEmpty: true,
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception e) when (e.Message.Contains("Your branch is ahead of"))
            {
                // There was no reason to merge, we're fast-forward ahead from the target branch
            }
        }     

        try
        {
            await MergeDependenciesAsync(
                mappingName,
                sourceRepo,
                headBranch,
                lastFlows.LastForwardFlow.RepoSha,
                // if there's a crossing flow, we need to make sure it doesn't bring in any downgrades https://github.com/dotnet/arcade-services/issues/5331
                lastFlows.CrossingFlow != null
                    ? lastFlows.LastBackFlow!.VmrSha
                    : lastFlows.LastForwardFlow.VmrSha,
                currentFlow,
                cancellationToken);

            if (!enableRebase)
            {
                await vmr.CommitAsync(
                    $"Update dependencies after merging {branchToMerge} into {headBranch}",
                    allowEmpty: true,
                    cancellationToken: CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            // We don't want to push this as there is some problem
            _logger.LogError(e, "Failed to update dependencies after merging {branchToMerge} into {headBranch} in {repoPath}",
                branchToMerge,
                headBranch,
                vmr.Path);
            throw;
        }

        return conflictedFiles;
    }

    private async Task<bool> TryResolvingConflicts(
        string mappingName,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        IReadOnlyCollection<UnixPath> conflictedFiles,
        ForwardFlow currentFlow,
        Codeflow? crossingFlow,
        CancellationToken cancellationToken)
    {
        foreach (var filePath in conflictedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await TryResolvingConflict(
                    mappingName,
                    vmr,
                    sourceRepo,
                    filePath,
                    currentFlow,
                    crossingFlow,
                    cancellationToken))
                {
                    continue;
                }
                else
                {
                    _logger.LogInformation("Conflict in {filePath} cannot be resolved automatically", filePath);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to resolve conflicts in {filePath}", filePath);
            }

            await AbortMerge(vmr);
            return false;
        }

        return true;
    }

    private async Task<bool> TryResolvingConflict(
        string mappingName,
        ILocalGitRepo vmr,
        ILocalGitRepo sourceRepo,
        UnixPath conflictedFile,
        ForwardFlow currentFlow,
        Codeflow? crossingFlow,
        CancellationToken cancellationToken)
    {
        // Known conflict in source-manifest.json
        if (string.Equals(conflictedFile, VmrInfo.DefaultRelativeSourceManifestPath, StringComparison.OrdinalIgnoreCase))
        {
            await TryResolvingSourceManifestConflict(vmr, mappingName!, cancellationToken);
            return true;
        }

        // Unknown conflict, but can be conflicting with a crossing flow
        // Check DetectCrossingFlow documentation for more details
        if (crossingFlow != null)
        {
            return await TryResolvingConflictWithCrossingFlow(
                mappingName,
                vmr,
                sourceRepo,
                conflictedFile,
                currentFlow,
                crossingFlow,
                cancellationToken);
        }

        return false;
    }

    private async Task TryResolvingSourceManifestConflict(ILocalGitRepo vmr, string mappingName, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Auto-resolving conflict in {file}", VmrInfo.DefaultRelativeSourceManifestPath);

        // We load the source manifest from the target branch and replace the
        // current mapping (and its submodules) with our branches' information
        var result = await vmr.RunGitCommandAsync(
            ["show", "MERGE_HEAD:" + VmrInfo.DefaultRelativeSourceManifestPath],
            cancellationToken);

        var theirSourceManifest = SourceManifest.FromJson(result.StandardOutput);
        var ourSourceManifest = _sourceManifest;
        var updatedMapping = ourSourceManifest.Repositories.First(r => r.Path == mappingName);

        theirSourceManifest.UpdateVersion(
            mappingName,
            updatedMapping.RemoteUri,
            updatedMapping.CommitSha,
            updatedMapping.BarId);

        var theirAffectedSubmodules = theirSourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mappingName + "/"))
            .ToList();
        foreach (var submodule in theirAffectedSubmodules)
        {
            theirSourceManifest.RemoveSubmodule(submodule);
        }

        var ourAffectedSubmodules = ourSourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mappingName + "/"))
            .ToList();
        foreach (var submodule in ourAffectedSubmodules)
        {
            theirSourceManifest.UpdateSubmodule(submodule);
        }

        _fileSystem.WriteToFile(_vmrInfo.SourceManifestPath, theirSourceManifest.ToJson());
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);
        await vmr.StageAsync([_vmrInfo.SourceManifestPath], cancellationToken);
    }

    public async Task MergeDependenciesAsync(
        string mappingName,
        ILocalGitRepo sourceRepo,
        string targetBranch,
        string repoComparisonSha,
        string vmrComparisonSha,
        ForwardFlow currentFlow,
        CancellationToken cancellationToken)
    {
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        var relativeSourceMappingPath = VmrInfo.GetRelativeRepoSourcesPath(mappingName);

        await _jsonFileMerger.MergeJsonsAsync(
            vmr,
            relativeSourceMappingPath / VersionFiles.GlobalJson,
            vmrComparisonSha,
            targetBranch,
            sourceRepo,
            VersionFiles.GlobalJson,
            repoComparisonSha,
            currentFlow.RepoSha);

        // and handle dotnet-tools.json if it exists
        bool dotnetToolsConfigExists =
            (await sourceRepo.GetFileFromGitAsync(VersionFiles.DotnetToolsConfigJson, repoComparisonSha) != null) ||
            (await sourceRepo.GetFileFromGitAsync(VersionFiles.DotnetToolsConfigJson, targetBranch) != null) ||
            (await vmr.GetFileFromGitAsync(relativeSourceMappingPath / VersionFiles.DotnetToolsConfigJson, currentFlow.VmrSha) != null ||
            (await vmr.GetFileFromGitAsync(relativeSourceMappingPath / VersionFiles.DotnetToolsConfigJson, vmrComparisonSha) != null));

        if (dotnetToolsConfigExists)
        {
            await _jsonFileMerger.MergeJsonsAsync(
                vmr,
                relativeSourceMappingPath / VersionFiles.DotnetToolsConfigJson,
                vmrComparisonSha,
                targetBranch,
                sourceRepo,
                VersionFiles.DotnetToolsConfigJson,
                repoComparisonSha,
                currentFlow.RepoSha,
                allowMissingFiles: true);
        }

        // If Version.Details.props exists in the source repo, but not in the VMR, we create it and fill it out later.
        // This can happen if a repo was initialized inside of the vmr when it didn't have this file
        bool versionDetailsPropsCreated = false;
        if (await _dependencyFileManager.VersionDetailsPropsExistsAsync(sourceRepo.Path, branch: null!)
                && !await _dependencyFileManager.VersionDetailsPropsExistsAsync(vmr.Path, branch: null!, VmrInfo.GetRelativeRepoSourcesPath(mappingName)))
        {
            _fileSystem.WriteToFile(vmr.Path / relativeSourceMappingPath / VersionFiles.VersionDetailsProps, string.Empty);
            versionDetailsPropsCreated = true;
        }

        var versionDetailsChanges = await _versionDetailsFileMerger.MergeVersionDetails(
            vmr,
            relativeSourceMappingPath / VersionFiles.VersionDetailsXml,
            vmrComparisonSha,
            targetBranch,
            sourceRepo,
            VersionFiles.VersionDetailsXml,
            repoComparisonSha,
            currentFlow.RepoSha,
            mappingName);

        // Also flow the Source tag if it changed
        var repoVersionDetails = await _dependencyFileManager.ParseVersionDetailsXmlAsync(
            sourceRepo.Path,
            currentFlow.RepoSha);
        var vmrVersionDetails = await _dependencyFileManager.ParseVersionDetailsXmlAsync(
            vmr.Path,
            targetBranch,
            relativeBasePath: relativeSourceMappingPath);

        XmlDocument? vmrVersionDetailsXml = null;
        if (repoVersionDetails.Source != null
            && repoVersionDetails.Source.BarId != vmrVersionDetails.Source?.BarId)
        {
            // Get the staged version details
            vmrVersionDetailsXml = await _dependencyFileManager.ReadVersionDetailsXmlAsync(vmr.Path, null!, relativeSourceMappingPath);
            _dependencyFileManager.UpdateVersionDetailsXmlSourceTag(vmrVersionDetailsXml, repoVersionDetails.Source);

            _fileSystem.WriteToFile(
                vmr.Path / relativeSourceMappingPath / VersionFiles.VersionDetailsXml,
                GitFile.GetIndentedXmlBody(vmrVersionDetailsXml));

            await vmr.StageAsync([relativeSourceMappingPath / VersionFiles.VersionDetailsXml], cancellationToken);
        }

        // If we didn't have any changes, and we just added Version.Details.props, we need to generate it
        if (!versionDetailsChanges.HasChanges && versionDetailsPropsCreated)
        {
            vmrVersionDetailsXml ??= await _dependencyFileManager.ReadVersionDetailsXmlAsync(vmr.Path, null!, relativeSourceMappingPath);
            var versionDetails = _versionDetailsParser.ParseVersionDetailsXml(vmrVersionDetailsXml);
            var versionPropsXml = DependencyFileManager.GenerateVersionDetailsProps(versionDetails);

            _fileSystem.WriteToFile(
                vmr.Path / relativeSourceMappingPath / VersionFiles.VersionDetailsProps,
                GitFile.GetIndentedXmlBody(versionPropsXml));

            await vmr.StageAsync([relativeSourceMappingPath / VersionFiles.VersionDetailsProps], cancellationToken);
        }

        if (!await vmr.HasWorkingTreeChangesAsync() && !await vmr.HasStagedChangesAsync())
        {
            _logger.LogInformation("No changes to dependencies in this forward flow update");
        }
    }
}
