// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public record AdditionalRemote(string Mapping, string RemoteUri);

public interface IRepositoryCloneManager
{
    /// <summary>
    /// Clones a target repo URI into a given directory and checks out a given ref.
    /// When clone is already present, it is re-used and we only fetch.
    /// When given remotes have already been fetched during this run, they are not fetched again.
    /// </summary>
    /// <param name="repoUri">Remote to fetch from</param>
    /// <param name="checkoutRef">Ref to check out at the end</param>
    /// <returns>Path to the clone</returns>
    Task<NativePath> PrepareCloneAsync(
        string repoUri,
        string checkoutRef,
        CancellationToken cancellationToken);

    /// <summary>
    /// Clones a repo in a target directory, fetches from given remotes and checks out a given ref.
    /// When clone is already present, it is re-used and only remotes are fetched.
    /// When given remotes have already been fetched during this run, they are not fetched again.
    /// </summary>
    /// <param name="mapping">VMR repo mapping to associate the clone with</param>
    /// <param name="remotes">Remotes to fetch from</param>
    /// <param name="checkoutRef">Ref to check out at the end</param>
    /// <returns>Path to the clone</returns>
    Task<NativePath> PrepareCloneAsync(
        SourceMapping mapping,
        IReadOnlyCollection<string> remotes,
        string checkoutRef,
        CancellationToken cancellationToken);

    /// <summary>
    /// Prepares a clone of a repository by fetching from given remotes one-by-one until all requested commits are available.
    /// </summary>
    /// <param name="mapping">Mapping that clone is associated with</param>
    /// <param name="remoteUris">Remotes to fetch one by one</param>
    /// <param name="requestedRefs">List of commits that </param>
    /// <param name="checkoutRef">Ref to check out at the end</param>
    /// <returns>Path to the clone</returns>
    Task<NativePath> PrepareCloneAsync(
        SourceMapping mapping,
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        CancellationToken cancellationToken);
}

/// <summary>
/// When we are synchronizing other repositories into the VMR, we need to temporarily clone them.
/// This class is responsible for managing the temporary clones so that:
///   - Previously cloned repositories are re-used
///   - Re-used clones pull newest updates once per run (the first time we use them)
/// </summary>
public class RepositoryCloneManager : IRepositoryCloneManager
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IGitRepoCloner _gitRepoCloner;
    private readonly ILocalGitClient _localGitRepo;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrPatchHandler> _logger;

    // Map of URI => dir name
    private readonly Dictionary<string, NativePath> _clones = new();

    // Repos we have already pulled updates for during this run
    private readonly List<string> _upToDateRepos = new();

    public RepositoryCloneManager(
        IVmrInfo vmrInfo,
        IGitRepoCloner gitRepoCloner,
        ILocalGitClient localGitRepo,
        IFileSystem fileSystem,
        ILogger<VmrPatchHandler> logger)
    {
        _vmrInfo = vmrInfo;
        _gitRepoCloner = gitRepoCloner;
        _localGitRepo = localGitRepo;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<NativePath> PrepareCloneAsync(
        SourceMapping mapping,
        IReadOnlyCollection<string> remoteUris,
        string checkoutRef,
        CancellationToken cancellationToken)
    {
        if (remoteUris.Count == 0)
        {
            throw new ArgumentException("No remote URIs provided to clone");
        }

        NativePath path = null!;
        foreach (string remoteUri in remoteUris)
        {
            // Path should be returned the same for all invocations
            // We checkout a default ref
            path = await PrepareCloneInternal(remoteUri, mapping.Name, cancellationToken);
        }

        await _localGitRepo.CheckoutAsync(path, checkoutRef);

        return path;
    }

    public async Task<NativePath> PrepareCloneAsync(
        string repoUri,
        string checkoutRef,
        CancellationToken cancellationToken)
    {
        // We store clones in directories named as a hash of the repo URI
        var cloneDir = StringUtils.GetXxHash64(repoUri);
        var path = await PrepareCloneInternal(repoUri, cloneDir, cancellationToken);
        await _localGitRepo.CheckoutAsync(path, checkoutRef);
        return path;
    }

    public async Task<NativePath> PrepareCloneAsync(
        SourceMapping mapping,
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
            path = await PrepareCloneInternal(remoteUri, mapping.Name, cancellationToken);
            bool missingCommit = false;

            // Verify that all requested commits are available
            foreach (string commit in refsToVerify.ToArray())
            {
                try
                {
                    string objectType = await _localGitRepo.GetObjectTypeAsync(path, commit);
                    if (objectType == "commit")
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
            throw new Exception($"Failed to find all requested refs ({string.Join(", ", requestedRefs)}) in {string.Join(", ", remoteUris)}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _localGitRepo.CheckoutAsync(path, checkoutRef);

        return path;
    }

    private async Task<NativePath> PrepareCloneInternal(string remoteUri, string dirName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_upToDateRepos.Contains(remoteUri))
        {
            return _clones[remoteUri];
        }

        var clonePath = _clones.TryGetValue(remoteUri, out var cachedPath)
            ? cachedPath
            : _vmrInfo.TmpPath / dirName;

        if (!_fileSystem.DirectoryExists(clonePath))
        {
            _logger.LogDebug("Cloning {repo} to {clonePath}", remoteUri, clonePath);
            await _gitRepoCloner.CloneNoCheckoutAsync(remoteUri, clonePath, null);
        }
        else
        {
            _logger.LogDebug("Clone of {repo} found in {clonePath}", remoteUri, clonePath);
            var remote = await _localGitRepo.AddRemoteIfMissingAsync(clonePath, remoteUri, cancellationToken);

            // We cannot do `fetch --all` as tokens might be needed but fetch +refs/heads/*:+refs/remotes/origin/* doesn't fetch new refs
            // So we need to call `remote update origin` to fetch everything
            await _localGitRepo.UpdateRemoteAsync(clonePath, remote, cancellationToken);
        }

        _upToDateRepos.Add(remoteUri);
        _clones[remoteUri] = clonePath;
        return clonePath;
    }
}
