// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

internal class GitOperationsHelper
{
    private readonly IProcessManager _processManager;

    public GitOperationsHelper()
    {
        _processManager = new ProcessManager(new NullLogger<ProcessManager>(), "git");
    }

    public async Task CommitAll(NativePath repo, string commitMessage, bool allowEmpty = false)
    {
        var result = await _processManager.ExecuteGit(repo, "add", "-A");

        if (!allowEmpty)
        {
            result.ThrowIfFailed($"No files to add in {repo}");
        }

        result = await _processManager.ExecuteGit(repo, "commit", "-m", commitMessage);
        if (!allowEmpty)
        {
            result.ThrowIfFailed($"No changes to commit in {repo}");
        }
    }

    public async Task InitialCommit(NativePath repo)
    {
        await _processManager.ExecuteGit(repo, "init", "-b", "main");
        await ConfigureGit(repo);
        await CommitAll(repo, "Initial commit", allowEmpty: true);
    }

    public async Task<string> GetRepoLastCommit(NativePath repo)
    {
        var log = await _processManager.ExecuteGit(repo, "log", "--format=format:%H");
        return log.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).First();
    }

    public async Task<string> GetRepoLastCommitMessage(NativePath repo)
    {
        var log = await _processManager.ExecuteGit(repo, "log", "--format=format:%s");
        return log.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).First();
    }

    public async Task CheckAllIsCommitted(string repo)
    {
        var gitStatus = await _processManager.ExecuteGit(repo, "status", "--porcelain");
        gitStatus.StandardOutput.Should().BeEmpty();
    }

    public async Task Checkout(NativePath repo, string gitRef)
    {
        var result = await _processManager.ExecuteGit(repo, "checkout", gitRef);
        result.ThrowIfFailed($"Could not checkout {gitRef} in {repo}");
    }

    public async Task DeleteBranch(NativePath repo, string branch)
    {
        var result = await _processManager.ExecuteGit(repo, "branch", "-D", branch);
        result.ThrowIfFailed($"Could not delete branch {branch} in {repo}");
    }

    public async Task InitializeSubmodule(
        NativePath repo,
        string submoduleName,
        string submoduleUrl,
        string pathInRepo)
    {
        await _processManager.ExecuteGit(
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

        await _processManager.ExecuteGit(
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
        return _processManager.ExecuteGit(repo, "rm", "-f", submoduleRelativePath);
    }

    public Task PullMain(NativePath repo)
    {
        return _processManager.ExecuteGit(repo, "pull", "origin", "main");
    }

    public Task ChangeSubmoduleUrl(NativePath repo, LocalPath submodulePath, LocalPath newUrl)
    {
        return _processManager.ExecuteGit(repo, "submodule", "set-url", submodulePath, newUrl);
    }

    public async Task MergePrBranch(NativePath repo, string branch, string targetBranch = "main")
    {
        var result = await _processManager.ExecuteGit(repo, "checkout", targetBranch);
        result.ThrowIfFailed($"Could not checkout main branch in {repo}");

        result = await _processManager.ExecuteGit(repo, "merge", "--squash", branch);
        result.ThrowIfFailed($"Could not merge branch {branch} to {targetBranch} in {repo}");

        await CommitAll(repo, $"Merged branch {branch} into {targetBranch}");
        await DeleteBranch(repo, branch);
        // Sometimes the local repo has a remote pointing to itself (due to how we prepare clones in the tests)
        // So after deleting a branch, it would still see the dead branch of the remote (itself)
        // So we just make sure we fetch the remote data to prune the dead branch
        await _processManager.ExecuteGit(repo, "fetch", "--all", "--prune");
    }

    public async Task ConfigureGit(NativePath repo)
    {
        await _processManager.ExecuteGit(repo, "config", "user.email", DarcLib.Constants.DarcBotEmail);
        await _processManager.ExecuteGit(repo, "config", "user.name", DarcLib.Constants.DarcBotName);
    }

    // mergeTheirs behaviour:
    //     null: abort merge
    //     true: merge using theirs
    //     false: merge using ours
    public async Task VerifyMergeConflict(
        NativePath repo,
        string branch,
        string? expectedConflictingFile = null,
        bool? mergeTheirs = null,
        string targetBranch = "main")
    {
        var result = await _processManager.ExecuteGit(repo, "checkout", targetBranch);
        result.ThrowIfFailed($"Could not checkout main branch in {repo}");

        result = await _processManager.ExecuteGit(repo, "merge", "--no-commit", "--no-ff", branch);
        result.Succeeded.Should().BeFalse($"Expected merge conflict in {repo} but none happened");

        if (expectedConflictingFile != null)
        {
            result.StandardOutput.Should().Match($"*Merge conflict in {expectedConflictingFile}*");
        }

        if (mergeTheirs.HasValue)
        {
            result = await _processManager.ExecuteGit(repo, "checkout", mergeTheirs.Value ? "--theirs" : "--ours", ".");
            result.ThrowIfFailed($"Failed to merge {(mergeTheirs.Value ? "theirs" : "ours")} {repo}");
            await CommitAll(repo, $"Merged {branch} into {targetBranch} {(mergeTheirs.Value ? "using " + targetBranch : "using " + targetBranch)}");
            await DeleteBranch(repo, branch);
        }
        else
        {
            result = await _processManager.ExecuteGit(repo, "merge", "--abort");
            result.ThrowIfFailed($"Failed to abort merge in {repo}");
        }
    }
}
