// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using FluentAssertions;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

#nullable enable
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
        Path.GetFullPath(_vmrPath),
        Path.GetFullPath(_tmpPath),
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
            vmrPath / "git-info" / "AllRepoVersions.props",
            vmrPath / "src" / "source-manifest.json",
            vmrPath / "src" / "source-mappings.json",
        };

        foreach (var repo in syncedRepos)
        {
            expectedFiles.Add(vmrPath / "src" / repo / "eng" / Constants.VersionDetailsName);
            expectedFiles.Add(vmrPath / "git-info" / $"{repo}.props");
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

        files.AddRange(directory.GetFiles().Select(f => new NativePath(f.FullName)));

        foreach (var subDir in directory.GetDirectories())
        {
            files.AddRange(GetAllFilesInDirectory(subDir));
        }

        return files;
    }

    protected string GenerateSourceMappings(
        ICollection<SourceMapping> mappings,
        string patchesPath = "",
        ICollection<AdditionalMapping>? additionalMappings = null,
        ICollection<string>? exclude = null)
    {
        var additionalMappingsString = string.Empty;
        var mappingsString = string.Empty;

        if(additionalMappings != null)
        {
            additionalMappingsString = string.Join(
                "," + Environment.NewLine, 
                additionalMappings.Select(m => 
                string.Format(Constants.AdditionalMappingTemplate, new[] { m.Source, m.Destination })));
        }

        mappingsString = string.Join("," + Environment.NewLine, mappings.Select(m => GenerateMappingString(m)));
        return string.Format(Constants.SourceMappingsTemplate, new[] { patchesPath, additionalMappingsString, mappingsString });
    }

    private string GenerateMappingString(SourceMapping mapping)
    {
        var excluded = string.Empty;

        if (mapping.Exclude != null)
        {
            excluded = string.Join(", ", mapping.Exclude.Select(e => $"\"{e}\""));
        }

        return string.Format(Constants.MappingTemplate, new[] { mapping.Name, mapping.DefaultRemote, excluded });
    }

    protected async Task<string> CopyRepoAndCreateVersionDetails(
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
                        new[] { dep.Name, EscapePath(dep.Uri), sha }));
            }
        }

        var versionDetails = string.Format(Constants.VersionDetailsTemplate, dependenciesString);
        File.WriteAllText(repoPath / "eng" / Constants.VersionDetailsName, versionDetails);
        await GitOperations.CommitAll(repoPath, "update version details");
        return await GitOperations.GetRepoLastCommit(repoPath);
    }

    public static string EscapePath(string path)
    {
        return path.Replace("\\", "\\\\");
    }

    public record SourceMapping(string Name, string DefaultRemote, List<string>? Exclude = null);
    public record AdditionalMapping(string Source, string Destination);
}
