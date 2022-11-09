// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IRepositoryCloneManager
{
    Task<string> PrepareClone(string repoUri, string checkoutRef, CancellationToken cancellationToken);
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
    private readonly IRemoteFactory _remoteFactory;
    private readonly IProcessManager _processManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrPatchHandler> _logger;

    private readonly List<string> _upToDateRepos = new();

    public RepositoryCloneManager(
        IVmrInfo vmrInfo,
        ILocalGitRepo localGitRepo,
        IRemoteFactory remoteFactory,
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

    public async Task<string> PrepareClone(string repoUri, string checkoutRef, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hash = GetCloneDirName(repoUri);
        var clonePath = _fileSystem.PathCombine(_vmrInfo.TmpPath, hash);

        if (_upToDateRepos.Contains(repoUri))
        {
            _localGitRepo.Checkout(clonePath, checkoutRef);
            cancellationToken.ThrowIfCancellationRequested();
            return clonePath;
        }

        if (!_fileSystem.DirectoryExists(clonePath))
        {
            _logger.LogDebug("Cloning {repo} to {clonePath}", repoUri, clonePath);
            var remoteRepo = await _remoteFactory.GetRemoteAsync(repoUri, _logger);
            remoteRepo.Clone(repoUri, checkoutRef, clonePath, checkoutSubmodules: false, null);
            cancellationToken.ThrowIfCancellationRequested();
        }
        else
        {
            _logger.LogDebug("Clone of {repo} found, pulling new changes...", repoUri);

            var result = await _processManager.ExecuteGit(clonePath, "fetch", "--all");
            result.ThrowIfFailed($"Failed to pull new changes from {repoUri} into {clonePath}");
            cancellationToken.ThrowIfCancellationRequested();

            _localGitRepo.Checkout(clonePath, checkoutRef);
        }

        _upToDateRepos.Add(repoUri);
        return clonePath;
    }

    // We store clones in directories named as a hash of the repo URI
    private static string GetCloneDirName(string input)
    {
        var hasher = new XxHash64(0);
        byte[] inputBytes = Encoding.ASCII.GetBytes(input);
        hasher.Append(inputBytes);
        byte[] hashBytes = hasher.GetCurrentHash();
        return Convert.ToHexString(hashBytes);
    }
}
