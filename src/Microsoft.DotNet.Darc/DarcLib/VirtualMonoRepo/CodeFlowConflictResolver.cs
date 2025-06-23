// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public abstract class CodeFlowConflictResolver
{
    private readonly ILogger _logger;

    protected CodeFlowConflictResolver(ILogger logger)
    {
        _logger = logger;
    }

    protected async Task<IReadOnlyCollection<UnixPath>> TryMergingBranch(
        ILocalGitRepo repo,
        string targetBranch,
        string branchToMerge,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Trying to merge {branchToMerge} into {targetBranch}...", branchToMerge, targetBranch);

        await repo.CheckoutAsync(targetBranch);
        var result = await repo.RunGitCommandAsync(["merge", "--no-commit", "--no-ff", branchToMerge], cancellationToken);
        if (result.Succeeded)
        {
            try
            {
                await repo.CommitAsync(
                    $"Merging {branchToMerge} into {targetBranch}",
                    allowEmpty: false,
                    cancellationToken: CancellationToken.None);

                _logger.LogInformation("Successfully merged the branch {targetBranch} into {headBranch} in {repoPath}",
                    branchToMerge,
                    targetBranch,
                    repo.Path);
            }
            catch (Exception e) when (e.Message.Contains("nothing to commit"))
            {
                // Our branch might be fast-forward and so no commit was needed
                _logger.LogInformation("Branch {targetBranch} had no updates since it was last merged into {headBranch}",
                    branchToMerge,
                    targetBranch);
            }

            return [];
        }
        else
        {
            result = await repo.RunGitCommandAsync(["diff", "--name-only", "--diff-filter=U", "--relative"], cancellationToken);
            if (!result.Succeeded)
            {
                var abort = await repo.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
                abort.ThrowIfFailed("Failed to abort a merge when resolving version file conflicts");
                result.ThrowIfFailed("Failed to resolve version file conflicts - failed to get a list of conflicted files");
                throw new InvalidOperationException(); // the line above will throw, including more details
            }

            return result
                .GetOutput()
                .Select(line => new UnixPath(line))
                .ToList();
        }
    }

    protected static async Task AbortMerge(ILocalGitRepo repo)
    {
        var result = await repo.RunGitCommandAsync(["merge", "--abort"], CancellationToken.None);
        result.ThrowIfFailed("Failed to abort a merge when resolving version file conflicts");
    }
}
