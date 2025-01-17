// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class is responsible for resolving well-known conflicts that can occur during codeflow operations.
/// The conflicts usually happen when backward a forward flow PRs get merged out of order.
/// </summary>
public abstract class CodeFlowConflictResolver
{
    private readonly ILogger _logger;

    public CodeFlowConflictResolver(ILogger logger)
    {
        _logger = logger;
    }

    protected async Task<bool> TryMergingBranch(
        ILocalGitRepo repo,
        Build build,
        string targetBranch,
        string branchToMerge)
    {
        _logger.LogInformation("Trying to merge target branch {targetBranch} into {headBranch}", branchToMerge, targetBranch);

        await repo.CheckoutAsync(targetBranch);
        var result = await repo.RunGitCommandAsync(["merge", "--no-commit", "--no-ff", branchToMerge]);
        if (result.Succeeded)
        {
            _logger.LogInformation("Successfully merged the branch {targetBranch} into {headBranch} in {repoPath}",
                branchToMerge,
                targetBranch,
                repo.Path);
            await repo.CommitAsync($"Merging {branchToMerge} into {targetBranch}", allowEmpty: true);
            return true;
        }

        result = await repo.RunGitCommandAsync(["diff", "--name-only", "--diff-filter=U", "--relative"]);
        if (!result.Succeeded)
        {
            _logger.LogInformation("Failed to merge the branch {targetBranch} into {headBranch} in {repoPath}",
                branchToMerge,
                targetBranch,
                repo.Path);
            result = await repo.RunGitCommandAsync(["merge", "--abort"]);
            return false;
        }

        var conflictedFiles = result.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new UnixPath(line.Trim()));

        var unknownConflicts = conflictedFiles
                .Select(f => f.Path.ToLowerInvariant())
                .Except(AllowedConflicts.Select(f => f.ToLowerInvariant()))
                .ToList();

        if (unknownConflicts.Count > 0)
        {
            _logger.LogInformation("Failed to merge the branch {targetBranch} into {headBranch} due to unresolvable conflicts in files: {files}",
                branchToMerge,
                targetBranch,
                string.Join(", ", unknownConflicts));
            result = await repo.RunGitCommandAsync(["merge", "--abort"]);
            return false;
        }

        if (!await TryResolveConflicts(repo, build, targetBranch, conflictedFiles))
        {
            return false;
        }

        _logger.LogInformation("Successfully resolved file conflicts between branches {targetBranch} and {headBranch}",
            branchToMerge,
            targetBranch);
        await repo.CommitAsync($"Merge branch {branchToMerge} into {targetBranch}", allowEmpty: false);
        return true;
    }

    protected virtual async Task<bool> TryResolveConflicts(
        ILocalGitRepo repo,
        Build build,
        string targetBranch,
        IEnumerable<UnixPath> conflictedFiles)
    {
        foreach (var filePath in conflictedFiles)
        {
            try
            {
                if (await TryResolvingConflict(repo, build, filePath))
                {
                    continue;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to resolve conflicts in {filePath}", filePath);
            }

            await repo.RunGitCommandAsync(["merge", "--abort"]);
            return false;
        }

        return true;
    }

    protected abstract Task<bool> TryResolvingConflict(ILocalGitRepo repo, Build build, string filePath);

    protected abstract string[] AllowedConflicts { get; }
}
