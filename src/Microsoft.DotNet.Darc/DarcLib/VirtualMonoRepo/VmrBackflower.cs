// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrBackFlower
{
    Task<string?> FlowBackAsync(
        string mapping,
        NativePath targetRepo,
        string? shaToFlow = null,
        bool discardPatches = false,
        CancellationToken cancellationToken = default);
}

internal class VmrBackFlower : VmrCodeflower, IVmrBackFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly ILocalGitClient _localGitClient;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IVmrPatchHandler _vmrPatchHandler;
    private readonly IProcessManager _processManager;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrCodeflower> _logger;

    public VmrBackFlower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrDependencyTracker dependencyTracker,
        IDependencyFileManager dependencyFileManager,
        ILocalGitClient localGitClient,
        IVersionDetailsParser versionDetailsParser,
        IVmrPatchHandler vmrPatchHandler,
        IProcessManager processManager,
        IWorkBranchFactory workBranchFactory,
        IFileSystem fileSystem,
        ILogger<VmrCodeflower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, versionDetailsParser, processManager, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _dependencyFileManager = dependencyFileManager;
        _localGitClient = localGitClient;
        _versionDetailsParser = versionDetailsParser;
        _vmrPatchHandler = vmrPatchHandler;
        _processManager = processManager;
        _workBranchFactory = workBranchFactory;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<string?> FlowBackAsync(
        string mappingName,
        NativePath targetRepo,
        string? shaToFlow = null,
        bool discardPatches = false,
        CancellationToken cancellationToken = default)
    {
        if (shaToFlow is null)
        {
            shaToFlow = await _localGitClient.GetShaForRefAsync(_vmrInfo.VmrPath);
        }
        else
        {
            await CheckOutVmr(shaToFlow);
        }

        var mapping = _dependencyTracker.Mappings.First(m => m.Name == mappingName);
        Codeflow lastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);

        var branchName = await FlowCodeAsync(
            lastFlow,
            new Backflow(lastFlow.TargetSha, shaToFlow),
            targetRepo,
            mapping,
            discardPatches,
            cancellationToken);

        if (branchName is null)
        {
            // TODO: We should still probably update package versions or at least try?
            // Should we clean up the repos?
            return null;
        }

        await UpdateVersionDetailsXml(targetRepo, shaToFlow, cancellationToken);
        return branchName;
    }

    protected override async Task<string?> SameDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        NativePath repoPath,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        var branchName = lastFlow.GetBranchName();

        // Exclude all submodules that belong to the mapping
        var submoduleExclusions = _sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/'))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1))
            .Select(VmrPatchHandler.GetExclusionRule)
            .ToList();

        // When flowing from the VMR, ignore all submodules
        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            _vmrInfo.TmpPath / (mapping.Name + branchName.Replace('/', '-') + ".patch"),
            lastFlow.VmrSha,
            currentFlow.TargetSha,
            path: null,
            filters: submoduleExclusions,
            relativePaths: true,
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
            applicationPath: null,
            cancellationToken);

        if (patches.Count == 0 || patches.All(p => _fileSystem.GetFileInfo(p.Path).Length == 0))
        {
            _logger.LogInformation("There are no new changes for VMR between {sha1} and {sha2}",
                lastFlow.SourceSha,
                currentFlow.TargetSha);

            if (discardPatches)
            {
                foreach (VmrIngestionPatch patch in patches)
                {
                    _fileSystem.DeleteFile(patch.Path);
                }
            }

            return null;
        }

        _logger.LogInformation("Created {count} patch(es)", patches.Count);

        await _localGitClient.CheckoutAsync(repoPath, lastFlow.TargetSha);
        await _workBranchFactory.CreateWorkBranchAsync(repoPath, branchName);

        // TODO: Remove VMR patches before we create the patches

        try
        {
            foreach (VmrIngestionPatch patch in patches)
            {
                await _vmrPatchHandler.ApplyPatch(patch, repoPath, discardPatches, cancellationToken);
            }
        }
        catch (Exception e) when (e.Message.Contains("Failed to apply the patch"))
        {
            // TODO: This can happen when we also update a PR branch but there are conflicting changes inside. In this case, we should just stop. We need a flag for that.

            // This happens when a conflicting change was made in the last backflow PR (before merging)
            // The scenario is described here: https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Full-Code-Flow.md#conflicts
            _logger.LogInformation("Failed to create PR branch because of a conflict. Re-creating the previous flow..");

            // Find the last target commit in the repo
            var previousRepoSha = await BlameLineAsync(
                repoPath / VersionFiles.VersionDetailsXml,
                line => line.Contains(VersionDetailsParser.SourceElementName) && line.Contains(lastFlow.SourceSha),
                lastFlow.TargetSha);
            await _localGitClient.CheckoutAsync(repoPath, previousRepoSha);

            // Reconstruct the previous flow's branch
            var lastLastFlow = await GetLastFlowAsync(mapping, repoPath, currentIsBackflow: true);

            branchName = await FlowCodeAsync(
                lastLastFlow,
                new Backflow(lastLastFlow.SourceSha, lastFlow.SourceSha),
                repoPath,
                mapping,
                discardPatches,
                cancellationToken);

            // The current patches should apply now
            foreach (VmrIngestionPatch patch in patches)
            {
                // TODO: Catch exceptions?
                await _vmrPatchHandler.ApplyPatch(patch, repoPath, discardPatches, cancellationToken);
            }
        }

        var commitMessage = $"""
            [VMR] Codeflow {Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await _localGitClient.CommitAsync(repoPath, commitMessage, allowEmpty: false, cancellationToken: cancellationToken);
        await _localGitClient.ResetWorkingTree(repoPath);

        _logger.LogInformation("New branch {branch} with flown code is ready in {repoDir}", branchName, repoPath);

        return branchName;
    }

    protected override async Task<string?> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        NativePath targetRepo,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        await _localGitClient.CheckoutAsync(targetRepo, lastFlow.SourceSha);

        var branchName = currentFlow.GetBranchName();
        var patchName = _vmrInfo.TmpPath / $"{branchName.Replace('/', '-')}.patch";
        var prBanch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, branchName);
        _logger.LogInformation("Created temporary branch {branchName} in {repoDir}", branchName, targetRepo);

        // We leave the inlined submodules in the VMR
        var submoduleExclusions = _sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/'))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1))
            .Select(VmrPatchHandler.GetExclusionRule)
            .ToList();

        // TODO: Remove VMR patches before we create the patches

        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            patchName,
            Constants.EmptyGitObject,
            currentFlow.TargetSha,
            path: null,
            submoduleExclusions,
            relativePaths: true,
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
            applicationPath: null,
            cancellationToken);

        _logger.LogInformation("Created {count} patch(es)", patches.Count);

        // We will remove everything not-cloaked and replace it with current contents of the source repo
        // When flowing to a repo, we remove all repo files but submodules and cloaked files
        List<string> removalFilters =
        [
            .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule),
            .. submoduleExclusions,
        ];

        ProcessExecutionResult result = await _processManager.ExecuteGit(targetRepo, ["rm", "-r", "-q", "--", .. removalFilters]);
        result.ThrowIfFailed($"Failed to remove files from {targetRepo}");

        // Now we insert the VMR files
        foreach (var patch in patches)
        {
            // TODO: Handle exceptions
            await _vmrPatchHandler.ApplyPatch(patch, targetRepo, discardPatches, cancellationToken);
        }

        // TODO: Check if there are any changes and only commit if there are
        result = await _processManager.ExecuteGit(
            targetRepo,
            ["diff-index", "--quiet", "--cached", "HEAD", "--"],
            cancellationToken: cancellationToken);

        if (result.ExitCode == 0)
        {
            // TODO: Handle + clean up the work branch
            return null;
        }

        var commitMessage = $"""
            [VMR] Codeflow {Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await _localGitClient.CommitAsync(targetRepo, commitMessage, false, cancellationToken: cancellationToken);
        await _localGitClient.ResetWorkingTree(targetRepo);

        return branchName;
    }

    private async Task UpdateVersionDetailsXml(NativePath repoPath, string currentVmrSha, CancellationToken cancellationToken)
    {
        // TODO: Do a proper full update of all dependencies, not just V.D.xml
        var versionDetailsXml = DependencyFileManager.GetXmlDocument(await _fileSystem.ReadAllTextAsync(repoPath / VersionFiles.VersionDetailsXml));
        var versionDetails = _versionDetailsParser.ParseVersionDetailsXml(versionDetailsXml);
        _dependencyFileManager.UpdateVersionDetails(
            versionDetailsXml,
            itemsToUpdate: [],
            // TODO: Fix the URL with the URI of the BAR build we're processing
            new SourceDependency("https://github.com/dotnet/dotnet", currentVmrSha),
            oldDependencies: versionDetails.Dependencies);

        _fileSystem.WriteToFile(repoPath / VersionFiles.VersionDetailsXml, new GitFile(VersionFiles.VersionDetailsXml, versionDetailsXml).Content);
        await _localGitClient.StageAsync(repoPath, [VersionFiles.VersionDetailsXml], cancellationToken);
        await _localGitClient.CommitAsync(repoPath, $"Update {VersionFiles.VersionDetailsXml} to {currentVmrSha}", false, cancellationToken: cancellationToken);
    }
}
