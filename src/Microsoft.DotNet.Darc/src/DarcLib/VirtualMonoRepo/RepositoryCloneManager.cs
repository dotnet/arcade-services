// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IRepositoryCloneManager
{
    Task<LocalPath> PrepareClone(string repoUri, string checkoutRef, CancellationToken cancellationToken);

    Task<LocalPath> PrepareClone(
        SourceMapping mapping,
        IEnumerable<string> remotes,
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
    private readonly ILocalGitRepo _localGitRepo;
    private readonly IGitRepoClonerFactory _remoteFactory;
    private readonly IProcessManager _processManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrPatchHandler> _logger;

    // Map of URI => dir name
    private readonly Dictionary<string, LocalPath> _clones = new();

    // Repos we have already pulled updates for during this run
    private readonly List<string> _upToDateRepos = new();

    public RepositoryCloneManager(
        IVmrInfo vmrInfo,
        ILocalGitRepo localGitRepo,
        IGitRepoClonerFactory remoteFactory,
        IProcessManager processManager,
        IFileSystem fileSystem,
        ILogger<VmrPatchHandler> logger)
    {
        _vmrInfo = vmrInfo;
        _localGitRepo = localGitRepo;
        _remoteFactory = remoteFactory;
        _processManager = processManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<LocalPath> PrepareClone(
        SourceMapping mapping,
        IEnumerable<string> remoteUris,
        string checkoutRef,
        CancellationToken cancellationToken)
    {
        LocalPath? path = null;
        foreach (string remoteUri in remoteUris)
        {
            // Path should be returned the same for all invocations
            path = await PrepareClone(remoteUri, checkoutRef, mapping.Name, cancellationToken);
        }

        return path ?? throw new ArgumentException("No remote URIs provided to clone");
    }

    public async Task<LocalPath> PrepareClone(
        string repoUri,
        string checkoutRef,
        CancellationToken cancellationToken)
    {
        return await PrepareClone(repoUri, checkoutRef, GetHash(repoUri), cancellationToken);
    }

    private async Task<LocalPath> PrepareClone(
        string remoteUri,
        string checkoutRef,
        string dirName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_upToDateRepos.Contains(remoteUri))
        {
            var path = _clones[remoteUri];
            _localGitRepo.Checkout(path, checkoutRef);
            cancellationToken.ThrowIfCancellationRequested();
            return path;
        }

        if (_clones.TryGetValue(remoteUri, out var clonePath))
        {
            _logger.LogDebug("Clone of {repo} found, pulling new changes...", remoteUri);

            var result = await _processManager.ExecuteGit(clonePath, "fetch", "--all");
            result.ThrowIfFailed($"Failed to pull new changes from {remoteUri} into {clonePath}");
            cancellationToken.ThrowIfCancellationRequested();

            _localGitRepo.Checkout(clonePath, checkoutRef);
        }
        else
        {
            clonePath = _vmrInfo.TmpPath / dirName;

            if (!_fileSystem.DirectoryExists(clonePath))
            {
                _logger.LogDebug("Cloning {repo} to {clonePath}", remoteUri, clonePath);
                var repoCloner = _remoteFactory.GetCloner(remoteUri, _logger);
                repoCloner.Clone(remoteUri, checkoutRef, clonePath, checkoutSubmodules: false, null);
                cancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                _logger.LogDebug("Clone of {repo} found in {clonePath}", remoteUri, clonePath);
                _localGitRepo.AddRemoteIfMissing(clonePath, remoteUri, forceFetch: true);
                _localGitRepo.Checkout(clonePath, checkoutRef);
            }
        }

        _upToDateRepos.Add(remoteUri);
        _clones[remoteUri] = clonePath;
        return clonePath;
    }

    // We store clones in directories named as a hash of the repo URI
    private static string GetHash(string input)
    {
        var hasher = new XxHash64(0);
        byte[] inputBytes = Encoding.ASCII.GetBytes(input);
        hasher.Append(inputBytes);
        byte[] hashBytes = hasher.GetCurrentHash();
        return Convert.ToHexString(hashBytes);
    }
}
