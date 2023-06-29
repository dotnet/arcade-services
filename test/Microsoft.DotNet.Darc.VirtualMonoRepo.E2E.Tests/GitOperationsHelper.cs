// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public class GitOperationsHelper
{
    public readonly IProcessManager ProcessManager;
    
    public GitOperationsHelper()
    {
        ProcessManager = new ProcessManager(new NullLogger<ProcessManager>(), "git");
    }
    
    public async Task CommitAll(NativePath repo, string commitMessage, bool allowEmpty = false)
    {
        var result = await ProcessManager.ExecuteGit(repo, "add", "-A");

        if (!allowEmpty)
        {
            result.ThrowIfFailed($"No files to add in {repo}");
        }

        result = await ProcessManager.ExecuteGit(repo, "commit", "-m", commitMessage);
        if (!allowEmpty)
        {
            result.ThrowIfFailed($"No changes to commit in {repo}");
        }
    }

    public async Task InitialCommit(NativePath repo)
    {
        await ProcessManager.ExecuteGit(repo, "init", "-b", "main");
        await ConfigureGit(repo);
        await CommitAll(repo, "Initial commit", allowEmpty: true);
    }

    public async Task<string> GetRepoLastCommit(NativePath repo)
    {
        var log = await ProcessManager.ExecuteGit(repo, "log", "--format=format:%H");
        return log.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).First();
    }

    public async Task CheckAllIsCommitted(string repo)
    {
        var gitStatus = await ProcessManager.ExecuteGit(repo, "status", "--porcelain");
        gitStatus.StandardOutput.Should().BeEmpty();
    }

    public async Task InitializeSubmodule(
        NativePath repo,
        string submoduleName,
        string submoduleUrl,
        string pathInRepo)
    {
        await ProcessManager.ExecuteGit(
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

        await ProcessManager.ExecuteGit(
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
        return ProcessManager.ExecuteGit(repo, "rm", "-f", submoduleRelativePath);
    }

    public Task PullMain(NativePath repo)
    {
        return ProcessManager.ExecuteGit(repo, "pull", "origin", "main");
    }

    public Task ChangeSubmoduleUrl(NativePath repo, LocalPath submodulePath, LocalPath newUrl)
    {
        return ProcessManager.ExecuteGit(repo, "submodule", "set-url", submodulePath, newUrl);
    }

    private async Task ConfigureGit(NativePath repo)
    {
        await ProcessManager.ExecuteGit(repo, "config", "user.email", DarcLib.Constants.DarcBotEmail);
        await ProcessManager.ExecuteGit(repo, "config", "user.name", DarcLib.Constants.DarcBotName);
    }
}
