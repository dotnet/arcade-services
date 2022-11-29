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
using Castle.DynamicProxy;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

[TestFixture]
[NonParallelizable]
public class VmrTests
{
    private LocalPath _tempDir = null!;
    private LocalPath _testRepoPath = null!;
    private LocalPath _externalRepoPath = null!;
    private LocalPath _vmrPath = null!;
    private LocalPath _tmpPath = null!;
    private readonly LocalPath baseDir;
    private readonly IProcessManager processManager;
    private readonly string darcExecutable;
    private readonly string darcDll;


    public VmrTests()
    {
        processManager = new ProcessManager(new NullLogger<ProcessManager>(), "git");
        var assembly = Assembly.GetAssembly(typeof(VmrTests)) ?? throw new Exception("Assembly not found");
        darcExecutable = Path.Join(Path.GetDirectoryName(assembly.Location), "Microsoft.DotNet.Darc.exe");
        darcDll = Path.Join(Path.GetDirectoryName(assembly.Location), "..", "..", "..", "Microsoft.DotNet.Darc", "Debug", "net6.0", "Microsoft.DotNet.Darc.dll");
        //darcExecutable = Path.Join(assembly.Location, "..", "Microsoft.DotNet.Darc.exe");
        var tmpPath = new NativePath(Path.GetTempPath());
        baseDir = tmpPath / "_vmrTests";
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
        _tempDir = baseDir / Path.GetRandomFileName();
        Directory.CreateDirectory(_tempDir);

        _testRepoPath = _tempDir / "test-repo";
        _externalRepoPath = _tempDir / "external-repo";
        _vmrPath = _tempDir / "vmr";
        _tmpPath = _tempDir / "tmp";

        Directory.CreateDirectory(_vmrPath);
        Directory.CreateDirectory(_testRepoPath);
        Directory.CreateDirectory(_testRepoPath / "excluded");
        Directory.CreateDirectory(_tmpPath);
        Directory.CreateDirectory(_externalRepoPath);

        var sourceMappings = string.Format(@"{{
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
        }}", _testRepoPath.Path.Replace("\\", "\\\\"));

        File.WriteAllText(_testRepoPath / "test-repo-file.txt", "Test repo file");
        File.WriteAllText(_testRepoPath / "excluded" / "excluded.txt", "File to be excluded");

        Directory.CreateDirectory(_vmrPath / "src");
        File.WriteAllText(_vmrPath / "src" / "source-mappings.json", sourceMappings);

        await InitialCommit(_vmrPath);
        await InitialCommit(_testRepoPath);
    }

    [TearDown]
    public void CleanUpOutputFile()
    {
        Trace.Flush();
        try
        {
            if (_tempDir is not null)
            {
                DeleteDirectory(_tempDir.ToString());
            }
        }
        catch
        {
            // Ignore
        }
    }

    [Test]
    [NonParallelizable]
    public async Task RepoIsInitializedTest()
    {
        var commit = await GetRepoLastCommit(_testRepoPath);
        string? root = processManager.FindGitRoot(darcExecutable);
        var res = await processManager.Execute("dotnet", new string[] { darcDll, "vmr", "initialize", "--verbose", "--vmr", _vmrPath, "--tmp", _tmpPath, $"test-repo:{commit}" }, workingDir:root);
        Assert.True(res.ExitCode == 0, res.StandardError);
        
        var expectedFiles = new List<string>
        {
            _vmrPath / "git-info" / "AllRepoVersions.props",
            _vmrPath / "git-info" / "test-repo.props",
            _vmrPath / "src" / "source-manifest.json",
            _vmrPath / "src" / "source-mappings.json",
            _vmrPath / "src" / "test-repo" / "test-repo-file.txt"
        };

        CheckDirectoryContents(new DirectoryInfo(_vmrPath.ToString()), expectedFiles);
        await CheckAllIsCommited(_vmrPath);
    }

    [Test]
    [NonParallelizable]
    public async Task FileChangesAreSyncedTest()
    {
        await RepoIsInitializedTest();

        File.AppendAllText(_testRepoPath / "test-repo-file.txt", "Change in test repo file");
        await CommitAll(_testRepoPath, "Changing a file in the repo");

        var commit = await GetRepoLastCommit(_testRepoPath);

        var res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", _vmrPath, "--tmp", _tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        var expectedFiles = new List<string>
        {
            _vmrPath / "git-info" / "AllRepoVersions.props",
            _vmrPath / "git-info" / "test-repo.props",
            _vmrPath / "src" / "source-manifest.json",
            _vmrPath / "src" / "source-mappings.json",
            _vmrPath / "src" / "test-repo" / "test-repo-file.txt",
        };

        CheckDirectoryContents(new DirectoryInfo(_vmrPath), expectedFiles);
        CheckFileContents(_vmrPath / "src" / "test-repo" / "test-repo-file.txt", "Test repo fileChange in test repo file");
        await CheckAllIsCommited(_vmrPath);
    }

    [Test]
    [NonParallelizable]
    public async Task FileIsIncludedTest()
    {
        await RepoIsInitializedTest();

        File.Move(_testRepoPath / "excluded" / "excluded.txt", _testRepoPath / "excluded.txt");
        await CommitAll(_testRepoPath, "Move a file from excluded to included folder");
        var commit = await GetRepoLastCommit(_testRepoPath);

        var res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", _vmrPath, "--tmp", _tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        var expectedFiles = new List<string>
        {
            _vmrPath / "git-info" / "AllRepoVersions.props",
            _vmrPath / "git-info" / "test-repo.props",
            _vmrPath / "src" / "source-manifest.json",
            _vmrPath / "src" / "source-mappings.json",
            _vmrPath / "src" / "test-repo" / "excluded.txt",
            _vmrPath / "src" / "test-repo" / "test-repo-file.txt"
        };

        CheckDirectoryContents(new DirectoryInfo(_vmrPath), expectedFiles);
        await CheckAllIsCommited(_vmrPath);
    }

    [Test]
    [NonParallelizable]
    public async Task RepoIsUpdatedWithAddedSubmoduleTest()
    {
        await RepoIsInitializedTest();

        File.WriteAllText(_externalRepoPath / "external-repo-file.txt", "External repo file");

        var submoduleRelativePath = new NativePath("externals") / "external-repo";
        var submoduleName = "submodule1";

        await InitialCommit(_externalRepoPath);
        await InitializeSubmodule(_testRepoPath, submoduleName, _externalRepoPath, submoduleRelativePath); 
        await CommitAll(_testRepoPath, "Add submodule");

        var commit = await GetRepoLastCommit(_testRepoPath);

        var res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", _vmrPath, "--tmp", _tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        var expectedFiles = new List<string>
        {
            _vmrPath / "git-info" / "AllRepoVersions.props",
            _vmrPath / "git-info" / "test-repo.props",
            _vmrPath / "src" / "source-manifest.json",
            _vmrPath / "src" / "source-mappings.json",
            _vmrPath / "src" / "test-repo" / ".gitmodules",
            _vmrPath / "src" / "test-repo" / "externals" / "external-repo" / "external-repo-file.txt",
            _vmrPath / "src" / "test-repo" / "test-repo-file.txt"
        };

        CheckDirectoryContents(new DirectoryInfo(_vmrPath), expectedFiles);
        await CheckAllIsCommited(_vmrPath);

        // Add a file in the submodule

        File.WriteAllText(_externalRepoPath / "additional-file.txt", "New external repo file");
        await CommitAll(_externalRepoPath, "Adding new file in the submodule");

        await processManager.ExecuteGit(_testRepoPath / submoduleRelativePath, new string[] { "pull", "origin", "main" }, CancellationToken.None);
        await CommitAll(_testRepoPath, "Checkout submodule");
        commit = await GetRepoLastCommit(_testRepoPath);

        res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", _vmrPath, "--tmp", _tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        expectedFiles = new List<string>
        {
            _vmrPath / "git-info" / "AllRepoVersions.props",
            _vmrPath / "git-info" / "test-repo.props",
            _vmrPath / "src" / "source-manifest.json",
            _vmrPath / "src" / "source-mappings.json",
            _vmrPath / "src" / "test-repo" / ".gitmodules",
            _vmrPath / "src" / "test-repo" / "externals" / "external-repo" / "additional-file.txt",
            _vmrPath / "src" / "test-repo" / "externals" / "external-repo" / "external-repo-file.txt",
            _vmrPath / "src" / "test-repo" / "test-repo-file.txt"
        };

        CheckDirectoryContents(new DirectoryInfo(_vmrPath), expectedFiles);
        await CheckAllIsCommited(_vmrPath);

        // Remove submodule

        await RemoveSubmodule(_testRepoPath, submoduleRelativePath, submoduleName);
        await CommitAll(_testRepoPath, "Remove the submodule");
        commit = await GetRepoLastCommit(_testRepoPath);

        res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", _vmrPath, "--tmp", _tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        expectedFiles = new List<string>
        {
            _vmrPath / "git-info" / "AllRepoVersions.props",
            _vmrPath / "git-info" / "test-repo.props",
            _vmrPath / "src" / "source-manifest.json",
            _vmrPath / "src" / "source-mappings.json",
            _vmrPath / "src" / "test-repo" / ".gitmodules",
            _vmrPath / "src" / "test-repo" / "test-repo-file.txt"
        };

        CheckDirectoryContents(new DirectoryInfo(_vmrPath), expectedFiles);
        await CheckAllIsCommited(_vmrPath);
    }

    private void CheckDirectoryContents(DirectoryInfo directory, IList<string> expectedFiles)
    {
        var filesInDir = GetAllFilesInDirectory(directory).OrderBy(x => x).ToList();
        filesInDir.Should().BeEquivalentTo(expectedFiles);
    }

    private void CheckFileContents(LocalPath filePath, string expected)
    {
        var fileContent = File.ReadAllText(filePath);
        fileContent.Should().BeEquivalentTo(expected);
    }

    private async Task CheckAllIsCommited(string repo)
    {
        var gitStatus = await processManager.ExecuteGit(repo, "status", "--porcelain");
        gitStatus.StandardOutput.Should().Be(string.Empty);
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

    private async Task ConfigureGit(LocalPath repo)
    {
        await processManager.ExecuteGit(repo, "config", "user.email", Constants.DarcBotEmail);
        await processManager.ExecuteGit(repo, "config", "user.name", Constants.DarcBotName);
    }

    private async Task CommitAll(LocalPath repo, string commitMessage)
    {
        await processManager.ExecuteGit(repo, "add", "-A");
        await processManager.ExecuteGit(repo, "commit", "-m", commitMessage);
    }

    private async Task InitialCommit(LocalPath repo)
    {
        await processManager.ExecuteGit(repo, "init", "-b", "main");
        await ConfigureGit(repo);
        await CommitAll(repo, "Initial commit");
    }

    private async Task<string> GetRepoLastCommit(LocalPath repo)
    {
        var log = await processManager.ExecuteGit(repo, "log", "--format=format:%H");
        return log.StandardOutput.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).First();
    }

    private async Task InitializeSubmodule(LocalPath repo, string submoduleName, string submoduleUrl, string pathInRepo)
    {
        await processManager.ExecuteGit(repo, 
            "submodule", "add", "--name", 
                submoduleName, "--", submoduleUrl, pathInRepo);
        
        await processManager.ExecuteGit(repo,
            "submodule update", "--init", "--recursive",
                submoduleName, "--", submoduleUrl, pathInRepo);
    }

    private async Task RemoveSubmodule(LocalPath repo, string submoduleRelativePath, string submoduleName)
    {
        await processManager.ExecuteGit(repo, "submodule", "deinit", "-f", submoduleRelativePath);
        DeleteDirectory(repo / ".git" / "modules" / submoduleName);
        await processManager.ExecuteGit(repo, "rm", "-f", submoduleRelativePath);
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

