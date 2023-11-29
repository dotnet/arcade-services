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
    Task BackflowAsync(
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
        _localGitClient = localGitClient;
        _versionDetailsParser = versionDetailsParser;
        _vmrPatchHandler = vmrPatchHandler;
        _processManager = processManager;
        _workBranchFactory = workBranchFactory;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task BackflowAsync(
        string mappingName,
        NativePath repoPath,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        var flowInfo = await GetLastFlowAsync(mappingName, repoPath);

        if (flowInfo is LastBackflow lastBackflow)
        {
            await SimpleDiffFlow(mappingName, repoPath, lastBackflow, cancellationToken);
        }
        else
        {
            await DeltaFlow(mappingName, (LastForwardFlow)flowInfo);
        }
    }

    private async Task SimpleDiffFlow(string mappingName, NativePath repoPath, LastBackflow lastFlow, CancellationToken cancellationToken)
    {
        var currentVmrSha = await _localGitClient.GetShaForRefAsync(_vmrInfo.VmrPath, Constants.HEAD);

        if (currentVmrSha == lastFlow.VmrSha)
        {
            _logger.LogInformation("No changes to synchronize, {repo} was already synchronized to {sha}", _vmrInfo.VmrPath, currentVmrSha);
            return;
        }

        _logger.LogInformation("Backflowing {sha} from VMR to {repoName}", currentVmrSha, mappingName);

        await _localGitClient.CheckoutAsync(repoPath, lastFlow.RepoSha);
        var shortShas = $"{Commit.GetShortSha(lastFlow.VmrSha)}-{Commit.GetShortSha(currentVmrSha)}";
        var branchName = $"backflow/{shortShas}";
        var patchName = _vmrInfo.TmpPath / (mappingName + "-backflow-" + shortShas + ".patch");

        // Ignore all submodules
        var ignoredPaths = _sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mappingName + '/'))
            .Select(s => s.Path.Substring(mappingName.Length + 1))
            .Select(VmrPatchHandler.GetExclusionRule)
            .ToList();

        List<VmrIngestionPatch> patches = await _vmrPatchHandler.CreatePatches(
            patchName,
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
            return;
        }

        var message = new StringBuilder();
        message.AppendLine($"{patches.Count} patch{(patches.Count == 1 ? null : "es")} were created:");

        var longestPath = patches.Max(p => p.Path.Length) + 4;

        foreach (var patch in patches)
        {
            message.AppendLine(patch.Path.PadRight(longestPath));
        }

        _logger.LogDebug(message.ToString());

        var prBanch = await _workBranchFactory.CreateWorkBranchAsync(repoPath, branchName);
        _logger.LogInformation("Created branch {branchName} in {repoDir}", branchName, repoPath);

        try
        {
            foreach (VmrIngestionPatch patch in patches)
            {
                await _vmrPatchHandler.ApplyPatch(patch, repoPath, cancellationToken);
            }
        }
        catch (Exception)
        {
            // TODO: Recursive fallback
            throw;
        }

        var commitMessage = $"""
            [VMR] Code backflow {shortShas}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await _localGitClient.CommitAsync(repoPath, commitMessage, allowEmpty: false, cancellationToken: cancellationToken);

        _logger.LogInformation("New branch {branch} with backflown code is ready in {repoDir}", branchName, repoPath);
    }

    private async Task DeltaFlow(string mappingName, LastForwardFlow lastFlow)
    {
        await Task.CompletedTask;
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
        bool isBackwardOlder = await IsOlderCommit(lastForwardFlow.VmrSha, lastBackflow.VmrSha);
        bool isForwardOlder = await IsOlderCommit(lastBackflow.VmrSha, lastForwardFlow.VmrSha);

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
        SourceDependency? source = _versionDetailsParser.ParseVersionDetailsXml(repoPath).Source;
        if (source is null)
        {
            return null;
        }

        string lastBackflowVmrSha = source.Sha;
        string lastBackflowRepoSha = await BlameLineAsync(
            repoPath / VersionFiles.VersionDetailsXml,
            line => line.Contains("Inflow") && line.Contains(lastBackflowVmrSha));

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

    private async Task<bool> IsOlderCommit(string firstSha, string secondSha)
    {
        var result = await _processManager.ExecuteGit(_vmrInfo.VmrPath, ["merge-base", "--is-ancestor", firstSha, secondSha]);

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            result.ThrowIfFailed($"Failed to determine which commit of {_vmrInfo.VmrPath} is older ({firstSha}, {secondSha})");
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

        throw new Exception($"Failed to blame file {filePath}");
    }
}
