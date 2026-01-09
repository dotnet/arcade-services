// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests.Helpers;

internal class GitOperationsHelper
{
    private readonly IProcessManager _processManager;

    public GitOperationsHelper()
    {
        _processManager = new ProcessManager(new NullLogger<ProcessManager>(), "git");
    }

    public async Task CommitAll(NativePath repo, string commitMessage, bool allowEmpty = false)
    {
        var result = await ExecuteGitCommand(repo, "add", "-A");

        if (!allowEmpty)
        {
            result.ThrowIfFailed($"No files to add in {repo}");
        }

        result = await ExecuteGitCommand(repo, "commit", "-m", commitMessage);
        if (!allowEmpty)
        {
            result.ThrowIfFailed($"No changes to commit in {repo}");
        }
    }

    public async Task<ProcessExecutionResult> ExecuteGitCommand(NativePath repo, params string[] args)
    {
        return await _processManager.ExecuteGit(repo, args);
    }

    public async Task InitialCommit(NativePath repo)
    {
        await ExecuteGitCommand(repo, "init", "-b", "main");
        await ConfigureGit(repo);
        await CommitAll(repo, "Initial commit", allowEmpty: true);
    }

    public async Task<string> GetRepoLastCommit(NativePath repo)
    {
        var log = await ExecuteGitCommand(repo, "log", "--format=format:%H");
        return log.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).First();
    }

    public async Task<string> GetRepoLastCommitMessage(NativePath repo)
    {
        var log = await ExecuteGitCommand(repo, "log", "--format=format:%s");
        return log.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).First();
    }

    public async Task CheckAllIsCommitted(NativePath repo)
    {
        var gitStatus = await ExecuteGitCommand(repo, "status", "--porcelain");
        gitStatus.StandardOutput.Should().BeEmpty();
    }

    public async Task Checkout(NativePath repo, string gitRef)
    {
        var result = await ExecuteGitCommand(repo, "checkout", gitRef);
        result.ThrowIfFailed($"Could not checkout {gitRef} in {repo}");
    }

    public async Task CreateBranch(NativePath repo, string branchName)
    {
        var result = await ExecuteGitCommand(repo, "checkout", "-B", branchName);
        result.ThrowIfFailed($"Failed to create branch {branchName} in {repo}");
    }

    public async Task DeleteBranch(NativePath repo, string branch)
    {
        var result = await ExecuteGitCommand(repo, "branch", "-D", branch);
        result.ThrowIfFailed($"Could not delete branch {branch} in {repo}");
    }

    public async Task InitializeSubmodule(
        NativePath repo,
        string submoduleName,
        string submoduleUrl,
        string pathInRepo)
    {
        await ExecuteGitCommand(
            repo,
            "-c",
            "protocol.file.allow=always",
            "submodule",
            "add",
            "--name",
            submoduleName,
            "--",
            submoduleUrl,
            pathInRepo);

        await ExecuteGitCommand(
            repo,
            "submodule",
            "update",
            "--init",
            "--recursive",
            submoduleName,
            "--",
            submoduleUrl,
            pathInRepo);
    }

    public async Task UpdateSubmodule(NativePath repo, string pathToSubmodule)
    {
        await PullMain(repo / pathToSubmodule);
        await CommitAll(repo, "Update submodule");
    }

    public Task RemoveSubmodule(NativePath repo, string submoduleRelativePath)
    {
        return ExecuteGitCommand(repo, "rm", "-f", submoduleRelativePath);
    }

    public Task PullMain(NativePath repo)
    {
        return ExecuteGitCommand(repo, "pull", "origin", "main");
    }

    public Task ChangeSubmoduleUrl(NativePath repo, LocalPath submodulePath, LocalPath newUrl)
    {
        return ExecuteGitCommand(repo, "submodule", "set-url", submodulePath, newUrl);
    }

    public async Task MergePrBranch(NativePath repo, string branch, string targetBranch = "main")
    {
        var result = await ExecuteGitCommand(repo, "checkout", targetBranch);
        result.ThrowIfFailed($"Could not checkout main branch in {repo}");

        result = await ExecuteGitCommand(repo, "merge", "--squash", branch);
        result.ThrowIfFailed($"Could not merge branch {branch} to {targetBranch} in {repo}");

        await CommitAll(repo, $"Merged branch {branch} into {targetBranch}");
        await DeleteBranch(repo, branch);
        // Sometimes the local repo has a remote pointing to itself (due to how we prepare clones in the tests)
        // So after deleting a branch, it would still see the dead branch of the remote (itself)
        // So we just make sure we fetch the remote data to prune the dead branch
        await ExecuteGitCommand(repo, "fetch", "--all", "--prune");
    }

    public async Task ConfigureGit(NativePath repo)
    {
        await ExecuteGitCommand(repo, "config", "user.email", DarcLib.Constants.DarcBotEmail);
        await ExecuteGitCommand(repo, "config", "user.name", DarcLib.Constants.DarcBotName);
    }

    // mergeTheirs behaviour:
    //     null: abort merge
    //     true: merge using theirs
    //     false: merge using ours
    public async Task VerifyMergeConflict(
        NativePath repo,
        string branch,
        string[]? expectedConflictingFiles = null,
        bool? mergeTheirs = null,
        string targetBranch = "main",
        bool changesStagedOnly = true)
    {
        ProcessExecutionResult result = null!;
        if (!changesStagedOnly)
        {
            result = await ExecuteGitCommand(repo, "checkout", targetBranch);
            result.ThrowIfFailed($"Could not checkout main branch in {repo}");

            result = await ExecuteGitCommand(repo, "merge", "--no-commit", "--no-ff", branch);
            result.Succeeded.Should().BeFalse($"Expected merge conflict in {repo} but none happened");
        }

        if (expectedConflictingFiles != null)
        {
            if (changesStagedOnly)
            {
                result = await ExecuteGitCommand(repo, "diff", "--name-only", "--diff-filter=U");
                var conflictedFiles = result.GetOutputLines();

                foreach (var expectedConflictingFile in expectedConflictingFiles)
                {
                    conflictedFiles.Should().Contain(expectedConflictingFile);
                }
            }
            else
            {
                foreach (var expectedConflictingFile in expectedConflictingFiles)
                {
                    result.StandardOutput.Should().Match($"*Merge conflict in {expectedConflictingFile}*");
                }
            }

            await VerifyConflictMarkers(repo, expectedConflictingFiles);

            if (mergeTheirs.HasValue)
            {
                foreach (var expectedConflictingFile in expectedConflictingFiles)
                {
                    result = await ExecuteGitCommand(repo, "checkout", mergeTheirs.Value ? "--theirs" : "--ours", expectedConflictingFile);
                    result.ThrowIfFailed($"Failed to merge {(mergeTheirs.Value ? "theirs" : "ours")} {expectedConflictingFile} in {repo}");
                }
            }
        }

        if (!mergeTheirs.HasValue)
        {
            result = await ExecuteGitCommand(repo, "merge", "--abort");
            result.ThrowIfFailed($"Failed to abort merge in {repo}");
            return;
        }

        // If we take theirs, we can just checkout all files (because version files will be accepted from our branch)
        if (mergeTheirs == true)
        {
            result = await ExecuteGitCommand(repo, "checkout", "--theirs", ".");
            result.ThrowIfFailed($"Failed to merge theirs in {repo}");
        }
        // If we take ours, we already resolved the conflicting files above but the version files need to come from our branch
        else
        {
            foreach (var file in DependencyFileManager.CodeflowDependencyFiles.Append(VmrInfo.DefaultRelativeSourceManifestPath))
            {
                if (!File.Exists(repo / file))
                {
                    continue;
                }

                result = await ExecuteGitCommand(repo, "checkout", "--ours", file);
                result.ThrowIfFailed($"Failed to merge ours {file} in {repo}");
            }
        }

        await CommitAll(repo, $"Merged {branch} into {targetBranch} using {(mergeTheirs.Value ? targetBranch : branch)}");

        if (changesStagedOnly)
        {
            await Checkout(repo, targetBranch);
            await ExecuteGitCommand(repo, "merge", branch);
        }

        await DeleteBranch(repo, branch);
    }

    public static Task VerifyConflictMarkers(NativePath productRepoPath, IEnumerable<string> files)
        => VerifyConflictMarkers(productRepoPath, files, shouldHaveMarkers: true);

    public static Task VerifyNoConflictMarkers(NativePath productRepoPath, IEnumerable<string> files)
        => VerifyConflictMarkers(productRepoPath, files, shouldHaveMarkers: false);

    private static async Task VerifyConflictMarkers(
        NativePath productRepoPath,
        IEnumerable<string> files,
        bool shouldHaveMarkers)
    {
        foreach (var file in files)
        {
            var filePath = productRepoPath / file;
            var content = await File.ReadAllTextAsync(filePath);

            if (shouldHaveMarkers)
            {
                content.Should().Contain("<<<<<<<", $"File {filePath} does not contain conflict markers");
            }
            else
            {
                content.Should().NotContain("<<<<<<<", $"File {filePath} contains conflict markers");
            }
        }
    }
}
