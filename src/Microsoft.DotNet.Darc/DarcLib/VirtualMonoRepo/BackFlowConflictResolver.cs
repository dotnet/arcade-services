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
    Task<bool> TryMergingRepoBranch(
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

    public async Task<bool> TryMergingRepoBranch(
        ILocalGitRepo repo,
        string targetBranch,
        string branchToMerge)
    {
        return await TryMergingBranch(repo, targetBranch, branchToMerge);
    }

    protected override async Task<bool> TryResolvingConflicts(ILocalGitRepo repo, IEnumerable<UnixPath> conflictedFiles)
    {
        foreach (var filePath in conflictedFiles)
        {
            // Known conflict in eng/Version.Details.xml
            if (string.Equals(filePath, VersionFiles.VersionDetailsXml, StringComparison.InvariantCultureIgnoreCase))
            {
                // TODO: Resolve conflicts in eng/Version.Details.xml
                await Task.CompletedTask;
                return true;
            }

            _logger.LogInformation("Unable to resolve conflicts in {file}", _vmrInfo.VmrPath);
            return false;
        }

        return true;
    }
}
