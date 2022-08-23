// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class VmrInitializer : VmrManagerBase, IVmrInitializer
{
    // Message shown when initializing an individual repo for the first time
    private const string InitializationCommitMessage =
        """
        [{name}] Initial pull of the individual repository ({newShaShort})

        Original commit: {remote}/commit/{newSha}
        """;

    private readonly ILogger<VmrUpdater> _logger;

    public VmrInitializer(
        IProcessManager processManager,
        IRemoteFactory remoteFactory,
        ILogger<VmrUpdater> logger,
        IReadOnlyCollection<SourceMapping> mappings,
        string vmrPath,
        string tmpPath)
        : base(processManager, remoteFactory, logger, mappings, vmrPath, tmpPath)
    {
        _logger = logger;
    }

    public async Task InitializeVmr(SourceMapping mapping, string? targetRevision, CancellationToken cancellationToken)
    {
        if (File.Exists(Path.Combine(SourcesPath, $".{mapping.Name}")))
        {
            throw new EmptySyncException($"Repository {mapping.Name} already exists");
        }

        _logger.LogInformation("Initializing {name}", mapping.Name);

        string clonePath = await CloneOrPull(mapping);
        string patchPath = GetPatchFilePath(mapping);

        cancellationToken.ThrowIfCancellationRequested();

        using var clone = new Repository(clonePath);
        var commit = GetCommit(clone, (targetRevision is null || targetRevision == HEAD) ? null : targetRevision);

        await CreatePatch(mapping, clonePath, Constants.EmptyGitObject, commit.Id.Sha, patchPath);
        cancellationToken.ThrowIfCancellationRequested();
        await ApplyPatch(mapping, patchPath);
        await TagRepo(mapping, commit.Id.Sha);

        var description = PrepareCommitMessage(InitializationCommitMessage, mapping, null, commit.Id.Sha, null);

        // Commit but do not add files (they were added to index directly)
        cancellationToken.ThrowIfCancellationRequested();
        Commit(description, DotnetBotCommitSignature);
    }
}
