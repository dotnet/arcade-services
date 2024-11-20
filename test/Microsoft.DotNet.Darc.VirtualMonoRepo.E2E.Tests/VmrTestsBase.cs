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
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.VirtualMonoRepo.E2E.Tests;

internal abstract class VmrTestsBase
{
    protected NativePath CurrentTestDirectory { get; private set; } = null!;
    protected NativePath ProductRepoPath { get; private set; } = null!;
    protected NativePath VmrPath { get; private set; } = null!;
    protected NativePath TmpPath { get; private set; } = null!;
    protected NativePath SecondRepoPath { get; private set; } = null!;
    protected NativePath DependencyRepoPath { get; private set; } = null!;
    protected NativePath SyncDisabledRepoPath { get; private set; } = null!;
    protected NativePath InstallerRepoPath { get; private set; } = null!;
    protected GitOperationsHelper GitOperations { get; } = new();
    protected IServiceProvider ServiceProvider { get; private set; } = null!;

    private readonly CancellationTokenSource _cancellationToken = new();

    [SetUp]
    public async Task Setup()
    {
        var testsDirName = "_tests";
        CurrentTestDirectory = VmrTestsOneTimeSetUp.TestsDirectory / testsDirName / Path.GetRandomFileName();
        Directory.CreateDirectory(CurrentTestDirectory);

        TmpPath = CurrentTestDirectory;
        ProductRepoPath = CurrentTestDirectory / Constants.ProductRepoName;
        VmrPath = CurrentTestDirectory / "vmr";
        SecondRepoPath = CurrentTestDirectory / Constants.SecondRepoName;
        DependencyRepoPath = CurrentTestDirectory / Constants.DependencyRepoName;
        InstallerRepoPath = CurrentTestDirectory / Constants.InstallerRepoName;
        SyncDisabledRepoPath = CurrentTestDirectory / Constants.SyncDisabledRepoName;

        Directory.CreateDirectory(TmpPath);

        await CopyReposForCurrentTest();
        await CopyVmrForCurrentTest();

        ServiceProvider = CreateServiceProvider().BuildServiceProvider();
        ServiceProvider.GetRequiredService<IVmrInfo>().VmrUri = VmrPath;
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

    protected virtual IServiceCollection CreateServiceProvider() => new ServiceCollection()
        .AddLogging(b => b.AddConsole().AddFilter(l => l >= LogLevel.Debug))
        .AddVmrManagers("git", VmrPath, TmpPath, null, null)
        .AddSingleton<IBasicBarClient>(new BarApiClient(
            buildAssetRegistryPat: null,
            managedIdentityId: null,
            disableInteractiveAuth: true,
            buildAssetRegistryBaseUri: MaestroApiOptions.StagingMaestroUri));

    protected static List<NativePath> GetExpectedFilesInVmr(
        NativePath vmrPath,
        string[] reposWithVersionFiles,
        List<NativePath> reposFiles,
        bool hasVersionFiles = false)
    {
        List<NativePath> expectedFiles =
        [
            vmrPath / VmrInfo.GitInfoSourcesDir / AllVersionsPropsFile.FileName,
            vmrPath / VmrInfo.DefaultRelativeSourceManifestPath,
            vmrPath / VmrInfo.DefaultRelativeSourceMappingsPath,
        ];

        foreach (var repo in reposWithVersionFiles)
        {
            expectedFiles.AddRange(GetExpectedVersionFiles(vmrPath / VmrInfo.SourcesDir / repo));
            expectedFiles.Add(vmrPath / VmrInfo.GitInfoSourcesDir / $"{repo}.props");
        }

        expectedFiles.AddRange(reposFiles);

        if (hasVersionFiles)
        {
            expectedFiles.AddRange(GetExpectedVersionFiles(vmrPath));
        }

        return expectedFiles;
    }

    protected static string[] GetExpectedVersionFiles() =>
    [
        VersionFiles.VersionDetailsXml,
        VersionFiles.VersionProps,
        VersionFiles.GlobalJson,
        VersionFiles.NugetConfig,
    ];

    protected static IEnumerable<NativePath> GetExpectedVersionFiles(NativePath repoPath)
        => GetExpectedVersionFiles().Select(file => repoPath / file);

    protected static void CheckDirectoryContents(string directory, IList<NativePath> expectedFiles)
    {
        var filesInDir = GetAllFilesInDirectory(new DirectoryInfo(directory));
        filesInDir.OrderBy(f => f.Path).ToList().Should().BeEquivalentTo(expectedFiles.OrderBy(f => f.Path).ToList());
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

    protected static void CompareFileContents(NativePath filePath, string resourceFileName)
    {
        var resourceContent = File.ReadAllLines(VmrTestsOneTimeSetUp.ResourcesPath / resourceFileName);
        CheckFileContents(filePath, resourceContent);
    }

    protected async Task InitializeRepoAtLastCommit(string repoName, NativePath repoPath, LocalPath? sourceMappingsPath = null)
    {
        var commit = await GitOperations.GetRepoLastCommit(repoPath);
        var sourceMappings = sourceMappingsPath ?? VmrPath / VmrInfo.DefaultRelativeSourceMappingsPath;
        await CallDarcInitialize(repoName, commit, sourceMappings);
    }

    protected async Task UpdateRepoToLastCommit(string repoName, NativePath repoPath, bool generateCodeowners = false, bool generateCredScanSuppressions = false)
    {
        var commit = await GitOperations.GetRepoLastCommit(repoPath);
        await CallDarcUpdate(repoName, commit, generateCodeowners, generateCredScanSuppressions);
    }

    private async Task CallDarcInitialize(string repository, string commit, LocalPath sourceMappingsPath)
    {
        using var scope = ServiceProvider.CreateScope();
        var vmrInitializer = scope.ServiceProvider.GetRequiredService<IVmrInitializer>();
        await vmrInitializer.InitializeRepository(repository, commit, null, true, sourceMappingsPath, Array.Empty<AdditionalRemote>(), null, null, false, false, true, _cancellationToken.Token);
    }

    protected async Task CallDarcUpdate(string repository, string commit, bool generateCodeowners = false, bool generateCredScanSuppressions = false)
    {
        await CallDarcUpdate(repository, commit, [], generateCodeowners, generateCredScanSuppressions);
    }

    protected async Task CallDarcUpdate(string repository, string commit, AdditionalRemote[] additionalRemotes, bool generateCodeowners = false, bool generateCredScanSuppressions = false)
    {
        using var scope = ServiceProvider.CreateScope();
        var vmrUpdater = scope.ServiceProvider.GetRequiredService<IVmrUpdater>();
        await vmrUpdater.UpdateRepository(repository, commit, null, true, additionalRemotes, null, null, generateCodeowners, generateCredScanSuppressions, true, _cancellationToken.Token);
    }

    protected async Task<bool> CallDarcBackflow(string mappingName, NativePath repoPath, string branch, string? shaToFlow = null, int? buildToFlow = null)
    {
        using var scope = ServiceProvider.CreateScope();
        var codeflower = scope.ServiceProvider.GetRequiredService<IVmrBackFlower>();
        return await codeflower.FlowBackAsync(mappingName, repoPath, shaToFlow, buildToFlow, "main", branch, cancellationToken: _cancellationToken.Token);
    }

    protected async Task<bool> CallDarcForwardflow(string mappingName, NativePath repoPath, string branch, string? shaToFlow = null, int? buildToFlow = null)
    {
        using var scope = ServiceProvider.CreateScope();
        var codeflower = scope.ServiceProvider.GetRequiredService<IVmrForwardFlower>();
        return await codeflower.FlowForwardAsync(mappingName, repoPath, shaToFlow, buildToFlow, "main", branch, cancellationToken: _cancellationToken.Token);
    }

    protected async Task<List<string>> CallDarcCloakedFileScan(string baselinesFilePath)
    {
        using var scope = ServiceProvider.CreateScope();
        var cloakedFileScanner = scope.ServiceProvider.GetRequiredService<VmrCloakedFileScanner>();
        return await cloakedFileScanner.ScanVmr(baselinesFilePath, _cancellationToken.Token);
    }

    protected static void CopyDirectory(string source, LocalPath destination)
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

    private static ICollection<LocalPath> GetAllFilesInDirectory(DirectoryInfo directory)
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

    protected async Task<string> CopyRepoAndCreateVersionFiles(
        string repoName,
        Dictionary<string, List<string>>? dependencies = null)
    {
        var repoPath = CurrentTestDirectory / repoName;

        var dependenciesString = new StringBuilder();
        var propsString = new StringBuilder();
        if (dependencies != null && dependencies.ContainsKey(repoName))
        {
            var repoDependencies = dependencies[repoName];
            foreach (var dependencyName in repoDependencies)
            {
                var sha = await CopyRepoAndCreateVersionFiles(dependencyName, dependencies);
                dependenciesString.AppendLine(
                    string.Format(
                        Constants.DependencyTemplate,
                        new[] { dependencyName, CurrentTestDirectory / dependencyName, sha }));

                var propsName = VersionFiles.GetVersionPropsPackageVersionElementName(dependencyName);
                propsString.AppendLine($"<{propsName}>8.0.0</{propsName}>");
            }
        }

        if (!Directory.Exists(repoPath))
        {
            CopyDirectory(VmrTestsOneTimeSetUp.TestsDirectory / repoName, repoPath);

            var versionDetails = string.Format(Constants.VersionDetailsTemplate, dependenciesString);
            Directory.CreateDirectory(repoPath / "eng");
            File.WriteAllText(repoPath / VersionFiles.VersionDetailsXml, versionDetails);

            var versionProps = string.Format(Constants.VersionPropsTemplate, propsString);
            File.WriteAllText(repoPath / VersionFiles.VersionProps, versionProps);

            File.WriteAllText(repoPath / VersionFiles.GlobalJson, Constants.GlobalJsonTemplate);

            File.WriteAllText(repoPath / VersionFiles.NugetConfig, Constants.NuGetConfigTemplate);

            await GitOperations.CommitAll(repoPath, "Update version files");
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

        File.WriteAllText(VmrPath / VmrInfo.DefaultRelativeSourceMappingsPath,
            JsonSerializer.Serialize(sourceMappings, settings));

        await GitOperations.CommitAll(VmrPath, "Add source mappings");
    }

    // Needed for some local git operations
    protected Local GetLocal(NativePath repoPath) => ActivatorUtilities.CreateInstance<Local>(ServiceProvider, repoPath.ToString());
    protected DependencyFileManager GetDependencyFileManager() => ActivatorUtilities.CreateInstance<DependencyFileManager>(ServiceProvider);
}
