// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public abstract class CloneManager
{
    // Map of URI => dir name
    private readonly Dictionary<string, NativePath> _clones = [];

    // Repos we have already pulled updates for during this run
    private readonly List<string> _upToDateRepos = [];

    private readonly IVmrInfo _vmrInfo;
    private readonly IGitRepoCloner _gitRepoCloner;
    private readonly ILocalGitClient _localGitRepo;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly ITelemetryRecorder _telemetryRecorder;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrPatchHandler> _logger;

    public CloneManager(
        IVmrInfo vmrInfo,
        IGitRepoCloner gitRepoCloner,
        ILocalGitClient localGitRepo,
        ILocalGitRepoFactory localGitRepoFactory,
        ITelemetryRecorder telemetryRecorder,
        IFileSystem fileSystem,
        ILogger<VmrPatchHandler> logger)
    {
        _vmrInfo = vmrInfo;
        _gitRepoCloner = gitRepoCloner;
        _localGitRepo = localGitRepo;
        _localGitRepoFactory = localGitRepoFactory;
        _telemetryRecorder = telemetryRecorder;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    protected async Task<ILocalGitRepo> PrepareCloneInternalAsync(
        string dirName,
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        CancellationToken cancellationToken)
    {
        if (remoteUris.Count == 0)
        {
            throw new ArgumentException("No remote URIs provided to clone");
        }

        // Rule out the null commit
        var refsToVerify = new HashSet<string>(requestedRefs.Where(sha => !Constants.EmptyGitObject.StartsWith(sha)));

        _logger.LogDebug("Fetching refs {refs} from {uris}",
            string.Join(", ", requestedRefs),
            string.Join(", ", remoteUris));

        NativePath path = null!;
        foreach (string remoteUri in remoteUris)
        {
            // Path should be returned the same for all invocations
            // We checkout a default ref
            path = await PrepareCloneInternal(remoteUri, dirName, cancellationToken);
            var missingCommit = false;

            // Verify that all requested commits are available
            foreach (string commit in refsToVerify.ToArray())
            {
                try
                {
                    var objectType = await _localGitRepo.GetObjectTypeAsync(path, commit);
                    if (objectType == GitObjectType.Commit)
                    {
                        refsToVerify.Remove(commit);
                    }
                }
                catch
                {
                    // Ref not found yet, let's try another remote
                    missingCommit = true;
                    break;
                }
            }

            if (!missingCommit)
            {
                _logger.LogDebug("All requested refs ({refs}) found in {repo}", string.Join(", ", requestedRefs), path);
                break;
            }
        }

        if (refsToVerify.Count > 0)
        {
            throw new NotFoundException($"Failed to find all requested refs ({string.Join(", ", requestedRefs)}) in {string.Join(", ", remoteUris)}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var repo = _localGitRepoFactory.Create(path);
        await repo.CheckoutAsync(checkoutRef);
        return repo;
    }

    protected async Task<NativePath> PrepareCloneInternal(string remoteUri, string dirName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_upToDateRepos.Contains(remoteUri))
        {
            var path = _clones[remoteUri];
            if (_fileSystem.DirectoryExists(path))
            {
                return path;
            }

            _upToDateRepos.Remove(remoteUri);
        }

        var clonePath = _clones.TryGetValue(remoteUri, out var cachedPath)
            ? cachedPath
            : GetClonePath(dirName);

        if (!_fileSystem.DirectoryExists(clonePath))
        {
            _logger.LogDebug("Cloning {repo} to {clonePath}", remoteUri, clonePath);

            using ITelemetryScope scope = _telemetryRecorder.RecordGitOperation(TrackedGitOperation.Clone, remoteUri);
            await _gitRepoCloner.CloneNoCheckoutAsync(remoteUri, clonePath, null);
            scope.SetSuccess();
        }
        else
        {
            _logger.LogDebug("Clone of {repo} found in {clonePath}", remoteUri, clonePath);
            var remote = await _localGitRepo.AddRemoteIfMissingAsync(clonePath, remoteUri, cancellationToken);

            // We cannot do `fetch --all` as tokens might be needed but fetch +refs/heads/*:+refs/remotes/origin/* doesn't fetch new refs
            // So we need to call `remote update origin` to fetch everything
            using ITelemetryScope scope = _telemetryRecorder.RecordGitOperation(TrackedGitOperation.Fetch, remoteUri);
            await _localGitRepo.UpdateRemoteAsync(clonePath, remote, cancellationToken);
            scope.SetSuccess();
        }

        _upToDateRepos.Add(remoteUri);
        _clones[remoteUri] = clonePath;
        return clonePath;
    }

    protected virtual NativePath GetClonePath(string dirName) => _vmrInfo.TmpPath / dirName;
}
