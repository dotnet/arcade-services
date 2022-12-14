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
using FluentAssertions.Collections;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrSyncToolingE2ETest
{
    private LocalPath _currentTestDirectory = null!;
    private LocalPath _commonPrivateRepoPath = null!;
    private LocalPath _commonDependencyRepoPath = null!;
    private LocalPath _commonVmrPath = null!;
    private LocalPath _privateRepoPath = null!;
    private LocalPath _externalRepoPath = null!;
    private LocalPath _dependencyRepoPath = null!;
    private LocalPath _vmrPath = null!;
    private LocalPath _tmpPath = null!;
    private readonly LocalPath _testsDirectory;
    private readonly IProcessManager _processManager;
    private readonly string _darcExecutable;
    private readonly string _sourceMappingsTemplate;
    private readonly string _emptyVersionDetails;
    private readonly string _versionDetailsName = "Version.Details.xml";


    public VmrSyncToolingE2ETest()
    {
        _processManager = new ProcessManager(new NullLogger<ProcessManager>(), "git");
        var assembly = Assembly.GetAssembly(typeof(VmrSyncToolingE2ETest)) ?? throw new Exception("Assembly not found");
        _darcExecutable = Path.Join(Path.GetDirectoryName(assembly.Location), "Microsoft.DotNet.Darc.exe");
        _testsDirectory = new NativePath(Path.GetTempPath()) / "_vmrTests" / Path.GetRandomFileName();
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
              }},
              {{
                ""name"": ""dependency"",
                ""defaultRemote"": ""{1}""
              }}
            ]
        }}";

        _emptyVersionDetails =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
               <Dependencies>
                <ProductDependencies>
                </ProductDependencies>
                <ToolsetDependencies>
                </ToolsetDependencies>
               </Dependencies>";

    }

    [OneTimeSetUp]
    public async Task SetUpCommonRepos()
    {
        Directory.CreateDirectory(_testsDirectory);

        _commonPrivateRepoPath = _testsDirectory / "test-repo";
        _commonVmrPath = _testsDirectory / "vmr";
        _commonDependencyRepoPath = _testsDirectory / "dependency";
       
        Directory.CreateDirectory(_commonVmrPath);
        Directory.CreateDirectory(_commonVmrPath / "src");
        Directory.CreateDirectory(_commonDependencyRepoPath);
        Directory.CreateDirectory(_commonPrivateRepoPath);
        Directory.CreateDirectory(_commonPrivateRepoPath / "excluded");
        Directory.CreateDirectory(_commonPrivateRepoPath / "eng");
        Directory.CreateDirectory(_commonDependencyRepoPath / "eng");
        File.WriteAllText(_commonPrivateRepoPath / "test-repo-file.txt", "Test repo file");
        File.WriteAllText(_commonPrivateRepoPath / "excluded" / "excluded.txt", "File to be excluded");
        File.WriteAllText(_commonDependencyRepoPath / "dependencyFile.txt", "File in the dependency repo");
        File.WriteAllText(_commonDependencyRepoPath / "eng" / _versionDetailsName, _emptyVersionDetails);
       
        await InitialCommit(_commonVmrPath);
        await InitialCommit(_commonPrivateRepoPath);
        await InitialCommit(_commonDependencyRepoPath);
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
        _dependencyRepoPath = _currentTestDirectory / "dependency"; 

        Directory.CreateDirectory(_tmpPath);
        CopyDirectory(_commonVmrPath, _vmrPath);
        CopyDirectory(_commonPrivateRepoPath, _privateRepoPath);
        CopyDirectory(_commonDependencyRepoPath, _dependencyRepoPath);

        File.WriteAllText(
            _vmrPath / "src" / "source-mappings.json",
            string.Format(_sourceMappingsTemplate,
            _privateRepoPath.Path.Replace("\\", "\\\\"),
            _dependencyRepoPath.Path.Replace("\\", "\\\\")));

        await CommitAll(_vmrPath, "Add source mappings");

        var versionDetails = 
            @"<?xml version=""1.0"" encoding=""utf-8""?>
            <Dependencies>
            <ProductDependencies>
            </ProductDependencies>
            <ToolsetDependencies>
            <Dependency Name=""Dependency"" Version=""8.0.0"">
            <Uri>{0}</Uri>
            <Sha>{1}</Sha>
            <SourceBuild RepoName=""dependency"" ManagedOnly=""true"" />
            </Dependency>
            </ToolsetDependencies>
            </Dependencies>";

        var commit = await GetRepoLastCommit(_dependencyRepoPath);
        File.WriteAllText(_privateRepoPath / "eng" / _versionDetailsName, string.Format(versionDetails, new object[] { _dependencyRepoPath.Path, commit }));
        await CommitAll(_privateRepoPath, "Add version details file");
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
        await EnsureTestRepoIsInitialized();
    }

    [Test]
    public async Task FileChangesAreSyncedTest()
    {
        var testRepoFilePath = _vmrPath / "src" / "test-repo" / "test-repo-file.txt";
        var dependencyFilePath = _vmrPath / "src" / "dependency" / "dependencyFile.txt";

        await EnsureTestRepoIsInitialized();

        File.WriteAllText(_privateRepoPath / "test-repo-file.txt", "Test changes in repo file");
        await CommitAll(_privateRepoPath, "Changing a file in the repo");

        var commit = await GetRepoLastCommit(_privateRepoPath);
        await CallDarcUpdate("test-repo", commit);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            testRepoFilePath,
            dependencyFilePath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { "test-repo", "dependency" },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CheckFileContents(testRepoFilePath, "Test changes in repo file");
        CheckFileContents(dependencyFilePath, "File in the dependency repo");
        await CheckAllIsCommited(_vmrPath);
    }

    [Test]
    public async Task FileIsIncludedTest()
    {
        await EnsureTestRepoIsInitialized();

        File.Move(_privateRepoPath / "excluded" / "excluded.txt", _privateRepoPath / "excluded.txt");
        await CommitAll(_privateRepoPath, "Move a file from excluded to included folder");
        
        var commit = await GetRepoLastCommit(_privateRepoPath);
        await CallDarcUpdate("test-repo", commit);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _vmrPath / "src" / "test-repo" / "test-repo-file.txt",
            _vmrPath / "src" / "dependency" / "dependencyFile.txt",
            _vmrPath / "src" / "test-repo" / "excluded.txt",
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { "test-repo", "dependency" },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CheckFileContents(_vmrPath / "src" / "test-repo" / "excluded.txt", "File to be excluded");
        await CheckAllIsCommited(_vmrPath);
    }

    [Test]
    public async Task SubmodulesAreInlinedProperlyTest()
    {
        var testRepoFilePath = _vmrPath / "src" / "test-repo" / "test-repo-file.txt";
        var dependencyFilePath = _vmrPath / "src" / "dependency" / "dependencyFile.txt";
        var submoduleFilePath = _vmrPath / "src" / "test-repo" / "externals" / "external-repo" / "external-repo-file.txt";
        var additionalSubmoduleFilePath = _vmrPath / "src" / "test-repo" / "externals" / "external-repo" / "additional-file.txt";

        await EnsureTestRepoIsInitialized();

        _externalRepoPath = _currentTestDirectory / "external-repo";
        Directory.CreateDirectory(_externalRepoPath);
        File.WriteAllText(_externalRepoPath / "external-repo-file.txt", "External repo file");
        await InitialCommit(_externalRepoPath);

        var submoduleRelativePath = new NativePath("externals") / "external-repo";
        await InitializeSubmodule(_privateRepoPath, "submodule1", _externalRepoPath, submoduleRelativePath); 
        await CommitAll(_privateRepoPath, "Add submodule");

        var commit = await GetRepoLastCommit(_privateRepoPath);
        await CallDarcUpdate("test-repo", commit);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            testRepoFilePath,
            dependencyFilePath,
            submoduleFilePath,
            _vmrPath / "src" / "test-repo" / ".gitmodules",
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { "test-repo", "dependency" },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CheckFileContents(testRepoFilePath, "Test repo file");
        CheckFileContents(dependencyFilePath, "File in the dependency repo");
        CheckFileContents(submoduleFilePath, "External repo file");
        await CheckAllIsCommited(_vmrPath);

        // Add a file in the submodule

        File.WriteAllText(_externalRepoPath / "additional-file.txt", "New external repo file");
        await CommitAll(_externalRepoPath, "Adding new file in the submodule");

        await _processManager.ExecuteGit(_privateRepoPath / submoduleRelativePath,
            new string[] { "pull", "origin", "main" },
            CancellationToken.None);

        await CommitAll(_privateRepoPath, "Checkout submodule");
        
        commit = await GetRepoLastCommit(_privateRepoPath);
        await CallDarcUpdate("test-repo", commit);

        expectedFiles.Add(additionalSubmoduleFilePath);

        CheckDirectoryContents(_vmrPath, expectedFiles);
        await CheckAllIsCommited(_vmrPath);

        // Remove submodule

        await RemoveSubmodule(_privateRepoPath, submoduleRelativePath);
        await CommitAll(_privateRepoPath, "Remove the submodule");
        
        commit = await GetRepoLastCommit(_privateRepoPath);
        await CallDarcUpdate("test-repo", commit);

        expectedFiles.Remove(submoduleFilePath);
        expectedFiles.Remove(additionalSubmoduleFilePath);

        CheckDirectoryContents(_vmrPath, expectedFiles);
        await CheckAllIsCommited(_vmrPath);
    }

    [Test]
    public async Task NonSrcContentIsSyncedTest()
    {
        var repoName = "special-repo";
        var repoPath = _currentTestDirectory / repoName;
        var filePath = repoPath / "content" / "special-file.txt";
        var versionDetailsPath = repoPath / "eng" / _versionDetailsName;
        var sourceMappingsPath = _vmrPath / "src" / "source-mappings.json";

        // add additional mappings in source-mappings.json

        var sourceMappings = @"{{
            ""additionalMappings"": [
               {{
                   ""source"": ""src/special-repo/content"",
                   ""destination"": """"
               }}
            ],
            ""defaults"": {{
              ""defaultRef"": ""main"",
              ""exclude"": [
                ""**/*.dll""
              ]
            }},
            ""mappings"": [
              {{
                ""name"": ""special-repo"",
                ""defaultRemote"": ""{0}""
              }}
            ]
        }}";

        
        Directory.CreateDirectory(repoPath);
        Directory.CreateDirectory(repoPath / "content" );
        Directory.CreateDirectory(repoPath / "eng");
        File.WriteAllText(
            filePath,
            "A file that needs to be copied outside of the src folder");
        File.WriteAllText(versionDetailsPath, _emptyVersionDetails);

        await InitialCommit(repoPath);

        File.WriteAllText(
            sourceMappingsPath,
            string.Format(
                sourceMappings,
                new[] 
                {
                    repoPath.Path.Replace("\\", "\\\\"),
                }));
        
        await CommitAll(_vmrPath, "Replace source mappings");

        // Initialize the repo

        var commit = await GetRepoLastCommit(repoPath);
        await CallDarcInitialize(repoName, commit);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _vmrPath / "src" / repoName / "content" / "special-file.txt",
            _vmrPath / "special-file.txt"
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { repoName},
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        await CheckAllIsCommited(_vmrPath);

        // Change a file in the mapped folder

        File.WriteAllText(filePath, "A file with a change that needs to be copied outside of the src folder");
        await CommitAll(repoPath, "Change file");
        commit = await GetRepoLastCommit(repoPath);
        await CallDarcUpdate(repoName, commit);

        CheckFileContents(_vmrPath / "special-file.txt", "A file with a change that needs to be copied outside of the src folder");
        await CheckAllIsCommited(_vmrPath);
    }

    private List<LocalPath> GetExpectedFilesInVmr(
        LocalPath vmrPath,
        string[] syncedRepos,
        List<LocalPath> reposFiles)
    {
        var expectedFiles = new List<LocalPath>
        {
            vmrPath / "git-info" / "AllRepoVersions.props",
            vmrPath / "src" / "source-manifest.json",
            vmrPath / "src" / "source-mappings.json",
        };

        foreach(var repo in syncedRepos)
        {
            expectedFiles.Add(vmrPath / "src" / repo / "eng" / _versionDetailsName);
            expectedFiles.Add(vmrPath / "git-info" / $"{repo}.props");
        }
        
        expectedFiles.AddRange(reposFiles);
        
        return expectedFiles;
    }

    private void CheckDirectoryContents(string directory, IList<LocalPath> expectedFiles)
    {
        var filesInDir = GetAllFilesInDirectory(new DirectoryInfo(directory));
        filesInDir.Should().BeEquivalentTo(expectedFiles);
    }

    private static void CheckFileContents(LocalPath filePath, string expected)
    {
        var fileContent = File.ReadAllText(filePath);
        fileContent.Should().BeEquivalentTo(expected);
    }

    private async Task CheckAllIsCommited(string repo)
    {
        var gitStatus = await _processManager.ExecuteGit(repo, "status", "--porcelain");
        gitStatus.StandardOutput.Should().BeEmpty();
    }

    private async Task EnsureTestRepoIsInitialized()
    {
        var testRepoFilePath = _vmrPath / "src" / "test-repo" / "test-repo-file.txt";
        var dependencyFilePath = _vmrPath / "src" / "dependency" / "dependencyFile.txt";

        var commit = await GetRepoLastCommit(_privateRepoPath);
        await CallDarcInitialize("test-repo", commit);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            testRepoFilePath,
            dependencyFilePath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { "test-repo", "dependency" },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CheckFileContents(testRepoFilePath, "Test repo file");
        CheckFileContents(dependencyFilePath, "File in the dependency repo");
        await CheckAllIsCommited(_vmrPath);
    }

    private async Task CallDarcInitialize(string repository, string commit)
    {
        await CallDarcVmrCommand("initialize", new[] { $"{repository}:{commit}" });
    }

    private async Task CallDarcUpdate(string repository, string commit)
    {
        await CallDarcVmrCommand("update", new[] { $"{repository}:{commit}" });
    }

    private async Task CallDarcVmrCommand(string command, string[] arguments)
    {
        var args = new List<string>
        {
            "vmr",
            command,
            "--recursive",
            "--vmr",
            _vmrPath,
            "--tmp",
            _tmpPath
        };

        args.AddRange(arguments);

        var res = await _processManager.Execute(_darcExecutable, args);
        res.ExitCode.Should().Be(0, res.ToString());
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
        return log.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).First();
    }

    private async Task InitializeSubmodule(
        LocalPath repo,
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

    private async Task RemoveSubmodule(LocalPath repo, string submoduleRelativePath)
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

    private ICollection<LocalPath> GetAllFilesInDirectory(DirectoryInfo directory)
    {
        var files = new List<LocalPath>();

        if (directory.Name == ".git")
        {
            return files;
        }

        files.AddRange(directory.GetFiles().Select(f => new NativePath(f.FullName)));

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

