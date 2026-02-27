// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Common.Telemetry;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public record AdditionalRemote(string Mapping, string RemoteUri);

public interface IRepositoryCloneManager : ICloneManager
{
    /// <summary>
    /// Clones a target repo URI into a given directory and checks out a given ref.
    /// When clone is already present, it is re-used and we only fetch.
    /// When given remotes have already been fetched during this run, they are not fetched again.
    /// </summary>
    /// <param name="repoUri">Remote to fetch from</param>
    /// <param name="checkoutRef">Ref to check out at the end</param>
    /// <param name="resetToRemote">Whether to reset the branch to the remote one</param>
    /// <returns>Path to the clone</returns>
    Task<ILocalGitRepo> PrepareCloneAsync(
        string repoUri,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clones a repo in a target directory, fetches from given remotes and checks out a given ref.
    /// When clone is already present, it is re-used and only remotes are fetched.
    /// When given remotes have already been fetched during this run, they are not fetched again.
    /// </summary>
    /// <param name="mapping">VMR repo mapping to associate the clone with</param>
    /// <param name="remotes">Remotes to fetch from</param>
    /// <param name="checkoutRef">Ref to check out at the end</param>
    /// <param name="resetToRemote">Whether to reset the branch to the remote one</param>
    /// <returns>Path to the clone</returns>
    Task<ILocalGitRepo> PrepareCloneAsync(
        SourceMapping mapping,
        IReadOnlyCollection<string> remotes,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares a clone of a repository by fetching from given remotes one-by-one until all requested commits are available.
    /// Then checks out the given ref.
    /// </summary>
    /// <param name="mapping">Mapping that clone is associated with</param>
    /// <param name="remoteUris">Remotes to fetch one by one</param>
    /// <param name="requestedRefs">List of refs that need to be available</param>
    /// <param name="checkoutRef">Ref to check out at the end</param>
    /// <param name="resetToRemote">Whether to reset the branch to the remote one</param>
    /// <returns>Path to the clone</returns>
    Task<ILocalGitRepo> PrepareCloneAsync(
        SourceMapping mapping,
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uses an existing clone of a repository and prepares it by fetching from given remotes one-by-one until all requested commits are available.
    /// Then checks out the given ref.
    /// </summary>
    /// <param name="clonePath">Path to an existing clone</param>
    /// <param name="remoteUris">Remotes to fetch one by one</param>
    /// <param name="requestedRefs">List of refs that need to be available</param>
    /// <param name="checkoutRef">Ref to check out at the end</param>
    /// <param name="resetToRemote">Whether to reset the branch to the remote one</param>
    /// <returns>Path to the clone</returns>
    Task<ILocalGitRepo> PrepareCloneAsync(
        NativePath clonePath,
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// When we are synchronizing other repositories into the VMR, we need to temporarily clone them.
/// This class is responsible for managing the temporary clones so that:
///   - Previously cloned repositories are re-used
///   - Re-used clones pull newest updates once per run (the first time we use them)
/// </summary>
public class RepositoryCloneManager : CloneManager, IRepositoryCloneManager
{
    public RepositoryCloneManager(
        IVmrInfo vmrInfo,
        IGitRepoCloner gitRepoCloner,
        ILocalGitClient localGitRepo,
        ILocalGitRepoFactory localGitRepoFactory,
        ITelemetryRecorder telemetryRecorder,
        IFileSystem fileSystem,
        ILogger<RepositoryCloneManager> logger)
        : base(vmrInfo, gitRepoCloner, localGitRepo, localGitRepoFactory, telemetryRecorder, fileSystem, logger)
    {
    }

    public async Task<ILocalGitRepo> PrepareCloneAsync(
        SourceMapping mapping,
        IReadOnlyCollection<string> remoteUris,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default)
    {
        if (remoteUris.Count == 0)
        {
            throw new ArgumentException("No remote URIs provided to clone");
        }

        return await PrepareCloneInternalAsync(mapping.Name, remoteUris, [checkoutRef], checkoutRef, resetToRemote, cancellationToken);
    }

    public async Task<ILocalGitRepo> PrepareCloneAsync(
        string repoUri,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default)
    {
        // We store clones in directories named as a hash of the repo URI
        var cloneDir = StringUtils.GetXxHash64(repoUri);
        return await PrepareCloneInternalAsync(cloneDir, [repoUri], [checkoutRef], checkoutRef, resetToRemote, cancellationToken);
    }

    public Task<ILocalGitRepo> PrepareCloneAsync(
        SourceMapping mapping,
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default)
        => PrepareCloneInternalAsync(mapping.Name, remoteUris, requestedRefs, checkoutRef, resetToRemote, cancellationToken);

    public Task<ILocalGitRepo> PrepareCloneAsync(
        NativePath clonePath,
        IReadOnlyCollection<string> remoteUris,
        IReadOnlyCollection<string> requestedRefs,
        string checkoutRef,
        bool resetToRemote = false,
        CancellationToken cancellationToken = default)
    {
        return PrepareCloneInternalAsync(clonePath, remoteUris, requestedRefs, checkoutRef, resetToRemote, cancellationToken);
    }
}
