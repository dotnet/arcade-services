// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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


namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public abstract class VmrTestsBase
{
    protected NativePath CurrentTestDirectory { get; private set; } = null!;
    protected NativePath ProductRepoPath { get; private set; } = null!;
    protected NativePath VmrPath { get; private set; } = null!;
    protected NativePath TmpPath { get; private set; } = null!;
    protected NativePath SecondRepoPath { get; private set; } = null!;
    protected NativePath DependencyRepoPath { get; private set; } = null!;
    protected NativePath InstallerRepoPath { get; private set; } = null!;
    protected GitOperationsHelper GitOperations { get; } = new();
    protected VmrInfo Info { get; private set; } = null!;
    
    private Lazy<IServiceProvider> _serviceProvider = null!;
    private readonly CancellationTokenSource _cancellationToken = new();

    [SetUp]
    public async Task Setup()
    {
        var testsDirName = "_tests";
        CurrentTestDirectory = VmrTestsOneTimeSetUp.TestsDirectory / testsDirName / Path.GetRandomFileName();
        Directory.CreateDirectory(CurrentTestDirectory);

        TmpPath = CurrentTestDirectory / "tmp";
        ProductRepoPath = CurrentTestDirectory / Constants.ProductRepoName;
        VmrPath = CurrentTestDirectory / "vmr";
        SecondRepoPath = CurrentTestDirectory / Constants.SecondRepoName;
        DependencyRepoPath = CurrentTestDirectory / Constants.DependencyRepoName;
        InstallerRepoPath = CurrentTestDirectory / Constants.InstallerRepoName;

        Directory.CreateDirectory(TmpPath);
        
        await CopyReposForCurrentTest();
        await CopyVmrForCurrentTest();
        
        _serviceProvider = new(CreateServiceProvider);
        Info = (VmrInfo)_serviceProvider.Value.GetRequiredService<IVmrInfo>();
    }

    [TearDown]
    public void DeleteCurrentTestDirectory()
    {
        try
        {
            if (CurrentTestDirectory is not null)
            {
                VmrTestsOneTimeSetUp.DeleteDirectory(CurrentTestDirectory.ToString());
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
        .AddLogging(b => b.AddConsole().AddFilter(l => l >= LogLevel.Information))
        .AddVmrManagers("git", VmrPath, TmpPath, null, null)
        .BuildServiceProvider();

    protected List<LocalPath> GetExpectedFilesInVmr(
        LocalPath vmrPath,
        string[] syncedRepos,
        List<LocalPath> reposFiles)
    {
        var expectedFiles = new List<LocalPath>
        {
            vmrPath / VmrInfo.GitInfoSourcesDir / AllVersionsPropsFile.FileName,
            Info.GetSourceManifestPath(),
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

    protected static void CheckFileContents(NativePath filePath, string expected, bool removeEmptyLines = true)
    {
        var expectedLines = expected.Split(Environment.NewLine, removeEmptyLines ? StringSplitOptions.RemoveEmptyEntries : StringSplitOptions.None);
        CheckFileContents(filePath, expectedLines);
    }

    protected static void CheckFileContents(NativePath filePath, string[] expectedLines)
    {
        var fileContent = File.ReadAllLines(filePath);
        fileContent.Should().BeEquivalentTo(expectedLines);
    }

    protected void CompareFileContents(NativePath filePath, string resourceFileName)
    {
        var resourceContent = File.ReadAllLines(VmrTestsOneTimeSetUp.ResourcesPath / resourceFileName);
        CheckFileContents(filePath, resourceContent);
    }

    protected async Task InitializeRepoAtLastCommit(string repoName, NativePath repoPath, LocalPath? sourceMappingsPath = null)
    {
        var commit = await GitOperations.GetRepoLastCommit(repoPath);
        var sourceMappings = sourceMappingsPath ?? VmrPath / VmrInfo.SourcesDir / VmrInfo.SourceMappingsFileName;
        await CallDarcInitialize(repoName, commit, sourceMappings);
    }

    protected async Task UpdateRepoToLastCommit(string repoName, NativePath repoPath, bool generateCodeowners = false)
    {
        var commit = await GitOperations.GetRepoLastCommit(repoPath);
        await CallDarcUpdate(repoName, commit, generateCodeowners);
    }

    private async Task CallDarcInitialize(string repository, string commit, LocalPath sourceMappingsPath)
    {
        var vmrInitializer = _serviceProvider.Value.GetRequiredService<IVmrInitializer>();
        await vmrInitializer.InitializeRepository(repository, commit, null, true, sourceMappingsPath, Array.Empty<AdditionalRemote>(), null, null, false, true, _cancellationToken.Token);
    }

    protected async Task CallDarcUpdate(string repository, string commit, bool generateCodeowners = false)
    {
        await CallDarcUpdate(repository, commit, Array.Empty<AdditionalRemote>(), generateCodeowners);
    }

    protected async Task CallDarcUpdate(string repository, string commit, AdditionalRemote[] additionalRemotes, bool generateCodeowners = false)
    {
        var vmrUpdater = _serviceProvider.Value.GetRequiredService<IVmrUpdater>();
        await vmrUpdater.UpdateRepository(repository, commit, null, true, additionalRemotes, null, null, generateCodeowners, true, _cancellationToken.Token);
    }

    protected async Task<List<string>> CallDarcCloakedFileScan(string baselinesFilePath)
    {
        var cloakedFileScanner = _serviceProvider.Value.GetRequiredService<VmrCloakedFileScanner>();
        return await cloakedFileScanner.ScanVmr(baselinesFilePath, _cancellationToken.Token);
    }

    protected async Task<List<string>> CallDarcBinaryFileScan(string baselinesFilePath)
    {
        var binaryFileScanner = _serviceProvider.Value.GetRequiredService<VmrBinaryFileScanner>();
        return await binaryFileScanner.ScanVmr(baselinesFilePath, _cancellationToken.Token);
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

    protected async Task<string> CopyRepoAndCreateVersionDetails(
        NativePath currentTestDir,
        string repoName,
        IDictionary<string, List<string>>? dependencies = null)
    {
        var repoPath = currentTestDir / repoName;
        
        var dependenciesString = new StringBuilder();
        if (dependencies != null && dependencies.ContainsKey(repoName))
        {
            var repoDependencies = dependencies[repoName];
            foreach (var dependencyName in repoDependencies)
            {
                string sha = await CopyRepoAndCreateVersionDetails(currentTestDir, dependencyName, dependencies);
                dependenciesString.AppendLine(
                    string.Format(
                        Constants.DependencyTemplate,
                        new[] { dependencyName, currentTestDir/ dependencyName, sha }));
            }
        }

        if (!Directory.Exists(repoPath))
        {
            CopyDirectory(VmrTestsOneTimeSetUp.TestsDirectory / repoName, repoPath);
            var versionDetails = string.Format(Constants.VersionDetailsTemplate, dependenciesString);
            File.WriteAllText(repoPath / VersionFiles.VersionDetailsXml, versionDetails);
            await GitOperations.CommitAll(repoPath, "update version details");
        }

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

        File.WriteAllText(VmrPath / VmrInfo.SourcesDir / VmrInfo.SourceMappingsFileName,
            JsonSerializer.Serialize(sourceMappings, settings));
        
        await GitOperations.CommitAll(VmrPath, "Add source mappings");
    }
}
