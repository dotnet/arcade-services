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

internal record FlowInfo(string SourceSha, string TargetSha);
internal record CodeflowInfo(bool IsBackflow, FlowInfo LastForwardFlow, FlowInfo? LastBackflow);

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
        var flowInfo = await GetLastFlowInformationAsync(mappingName, repoPath);

        if (flowInfo.IsBackflow)
        {
            await SimpleDiffFlow(mappingName, flowInfo);
        }
        else
        {
            await DeltaFlow(mappingName, flowInfo);
        }


        /*

        var patches = await CreateBackflowPatchesAsync(repo, vmrSourceSha, vmrTargetSha, cancellationToken);
        if (patches.Count == 0)
        {
            return;
        }

        var message = new StringBuilder();
        message.AppendLine($"{patches.Count} patch{(patches.Count == 1 ? null : "es")} were created:");

        var longestPath = patches.Max(p => p.Path.Length) + 4;

        foreach (var patch in patches)
        {
            message.Append(patch.Path.PadRight(longestPath));
            message.AppendLine(patch.ApplicationPath ?? string.Empty);
        }

        _logger.LogDebug(message.ToString());

        // Let's apply the patches onto the target repo
        _logger.LogInformation("Synchronizing {repo} from {repoSourceSha}", mappingName, repo.CommitSha);
        _logger.LogDebug($"VMR range to be synchronized: {{sourceSha}} {Constants.Arrow} {{targetSha}}", vmrSourceSha, vmrTargetSha);

        var branchName = $"backflow/{Commit.GetShortSha(vmrSourceSha)}-{Commit.GetShortSha(vmrTargetSha)}";

        string[] remotes = additionalRemotes
            .Where(r => r.Mapping == mappingName)
            .Select(r => r.RemoteUri)
            .Append(repo.RemoteUri)
            .ToArray();

        try
        {
            await _localGitClient.CheckoutAsync(repoDirectory, repo.CommitSha);
        }
        catch
        {
            _logger.LogInformation("Failed to checkout {sha} in {repo}, will fetch from all remotes and try again...", repo.CommitSha, repoDirectory);

            foreach (var remoteUri in remotes)
            {
                var remoteName = await _localGitClient.AddRemoteIfMissingAsync(repoDirectory, remoteUri, cancellationToken);
                await _localGitClient.UpdateRemoteAsync(repoDirectory, remoteName, cancellationToken);
            }

            await _localGitClient.CheckoutAsync(repoDirectory, repo.CommitSha);
        }

        cancellationToken.ThrowIfCancellationRequested();
        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(repoDirectory, branchName);

        _logger.LogInformation("Created branch {branchName} in {repoDir}", branchName, repoDirectory);

        foreach (var patch in patches)
        {
            await _vmrPatchHandler.ApplyPatch(patch, repoDirectory, cancellationToken);
        }

        var commitMessage = $"""
            [VMR] Code backflow {Commit.GetShortSha(vmrSourceSha)}{Constants.Arrow}{Commit.GetShortSha(vmrTargetSha)}

            {Constants.AUTOMATION_COMMIT_TAG}
            """;

        await _localGitClient.CommitAsync(repoDirectory, commitMessage, allowEmpty: false, ((string, string)?)null, cancellationToken: cancellationToken);

        _logger.LogInformation("New branch {branch} with backflown code is ready in {repoDir}", branchName, repoDirectory);*/
    }

    private async Task SimpleDiffFlow(string mappingName, CodeflowInfo flowInfo)
    {
        var currentVmrSha = await _localGitClient.GetShaForRefAsync(_vmrInfo.VmrPath, Constants.HEAD);

        if (currentVmrSha == flowInfo.LastBackflow?.SourceSha)
        {
            _logger.LogInformation("No changes to synchronize, {repo} was already synchronized to {sha}", _vmrInfo.VmrPath, currentVmrSha);
            return;
        }

        _logger.LogInformation("Backflowing {sha} from VMR to {repoName}", currentVmrSha, mappingName);


    }

    private async Task DeltaFlow(string mappingName, CodeflowInfo flowInfo)
    {
        await Task.CompletedTask;
    }

    private async Task<CodeflowInfo> GetLastFlowInformationAsync(string mappingName, NativePath repoPath)
    {
        IVersionedSourceComponent repoInVmr = _sourceManifest.Repositories.FirstOrDefault(r => r.Path == mappingName)
            ?? throw new ArgumentException($"No repository mapping named {mappingName} found");

        // Last forward flow SHAs come from source-manifest.json in the VMR
        string lastForwardRepoSha = repoInVmr.CommitSha;
        string lastForwardVmrSha = await BlameLineAsync(
            _vmrInfo.GetSourceManifestPath(),
            line => line.Contains(lastForwardRepoSha));

        // Last backflow SHA comes from Version.Details.xml in the repo
        VmrCodeflow? codeflowInformation = _versionDetailsParser.ParseVersionDetailsXml(repoPath).VmrCodeflow;
        if (codeflowInformation is null)
        {
            return new CodeflowInfo(
                IsBackflow: true,
                LastForwardFlow: new FlowInfo(lastForwardRepoSha, lastForwardVmrSha),
                LastBackflow: null);
        }

        string lastBackflowVmrSha = codeflowInformation.Inflow.Sha;
        string lastBackflowRepoSha = await BlameLineAsync(
            repoPath / VersionFiles.VersionDetailsXml,
            line => line.Contains("Inflow") && line.Contains(lastBackflowVmrSha));

        string objectType1 = await _localGitClient.GetObjectTypeAsync(_vmrInfo.VmrPath, lastForwardVmrSha);
        string objectType2 = await _localGitClient.GetObjectTypeAsync(_vmrInfo.VmrPath, lastForwardVmrSha);

        if (objectType1 != "commit" || objectType2 != "commit")
        {
            throw new Exception($"Failed to find commits {lastBackflowVmrSha}, {lastForwardVmrSha} in {_vmrInfo.VmrPath}");
        }

        bool isBackwardOlder = await IsOlderCommit(lastForwardVmrSha, lastBackflowVmrSha);
        bool isForwardOlder = await IsOlderCommit(lastBackflowVmrSha, lastForwardVmrSha);

        if (isBackwardOlder == isForwardOlder)
        {
            throw new Exception($"Failed to determine which commit of {_vmrInfo.VmrPath} is older ({lastForwardVmrSha}, {lastBackflowVmrSha})");
        };

        // Source commit of last backflow is older than target commit of last forward flow => forward flow was last
        return new CodeflowInfo(
            IsBackflow: true,
            LastForwardFlow: new FlowInfo(lastForwardRepoSha, lastForwardVmrSha),
            LastBackflow: new FlowInfo(lastBackflowVmrSha, lastForwardRepoSha));
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

    /*public async Task<List<VmrIngestionPatch>> CreateBackflowPatchesAsync(
        IVersionedSourceComponent repo,
        string vmrSourceSha,
        string vmrTargetSha,
        CancellationToken cancellationToken)
    {
        string mappingName = repo.Path;

        if (_vmrPatchHandler.GetVmrPatches(mappingName).Any())
        {
            throw new InvalidOperationException($"Cannot backflow commit that contains VMR patches");
        }

        if (vmrSourceSha == vmrTargetSha)
        {
            _logger.LogInformation("No changes to synchronize, {repo} was just synchronized into the VMR ({sha})", mappingName, vmrTargetSha);
            return new();
        }

        _logger.LogDebug($"VMR range to be synchronized: {{sourceSha}} {Constants.Arrow} {{targetSha}}", vmrSourceSha, vmrTargetSha);

        var patchName = $"{Commit.GetShortSha(vmrSourceSha)}-{Commit.GetShortSha(vmrTargetSha)}";
        var workBranchName = $"backflow/" + patchName;
        patchName = $"{mappingName}.{patchName}.patch";

        // Ignore all submodules
        List<string> ignoredPaths = _sourceManifest.Submodules
            .Where(s => s.Path.StartsWith(mappingName + '/'))
            .Select(s => s.Path.Substring(mappingName.Length + 1))
            .Select(p => $":(exclude,glob,attr:!{VmrInfo.KeepAttribute}){p}")
            .ToList();

        // Create patches
        var patches = await _vmrPatchHandler.CreatePatches(
            _vmrInfo.TmpPath / patchName,
            vmrSourceSha,
            vmrTargetSha,
            path: null,
            ignoredPaths,
            relativePaths: true, // Relative paths so that we can apply the patch on repo's root
            workingDir: _vmrInfo.GetRepoSourcesPath(mappingName),
            applicationPath: null, // We will apply this onto the repo root
            cancellationToken);

        if (patches.All(p => _fileSystem.GetFileInfo(p.Path).Length == 0))
        {
            _logger.LogInformation($"There are no new changes between the VMR and {mappingName}");
            return new();
        }

        return patches;
    }*/
}
