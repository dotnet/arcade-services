﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrBackFlower
{
    Task<string?> FlowBackAsync(
        string mapping,
        NativePath targetRepo,
        string? shaToFlow,
        int? buildToFlow,
        bool discardPatches = false,
        CancellationToken cancellationToken = default);

    Task<string?> FlowBackAsync(
        string mapping,
        ILocalGitRepo targetRepo,
        string? shaToFlow,
        int? buildToFlow,
        bool discardPatches = false,
        CancellationToken cancellationToken = default);
}

internal class VmrBackFlower : VmrCodeFlower, IVmrBackFlower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly ILocalGitClient _localGitClient;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IVmrPatchHandler _vmrPatchHandler;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IBasicBarClient _barClient;
    private readonly ILocalLibGit2Client _libGit2Client;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IAssetLocationResolver _assetLocationResolver;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrCodeFlower> _logger;

    public VmrBackFlower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrDependencyTracker dependencyTracker,
        IDependencyFileManager dependencyFileManager,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IVmrPatchHandler vmrPatchHandler,
        IWorkBranchFactory workBranchFactory,
        IBasicBarClient basicBarClient,
        ILocalLibGit2Client libGit2Client,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IAssetLocationResolver assetLocationResolver,
        IFileSystem fileSystem,
        ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, localGitRepoFactory, versionDetailsParser, fileSystem, logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _dependencyFileManager = dependencyFileManager;
        _localGitClient = localGitClient;
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _vmrPatchHandler = vmrPatchHandler;
        _workBranchFactory = workBranchFactory;
        _barClient = basicBarClient;
        _libGit2Client = libGit2Client;
        _coherencyUpdateResolver = coherencyUpdateResolver;
        _assetLocationResolver = assetLocationResolver;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public Task<string?> FlowBackAsync(
        string mapping,
        NativePath targetRepoPath,
        string? shaToFlow,
        int? buildToFlow,
        bool discardPatches = false,
        CancellationToken cancellationToken = default)
        => FlowBackAsync(mapping, _localGitRepoFactory.Create(targetRepoPath), shaToFlow, buildToFlow, discardPatches, cancellationToken);

    public async Task<string?> FlowBackAsync(
        string mappingName,
        ILocalGitRepo targetRepo,
        string? shaToFlow,
        int? buildToFlow,
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

        await UpdateDependenciesAndToolset(targetRepo, shaToFlow, buildToFlow, cancellationToken);
        return branchName;
    }

    protected override async Task<string?> SameDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
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

        await targetRepo.CheckoutAsync(lastFlow.TargetSha);
        await _workBranchFactory.CreateWorkBranchAsync(targetRepo, branchName);

        // TODO: Remove VMR patches before we create the patches

        try
        {
            foreach (VmrIngestionPatch patch in patches)
            {
                await _vmrPatchHandler.ApplyPatch(patch, targetRepo.Path, discardPatches, cancellationToken);
            }
        }
        catch (PatchApplicationFailedException e)
        {
            _logger.LogInformation(e.Message);

            // TODO: This can happen when we also update a PR branch but there are conflicting changes inside. In this case, we should just stop. We need a flag for that.

            // This happens when a conflicting change was made in the last backflow PR (before merging)
            // The scenario is described here: https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Full-Code-Flow.md#conflicts
            _logger.LogInformation("Failed to create PR branch because of a conflict. Re-creating the previous flow..");

            // Find the last target commit in the repo
            var previousRepoSha = await BlameLineAsync(
                targetRepo.Path / VersionFiles.VersionDetailsXml,
                line => line.Contains(VersionDetailsParser.SourceElementName) && line.Contains(lastFlow.SourceSha),
                lastFlow.TargetSha);
            await targetRepo.CheckoutAsync(previousRepoSha);

            // Reconstruct the previous flow's branch
            var lastLastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);

            branchName = await FlowCodeAsync(
                lastLastFlow,
                new Backflow(lastLastFlow.SourceSha, lastFlow.SourceSha),
                targetRepo,
                mapping,
                discardPatches,
                cancellationToken);

            // The current patches should apply now
            foreach (VmrIngestionPatch patch in patches)
            {
                // TODO: Catch exceptions?
                await _vmrPatchHandler.ApplyPatch(patch, targetRepo.Path, discardPatches, cancellationToken);
            }
        }

        var commitMessage = $"""
            [VMR] Codeflow {Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await targetRepo.CommitAsync(commitMessage, allowEmpty: false, cancellationToken: cancellationToken);
        await targetRepo.ResetWorkingTree();

        _logger.LogInformation("New branch {branch} with flown code is ready in {repoDir}", branchName, targetRepo);

        return branchName;
    }

    protected override async Task<string?> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        Codeflow lastFlow,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        bool discardPatches,
        CancellationToken cancellationToken)
    {
        await targetRepo.CheckoutAsync(lastFlow.SourceSha);

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

        ProcessExecutionResult result = await targetRepo.ExecuteGitCommand(["rm", "-r", "-q", "--", .. removalFilters], cancellationToken);
        result.ThrowIfFailed($"Failed to remove files from {targetRepo}");

        // Now we insert the VMR files
        foreach (var patch in patches)
        {
            // TODO: Handle exceptions
            await _vmrPatchHandler.ApplyPatch(patch, targetRepo.Path, discardPatches, cancellationToken);
        }

        // TODO: Check if there are any changes and only commit if there are
        result = await targetRepo.ExecuteGitCommand(["diff-index", "--quiet", "--cached", "HEAD", "--"], cancellationToken);

        if (result.ExitCode == 0)
        {
            // TODO: Handle + clean up the work branch
            return null;
        }

        var commitMessage = $"""
            [VMR] Codeflow {Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(currentFlow.TargetSha)}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await targetRepo.CommitAsync(commitMessage, false, cancellationToken: cancellationToken);
        await targetRepo.ResetWorkingTree();

        return branchName;
    }

    private async Task UpdateDependenciesAndToolset(ILocalGitRepo repo, string currentVmrSha, int? buildToFlow, CancellationToken cancellationToken)
    {
        string versionDetailsXml = await repo.GetFileFromGitAsync(VersionFiles.VersionDetailsXml)
            ?? throw new Exception($"Failed to read {VersionFiles.VersionDetailsXml} from {repo.Path}");
        VersionDetails versionDetails = _versionDetailsParser.ParseVersionDetailsXml(versionDetailsXml);
        await _assetLocationResolver.AddAssetLocationToDependenciesAsync(versionDetails.Dependencies);

        SourceDependency? sourceOrigin;
        List<DependencyUpdate> updates;

        // Generate the <Source /> element and get updates
        if (buildToFlow.HasValue)
        {
            Build build = await _barClient.GetBuildAsync(buildToFlow.Value)
                ?? throw new Exception($"Failed to find build with ID {buildToFlow}");

            sourceOrigin = new SourceDependency(
                build.GitHubRepository ?? build.AzureDevOpsRepository,
                currentVmrSha);

            IEnumerable<AssetData> assetData = build.Assets.Select(
                a => new AssetData(a.NonShipping)
                {
                    Name = a.Name,
                    Version = a.Version
                });

            updates = _coherencyUpdateResolver.GetRequiredNonCoherencyUpdates(
                sourceOrigin.Uri,
                sourceOrigin.Sha,
                assetData,
                versionDetails.Dependencies);

            await _assetLocationResolver.AddAssetLocationToDependenciesAsync([.. updates.Select(u => u.To)]);
        }
        else
        {
            sourceOrigin = versionDetails.Source != null
                ? versionDetails.Source with { Sha = currentVmrSha }
                : new SourceDependency(Constants.DefaultVmrUri, currentVmrSha); // First ever backflow for the repo
            updates = [];
        }

        // If we are updating the arcade sdk we need to update the eng/common files as well
        DependencyDetail? arcadeItem = updates.GetArcadeUpdate();
        SemanticVersion? targetDotNetVersion = null;

        if (arcadeItem != null)
        {
            targetDotNetVersion = await _dependencyFileManager.ReadToolsDotnetVersionAsync(arcadeItem.RepoUri, arcadeItem.Commit);
        }

        GitFileContentContainer updatedFiles = await _dependencyFileManager.UpdateDependencyFiles(
            updates.Select(u => u.To),
            sourceOrigin,
            repo.Path,
            Constants.HEAD,
            versionDetails.Dependencies,
            targetDotNetVersion);

        // TODO https://github.com/dotnet/arcade-services/issues/3251: Stop using LibGit2SharpClient for this
        await _libGit2Client.CommitFilesAsync(updatedFiles.GetFilesToCommit(), repo.Path, null, null);

        // Update eng/common files
        if (arcadeItem != null)
        {
            var commonDir = repo.Path / Constants.CommonScriptFilesPath;
            if (_fileSystem.DirectoryExists(commonDir))
            {
                _fileSystem.DeleteDirectory(commonDir, true);
            }

            _fileSystem.CopyDirectory(
                _vmrInfo.VmrPath / Constants.CommonScriptFilesPath,
                repo.Path / Constants.CommonScriptFilesPath,
                true);
        }

        await repo.StageAsync(["."], cancellationToken);
        await repo.CommitAsync($"Update dependency files to {currentVmrSha}", false, cancellationToken: cancellationToken);
    }
}
