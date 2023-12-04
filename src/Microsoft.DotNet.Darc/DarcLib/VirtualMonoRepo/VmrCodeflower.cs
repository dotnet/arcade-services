// Licensed to the .NET Foundation under one or more agreements.
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
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrCodeflower
{
    // TODO: Doc
    Task<string?> FlowCodeAsync(
        NativePath sourceRepo,
        NativePath targetRepo,
        string mappingName,
        string? shaToFlow = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// This class is responsible for taking changes done to a repo in the VMR and backflowing them into the repo.
/// It only makes patches/changes locally, no other effects are done.
/// </summary>
public class VmrCodeflower : IVmrCodeflower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrUpdater _vmrUpdater;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly ILocalGitClient _localGitClient;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IVmrPatchHandler _vmrPatchHandler;
    private readonly IProcessManager _processManager;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrCodeflower> _logger;

    public VmrCodeflower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        IVmrUpdater vmrUpdater,
        IVmrDependencyTracker dependencyTracker,
        IDependencyFileManager dependencyFileManager,
        ILocalGitClient localGitClient,
        IVersionDetailsParser versionDetailsParser,
        IVmrPatchHandler vmrPatchHandler,
        IProcessManager processManager,
        IWorkBranchFactory workBranchFactory,
        IFileSystem fileSystem,
        ILogger<VmrCodeflower> logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _vmrUpdater = vmrUpdater;
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

    public async Task<string?> FlowCodeAsync(
        NativePath sourceRepo,
        NativePath targetRepo,
        string mappingName,
        string? shaToFlow = null,
        CancellationToken cancellationToken = default)
    {
        if (shaToFlow is null)
        {
            shaToFlow = await _localGitClient.GetShaForRefAsync(sourceRepo, "HEAD");
        }
        else
        {
            await _localGitClient.CheckoutAsync(sourceRepo, shaToFlow);
        }

        await _dependencyTracker.InitializeSourceMappings();
        var mapping = _dependencyTracker.Mappings.First(m => m.Name == mappingName);

        var branchName = IsVmr(sourceRepo)
            ? await BackflowAsync(mapping, shaToFlow, targetRepo, cancellationToken)
            : await ForwardFlowAsync(mapping, shaToFlow, sourceRepo, cancellationToken);

        if (branchName == null)
        {
            _logger.LogInformation("No changes to synchronize, {repo} was already synchronized to {sha}",
                IsVmr(sourceRepo) ? mappingName : "VMR",
                shaToFlow);
            return null;
        }

        return branchName;
    }

    // TODO: Docs
    private async Task<string?> BackflowAsync(
        SourceMapping mapping,
        string shaToFlow,
        NativePath targetRepo,
        CancellationToken cancellationToken)
    {
        Codeflow lastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);

        // TODO: Log

        var branchName = lastFlow is Backflow
            ? await SameDirectionFlowAsync(mapping, shaToFlow, targetRepo, lastFlow, cancellationToken)
            : await OppositeDirectionFlowAsync(mapping, shaToFlow, targetRepo, lastFlow, cancellationToken);

        if (branchName is null)
        {
            // TODO: We should still probably update package versions or at least try?
            return null;
        }

        await UpdateVersionDetailsXml(targetRepo, shaToFlow, cancellationToken);
        return branchName;
    }

    // TODO: Docs
    private async Task<string?> ForwardFlowAsync(
        SourceMapping mapping,
        string shaToFlow,
        NativePath sourceRepo,
        CancellationToken cancellationToken)
    {
        Codeflow lastFlow = await GetLastFlowAsync(mapping, sourceRepo, currentIsBackflow: false);

        // TODO: Log

        var branchName = lastFlow is Backflow
            ? await OppositeDirectionFlowAsync(mapping, shaToFlow, sourceRepo, lastFlow, cancellationToken)
            : await SameDirectionFlowAsync(mapping, shaToFlow, sourceRepo, lastFlow, cancellationToken);

        if (branchName is null)
        {
            // TODO: Do something here?
            return null;
        }

        return branchName;
    }

    // TODO: Docs
    private async Task<string?> SameDirectionFlowAsync(
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

        if (!isBackflow)
        {
            await _workBranchFactory.CreateWorkBranchAsync(targetRepo, branchName);

            // TODO: Detect if no changes
            var updated = await _vmrUpdater.UpdateRepository(
                mapping.Name,
                shaToFlow,
                "1.2.3",
                updateDependencies: false,
                // TODO
                additionalRemotes: Array.Empty<AdditionalRemote>(),
                readmeTemplatePath: null,
                tpnTemplatePath: null,
                generateCodeowners: true,
                discardPatches: false,
                cancellationToken);

            return updated ? branchName : null;
        }

        List<VmrIngestionPatch> patches;

        if (isBackflow)
        {
            // When flowing from the VMR, ignore all submodules
            var submoduleExclusions = _sourceManifest.Submodules
                .Where(s => s.Path.StartsWith(mapping.Name + '/'))
                .Select(s => s.Path.Substring(mapping.Name.Length + 1))
                .Select(VmrPatchHandler.GetExclusionRule)
                .ToList();

            patches = await _vmrPatchHandler.CreatePatches(
                _vmrInfo.TmpPath / (mapping.Name + "-backflow-" + shortShas + ".patch"),
                lastFlow.VmrSha,
                shaToFlow,
                path: null,
                filters: submoduleExclusions,
                relativePaths: true,
                workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
                applicationPath: null,
                cancellationToken);
        }
        else
        {
            // When forward-flowing, we create the patches the usual way (just like VMR-lite)
            patches = await _vmrPatchHandler.CreatePatches(
                mapping,
                repoPath,
                lastFlow.SourceSha,
                shaToFlow,
                _vmrInfo.TmpPath,
                _vmrInfo.TmpPath,
                cancellationToken);
        }

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
    private async Task<string?> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        string shaToFlow,
        NativePath repoPath,
        Codeflow lastFlow,
        CancellationToken cancellationToken)
    {
        var isBackflow = lastFlow is ForwardFlow;
        var targetRepo = isBackflow ? repoPath : _vmrInfo.VmrPath;
        var shortShas = $"{Commit.GetShortSha(lastFlow.SourceSha)}-{Commit.GetShortSha(shaToFlow)}";
        var patchName = _vmrInfo.TmpPath / $"{mapping.Name}-{(isBackflow ? "backflow" : "forwardflow")}-{shortShas}.patch";

        await _localGitClient.CheckoutAsync(targetRepo, lastFlow.SourceSha);

        var branchName = $"codeflow/{lastFlow.GetType().Name.ToLower()}/{shortShas}";
        var prBanch = await _workBranchFactory.CreateWorkBranchAsync(targetRepo, branchName);
        _logger.LogInformation("Created temporary branch {branchName} in {repoDir}", branchName, repoPath);

        // We will remove everything not-cloaked and replace it with current contents of the source repo
        List<string> removalFilters;
        List<VmrIngestionPatch> patches = null!; // TODO
        ProcessExecutionResult result;

        // Let's create a patch representing files in the source repo (zero commit -> HEAD)
        // Using a patch will allow us to cloak well
        // TODO: This might be an extra work - we could possibly just copy the contents of the VMR folder (minus cloaking)
        if (!isBackflow)
        {
            var submodules = await _localGitClient.GetGitSubmodulesAsync(repoPath, shaToFlow);

            // When flowing to the VMR, we remove all files but sobmodules and cloaked files
            removalFilters =
            [
                .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
                .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule),
                .. submodules.Select(s => s.Path).Select(VmrPatchHandler.GetExclusionRule),
            ];

            result = await _processManager.Execute(
                _processManager.GitExecutable,
                ["rm", "-r", "-q", "--", .. removalFilters],
                workingDir: _vmrInfo.GetRepoSourcesPath(mapping),
                cancellationToken: cancellationToken);

            result.ThrowIfFailed($"Failed to remove files from {targetRepo}");

            _dependencyTracker.UpdateDependencyVersion(new VmrDependencyUpdate(
                mapping,
                repoPath, // TODO
                Constants.EmptyGitObject,
                _dependencyTracker.GetDependencyVersion(mapping)!.PackageVersion,
                null));

            // TODO: Detect if no changes
            var updated = await _vmrUpdater.UpdateRepository(
                mapping.Name,
                shaToFlow,
                "1.2.3",
                updateDependencies: false,
                // TODO
                additionalRemotes: Array.Empty<AdditionalRemote>(),
                readmeTemplatePath: null,
                tpnTemplatePath: null,
                generateCodeowners: true,
                discardPatches: false,
                cancellationToken);

            return updated ? branchName : null;
        }

        // We leave the inlined submodules in the VMR
        var submoduleExclusions = _sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/'))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1))
            .Select(VmrPatchHandler.GetExclusionRule)
            .ToList();

        patches = await _vmrPatchHandler.CreatePatches(
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

        // When flowing to a repo, we remove all repo files but submodules and cloaked files
        removalFilters =
        [
            .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule),
            .. submoduleExclusions,
        ];

        result = await _processManager.ExecuteGit(targetRepo, ["rm", "-r", "-q", "--", .. removalFilters]);
        result.ThrowIfFailed($"Failed to remove files from {targetRepo}");

        // Now we insert the VMR files
        foreach (var patch in patches)
        {
            // TODO: Handle exceptions
            await _vmrPatchHandler.ApplyPatch(patch, repoPath, cancellationToken);
            // TODO: Discard patches
        }

        // TODO: Check if there are any changes and only commit if there are
        result = await _processManager.ExecuteGit(
            targetRepo,
            ["git", "diff-index", "--quiet", "--cached", "HEAD", "--"],
            cancellationToken: cancellationToken);

        if (result.ExitCode == 0)
        {
            _logger.LogInformation("There are no new changes for {mappingName} between {sha1} and {sha2}",
                isBackflow ? "VMR" : mapping.Name,
                lastFlow.SourceSha,
                shaToFlow);
            return null;
        }

        var commitMessage = $"""
            [{(isBackflow ? "VMR" : mapping.Name)}] Codeflow {shortShas}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await _localGitClient.CommitAsync(targetRepo, commitMessage, false, cancellationToken: cancellationToken);
        await _localGitClient.ResetWorkingTree(targetRepo);

        return branchName;
    }

    private async Task<Codeflow> GetLastFlowAsync(SourceMapping mapping, NativePath repoPath, bool currentIsBackflow)
    {
        ForwardFlow lastForwardFlow = await GetLastForwardFlow(mapping.Name);
        Backflow? lastBackflow = await GetLastBackflow(repoPath);

        if (lastBackflow is null)
        {
            return lastForwardFlow;
        }

        string backwardSha, forwardSha;
        NativePath sourceRepo;
        if (currentIsBackflow)
        {
            (backwardSha, forwardSha) = (lastBackflow.VmrSha, lastForwardFlow.VmrSha);
            sourceRepo = _vmrInfo.VmrPath;
        }
        else
        {
            (backwardSha, forwardSha) = (lastBackflow.RepoSha, lastForwardFlow.RepoSha);
            sourceRepo = repoPath;
        }

        string objectType1 = await _localGitClient.GetObjectTypeAsync(sourceRepo, backwardSha);
        string objectType2 = await _localGitClient.GetObjectTypeAsync(sourceRepo, forwardSha);

        if (objectType1 != "commit" || objectType2 != "commit")
        {
            throw new Exception($"Failed to find commits {lastBackflow.VmrSha}, {lastForwardFlow.VmrSha} in {sourceRepo}");
        }

        // Let's determine the last flow by comparing source commit of last backflow with target commit of last forward flow
        bool isForwardOlder = await IsAncestorCommit(sourceRepo, forwardSha, backwardSha);
        bool isBackwardOlder = await IsAncestorCommit(sourceRepo, backwardSha, forwardSha);

        // Commits not comparable
        if (isBackwardOlder == isForwardOlder)
        {
            throw new Exception($"Failed to determine which commit of {sourceRepo} is older ({lastForwardFlow.VmrSha}, {lastBackflow.VmrSha})");
        };

        return isBackwardOlder ? lastForwardFlow : lastBackflow;
    }

    private async Task<Backflow?> GetLastBackflow(NativePath repoPath)
    {
        // Last backflow SHA comes from Version.Details.xml in the repo
        var content = await _fileSystem.ReadAllTextAsync(repoPath / VersionFiles.VersionDetailsXml);
        SourceDependency? source = _versionDetailsParser.ParseVersionDetailsXml(content).Source;
        if (source is null)
        {
            return null;
        }

        string lastBackflowVmrSha = source.Sha;
        string lastBackflowRepoSha = await BlameLineAsync(
            repoPath / VersionFiles.VersionDetailsXml,
            line => line.Contains(VersionDetailsParser.SourceElementName) && line.Contains(lastBackflowVmrSha));

        return new Backflow(lastBackflowVmrSha, lastBackflowRepoSha);
    }

    private async Task<ForwardFlow> GetLastForwardFlow(string mappingName)
    {
        IVersionedSourceComponent repoInVmr = _sourceManifest.Repositories.FirstOrDefault(r => r.Path == mappingName)
            ?? throw new ArgumentException($"No repository mapping named {mappingName} found");

        // Last forward flow SHAs come from source-manifest.json in the VMR
        string lastForwardRepoSha = repoInVmr.CommitSha;
        string lastForwardVmrSha = await BlameLineAsync(
            _vmrInfo.GetSourceManifestPath(),
            line => line.Contains(lastForwardRepoSha));

        return new ForwardFlow(lastForwardVmrSha, lastForwardRepoSha);
    }

    private async Task<bool> IsAncestorCommit(NativePath repoPath, string parent, string ancestor)
    {
        var result = await _processManager.ExecuteGit(repoPath, ["merge-base", "--is-ancestor", parent, ancestor]);

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            result.ThrowIfFailed($"Failed to determine which commit of {repoPath} is older ({parent}, {ancestor})");
        }

        return result.ExitCode == 0;
    }

    private async Task<string> BlameLineAsync(string filePath, Func<string, bool> isTargetLine)
    {
        using (var stream = _fileSystem.GetFileStream(filePath, FileMode.Open, FileAccess.Read))
        using (var reader = new StreamReader(stream))
        {
            string? line;
            int lineNumber = 1;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (isTargetLine(line))
                {
                    return await _localGitClient.BlameLineAsync(_fileSystem.GetDirectoryName(filePath)!, filePath, lineNumber);
                }

                lineNumber++;
            }
        }

        throw new Exception($"Failed to blame file {filePath} - no matching line found");
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

    private bool IsVmr(NativePath repoPath) => _vmrInfo.VmrPath.Equals(repoPath);
}

internal abstract record Codeflow(string SourceSha, string TargetSha)
{
    public abstract string RepoSha { get; init; }

    public abstract string VmrSha { get; init; }
}

internal record ForwardFlow(string VmrSha, string RepoSha) : Codeflow(RepoSha, VmrSha);

internal record Backflow(string VmrSha, string RepoSha) : Codeflow(VmrSha, RepoSha);
