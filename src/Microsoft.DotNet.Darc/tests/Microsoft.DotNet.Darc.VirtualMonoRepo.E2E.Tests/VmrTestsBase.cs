// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using FluentAssertions;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using static Microsoft.DotNet.Darc.Tests.VirtualMonoRepo.VmrTestsBase;


namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public abstract class VmrTestsBase
{
    private Lazy<IServiceProvider> _serviceProvider = null!;
    private readonly CancellationTokenSource _cancellationToken = new();
    protected LocalPath _currentTestDirectory = null!;
    protected LocalPath _privateRepoPath = null!;
    protected LocalPath _vmrPath = null!;
    protected LocalPath _tmpPath = null!;
    protected LocalPath _externalRepoPath = null!;
    protected LocalPath _dependencyRepoPath = null!;
    protected LocalPath _specialRepoPath = null!;
    protected LocalPath _installerRepoPath = null!;
    protected GitOperationsHelper GitOperations { get; } = new();
    protected VmrInfo vmrInfo = null!;
   
    [SetUp]
    public async Task Setup()
    {
        _currentTestDirectory = VmrTestsOneTimeSetUp.TestsDirectory / Path.GetRandomFileName();
        Directory.CreateDirectory(_currentTestDirectory);

        _tmpPath = _currentTestDirectory / "tmp";
        _privateRepoPath = _currentTestDirectory / "test-repo";
        _vmrPath = _currentTestDirectory / "vmr";
        _externalRepoPath = _currentTestDirectory / "external-repo";
        _dependencyRepoPath = _currentTestDirectory / "dependency";
        _specialRepoPath = _currentTestDirectory / "special-repo";
        _installerRepoPath = _currentTestDirectory / "installer";
        _externalRepoPath = _currentTestDirectory / "external-repo";

        Directory.CreateDirectory(_tmpPath);

        await CopyVmrForCurrentTest();
        await CopyReposForCurrentTest();
        _serviceProvider = new(CreateServiceProvider);
        vmrInfo = (VmrInfo)_serviceProvider.Value.GetRequiredService<IVmrInfo>();
    }

    [TearDown]
    public void DeleteCurrentTestDirectory()
    {
        try
        {
            if (_currentTestDirectory is not null)
            {
                VmrTestsOneTimeSetUp.DeleteDirectory(_currentTestDirectory.ToString());
            }
        }
        catch
        {
            // Ignore
        }
    }

    protected abstract Task CopyReposForCurrentTest();

    protected abstract Task CopyVmrForCurrentTest();

    private IServiceProvider CreateServiceProvider() => new ServiceCollection()
        .AddTransient<GitFileManagerFactory>()
        .AddLogging(b => b.AddConsole().AddFilter(l => l >= LogLevel.Information))
        .AddVmrManagers(
        sp => sp.GetRequiredService<GitFileManagerFactory>(),
        "git",
        _vmrPath,
        _tmpPath,
        null,
        null)
        .BuildServiceProvider();

    protected List<LocalPath> GetExpectedFilesInVmr(
        LocalPath vmrPath,
        string[] syncedRepos,
        List<LocalPath> reposFiles)
    {
        var expectedFiles = new List<LocalPath>
        {
            vmrPath / VmrInfo.GitInfoSourcesDir / AllVersionsPropsFile.FileName,
            vmrInfo.GetSourceManifestPath(),
            vmrPath / VmrInfo.SourcesDir / VmrInfo.SourceMappingsFileName
        };

        foreach (var repo in syncedRepos)
        {
            expectedFiles.Add(vmrPath / VmrInfo.SourcesDir / repo / VersionFiles.VersionDetailsXml);
            expectedFiles.Add(vmrPath / VmrInfo.GitInfoSourcesDir / $"{repo}.props");
        }

        expectedFiles.AddRange(reposFiles);

        return expectedFiles;
    }

    protected void CheckDirectoryContents(string directory, IList<LocalPath> expectedFiles)
    {
        var filesInDir = GetAllFilesInDirectory(new DirectoryInfo(directory));
        filesInDir.Should().BeEquivalentTo(expectedFiles);
    }

    protected static void CheckFileContents(LocalPath filePath, string expected)
    {
        var expectedLines = expected.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        CheckFileContents(filePath, expectedLines);
    }

    protected static void CheckFileContents(LocalPath filePath, string[] expectedLines)
    {
        var fileContent = File.ReadAllLines(filePath);
        fileContent.Should().BeEquivalentTo(expectedLines);
    }

    protected void CompareFileContents(LocalPath filePath, string resourceFileName)
    {
        var resourceContent = File.ReadAllLines(VmrTestsOneTimeSetUp.ResourcesPath / resourceFileName);
        CheckFileContents(filePath, resourceContent);
    }

    protected async Task InitializeRepoAtLastCommit(string repoName, LocalPath repoPath)
    {
        var commit = await GitOperations.GetRepoLastCommit(repoPath);
        await CallDarcInitialize(repoName, commit);
    }

    protected async Task UpdateRepoToLastCommit(string repoName, LocalPath repoPath)
    {
        var commit = await GitOperations.GetRepoLastCommit(repoPath);
        await CallDarcUpdate(repoName, commit);
    }

    private async Task CallDarcInitialize(string repository, string commit)
    {
        var vmrInitializer = _serviceProvider.Value.GetRequiredService<IVmrInitializer>();
        await vmrInitializer.InitializeRepository(repository, commit, null, true, _cancellationToken.Token);
    }

    protected async Task CallDarcUpdate(string repository, string commit)
    {
        var vmrUpdater = _serviceProvider.Value.GetRequiredService<IVmrUpdater>();
        await vmrUpdater.UpdateRepository(repository, commit, null, false, true, _cancellationToken.Token);
    }

    protected void CopyDirectory(string source, LocalPath destination)
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

        files.AddRange(directory.GetFiles().Where(f => f.Name != ".gitmodules").Select(f => new NativePath(f.FullName)));

        foreach (var subDir in directory.GetDirectories())
        {
            files.AddRange(GetAllFilesInDirectory(subDir));
        }

        return files;
    }

    internal async Task<string> CopyRepoAndCreateVersionDetails(
        LocalPath currentTestDir,
        string repoName,
        IDictionary<string, List<Dependency>>? dependencies = null)
    {
        var repoPath = currentTestDir / repoName;
        CopyDirectory(VmrTestsOneTimeSetUp.TestsDirectory / repoName, repoPath);
        
        var dependenciesString = new StringBuilder();
        if (dependencies != null && dependencies.ContainsKey(repoName))
        {
            var repoDependencies = dependencies[repoName];
            foreach (var dep in repoDependencies)
            {
                string sha = await CopyRepoAndCreateVersionDetails(currentTestDir, dep.Name, dependencies);
                dependenciesString.AppendLine(
                    string.Format(
                        Constants.DependencyTemplate,
                        new[] { dep.Name, dep.Uri, sha }));
            }
        }

        var versionDetails = string.Format(Constants.VersionDetailsTemplate, dependenciesString);
        File.WriteAllText(repoPath / VersionFiles.VersionDetailsXml, versionDetails);
        await GitOperations.CommitAll(repoPath, "update version details");
        return await GitOperations.GetRepoLastCommit(repoPath);
    }

    protected async Task WriteSourceMappingsInVmr(SourceMappingFile sourceMappings)
    {
        var settings = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
        };

        File.WriteAllText(_vmrPath / VmrInfo.SourcesDir / VmrInfo.SourceMappingsFileName,
            JsonSerializer.Serialize(sourceMappings, settings));
        
        await GitOperations.CommitAll(_vmrPath, "Add source mappings");
    }
}
