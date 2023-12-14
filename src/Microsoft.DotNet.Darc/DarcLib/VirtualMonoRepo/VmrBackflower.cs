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

public interface IVmrBackflower
{
    Task<string?> FlowBackAsync(
        string mapping,
        NativePath targetRepo,
        string? shaToFlow = null,
        bool discardPatches = false,
        CancellationToken cancellationToken = default);
}

internal class VmrBackflower : VmrCodeflower, IVmrBackflower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly ILocalGitClient _localGitClient;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IVmrPatchHandler _vmrPatchHandler;
    private readonly IProcessManager _processManager;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrCodeflower> _logger;

    public VmrBackflower(
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
        string mapping,
        NativePath targetRepo,
        string? shaToFlow = null,
        bool discardPatches = false,
        CancellationToken cancellationToken = default)
    {
        if (shaToFlow is null)
        {
            shaToFlow = await _localGitClient.GetShaForRefAsync(_vmrInfo.VmrPath, Constants.HEAD);
        }
        else
        {
            await CheckOutVmr(shaToFlow);
        }

        var branchName = await FlowCodeAsync(
            isBackflow: true,
            targetRepo,
            mapping,
            shaToFlow,
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
        string shaToFlow,
        NativePath repoPath,
        Codeflow lastFlow,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        var isBackflow = lastFlow is Backflow;
        var shortShas = $"{Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(shaToFlow)}";
        var branchName = $"codeflow/{lastFlow.GetType().Name.ToLower()}/{shortShas}";
        var targetRepo = isBackflow ? repoPath : _vmrInfo.VmrPath;
        var submoduleExclusions = _sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/'))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1))
            .Select(VmrPatchHandler.GetExclusionRule)
            .ToList();

        // When flowing from the VMR, ignore all submodules
        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            _vmrInfo.TmpPath / (mapping.Name + "-backflow-" + shortShas + ".patch"),
            lastFlow.VmrSha,
            shaToFlow,
            path: null,
            filters: submoduleExclusions,
            relativePaths: true,
            workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
            applicationPath: null,
            cancellationToken);

        if (patches.Count == 0 || patches.All(p => _fileSystem.GetFileInfo(p.Path).Length == 0))
        {
            _logger.LogInformation("There are no new changes for {mappingName} between {sha1} and {sha2}",
                isBackflow ? "VMR" : mapping.Name,
                lastFlow.SourceSha,
                shaToFlow);

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

        await _localGitClient.CheckoutAsync(targetRepo, lastFlow.TargetSha);
        await _workBranchFactory.CreateWorkBranchAsync(targetRepo, branchName);


        // TODO: Remove VMR patches before we create the patches

        try
        {
            foreach (VmrIngestionPatch patch in patches)
            {
                await _vmrPatchHandler.ApplyPatch(patch, targetRepo, cancellationToken);

                if (discardPatches)
                {
                    _fileSystem.DeleteFile(patch.Path);
                }
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
            await _localGitClient.CheckoutAsync(targetRepo, previousRepoSha);

            // Reconstruct the previous flow's branch
            branchName = await FlowCodeAsync(
                isBackflow: true,
                repoPath,
                mapping.Name,
                lastFlow.SourceSha,
                discardPatches,
                cancellationToken);

            // The current patches should apply now
            foreach (VmrIngestionPatch patch in patches)
            {
                // TODO: Catch exceptions?
                await _vmrPatchHandler.ApplyPatch(patch, targetRepo, cancellationToken);

                if (discardPatches)
                {
                    _fileSystem.DeleteFile(patch.Path);
                }
            }
        }

        var commitMessage = $"""
            [{(isBackflow ? "VMR" : mapping.Name)}] Codeflow {shortShas}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await _localGitClient.CommitAsync(targetRepo, commitMessage, allowEmpty: false, cancellationToken: cancellationToken);
        await _localGitClient.ResetWorkingTree(targetRepo);

        _logger.LogInformation("New branch {branch} with flown code is ready in {repoDir}", branchName, repoPath);

        return branchName;
    }

    protected override async Task<string?> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        string shaToFlow,
        NativePath targetRepo,
        Codeflow lastFlow,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        var shortShas = $"{Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(shaToFlow)}";
        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-backflow-{shortShas}.patch";

        await _localGitClient.CheckoutAsync(targetRepo, lastFlow.SourceSha);

        var branchName = $"codeflow/{lastFlow.GetType().Name.ToLower()}/{shortShas}";
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
            shaToFlow,
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
            await _vmrPatchHandler.ApplyPatch(patch, targetRepo, cancellationToken);

            if (discardPatches)
            {
                _fileSystem.DeleteFile(patch.Path);
            }
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
            [VMR] Codeflow {shortShas}

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
