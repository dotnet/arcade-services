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
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrBackflower
{
    Task<List<VmrIngestionPatch>> CreateBackflowPatchesAsync(string mappingName, CancellationToken cancellationToken);

    Task BackflowAsync(
        BackflowAction action,
        string repoName,
        NativePath targetDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken);
}

public enum BackflowAction
{
    CreatePatches,
    ApplyPatches,
}

/// <summary>
/// This class is responsible for taking changes done to a repo in the VMR and backflowing them into the repo.
/// It only makes patches/changes locally, no other effects are done.
/// </summary>
public class CodeBackflower : IVmrBackflower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly ILocalGitClient _localGitClient;
    private readonly IVmrPatchHandler _vmrPatchHandler;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<CodeBackflower> _logger;

    public CodeBackflower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        ILocalGitClient localGitClient,
        IVmrPatchHandler vmrPatchHandler,
        IWorkBranchFactory workBranchFactory,
        IFileSystem fileSystem,
        ILogger<CodeBackflower> logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _localGitClient = localGitClient;
        _vmrPatchHandler = vmrPatchHandler;
        _workBranchFactory = workBranchFactory;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<List<VmrIngestionPatch>> CreateBackflowPatchesAsync(string mappingName, CancellationToken cancellationToken)
    {
        (string vmrSourceSha, string vmrTargetSha, IVersionedSourceComponent repo) = await GetMappingInformation(mappingName);
        return await CreateBackflowPatchesAsync(repo, vmrSourceSha, vmrTargetSha, cancellationToken);
    }

    public async Task BackflowAsync(
        BackflowAction action,
        string mappingName,
        NativePath repoDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        (string vmrSourceSha, string vmrTargetSha, IVersionedSourceComponent repo) = await GetMappingInformation(mappingName);

        var patches = await CreateBackflowPatchesAsync(repo, vmrSourceSha, vmrTargetSha, cancellationToken);
        if (patches.Count == 0)
        {
            return;
        }

        // When we only care about patch creation, we print the paths of the patches we have created and return
        if (action == BackflowAction.CreatePatches)
        {
            var message = new StringBuilder();
            message.AppendLine($"{patches.Count} patch{(patches.Count == 1 ? null : "es")} were created:");

            var longestPath = patches.Max(p => p.Path.Length) + 4;

            foreach (var patch in patches)
            {
                message.Append(patch.Path.PadRight(longestPath));
                message.AppendLine(patch.ApplicationPath ?? string.Empty);
            }

            Console.WriteLine(message);
            return;
        }

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

        _logger.LogInformation("New branch {branch} with backflown code is ready in {repoDir}", branchName, repoDirectory);
    }

    public async Task<List<VmrIngestionPatch>> CreateBackflowPatchesAsync(
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
    }

    private async Task<string> GetShaOfLastSyncForRepo(IVersionedSourceComponent repo)
    {
        var manifestPath = _vmrInfo.GetSourceManifestPath();

        using (var stream = _fileSystem.GetFileStream(manifestPath, FileMode.Open, FileAccess.Read))
        using (var reader = new StreamReader(stream))
        {
            string? line;
            int lineNumber = 1;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.Contains(repo.CommitSha))
                {
                    return await _localGitClient.BlameLineAsync(_vmrInfo.VmrPath, manifestPath, lineNumber);
                }

                lineNumber++;
            }
        }

        throw new Exception($"Failed to find {repo.Path}'s revision {repo.CommitSha} in {manifestPath}");
    }

    private async Task<(string VmrSourceSha, string VmrTargetSha, IVersionedSourceComponent Repo)> GetMappingInformation(string mappingName)
    {
        IVersionedSourceComponent repo = _sourceManifest.Repositories.FirstOrDefault(r => r.Path == mappingName)
            ?? throw new ArgumentException($"No repository mapping named {mappingName} found");

        string vmrSourceSha = await GetShaOfLastSyncForRepo(repo);
        string vmrTargetSha = await _localGitClient.GetGitCommitAsync(_vmrInfo.VmrPath);

        return (vmrSourceSha, vmrTargetSha, repo);
    }
}
