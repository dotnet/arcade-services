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

/// <summary>
/// This class is responsible for resolving well-known conflicts that can occur during a backflow operation.
/// The conflicts can happen when backward a forward flow PRs get merged out of order.
/// This can be shown on the following schema (the order of events is numbered):
/// 
///     repo VMR
///       O────────────────────►O 
///       │                 2.  │ 
///     1.O────────────────O    │ 
///       │  4.            │    │ 
///       │    O───────────┼────O 3. 
///       │    │           │    │ 
///       │    │           │    │ 
///     6.O◄───┘           └───►O 5.
///       │    7.               │ 
///       │     O───────────────| 
///     8.O◄────┘               │ 
///       │                     │
///
/// The conflict arises in step 8. and is caused by the fact that:
///   - When the backflow PR branch is being opened in 7., the last sync (from the point of view of 5.) is from 1.
///   - This means that the PR branch will be based on 1. (the real PR branch will be a commit on top of 1.)
///   - This means that when 6. merged, Version.Details.xml got updated with the SHA of the 3.
///   - So the Source tag in Version.Details.xml in 6. contains the SHA of 3.
///   - The backflow PR branch contains the SHA of 5.
///   - So the Version.Details.xml file conflicts on the SHA (3. vs 5.)
///   - There's also a similar conflict in the package versions that got updated in those commits.
///   - However, if only the version files are in conflict, we can try merging 6. into 7. and resolve the conflict.
///   - This is because basically we know we want to set the version files to point at 5.
/// </summary>
public class BackFlowConflictResolver : CodeFlowConflictResolver, IBackFlowConflictResolver
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ILogger<ForwardFlowConflictResolver> _logger;

    public BackFlowConflictResolver(
            IVmrInfo vmrInfo,
            ILogger<ForwardFlowConflictResolver> logger)
        : base(vmrInfo, logger)
    {
        _vmrInfo = vmrInfo;
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
                await Task.CompletedTask;
                return false;

                // TODO https://github.com/dotnet/arcade-services/issues/4196: Resolve conflicts in eng/Version.Details.xml
                // return true;
            }

            // Known conflict in eng/Versions.props
            if (string.Equals(filePath, VersionFiles.VersionProps, StringComparison.InvariantCultureIgnoreCase))
            {
                await Task.CompletedTask;
                return false;

                // TODO https://github.com/dotnet/arcade-services/issues/4196: Resolve conflicts in eng/Version.Details.xml
                // return true;
            }

            _logger.LogInformation("Unable to resolve conflicts in {file}", _vmrInfo.VmrPath);
            return false;
        }

        return true;
    }
}
