// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class MergeBandsOperation : Operation
{
    private readonly MergeBandsCommandLineOptions _options;
    private readonly IProcessManager _processManager;
    private readonly ILogger<MergeBandsOperation> _logger;

    public MergeBandsOperation(
        MergeBandsCommandLineOptions options,
        IProcessManager processManager,
        ILogger<MergeBandsOperation> logger)
    {
        _options = options;
        _processManager = processManager;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            return await ExecuteInternalAsync();
        }
        catch (Exception e)
        {
            _logger.LogError("Merge-bands operation failed: {message}", e.Message);
            _logger.LogDebug("{exception}", e);
            return Constants.ErrorCode;
        }
    }

    private async Task<int> ExecuteInternalAsync()
    {
        if (string.IsNullOrEmpty(_options.SourceBranch))
        {
            _logger.LogError("Source branch is not specified.");
            return Constants.ErrorCode;
        }

        // Find the git root from current directory
        var currentDirectory = Environment.CurrentDirectory;
        var vmrPath = _processManager.FindGitRoot(currentDirectory);

        _logger.LogInformation("VMR path: {vmrPath}", vmrPath);

        // Get the current branch (target branch)
        var currentBranchResult = await _processManager.ExecuteGit(vmrPath, "branch", "--show-current");
        if (currentBranchResult.ExitCode != 0)
        {
            _logger.LogError("Failed to get current branch: {error}", currentBranchResult.StandardError);
            return Constants.ErrorCode;
        }

        var targetBranch = currentBranchResult.StandardOutput.Trim();
        _logger.LogInformation("Target branch (current): {targetBranch}", targetBranch);
        _logger.LogInformation("Source branch to merge: {sourceBranch}", _options.SourceBranch);

        // Step 1: Merge the source branch
        _logger.LogInformation("Merging {sourceBranch} into {targetBranch}...", _options.SourceBranch, targetBranch);
        var mergeResult = await _processManager.ExecuteGit(vmrPath, "merge", _options.SourceBranch);
        if (mergeResult.ExitCode != 0)
        {
            _logger.LogError("Failed to merge {sourceBranch}: {error}", _options.SourceBranch, mergeResult.StandardError);
            return Constants.ErrorCode;
        }

        // Step 2: Reset paths that should be excluded from the merge
        _logger.LogInformation("Resetting excluded paths...");
        var resetResult = await _processManager.ExecuteGit(vmrPath, "reset", "--", "src");
        if (resetResult.ExitCode != 0)
        {
            _logger.LogError("Failed to reset src: {error}", resetResult.StandardError);
            return Constants.ErrorCode;
        }

        // Step 3: Checkout files from target branch
        var filesToCheckout = new[]
        {
            "src",
            "eng/common",
            "eng/Version.Details.props",
            "eng/Version.Details.xml",
            "eng/Version.props",
            "global.json"
        };

        _logger.LogInformation("Checking out files from {targetBranch}...", targetBranch);
        foreach (var file in filesToCheckout)
        {
            var checkoutResult = await _processManager.ExecuteGit(vmrPath, "checkout", targetBranch, "--", file);
            if (checkoutResult.ExitCode != 0)
            {
                _logger.LogWarning("Failed to checkout {file}: {error}", file, checkoutResult.StandardError);
            }
        }

        // Step 4: Clean untracked files in src
        _logger.LogInformation("Cleaning untracked files in src...");
        var cleanResult = await _processManager.ExecuteGit(vmrPath, "clean", "-fdx", "--", "src");
        if (cleanResult.ExitCode != 0)
        {
            _logger.LogWarning("Failed to clean src: {error}", cleanResult.StandardError);
        }

        _logger.LogInformation("Merge-bands operation completed successfully.");
        _logger.LogInformation("");
        _logger.LogInformation("Next steps:");
        _logger.LogInformation("  1. Review the changes with: git status");
        _logger.LogInformation("  2. Review the diff with: git diff");
        _logger.LogInformation("  3. Commit the changes if they look correct");
        _logger.LogInformation("  4. Push the changes to your branch");
        _logger.LogInformation("  5. Open a pull request");
        _logger.LogInformation("  6. Merge (do NOT squash) the pull request");

        return Constants.SuccessCode;
    }
}
