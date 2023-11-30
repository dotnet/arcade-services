// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

internal abstract record LastCodeflow(bool IsBackflow, string SourceSha, string TargetSha);
internal record LastForwardFlow(string VmrSha, string RepoSha) : LastCodeflow(false, RepoSha, VmrSha);
internal record LastBackflow(string VmrSha, string RepoSha) : LastCodeflow(true, VmrSha, RepoSha);

public interface IVmrBackflowManager
{
    // TODO: Doc
    Task<string?> BackflowAsync(
        string mapping,
        NativePath targetDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken);
}

/// <summary>
/// This class is responsible for taking changes done to a repo in the VMR and backflowing them into the repo.
/// It only makes patches/changes locally, no other effects are done.
/// </summary>
public class VmrBackflowManager : IVmrBackflowManager
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
    private readonly ILogger<VmrBackflowManager> _logger;

    public VmrBackflowManager(
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
        ILogger<VmrBackflowManager> logger)
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

    public async Task<string?> BackflowAsync(
        string mappingName,
        NativePath repoPath,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        await _dependencyTracker.InitializeSourceMappings();

        var lastFlow = await GetLastFlowAsync(mappingName, repoPath);

        var currentVmrSha = await _localGitClient.GetShaForRefAsync(_vmrInfo.VmrPath, Constants.HEAD);
        if (currentVmrSha == lastFlow.TargetSha || currentVmrSha == lastFlow.SourceSha)
        {
            _logger.LogInformation("No changes to synchronize, {repo} was already synchronized to {sha}", _vmrInfo.VmrPath, currentVmrSha);
            return null;
        }

        _logger.LogInformation("Flowing {sha} from VMR to {repoName} (backflow after {type})",
            currentVmrSha,
            mappingName,
            lastFlow is LastBackflow ? "backflow" : "forward flow");

        string? branchName;
        if (lastFlow is LastBackflow lastBackflow)
        {
            branchName = await SimpleDiffFlow(mappingName, currentVmrSha, repoPath, lastBackflow, cancellationToken);
        }
        else
        {
            branchName = await DeltaFlow(mappingName, currentVmrSha, repoPath, (LastForwardFlow)lastFlow, cancellationToken);
        }

        if (branchName == null)
        {
            _logger.LogInformation("No changes to synchronize, {repo} was already synchronized to {sha}", _vmrInfo.VmrPath, currentVmrSha);
            return null;
        }

        await UpdateVersionDetailsXml(repoPath, currentVmrSha, cancellationToken);

        return branchName;
    }

    private async Task<string?> SimpleDiffFlow(
        string mappingName,
        string currentVmrSha,
        NativePath repoPath,
        LastBackflow lastFlow,
        CancellationToken cancellationToken)
    {
        await _localGitClient.CheckoutAsync(repoPath, lastFlow.RepoSha);

        // Ignore all submodules
        var ignoredPaths = _sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mappingName + '/'))
            .Select(s => s.Path.Substring(mappingName.Length + 1))
            .Select(VmrPatchHandler.GetExclusionRule)
            .ToList();

        var shortShas = $"{Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentVmrSha)}";

        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            _vmrInfo.TmpPath / (mappingName + "-backflow-" + shortShas + ".patch"),
            lastFlow.VmrSha,
            currentVmrSha,
            VmrInfo.GetRelativeRepoSourcesPath(mappingName),
            filters: ignoredPaths,
            relativePaths: true,
            workingDir: _vmrInfo.VmrPath,
            applicationPath: null,
            cancellationToken);

        if (patches.Count == 0)
        {
            _logger.LogInformation("There are no new changes for {mappingName} between {sha1} and {sha2}",
                mappingName,
                lastFlow.VmrSha,
                currentVmrSha);
            return null;
        }

        var message = new StringBuilder();
        message.AppendLine($"{patches.Count} patch{(patches.Count == 1 ? null : "es")} were created:");

        var longestPath = patches.Max(p => p.Path.Length) + 4;

        foreach (var patch in patches)
        {
            message.AppendLine(patch.Path.PadRight(longestPath));
        }

        _logger.LogDebug(message.ToString());

        var branchName = $"backflow/{shortShas}";
        await _workBranchFactory.CreateWorkBranchAsync(repoPath, branchName);

        try
        {
            foreach (VmrIngestionPatch patch in patches)
            {
                await _vmrPatchHandler.ApplyPatch(patch, repoPath, cancellationToken);
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
            [VMR] Code backflow {shortShas}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await _localGitClient.CommitAsync(repoPath, commitMessage, allowEmpty: false, cancellationToken: cancellationToken);

        _logger.LogInformation("New branch {branch} with backflown code is ready in {repoDir}", branchName, repoPath);

        return branchName;
    }

    private async Task<string?> DeltaFlow(
        string mappingName,
        string currentVmrSha,
        NativePath repoPath,
        LastForwardFlow lastFlow,
        CancellationToken cancellationToken)
    {
        var mapping = _dependencyTracker.Mappings.First(m => m.Name == mappingName);

        await _localGitClient.CheckoutAsync(repoPath, lastFlow.RepoSha);
        var shortShas = $"{Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentVmrSha)}";
        var patchName = _vmrInfo.TmpPath / (mappingName + "-backflow-" + shortShas + ".patch");

        // Let's create a patch representing files in the VMR so that we can apply it to the repo
        // TODO: This might be an extra work - we could possibly just copy the contents of the VMR folder
        List<GitSubmoduleInfo> submodules = await _localGitClient.GetGitSubmodulesAsync(repoPath, lastFlow.RepoSha);
        var submoduleExclusions = submodules
            .Select(s => s.Path)
            .Select(VmrPatchHandler.GetExclusionRule)
            .ToList();

        var patches = await _vmrPatchHandler.CreatePatches(
            patchName,
            Constants.EmptyGitObject,
            currentVmrSha,
            _vmrInfo.GetRepoSourcesPath(mapping),
            submoduleExclusions,
            relativePaths: true,
            _vmrInfo.GetRepoSourcesPath(mapping),
            applicationPath: null,
            cancellationToken);

        if (patches.Count == 0)
        {
            _logger.LogInformation("There are no new changes for {mappingName} between {sha1} and {sha2}",
                mappingName,
                lastFlow.VmrSha,
                currentVmrSha);
            return null;
        }

        var branchName = $"backflow/{shortShas}";
        var prBanch = await _workBranchFactory.CreateWorkBranchAsync(repoPath, branchName);
        _logger.LogInformation("Created temporary branch {branchName} in {repoDir}", branchName, repoPath);

        // We will remove everything and replace it with current contents of the VMR
        // The repo is a superset of the VMR files so we only need to remove non-cloaked files
        List<string> filters =
        [
            .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule),
            .. submoduleExclusions,
        ];

        var result = await _processManager.ExecuteGit(repoPath, ["rm", "-r", "-q", "--", .. filters]);
        result.ThrowIfFailed($"Failed to remove files from {repoPath} to prepare forward flow");

        // Now we insert the VMR files
        foreach (var patch in patches)
        {
            // TODO: Handle exceptions
            await _vmrPatchHandler.ApplyPatch(patch, repoPath, cancellationToken);
            // TODO: Discard patches
        }

        // TODO: Commit message
        await _localGitClient.CommitAsync(repoPath, $"TODO - backflow of {shortShas}", false, cancellationToken: cancellationToken);
        await _localGitClient.ResetWorkingTree(repoPath);

        return branchName;
    }

    private async Task<LastCodeflow> GetLastFlowAsync(string mappingName, NativePath repoPath)
    {
        LastForwardFlow lastForwardFlow = await GetLastForwardFlow(mappingName);
        LastBackflow? lastBackflow = await GetLastBackflow(repoPath);

        if (lastBackflow is null)
        {
            return lastForwardFlow;
        }

        string objectType1 = await _localGitClient.GetObjectTypeAsync(_vmrInfo.VmrPath, lastBackflow.VmrSha);
        string objectType2 = await _localGitClient.GetObjectTypeAsync(_vmrInfo.VmrPath, lastForwardFlow.VmrSha);

        if (objectType1 != "commit" || objectType2 != "commit")
        {
            throw new Exception($"Failed to find commits {lastBackflow.VmrSha}, {lastForwardFlow.VmrSha} in {_vmrInfo.VmrPath}");
        }

        // Let's determine the last flow by comparing source commit of last backflow with target commit of last forward flow
        bool isForwardOlder = await IsAncestorCommit(lastForwardFlow.VmrSha, lastBackflow.VmrSha);
        bool isBackwardOlder = await IsAncestorCommit(lastBackflow.VmrSha, lastForwardFlow.VmrSha);

        // Commits not comparable
        if (isBackwardOlder == isForwardOlder)
        {
            throw new Exception($"Failed to determine which commit of {_vmrInfo.VmrPath} is older ({lastForwardFlow.VmrSha}, {lastBackflow.VmrSha})");
        };

        return isBackwardOlder ? lastForwardFlow : lastBackflow;
    }

    private async Task<LastBackflow?> GetLastBackflow(NativePath repoPath)
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

        return new LastBackflow(lastBackflowVmrSha, lastBackflowRepoSha);
    }

    private async Task<LastForwardFlow> GetLastForwardFlow(string mappingName)
    {
        IVersionedSourceComponent repoInVmr = _sourceManifest.Repositories.FirstOrDefault(r => r.Path == mappingName)
            ?? throw new ArgumentException($"No repository mapping named {mappingName} found");

        // Last forward flow SHAs come from source-manifest.json in the VMR
        string lastForwardRepoSha = repoInVmr.CommitSha;
        string lastForwardVmrSha = await BlameLineAsync(
            _vmrInfo.GetSourceManifestPath(),
            line => line.Contains(lastForwardRepoSha));

        return new LastForwardFlow(lastForwardVmrSha, lastForwardRepoSha);
    }

    private async Task<bool> IsAncestorCommit(string parent, string ancestor)
    {
        var result = await _processManager.ExecuteGit(_vmrInfo.VmrPath, ["merge-base", "--is-ancestor", parent, ancestor]);

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            result.ThrowIfFailed($"Failed to determine which commit of {_vmrInfo.VmrPath} is older ({parent}, {ancestor})");
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
}
