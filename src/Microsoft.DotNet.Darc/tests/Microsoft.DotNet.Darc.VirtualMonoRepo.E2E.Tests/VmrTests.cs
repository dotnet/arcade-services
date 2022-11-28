// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using System.Diagnostics;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrTests
{
    private string _tempDir = null!;
    private string _testRepoPath = null!;
    private string _externalRepoPath = null!;
    private string _submodulePath = null!;
    private string _vmrPath = null!;
    private string _tmpPath = null!;
    private readonly string baseDir;
    private readonly IProcessManager processManager;
    private readonly string darcExecutable;

    public VmrTests()
    {
        processManager = new ProcessManager(new NullLogger<ProcessManager>(), "git");
        var assembly = Assembly.GetAssembly(typeof(VmrTests)) ?? throw new Exception("Assembly not found");

        darcExecutable = Path.Combine(assembly.Location, "..", "Microsoft.DotNet.Darc.exe");
        baseDir = Path.Combine(Path.GetTempPath(), "_vmrTests");
    }

    [OneTimeSetUp]
    public void StartTest()
    {
        Trace.Listeners.Add(new ConsoleTraceListener());
    }

    [OneTimeTearDown]
    public void EndTest()
    {
        Trace.Flush();
    }

    [SetUp]
    public async Task SetUp()
    {
        _tempDir = Path.Combine(baseDir, Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);

        _testRepoPath = Path.Combine(_tempDir, "test-repo");
        _externalRepoPath = Path.Combine(_tempDir, "external-repo");
        _submodulePath = Path.Combine(_tempDir, "test-repo", "externals", "external-repo");
        _vmrPath = Path.Combine(_tempDir, "vmr");
        _tmpPath = Path.Combine(_tempDir, "tmp");

        Directory.CreateDirectory(_vmrPath);
        Directory.CreateDirectory(_testRepoPath);
        Directory.CreateDirectory(Path.Combine(_testRepoPath, "excluded"));
        Directory.CreateDirectory(_tmpPath);
        Directory.CreateDirectory(_externalRepoPath);

        var sourceMappings = string.Format(@"{{
            ""patchesPath"": ""src/installer/src/SourceBuild/tarball/patches"",
            ""defaults"": {{
              ""defaultRef"": ""main"",
              ""exclude"": [
                ""**/*.dll""
              ]
            }},
            ""mappings"": [
              {{
                ""name"": ""test-repo"",
                ""defaultRemote"": ""{0}"",
                ""exclude"": [
                  ""externals/external-repo/**/*.exe"",
                  ""excluded/*""
                ]
              }}
            ]
        }}", _testRepoPath.Replace("\\", "\\\\"));

        File.WriteAllText(Path.Combine(_testRepoPath, "test-repo-file.txt"), "Test repo file");
        File.WriteAllText(Path.Combine(_testRepoPath, "excluded", "excluded.txt"), "File to be excluded");

        Directory.CreateDirectory(Path.Combine(_vmrPath, "src"));
        File.WriteAllText(Path.Combine(_vmrPath, "src", "source-mappings.json"), sourceMappings);

        await InitialCommit(_vmrPath);
        await InitialCommit(_testRepoPath);
    }

    [TearDown]
    public void CleanUpOutputFile()
    {
        try
        {
            if (_tempDir is not null)
            {
                DeleteDirectory(_tempDir);
            }
        }
        catch
        {
            // Ignore
        }
    }

    [Test]
    public async Task RepoIsInitializedTest()
    {
        var commit = await GetRepoLastCommit(_testRepoPath);
        
        var res = await processManager.Execute(darcExecutable, new string[] { "vmr", "initialize", "--debug", "--vmr", _vmrPath, "--tmp", _tmpPath, $"test-repo:{commit}" });
        Debug.WriteLine(res.StandardOutput);
        res.ExitCode.Should().Be(0);

        var expectedFiles = new List<string>
        {
            Path.Combine(_vmrPath, "git-info", "AllRepoVersions.props"),
            Path.Combine(_vmrPath, "git-info", "test-repo.props"),
            Path.Combine(_vmrPath, "src", "source-manifest.json"),
            Path.Combine(_vmrPath, "src", "source-mappings.json"),
            Path.Combine(_vmrPath, "src", "test-repo", "test-repo-file.txt")
        };

        CheckDirectoryContents(new DirectoryInfo(_vmrPath), expectedFiles);
        await CheckAllIsCommited(_vmrPath);
    }

    [Test]
    public async Task FileChangesAreSyncedTest()
    {
        await RepoIsInitializedTest();

        File.AppendAllText(Path.Combine(_testRepoPath, "test-repo-file.txt"), "Change in test repo file");
        await CommitAll(_testRepoPath, "Changing a file in the repo");

        var commit = await GetRepoLastCommit(_testRepoPath);

        var res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", _vmrPath, "--tmp", _tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        var expectedFiles = new List<string>
        {
            Path.Combine(_vmrPath, "git-info", "AllRepoVersions.props"),
            Path.Combine(_vmrPath, "git-info", "test-repo.props"),
            Path.Combine(_vmrPath, "src", "source-manifest.json"),
            Path.Combine(_vmrPath, "src", "source-mappings.json"),
            Path.Combine(_vmrPath, "src", "test-repo", "test-repo-file.txt"),
        };

        CheckDirectoryContents(new DirectoryInfo(_vmrPath), expectedFiles);
        File.ReadAllText(Path.Combine(_vmrPath, "src", "test-repo", "test-repo-file.txt"))
            .Should().Contain("Change in test repo file");
        await CheckAllIsCommited(_vmrPath);
    }

    [Test]
    public async Task FileIsIncludedTest()
    {
        await RepoIsInitializedTest();

        File.Move(Path.Combine(_testRepoPath, "excluded", "excluded.txt"), Path.Combine(_testRepoPath, "excluded.txt"));
        await CommitAll(_testRepoPath, "Move a file from excluded to included folder");
        var commit = await GetRepoLastCommit(_testRepoPath);

        var res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", _vmrPath, "--tmp", _tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        var expectedFiles = new List<string>
        {
            Path.Combine(_vmrPath, "git-info", "AllRepoVersions.props"),
            Path.Combine(_vmrPath, "git-info", "test-repo.props"),
            Path.Combine(_vmrPath, "src", "source-manifest.json"),
            Path.Combine(_vmrPath, "src", "source-mappings.json"),
            Path.Combine(_vmrPath, "src", "test-repo", "excluded.txt"),
            Path.Combine(_vmrPath, "src", "test-repo", "test-repo-file.txt")
        };

        CheckDirectoryContents(new DirectoryInfo(_vmrPath), expectedFiles);
        await CheckAllIsCommited(_vmrPath);
    }

    [Test]
    public async Task RepoIsUpdatedWithAddedSubmoduleTest()
    {
        await RepoIsInitializedTest();

        File.WriteAllText(Path.Combine(_externalRepoPath, "external-repo-file.txt"), "External repo file");

        await InitialCommit(_externalRepoPath);
        await InitializeSubmodule(_testRepoPath, "submodule1", _externalRepoPath, _submodulePath); 
        await CommitAll(_testRepoPath, "Add submodule");

        var commit = await GetRepoLastCommit(_testRepoPath);

        var res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", _vmrPath, "--tmp", _tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        var expectedFiles = new List<string>
        {
            Path.Combine(_vmrPath, "git-info", "AllRepoVersions.props"),
            Path.Combine(_vmrPath, "git-info", "test-repo.props"),
            Path.Combine(_vmrPath, "src", "source-manifest.json"),
            Path.Combine(_vmrPath, "src", "source-mappings.json"),
            Path.Combine(_vmrPath, "src", "test-repo", ".gitmodules"),
            Path.Combine(_vmrPath, "src", "test-repo", "externals", "external-repo", "external-repo-file.txt"),
            Path.Combine(_vmrPath, "src", "test-repo", "test-repo-file.txt")
        };

        CheckDirectoryContents(new DirectoryInfo(_vmrPath), expectedFiles);
        await CheckAllIsCommited(_vmrPath);

        // Add a file in the submodule

        File.WriteAllText(Path.Combine(_externalRepoPath, "additional-file.txt"), "New external repo file");
        await CommitAll(_externalRepoPath, "Adding new file in the submodule");

        await processManager.ExecuteGit(_submodulePath, new string[] { "pull", "origin", "main" }, CancellationToken.None);
        await CommitAll(_testRepoPath, "Checkout submodule");
        commit = await GetRepoLastCommit(_testRepoPath);

        res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", _vmrPath, "--tmp", _tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        expectedFiles = new List<string>
        {
            Path.Combine(_vmrPath, "git-info", "AllRepoVersions.props"),
            Path.Combine(_vmrPath, "git-info", "test-repo.props"),
            Path.Combine(_vmrPath, "src", "source-manifest.json"),
            Path.Combine(_vmrPath, "src", "source-mappings.json"),
            Path.Combine(_vmrPath, "src", "test-repo", ".gitmodules"),
            Path.Combine(_vmrPath, "src", "test-repo", "externals", "external-repo", "additional-file.txt"),
            Path.Combine(_vmrPath, "src", "test-repo", "externals", "external-repo", "external-repo-file.txt"),
            Path.Combine(_vmrPath, "src", "test-repo", "test-repo-file.txt")
        };

        CheckDirectoryContents(new DirectoryInfo(_vmrPath), expectedFiles);
        await CheckAllIsCommited(_vmrPath);

        // Remove submodule

        await RemoveSubmodule(_testRepoPath, _submodulePath);
        await CommitAll(_testRepoPath, "Remove the submodule");
        commit = await GetRepoLastCommit(_testRepoPath);

        res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", _vmrPath, "--tmp", _tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        expectedFiles = new List<string>
        {
            Path.Combine(_vmrPath, "git-info", "AllRepoVersions.props"),
            Path.Combine(_vmrPath, "git-info", "test-repo.props"),
            Path.Combine(_vmrPath, "src", "source-manifest.json"),
            Path.Combine(_vmrPath, "src", "source-mappings.json"),
            Path.Combine(_vmrPath, "src", "test-repo", ".gitmodules"),
            Path.Combine(_vmrPath, "src", "test-repo", "test-repo-file.txt")
        };

        CheckDirectoryContents(new DirectoryInfo(_vmrPath), expectedFiles);
        await CheckAllIsCommited(_vmrPath);
    }

    private void CheckDirectoryContents(DirectoryInfo directory, IList<string> expectedFiles)
    {
        var filesInDir = GetAllFilesInDirectory(directory).OrderBy(x => x).ToList();
        filesInDir.Should().BeEquivalentTo(expectedFiles);
    }

    private async Task CheckAllIsCommited(string repo)
    {
        var gitStatus = await processManager.ExecuteGit(repo, "status");
        var statusLog = gitStatus.StandardOutput;
        statusLog.Should().NotContain("Changes to be committed");
        statusLog.Should().NotContain("Changes not staged for commit");
    }

    private ICollection<string> GetAllFilesInDirectory(DirectoryInfo directory)
    {
        var files = new List<string>();

        if(directory.Name == ".git")
        {
            return files;
        }

        files.AddRange(directory.GetFiles().Select(f => f.FullName));

        foreach (var subDir in directory.GetDirectories())
        {
            files.AddRange(GetAllFilesInDirectory(subDir));
        }

        return files;
    }

    private async Task ConfigureGit(string repo)
    {
        await processManager.ExecuteGit(repo, "config", "user.email", Constants.DarcBotEmail);
        await processManager.ExecuteGit(repo, "config", "user.name", Constants.DarcBotName);
    }

    private async Task CommitAll(string repo, string commitMessage)
    {
        await processManager.ExecuteGit(repo, "add", "-A");
        await processManager.ExecuteGit(repo, "commit", "-m", commitMessage);
    }

    private async Task InitialCommit(string repo)
    {
        await processManager.ExecuteGit(repo, "init", "-b", "main");
        await ConfigureGit(repo);
        await CommitAll(repo, "Initial commit");
    }

    private async Task<string> GetRepoLastCommit(string repo)
    {
        var log = await processManager.ExecuteGit(repo, "log", "--format=format:%H");
        return log.StandardOutput.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).First();
    }

    private async Task InitializeSubmodule(string repo, string submoduleName, string submoduleUrl, string pathInRepo)
    {
        await processManager.ExecuteGit(repo, 
            "submodule", "add", "--name", 
                submoduleName, "--", submoduleUrl, "externals/external-repo");
        
        await processManager.ExecuteGit(repo,
            "submodule update", "--init", "--recursive",
                submoduleName, "--", submoduleUrl, "externals/external-repo");
    }

    private async Task RemoveSubmodule(string repo, string _submodulePath)
    {
        await processManager.ExecuteGit(repo, "rm", _submodulePath);
    }

    private void DeleteDirectory(string targetDir)
    {
        File.SetAttributes(targetDir, FileAttributes.Normal);

        string[] files = Directory.GetFiles(targetDir);
        string[] dirs = Directory.GetDirectories(targetDir);

        foreach (string file in files)
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        foreach (string dir in dirs)
        {
            DeleteDirectory(dir);
        }

        Directory.Delete(targetDir, false);
    }
}

