// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
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
    Task<ILocalGitRepo> PrepareCloneAsync(
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
    Task<ILocalGitRepo> PrepareCloneAsync(
        SourceMapping mapping,
        IReadOnlyCollection<string> remotes,
        string checkoutRef,
        CancellationToken cancellationToken);

    /// <summary>
    /// Prepares a clone of a repository by fetching from given remotes one-by-one until all requested commits are available.
    /// </summary>
    /// <param name="mapping">Mapping that clone is associated with</param>
    /// <param name="remoteUris">Remotes to fetch one by one</param>
    /// <param name="requestedRefs">List of refs that need to be available</param>
    /// <param name="checkoutRef">Ref to check out at the end</param>
    /// <returns>Path to the clone</returns>
    Task<ILocalGitRepo> PrepareCloneAsync(
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
public class RepositoryCloneManager : CloneManager, IRepositoryCloneManager
{
    private readonly ILocalGitRepoFactory _localGitRepoFactory;

    public RepositoryCloneManager(
        IVmrInfo vmrInfo,
        IGitRepoCloner gitRepoCloner,
        ILocalGitClient localGitRepo,
        ILocalGitRepoFactory localGitRepoFactory,
        ITelemetryRecorder telemetryRecorder,
        IFileSystem fileSystem,
        ILogger<VmrPatchHandler> logger)
        : base(vmrInfo, gitRepoCloner, localGitRepo, localGitRepoFactory, telemetryRecorder, fileSystem, logger)
    {
        _localGitRepoFactory = localGitRepoFactory;
    }

    public async Task<ILocalGitRepo> PrepareCloneAsync(
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

        var repo = _localGitRepoFactory.Create(path);
        await repo.CheckoutAsync(checkoutRef);
        return repo;
    }

    public async Task<ILocalGitRepo> PrepareCloneAsync(
        string repoUri,
        string checkoutRef,
        CancellationToken cancellationToken)
    {
        // We store clones in directories named as a hash of the repo URI
        var cloneDir = StringUtils.GetXxHash64(repoUri);
        var path = await PrepareCloneInternal(repoUri, cloneDir, cancellationToken);
        var repo = _localGitRepoFactory.Create(path);
        await repo.CheckoutAsync(checkoutRef);
        return repo;
    }

    public Task<ILocalGitRepo> PrepareCloneAsync(
        SourceMapping mapping,
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        CancellationToken cancellationToken)
        => PrepareCloneInternalAsync(mapping.Name, remoteUris, requestedRefs, checkoutRef, cancellationToken);
}
