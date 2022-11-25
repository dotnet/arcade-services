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

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrTests
{
    private string? tempDir;
    private string? testRepoPath;
    private string? externalRepoPath;
    private string? submodulePath;
    private string? vmrPath;
    private string? tmpPath;
    private readonly string baseDir;
    private readonly ProcessManager processManager;
    private readonly string darcExecutable;

    public VmrTests()
    {
        processManager = new ProcessManager(new NullLogger<ProcessManager>(), "git");
        var assembly = Assembly.GetAssembly(typeof(VmrTests));
        if(assembly == null)
        {
            throw new Exception("Assembly not found");
        }

        darcExecutable = Path.Combine(assembly.Location, "..", "Microsoft.DotNet.Darc.exe");
        baseDir = Path.Combine(Path.GetTempPath(), "_vmrTests");
    }

    [SetUp]
    public async Task SetUp()
    {
        tempDir = Path.Combine(baseDir, Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        testRepoPath = Path.Combine(tempDir, "test-repo");
        externalRepoPath = Path.Combine(tempDir, "external-repo");
        submodulePath = Path.Combine(tempDir, "test-repo", "externals", "external-repo");
        vmrPath = Path.Combine(tempDir, "vmr");
        tmpPath = Path.Combine(tempDir, "tmp");

        Directory.CreateDirectory(vmrPath);
        Directory.CreateDirectory(testRepoPath);
        Directory.CreateDirectory(Path.Combine(testRepoPath, "excluded"));
        Directory.CreateDirectory(tmpPath);
        Directory.CreateDirectory(externalRepoPath);

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
        }}", testRepoPath.Replace("\\", "\\\\"));

        File.WriteAllText(Path.Combine(testRepoPath, "test-repo-file.txt"), "Test repo file");
        File.WriteAllText(Path.Combine(testRepoPath, "excluded", "excluded.txt"), "File to be excluded");

        Directory.CreateDirectory(Path.Combine(vmrPath, "src"));
        File.WriteAllText(Path.Combine(vmrPath, "src", "source-mappings.json"), sourceMappings);

        await InitialCommit(vmrPath);
        await InitialCommit(testRepoPath);
    }

    [TearDown]
    public void CleanUpOutputFile()
    {
        try
        {
            if (tempDir is not null)
            {
                DeleteDirectory(tempDir);
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
        if (testRepoPath == null || vmrPath == null || tmpPath == null)
        {
            throw new Exception("Filenames are not initialized");
        }

        var commit = await GetRepoLastCommit(testRepoPath);
        
        var res = await processManager.Execute(darcExecutable, new string[] { "vmr", "initialize", "--vmr", vmrPath, "--tmp", tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        var expectedFiles = new List<string>
        {
            Path.Combine(vmrPath, "git-info", "AllRepoVersions.props"),
            Path.Combine(vmrPath, "git-info", "test-repo.props"),
            Path.Combine(vmrPath, "src", "source-manifest.json"),
            Path.Combine(vmrPath, "src", "source-mappings.json"),
            Path.Combine(vmrPath, "src", "test-repo", "test-repo-file.txt")
        };

        CheckDirectoryContents(new DirectoryInfo(vmrPath), expectedFiles);
        await CheckAllIsCommited(vmrPath);
    }

    [Test]
    public async Task FileChangesAreSyncedTest()
    {
        if (testRepoPath == null || vmrPath == null || tmpPath == null)
        {
            throw new Exception("Filenames are not initialized");
        }

        await RepoIsInitializedTest();

        File.AppendAllText(Path.Combine(testRepoPath, "test-repo-file.txt"), "Change in test repo file");
        await CommitAll(testRepoPath, "Changing a file in the repo");

        var commit = await GetRepoLastCommit(testRepoPath);

        var res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", vmrPath, "--tmp", tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        var expectedFiles = new List<string>
        {
            Path.Combine(vmrPath, "git-info", "AllRepoVersions.props"),
            Path.Combine(vmrPath, "git-info", "test-repo.props"),
            Path.Combine(vmrPath, "src", "source-manifest.json"),
            Path.Combine(vmrPath, "src", "source-mappings.json"),
            Path.Combine(vmrPath, "src", "test-repo", "test-repo-file.txt"),
        };

        CheckDirectoryContents(new DirectoryInfo(vmrPath), expectedFiles);
        File.ReadAllText(Path.Combine(vmrPath, "src", "test-repo", "test-repo-file.txt"))
            .Should().Contain("Change in test repo file");
        await CheckAllIsCommited(vmrPath);
    }

    [Test]
    public async Task FileIsIncludedTest()
    {
        if (testRepoPath == null || vmrPath == null || tmpPath == null)
        {
            throw new Exception("Filenames are not initialized");
        }

        await RepoIsInitializedTest();

        File.Move(Path.Combine(testRepoPath, "excluded", "excluded.txt"), Path.Combine(testRepoPath, "excluded.txt"));
        await CommitAll(testRepoPath, "Move a file from excluded to included folder");
        var commit = await GetRepoLastCommit(testRepoPath);

        var res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", vmrPath, "--tmp", tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        var expectedFiles = new List<string>
        {
            Path.Combine(vmrPath, "git-info", "AllRepoVersions.props"),
            Path.Combine(vmrPath, "git-info", "test-repo.props"),
            Path.Combine(vmrPath, "src", "source-manifest.json"),
            Path.Combine(vmrPath, "src", "source-mappings.json"),
            Path.Combine(vmrPath, "src", "test-repo", "excluded.txt"),
            Path.Combine(vmrPath, "src", "test-repo", "test-repo-file.txt")
        };

        CheckDirectoryContents(new DirectoryInfo(vmrPath), expectedFiles);
        await CheckAllIsCommited(vmrPath);
    }

    [Test]
    public async Task RepoIsUpdatedWithAddedSubmoduleTest()
    {
        if (testRepoPath == null || vmrPath == null || tmpPath == null || externalRepoPath == null || submodulePath == null)
        {
            throw new Exception("Filenames are not initialized");
        }

        await RepoIsInitializedTest();

        File.WriteAllText(Path.Combine(externalRepoPath, "external-repo-file.txt"), "External repo file");

        await InitialCommit(externalRepoPath);
        await InitializeSubmodule(testRepoPath, "submodule1", externalRepoPath, submodulePath); 
        await CommitAll(testRepoPath, "Add submodule");

        var commit = await GetRepoLastCommit(testRepoPath);

        var res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", vmrPath, "--tmp", tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        var expectedFiles = new List<string>
        {
            Path.Combine(vmrPath, "git-info", "AllRepoVersions.props"),
            Path.Combine(vmrPath, "git-info", "test-repo.props"),
            Path.Combine(vmrPath, "src", "source-manifest.json"),
            Path.Combine(vmrPath, "src", "source-mappings.json"),
            Path.Combine(vmrPath, "src", "test-repo", ".gitmodules"),
            Path.Combine(vmrPath, "src", "test-repo", "externals", "external-repo", "external-repo-file.txt"),
            Path.Combine(vmrPath, "src", "test-repo", "test-repo-file.txt")
        };

        CheckDirectoryContents(new DirectoryInfo(vmrPath), expectedFiles);
        await CheckAllIsCommited(vmrPath);

        // Add a file in the submodule

        File.WriteAllText(Path.Combine(externalRepoPath, "additional-file.txt"), "New external repo file");
        await CommitAll(externalRepoPath, "Adding new file in the submodule");

        await processManager.ExecuteGit(submodulePath, new string[] { "pull", "origin", "main" }, CancellationToken.None);
        await CommitAll(testRepoPath, "Checkout submodule");
        commit = await GetRepoLastCommit(testRepoPath);

        res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", vmrPath, "--tmp", tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        expectedFiles = new List<string>
        {
            Path.Combine(vmrPath, "git-info", "AllRepoVersions.props"),
            Path.Combine(vmrPath, "git-info", "test-repo.props"),
            Path.Combine(vmrPath, "src", "source-manifest.json"),
            Path.Combine(vmrPath, "src", "source-mappings.json"),
            Path.Combine(vmrPath, "src", "test-repo", ".gitmodules"),
            Path.Combine(vmrPath, "src", "test-repo", "externals", "external-repo", "additional-file.txt"),
            Path.Combine(vmrPath, "src", "test-repo", "externals", "external-repo", "external-repo-file.txt"),
            Path.Combine(vmrPath, "src", "test-repo", "test-repo-file.txt")
        };

        CheckDirectoryContents(new DirectoryInfo(vmrPath), expectedFiles);
        await CheckAllIsCommited(vmrPath);

        // Remove submodule

        await RemoveSubmodule(testRepoPath, submodulePath);
        await CommitAll(testRepoPath, "Remove the submodule");
        commit = await GetRepoLastCommit(testRepoPath);

        res = await processManager.Execute(darcExecutable, new string[] { "vmr", "update", "--vmr", vmrPath, "--tmp", tmpPath, $"test-repo:{commit}" });
        res.ExitCode.Should().Be(0);

        expectedFiles = new List<string>
        {
            Path.Combine(vmrPath, "git-info", "AllRepoVersions.props"),
            Path.Combine(vmrPath, "git-info", "test-repo.props"),
            Path.Combine(vmrPath, "src", "source-manifest.json"),
            Path.Combine(vmrPath, "src", "source-mappings.json"),
            Path.Combine(vmrPath, "src", "test-repo", ".gitmodules"),
            Path.Combine(vmrPath, "src", "test-repo", "test-repo-file.txt")
        };

        CheckDirectoryContents(new DirectoryInfo(vmrPath), expectedFiles);
        await CheckAllIsCommited(vmrPath);
    }

    private void CheckDirectoryContents(DirectoryInfo directory, IList<string> expectedFiles)
    {
        var filesInDir = GetAllFilesInDirectory(directory).OrderBy(x => x).ToList();
        filesInDir.Count.Should().Be(expectedFiles.Count);
        for(int i = 0; i < expectedFiles.Count; i++)
        {
            filesInDir[i].Should().Be(expectedFiles[i]);
        }
    }

    private async Task CheckAllIsCommited(string repo)
    {
        var gitStatus = await processManager.ExecuteGit(repo, new string[] { "status"}, CancellationToken.None);
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

    private async Task CommitAll(string repo, string commitMsg)
    {
        await processManager.ExecuteGit(repo, new string[] { "add", "-A" }, CancellationToken.None);
        await processManager.ExecuteGit(repo, new string[] { "commit", "-m", commitMsg }, CancellationToken.None);
    }

    private async Task InitialCommit(string repo)
    {
        await processManager.ExecuteGit(repo, new string[] { "init", "-b", "main" }, CancellationToken.None);
        await CommitAll(repo, "Initial commit");
    }

    private async Task<string> GetRepoLastCommit(string repo)
    {
        var log = await processManager.ExecuteGit(repo, new string[] { "log", "--format=format:%H" }, CancellationToken.None);
        return log.StandardOutput.TrimEnd('\n').TrimEnd('\r').Split("\r\n").First();
    }

    private async Task InitializeSubmodule(string repo, string submoduleName, string submoduleUrl, string pathInRepo)
    {
        await processManager.ExecuteGit(repo, 
            new string[] { "submodule", "add", "--name", 
                submoduleName, "--", submoduleUrl, "externals/external-repo" }, CancellationToken.None);
        
        await processManager.ExecuteGit(repo,
            new string[] { "submodule update", "--init", "--recursive",
                submoduleName, "--", submoduleUrl, "externals/external-repo" }, CancellationToken.None);
    }

    private async Task RemoveSubmodule(string repo, string submodulePath)
    {
        await processManager.ExecuteGit(repo, new string[] {"rm", submodulePath}, CancellationToken.None);
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

