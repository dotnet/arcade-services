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
        string shaToFlow,
        CancellationToken cancellationToken);
}

/// <summary>
/// This class is responsible for taking changes done to a repo in the VMR and backflowing them into the repo.
/// It only makes patches/changes locally, no other effects are done.
/// </summary>
public class VmrCodeflower : IVmrCodeflower
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

    public VmrCodeflower(
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

    public async Task<string?> FlowCodeAsync(
        NativePath sourceRepo,
        NativePath targetRepo,
        string mappingName,
        string shaToFlow,
        CancellationToken cancellationToken)
    {
        await _dependencyTracker.InitializeSourceMappings();
        var mapping = _dependencyTracker.Mappings.First(m => m.Name == mappingName);
        await _localGitClient.CheckoutAsync(sourceRepo, shaToFlow);

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

    private async Task<string?> BackflowAsync(
        SourceMapping mapping,
        string shaToFlow,
        NativePath targetRepo,
        CancellationToken cancellationToken)
    {
        LastCodeflow lastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);

        var branchName = lastFlow is LastBackflow
            ? await SameDirectionFlowAsync(mapping, shaToFlow, targetRepo, lastFlow, cancellationToken)
            : await OppositeDirectionFlowAsync(mapping, shaToFlow, targetRepo, lastFlow, cancellationToken);

        await UpdateVersionDetailsXml(targetRepo, shaToFlow, cancellationToken);

        return branchName;
    }

    private async Task<string?> ForwardFlowAsync(
        SourceMapping mapping,
        string shaToFlow,
        NativePath sourceRepo,
        CancellationToken cancellationToken)
    {
        LastCodeflow lastFlow = await GetLastFlowAsync(mapping, sourceRepo, currentIsBackflow: false);

        return lastFlow is LastBackflow
            ? await SameDirectionFlowAsync(mapping, shaToFlow, sourceRepo, lastFlow, cancellationToken)
            : await OppositeDirectionFlowAsync(mapping, shaToFlow, sourceRepo, lastFlow, cancellationToken);
    }

    private Task<string?> SameDirectionFlowAsync(
        SourceMapping mapping,
        string shaToFlow,
        NativePath repoPath,
        LastCodeflow lastFlow,
        CancellationToken cancellationToken) => throw new NotImplementedException();

    private Task<string?> OppositeDirectionFlowAsync(
        SourceMapping mapping,
        string shaToFlow,
        NativePath repoPath,
        LastCodeflow lastFlow,
        CancellationToken cancellationToken) => throw new NotImplementedException();

    private async Task<LastCodeflow> GetLastFlowAsync(SourceMapping mapping, NativePath repoPath, bool currentIsBackflow)
    {
        LastForwardFlow lastForwardFlow = await GetLastForwardFlow(mapping.Name);
        LastBackflow? lastBackflow = await GetLastBackflow(repoPath);

        if (lastBackflow is null)
        {
            return lastForwardFlow;
        }

        (string BackwardSha, string ForwardSha) commitsToCompare = currentIsBackflow
            ? (lastBackflow.VmrSha, lastForwardFlow.VmrSha)
            : (lastBackflow.RepoSha, lastForwardFlow.RepoSha);

        string objectType1 = await _localGitClient.GetObjectTypeAsync(_vmrInfo.VmrPath, commitsToCompare.BackwardSha);
        string objectType2 = await _localGitClient.GetObjectTypeAsync(_vmrInfo.VmrPath, commitsToCompare.ForwardSha);

        if (objectType1 != "commit" || objectType2 != "commit")
        {
            throw new Exception($"Failed to find commits {lastBackflow.VmrSha}, {lastForwardFlow.VmrSha} in {_vmrInfo.VmrPath}");
        }

        // Let's determine the last flow by comparing source commit of last backflow with target commit of last forward flow
        bool isForwardOlder = await IsAncestorCommit(commitsToCompare.ForwardSha, commitsToCompare.BackwardSha);
        bool isBackwardOlder = await IsAncestorCommit(commitsToCompare.BackwardSha, commitsToCompare.ForwardSha);

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

    private bool IsVmr(NativePath repoPath) => _vmrInfo.VmrPath == repoPath;
}
