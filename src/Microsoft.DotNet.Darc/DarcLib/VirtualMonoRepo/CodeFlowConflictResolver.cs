// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public abstract class CodeFlowConflictResolver
{
    private readonly IVmrInfo _vmrInfo;
    private readonly ILogger _logger;

    public CodeFlowConflictResolver(
        IVmrInfo vmrInfo,
        ILogger logger)
    {
        _vmrInfo = vmrInfo;
        _logger = logger;
    }

    protected async Task<bool> TryMergingBranch(
        ILocalGitRepo repo,
        string targetBranch,
        string branchToMerge)
    {
        _logger.LogInformation("Trying to merge target branch {targetBranch} into {baseBranch}", branchToMerge, targetBranch);

        await repo.CheckoutAsync(targetBranch);
        var result = await repo.RunGitCommandAsync(["merge", "--no-commit", "--no-ff", branchToMerge]);
        if (result.Succeeded)
        {
            _logger.LogInformation("Successfully merged the branch {targetBranch} into {headBranch} in {repoPath}",
                branchToMerge,
                targetBranch,
                _vmrInfo.VmrPath);
            await repo.CommitAsync($"Merging {branchToMerge} into {targetBranch}", allowEmpty: true);
            return true;
        }

        result = await repo.RunGitCommandAsync(["diff", "--name-only", "--diff-filter=U", "--relative"]);
        if (!result.Succeeded)
        {
            _logger.LogInformation("Failed to merge the branch {targetBranch} into {headBranch} in {repoPath}",
                branchToMerge,
                targetBranch,
                _vmrInfo.VmrPath);
            result = await repo.RunGitCommandAsync(["merge", "--abort"]);
            return false;
        }

        var conflictedFiles = result.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new UnixPath(line.Trim()));

        if (!await TryResolvingConflicts(repo, conflictedFiles))
        {
            result = await repo.RunGitCommandAsync(["merge", "--abort"]);
            return false;
        }

        _logger.LogInformation("Successfully resolved version file conflicts between branches {targetBranch} and {headBranch} in {repoPath}",
            branchToMerge,
            targetBranch,
            _vmrInfo.VmrPath);
        await repo.CommitAsync($"Merge branch {branchToMerge} into {targetBranch}", allowEmpty: false);
        return true;
    }

    protected abstract Task<bool> TryResolvingConflicts(ILocalGitRepo repo, IEnumerable<UnixPath> conflictedFiles);
}
