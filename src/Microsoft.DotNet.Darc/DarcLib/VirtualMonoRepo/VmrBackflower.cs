// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrBackflower
{
    Task BackflowAsync(string repoName, string targetDirectory, IReadOnlyCollection<AdditionalRemote> additionalRemotes);
}

public class VmrBackflower : IVmrBackflower
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly ILocalGitRepo _localGitClient;
    private readonly IVmrPatchHandler _vmrPatchHandler;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrBackflower> _logger;

    public VmrBackflower(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        ILocalGitRepo localGitClient,
        IVmrPatchHandler vmrPatchHandler,
        IFileSystem fileSystem,
        ILogger<VmrBackflower> logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _localGitClient = localGitClient;
        _vmrPatchHandler = vmrPatchHandler;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task BackflowAsync(string mappingName, string targetDirectory, IReadOnlyCollection<AdditionalRemote> additionalRemotes)
    {
        IVersionedSourceComponent repo = _sourceManifest.Repositories.FirstOrDefault(r => r.Path == mappingName)
            ?? throw new ArgumentException($"No repository mapping named {mappingName} found");

        var repoSourceSha = repo.CommitSha;

        var vmrSourceSha = await GetShaOfLastSyncForRepo(repo);
        var vmrTargetSha = await _localGitClient.GetGitCommitAsync(_vmrInfo.VmrPath);

        if (vmrSourceSha == vmrTargetSha)
        {
            _logger.LogInformation("No changes to synchronize, {repo} was just synchronized into the VMR ({sha})", mappingName, vmrTargetSha);
            return;
        }

        _logger.LogInformation("Synchronizing {repo} from {repoSourceSha}", mappingName, repoSourceSha);
        _logger.LogDebug($"VMR range to be synchronized: {{sourceSha}} {VmrUpdater.Arrow} {{targetSha}}", vmrSourceSha, vmrTargetSha);
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
}
