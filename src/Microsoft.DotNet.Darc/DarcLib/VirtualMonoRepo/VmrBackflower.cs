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
    // TODO: Docs
    Task<string?> FlowBackAsync(
        string mapping,
        NativePath targetRepo,
        string? shaToFlow = null,
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
        CancellationToken cancellationToken = default)
    {
        shaToFlow ??= await _localGitClient.GetShaForRefAsync(_vmrInfo.VmrPath, Constants.HEAD);

        var branchName = await FlowCodeAsync(isBackflow: true, targetRepo, mapping, shaToFlow, cancellationToken);

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
            // TODO: Remove empty patches
            _logger.LogInformation("There are no new changes for {mappingName} between {sha1} and {sha2}",
                isBackflow ? "VMR" : mapping.Name,
                lastFlow.SourceSha,
                shaToFlow);
            return null;
        }

        _logger.LogInformation("Created {count} patch(es)", patches.Count);

        await _localGitClient.CheckoutAsync(targetRepo, lastFlow.TargetSha);
        await _workBranchFactory.CreateWorkBranchAsync(targetRepo, branchName);

        try
        {
            foreach (VmrIngestionPatch patch in patches)
            {
                await _vmrPatchHandler.ApplyPatch(patch, targetRepo, cancellationToken);
                // TODO: Discard patches
            }
        }
        catch (Exception)
        {
            // Go one step back and prepare the previous branch
            // TODO: Reset and try from an earlier commit
            //await BackflowAsync(mappingName, repoPath,);

            //foreach (VmrIngestionPatch patch in patches)
            //{
            //    await _vmrPatchHandler.ApplyPatch(patch, repoPath, cancellationToken);
            // TODO: Discard patches
            //}
            throw new NotImplementedException();
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

    // TODO: Docs
    protected override async Task<string?> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        string shaToFlow,
        NativePath targetRepo,
        Codeflow lastFlow,
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
            // TODO: Discard patches
        }

        // TODO: Check if there are any changes and only commit if there are
        result = await _processManager.ExecuteGit(
            targetRepo,
            ["git", "diff-index", "--quiet", "--cached", "HEAD", "--"],
            cancellationToken: cancellationToken);

        if (result.ExitCode == 0)
        {
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
        // TODO: Error handling
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
