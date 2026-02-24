// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Maestro.Common.Telemetry;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface ICloneManager
{
    /// <summary>
    /// Registers a known local location that contains a clone.
    /// </summary>
    Task RegisterCloneAsync(NativePath localPath);
}

public abstract class CloneManager : ICloneManager
{
    // Map of URI => dir name
    protected readonly Dictionary<string, NativePath> _clones = [];

    // Repos we have already pulled updates for during this run
    private readonly List<string> _upToDateRepos = [];

    private readonly IVmrInfo _vmrInfo;
    private readonly IGitRepoCloner _gitRepoCloner;
    private readonly ILocalGitClient _localGitRepo;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly ITelemetryRecorder _telemetryRecorder;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;

    public CloneManager(
        IVmrInfo vmrInfo,
        IGitRepoCloner gitRepoCloner,
        ILocalGitClient localGitRepo,
        ILocalGitRepoFactory localGitRepoFactory,
        ITelemetryRecorder telemetryRecorder,
        IFileSystem fileSystem,
        ILogger logger)
    {
        _vmrInfo = vmrInfo;
        _gitRepoCloner = gitRepoCloner;
        _localGitRepo = localGitRepo;
        _localGitRepoFactory = localGitRepoFactory;
        _telemetryRecorder = telemetryRecorder;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task RegisterCloneAsync(NativePath localPath)
    {
        var remotes = await _localGitRepo.GetRemotesAsync(localPath);
        var branch = await _localGitRepo.GetCheckedOutBranchAsync(localPath);

        if (string.IsNullOrEmpty(branch))
        {
            throw new DarcException($"The provided path '{localPath}' does not appear to be a git repository.");
        }

        _clones[localPath] = localPath;

        foreach (var remote in remotes)
        {
            _clones[remote.Uri] = localPath;
        }
    }

    protected async Task<ILocalGitRepo> PrepareCloneInternalAsync(
        NativePath clonePath,
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default)
    {
        foreach (var uri in remoteUris)
        {
            _clones[uri] = clonePath;
        }

        return await PrepareCloneInternalAsync(_fileSystem.GetDirectoryName(clonePath.Path)!, remoteUris, requestedRefs, checkoutRef, resetToRemote, cancellationToken);
    }

    /// <summary>
    /// Prepares a clone of a repository by fetching from given remotes one-by-one until all requested commits are available.
    /// Then checks out the given ref.
    /// </summary>
    protected async Task<ILocalGitRepo> PrepareCloneInternalAsync(
        string dirName,
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default)
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
        bool cleanup = true;
        foreach (string remoteUri in remoteUris)
        {
            // Path should be returned the same for all invocations
            // We checkout a default ref
            path = await PrepareCloneInternal(remoteUri, dirName, cleanup, cancellationToken);
            cleanup = false;

            // Verify that all requested commits are available
            foreach (string gitRef in refsToVerify.ToArray())
            {
                var gitRefType = await _localGitRepo.GetRefType(path, gitRef, cancellationToken);
                if (gitRefType != GitObjectType.Unknown)
                {
                    refsToVerify.Remove(gitRef);

                    // Force-create the local branch to track the remote branch
                    if (gitRefType == GitObjectType.RemoteRef)
                    {
                        var remoteName = await _localGitRepo.AddRemoteIfMissingAsync(path, remoteUri, cancellationToken);
                        var result = await _localGitRepo.RunGitCommandAsync(
                            path,
                            ["branch", "-f", "--track", gitRef, $"{remoteName}/{gitRef}"],
                            cancellationToken);
                        result.ThrowIfFailed($"Couldn't create local branch for remote ref {gitRef} from {remoteName}");
                    }
                }
            }

            if (refsToVerify.Count == 0)
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

        try
        {
            await repo.CheckoutAsync(checkoutRef);
        }
        catch (ProcessFailedException e) when (e.Message.Contains("files would be overwritten by checkout"))
        {
            var result = await repo.RunGitCommandAsync(["clean", "-fdqx", "."], cancellationToken);
            result.ThrowIfFailed("Couldn't clean the repository");
            await repo.ForceCheckoutAsync(checkoutRef);
        }

        if (resetToRemote)
        {
            // get the upstream branch for the currently checked out branch
            var result = await _localGitRepo.RunGitCommandAsync(path, ["for-each-ref", "--format=%(upstream:short)", $"refs/heads/{checkoutRef}"], cancellationToken);
            result.ThrowIfFailed("Couldn't get upstream branch for the current branch");
            var upstream = result.StandardOutput.Trim();

            // Only reset if we have an upstream branch to reset to
            if (!string.IsNullOrEmpty(upstream))
            {
                // reset the branch to the remote one
                result = await _localGitRepo.RunGitCommandAsync(path, ["reset", "--hard", upstream], cancellationToken);
                result.ThrowIfFailed($"Couldn't reset to remote ref {upstream}");

                // also clean the repo
                result = await _localGitRepo.RunGitCommandAsync(path, ["clean", "-fdqx", "."], cancellationToken);
                result.ThrowIfFailed("Couldn't clean the repository");
            }
        }

        return repo;
    }

    /// <summary>
    /// Prepares a clone of the given remote URI in the given directory name (under tmp path).
    /// When clone is already present, it is re-used and we only fetch.
    /// When given remotes have already been fetched during this run, they are not fetched again.
    /// </summary>
    protected async Task<NativePath> PrepareCloneInternal(string remoteUri, string dirName, bool performCleanup, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        NativePath clonePath;

        if (_upToDateRepos.Contains(remoteUri))
        {
            var path = _clones[remoteUri];
            if (_fileSystem.DirectoryExists(path))
            {
                return path;
            }

            _upToDateRepos.Remove(remoteUri);
            clonePath = path;
        }
        else
        {
            clonePath = _clones.TryGetValue(remoteUri, out var cachedPath)
                ? cachedPath
                : GetClonePath(dirName);
        }

        if (!_fileSystem.DirectoryExists(clonePath))
        {
            _logger.LogDebug("Cloning {repo} to {clonePath}", remoteUri, clonePath);

            using ITelemetryScope scope = _telemetryRecorder.RecordGitOperation(TrackedGitOperation.Clone, remoteUri);
            await _gitRepoCloner.CloneNoCheckoutAsync(remoteUri, clonePath, null);
            scope.SetSuccess();
        }
        else
        {
            _logger.LogDebug("Clone of {repo} found in {clonePath}. Preparing for use...", remoteUri, clonePath);

            // We make sure the clone is clean and we re-clone if it's unusable
            if (performCleanup)
            {
                var result = await _localGitRepo.RunGitCommandAsync(clonePath, ["reset", "--hard"], cancellationToken);
                if (!result.Succeeded)
                {
                    _logger.LogWarning("Failed to clean up {clonePath}, re-cloning", clonePath);
                    _fileSystem.DeleteDirectory(clonePath, recursive: true);
                    return await PrepareCloneInternal(remoteUri, dirName, performCleanup: true, cancellationToken);
                }
            }

            string remote;

            try
            {
                remote = await _localGitRepo.AddRemoteIfMissingAsync(clonePath, remoteUri, cancellationToken);
            }
            catch (Exception e) when (e.Message.Contains("fatal: not a git repository"))
            {
                _logger.LogWarning("Clone at {clonePath} is not a git repository, re-cloning", clonePath);
                _fileSystem.DeleteDirectory(clonePath, recursive: true);
                return await PrepareCloneInternal(remoteUri, dirName, performCleanup: true, cancellationToken);
            }

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
