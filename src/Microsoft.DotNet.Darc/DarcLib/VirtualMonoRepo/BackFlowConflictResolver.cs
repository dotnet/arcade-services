// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IBackFlowConflictResolver
{
    Task<bool> TryMergingBranch(
        ILocalGitRepo repo,
        string baseBranch,
        string targetBranch);
}

public class BackFlowConflictResolver : CodeFlowConflictResolver, IBackFlowConflictResolver
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ForwardFlowConflictResolver> _logger;

    public BackFlowConflictResolver(
            IVmrInfo vmrInfo,
            IFileSystem fileSystem,
            ILogger<ForwardFlowConflictResolver> logger)
        : base(vmrInfo, logger)
    {
        _vmrInfo = vmrInfo;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<bool> TryMergingBranch(
        ILocalGitRepo repo,
        string targetBranch,
        string branchToMerge)
    {
        return await base.TryMergingBranch(repo, targetBranch, branchToMerge);
    }

    protected override Task<bool> TryResolvingConflicts(ILocalGitRepo repo, IEnumerable<UnixPath> conflictedFiles) => throw new NotImplementedException();
}
