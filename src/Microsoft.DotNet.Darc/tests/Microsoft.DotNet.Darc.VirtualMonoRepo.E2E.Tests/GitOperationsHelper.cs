// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

#nullable enable
namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public class GitOperationsHelper
{
    public readonly IProcessManager ProcessManager;
    
    public GitOperationsHelper()
    {
        ProcessManager = new ProcessManager(new NullLogger<ProcessManager>(), "git");
    }
    
    public async Task CommitAll(LocalPath repo, string commitMessage)
    {
        await ProcessManager.ExecuteGit(repo, "add", "-A");
        await ProcessManager.ExecuteGit(repo, "commit", "-m", commitMessage);
    }

    public async Task InitialCommit(LocalPath repo)
    {
        await ProcessManager.ExecuteGit(repo, "init", "-b", "main");
        await ConfigureGit(repo);
        await CommitAll(repo, "Initial commit");
    }

    public async Task<string> GetRepoLastCommit(LocalPath repo)
    {
        var log = await ProcessManager.ExecuteGit(repo, "log", "--format=format:%H");
        return log.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).First();
    }

    public async Task CheckAllIsCommited(string repo)
    {
        var gitStatus = await ProcessManager.ExecuteGit(repo, "status", "--porcelain");
        gitStatus.StandardOutput.Should().BeEmpty();
    }

    public async Task InitializeSubmodule(
        LocalPath repo,
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

    public async Task RemoveSubmodule(LocalPath repo, string submoduleRelativePath)
    {
        await ProcessManager.ExecuteGit(repo, "rm", "-f", submoduleRelativePath);
    }

    public async Task PullMain(LocalPath repo)
    {
        await ProcessManager.ExecuteGit(repo, "pull", "origin", "main");
    }

    public async Task ChangeSubmoduleUrl(LocalPath repo, LocalPath submodulePath, LocalPath newUrl)
    {
        await ProcessManager.ExecuteGit(repo, "submodule", "set-url", submodulePath, newUrl);
    }

    private async Task ConfigureGit(LocalPath repo)
    {
        await ProcessManager.ExecuteGit(repo, "config", "user.email", DarcLib.Constants.DarcBotEmail);
        await ProcessManager.ExecuteGit(repo, "config", "user.name", DarcLib.Constants.DarcBotName);
    }
}
