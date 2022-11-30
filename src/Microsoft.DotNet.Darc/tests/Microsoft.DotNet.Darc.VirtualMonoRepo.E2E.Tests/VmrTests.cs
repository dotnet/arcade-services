// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrTests
{
    private LocalPath _currentTestDirectory = null!;
    private LocalPath _commonPrivateRepoPath = null!;
    private LocalPath _commonVmrPath = null!;
    private LocalPath _privateRepoPath = null!;
    private LocalPath _externalRepoPath = null!;
    private LocalPath _vmrPath = null!;
    private LocalPath _tmpPath = null!;
    private readonly LocalPath _testsDirectory;
    private readonly IProcessManager _processManager;
    private readonly string _darcDll;
    private readonly string _sourceMappingsTemplate;


    public VmrTests()
    {
        _processManager = new ProcessManager(new NullLogger<ProcessManager>(), "git");
        var assembly = Assembly.GetAssembly(typeof(VmrTests)) ?? throw new Exception("Assembly not found");
        _darcDll = Path.Join(Path.GetDirectoryName(assembly.Location), "Microsoft.DotNet.Darc.dll");
        _testsDirectory = new NativePath(Path.GetTempPath()) / Path.GetRandomFileName();
        _sourceMappingsTemplate = @"{{
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
        }}";
    }

    [OneTimeSetUp]
    public async Task SetUpCommonRepos()
    {
        Directory.CreateDirectory(_testsDirectory);

        _commonPrivateRepoPath = _testsDirectory / "test-repo";
        _commonVmrPath = _testsDirectory / "vmr";
       
        Directory.CreateDirectory(_commonVmrPath);
        Directory.CreateDirectory(_commonPrivateRepoPath);
        Directory.CreateDirectory(_commonPrivateRepoPath / "excluded");
        
        File.WriteAllText(_commonPrivateRepoPath / "test-repo-file.txt", "Test repo file");
        File.WriteAllText(_commonPrivateRepoPath / "excluded" / "excluded.txt", "File to be excluded");

        Directory.CreateDirectory(_commonVmrPath / "src");
        
        await InitialCommit(_commonVmrPath);
        await InitialCommit(_commonPrivateRepoPath);
    }

    [OneTimeTearDown]
    public void DeleteTestsDirectory()
    {
        try
        {
            if (_testsDirectory is not null)
            {
                DeleteDirectory(_testsDirectory);
            }
        }
        catch
        {
            // Ignore
        }
    }

    [SetUp]
    public async Task CopyTestRepos()
    {
        _currentTestDirectory = _testsDirectory / Path.GetRandomFileName();
        Directory.CreateDirectory(_currentTestDirectory);

        _tmpPath = _currentTestDirectory / "tmp";
        _privateRepoPath = _currentTestDirectory / "test-repo";
        _vmrPath = _currentTestDirectory / "vmr";

        Directory.CreateDirectory(_tmpPath);
        CopyDirectory(_commonVmrPath, _vmrPath);
        CopyDirectory(_commonPrivateRepoPath, _privateRepoPath);

        File.WriteAllText(_vmrPath / "src" / "source-mappings.json", string.Format(_sourceMappingsTemplate, _privateRepoPath.Path.Replace("\\", "\\\\")));
        await CommitAll(_vmrPath, "Add source mappings"); 
    }

    [TearDown]
    public void DeleteCurrentTestDirectory()
    {
        try
        {
            if (_currentTestDirectory is not null)
            {
                DeleteDirectory(_currentTestDirectory.ToString());
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
        var commit = await GetRepoLastCommit(_privateRepoPath);
        await CallDarcInitialize("test-repo", commit);

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
    public async Task FileChangesAreSyncedTest()
    {
        await RepoIsInitializedTest();

        File.AppendAllText(_privateRepoPath / "test-repo-file.txt", "Change in test repo file");
        await CommitAll(_privateRepoPath, "Changing a file in the repo");

        var commit = await GetRepoLastCommit(_privateRepoPath);
        await CallDarcUpdate("test-repo", commit);

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
    public async Task FileIsIncludedTest()
    {
        await RepoIsInitializedTest();

        File.Move(_privateRepoPath / "excluded" / "excluded.txt", _privateRepoPath / "excluded.txt");
        await CommitAll(_privateRepoPath, "Move a file from excluded to included folder");
        
        var commit = await GetRepoLastCommit(_privateRepoPath);
        await CallDarcUpdate("test-repo", commit);

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
    public async Task SubmodulesAreInlinedProperlyTest()
    {
        await RepoIsInitializedTest();

        _externalRepoPath = _currentTestDirectory / "external-repo";
        Directory.CreateDirectory(_externalRepoPath);
        File.WriteAllText(_externalRepoPath / "external-repo-file.txt", "External repo file");
        await InitialCommit(_externalRepoPath);

        var submoduleRelativePath = new NativePath("externals") / "external-repo";
        var submoduleName = "submodule1";
        await InitializeSubmodule(_privateRepoPath, submoduleName, _externalRepoPath, submoduleRelativePath); 
        await CommitAll(_privateRepoPath, "Add submodule");

        var commit = await GetRepoLastCommit(_privateRepoPath);
        await CallDarcUpdate("test-repo", commit);

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

        await _processManager.ExecuteGit(_privateRepoPath / submoduleRelativePath, new string[] { "pull", "origin", "main" }, CancellationToken.None);
        await CommitAll(_privateRepoPath, "Checkout submodule");
        
        commit = await GetRepoLastCommit(_privateRepoPath);
        await CallDarcUpdate("test-repo", commit);

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

        await RemoveSubmodule(_privateRepoPath, submoduleRelativePath, submoduleName);
        await CommitAll(_privateRepoPath, "Remove the submodule");
        
        commit = await GetRepoLastCommit(_privateRepoPath);
        await CallDarcUpdate("test-repo", commit);

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
        var gitStatus = await _processManager.ExecuteGit(repo, "status", "--porcelain");
        gitStatus.StandardOutput.Should().Be(string.Empty);
    }

    private async Task CallDarcInitialize(string repoName, string commit)
    {
        var res = await _processManager.Execute("dotnet", new string[] { _darcDll, "vmr", "initialize", "--verbose", "--vmr", _vmrPath, "--tmp", _tmpPath, $"{repoName}:{commit}" });
        Assert.True(res.ExitCode == 0, res.StandardError);
    }

    private async Task CallDarcUpdate(string repoName, string commit)
    {
        var res = await _processManager.Execute("dotnet", new string[] { _darcDll, "vmr", "update", "--vmr", _vmrPath, "--tmp", _tmpPath, $"{repoName}:{commit}" });
        Assert.True(res.ExitCode == 0, res.StandardError);
    }

    private async Task ConfigureGit(LocalPath repo)
    {
        await _processManager.ExecuteGit(repo, "config", "user.email", Constants.DarcBotEmail);
        await _processManager.ExecuteGit(repo, "config", "user.name", Constants.DarcBotName);
    }

    private async Task CommitAll(LocalPath repo, string commitMessage)
    {
        await _processManager.ExecuteGit(repo, "add", "-A");
        await _processManager.ExecuteGit(repo, "commit", "-m", commitMessage);
    }

    private async Task InitialCommit(LocalPath repo)
    {
        await _processManager.ExecuteGit(repo, "init", "-b", "main");
        await ConfigureGit(repo);
        await CommitAll(repo, "Initial commit");
    }

    private async Task<string> GetRepoLastCommit(LocalPath repo)
    {
        var log = await _processManager.ExecuteGit(repo, "log", "--format=format:%H");
        return log.StandardOutput.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).First();
    }

    private async Task InitializeSubmodule(LocalPath repo, string submoduleName, string submoduleUrl, string pathInRepo)
    {
        await _processManager.ExecuteGit(repo,
            "-c", "protocol.file.allow=always",
            "submodule", "add", "--name", 
                submoduleName, "--", submoduleUrl, pathInRepo);
        
        await _processManager.ExecuteGit(repo,
            "submodule update", "--init", "--recursive",
                submoduleName, "--", submoduleUrl, pathInRepo);
    }

    private async Task RemoveSubmodule(LocalPath repo, string submoduleRelativePath, string submoduleName)
    {
        await _processManager.ExecuteGit(repo, "rm", "-f", submoduleRelativePath);
    }

    private void CopyDirectory(string source, LocalPath destination)
    {
        if (!Directory.Exists(destination))
        {
            Directory.CreateDirectory(destination);
        }

        DirectoryInfo sourceDir = new(source);

        FileInfo[] files = sourceDir.GetFiles();
        foreach (FileInfo file in files)
        {
            file.CopyTo(destination / file.Name, true);
        }

        DirectoryInfo[] subDirs = sourceDir.GetDirectories();
        foreach (DirectoryInfo dir in subDirs)
        {
            CopyDirectory(dir.FullName, destination / dir.Name);
        }
    }

    private ICollection<string> GetAllFilesInDirectory(DirectoryInfo directory)
    {
        var files = new List<string>();

        if (directory.Name == ".git")
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

