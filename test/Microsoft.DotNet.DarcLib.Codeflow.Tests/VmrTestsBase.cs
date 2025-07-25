// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

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
    protected NativePath ArcadeInVmrPath { get; private set;} = null!;
    protected GitOperationsHelper GitOperations { get; } = new();
    protected IServiceProvider ServiceProvider { get; private set; } = null!;

    private readonly CancellationTokenSource _cancellationToken = new();
    protected readonly Mock<IBasicBarClient> _basicBarClient = new();

    private int _buildId = 100;
    private readonly Dictionary<int, Build> _builds = [];

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
        ArcadeInVmrPath = VmrPath / VmrInfo.SourcesDir / "arcade";

        Directory.CreateDirectory(TmpPath);

        await CopyReposForCurrentTest();
        await CopyVmrForCurrentTest();

        ServiceProvider = CreateServiceProvider().BuildServiceProvider();
        ServiceProvider.GetRequiredService<IVmrInfo>().VmrUri = VmrPath;
    
        _basicBarClient.Setup(x => x.GetBuildAsync(It.IsAny<int>()))
             .ReturnsAsync((int id) => _builds[id]);
    }

    [TearDown]
    public void DeleteCurrentTestDirectory()
    {
        _basicBarClient.Reset();
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
        .AddSingleVmrSupport("git", VmrPath, TmpPath, null, null)
        .AddSingleton(_basicBarClient.Object)
        .AddTransient<IRemoteFactory, RemoteFactory>()
        .AddSingleton<IRedisCacheClient, NoOpRedisClient>();

    protected static List<NativePath> GetExpectedFilesInVmr(
        NativePath vmrPath,
        string[] reposWithVersionFiles,
        List<NativePath> reposFiles)
    {
        List<NativePath> expectedFiles =
        [
            vmrPath / VmrInfo.DefaultRelativeSourceManifestPath,
            vmrPath / VmrInfo.DefaultRelativeSourceMappingsPath,
        ];

        foreach (var repo in reposWithVersionFiles)
        {
            expectedFiles.AddRange(GetExpectedVersionFiles(vmrPath / VmrInfo.SourcesDir / repo));
        }

        expectedFiles.AddRange(reposFiles);

        return expectedFiles;
    }

    protected static string[] GetExpectedVersionFiles() =>
    [
        VersionFiles.VersionDetailsXml,
        VersionFiles.VersionProps,
        VersionFiles.GlobalJson,
        VersionFiles.NugetConfigNames.First(),
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
        await CreateNewBuild(repoPath, []);
        var commit = await GitOperations.GetRepoLastCommit(repoPath);
        var sourceMappings = sourceMappingsPath ?? VmrPath / VmrInfo.DefaultRelativeSourceMappingsPath;
        await CallDarcInitialize(repoName, commit, sourceMappings);
    }

    protected async Task UpdateRepoToLastCommit(string repoName, NativePath repoPath, bool generateCodeowners = false, bool generateCredScanSuppressions = false)
    {
        await CreateNewBuild(repoPath, []);
        var commit = await GitOperations.GetRepoLastCommit(repoPath);
        await CallDarcUpdate(repoName, commit, generateCodeowners, generateCredScanSuppressions);
    }

    private async Task CallDarcInitialize(string mapping, string commit, LocalPath sourceMappingsPath)
    {
        using var scope = ServiceProvider.CreateScope();
        var vmrInitializer = scope.ServiceProvider.GetRequiredService<IVmrInitializer>();
        await vmrInitializer.InitializeRepository(
            mappingName: mapping,
            targetRevision: commit,
            sourceMappingsPath: sourceMappingsPath,
            new CodeFlowParameters(
                AdditionalRemotes: [],
                TpnTemplatePath: null,
                GenerateCodeOwners: false,
                GenerateCredScanSuppressions: false),
            cancellationToken: _cancellationToken.Token);
    }

    protected async Task CallDarcUpdate(string mapping, string commit, bool generateCodeowners = false, bool generateCredScanSuppressions = false)
    {
        await CallDarcUpdate(mapping, commit, [], generateCodeowners, generateCredScanSuppressions);
    }

    protected async Task CallDarcUpdate(string mapping, string commit, AdditionalRemote[] additionalRemotes, bool generateCodeowners = false, bool generateCredScanSuppressions = false)
    {
        using var scope = ServiceProvider.CreateScope();
        var vmrUpdater = scope.ServiceProvider.GetRequiredService<IVmrUpdater>();
        await vmrUpdater.UpdateRepository(
            mappingName: mapping,
            targetRevision: commit,
            new CodeFlowParameters(
                AdditionalRemotes: additionalRemotes,
                TpnTemplatePath: null,
                GenerateCodeOwners: generateCodeowners,
                GenerateCredScanSuppressions: generateCredScanSuppressions),
            cancellationToken: _cancellationToken.Token);
    }

    protected async Task<CodeFlowResult> CallDarcBackflow(
        string mappingName,
        NativePath repoPath,
        string branch,
        Build? buildToFlow = null,
        IReadOnlyCollection<string>? excludedAssets = null,
        bool useLatestBuild = false)
    {
        using var scope = ServiceProvider.CreateScope();
        var codeflower = scope.ServiceProvider.GetRequiredService<IVmrBackFlower>();

        if (useLatestBuild)
        {
            buildToFlow = await _basicBarClient.Object.GetBuildAsync(_buildId);
        }

        CodeFlowResult codeFlowResult = await codeflower.FlowBackAsync(
            mappingName,
            repoPath,
            buildToFlow ?? await CreateNewVmrBuild([]),
            excludedAssets,
            "main",
            branch,
            cancellationToken: _cancellationToken.Token);
        return codeFlowResult;
    }

    protected async Task<CodeFlowResult> CallDarcForwardflow(
        string mappingName,
        NativePath repoPath,
        string branch,
        Build? buildToFlow = null,
        IReadOnlyCollection<string>? excludedAssets = null,
        bool skipMeaninglessUpdates = false)
    {
        using var scope = ServiceProvider.CreateScope();
        var codeflower = scope.ServiceProvider.GetRequiredService<IVmrForwardFlower>();
        CodeFlowResult codeFlowRes = await codeflower.FlowForwardAsync(
            mappingName,
            repoPath,
            buildToFlow ?? await CreateNewRepoBuild(repoPath, []),
            excludedAssets,
            "main",
            branch,
            VmrPath,
            skipMeaninglessUpdates,
            cancellationToken: _cancellationToken.Token);

        return codeFlowRes;
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

    private static List<LocalPath> GetAllFilesInDirectory(DirectoryInfo directory)
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
        if (dependencies != null && dependencies.TryGetValue(repoName, out List<string>? repoDependencies))
        {
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
            File.WriteAllText(repoPath/ VersionFiles.GlobalJson, Constants.GlobalJsonTemplate);
            File.WriteAllText(repoPath / VersionFiles.NugetConfigNames.First(), Constants.NuGetConfigTemplate);

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

    protected async Task<Build> CreateNewVmrBuild((string name, string version)[] assets, string? commit = null)
        => await CreateNewBuild(VmrPath, assets, commit);

    protected async Task<Build> CreateNewRepoBuild((string name, string version)[] assets, string? commit = null)
        => await CreateNewBuild(ProductRepoPath, assets, commit);

    protected async Task<Build> CreateNewRepoBuild(NativePath repoPath, (string name, string version)[] assets, string? commit = null)
        => await CreateNewBuild(repoPath, assets, commit);

    protected async Task<Build> CreateNewBuild(NativePath repoPath, (string name, string version)[] assets, string? commit = null)
    {
        var assetId = 1;
        _buildId++;
        commit ??= await GitOperations.GetRepoLastCommit(repoPath);

        var build = new Build(
            id: _buildId,
            dateProduced: DateTimeOffset.Now,
            staleness: 0,
            released: false,
            stable: true,
            commit: commit,
            channels: [],
            assets:
            [
                ..assets.Select(a => new Asset(++assetId, _buildId, true, a.name, a.version,
                    [
                        new AssetLocation(assetId, LocationType.NugetFeed, "https://source.feed/index.json")
                    ]))
            ],
            dependencies: [],
            incoherencies: [])
        {
            GitHubBranch = "main",
            GitHubRepository = repoPath,
        };
        _builds[build.Id] = build;

        _basicBarClient
            .Setup(x => x.GetBuildsAsync(repoPath.Path, commit))
            .ReturnsAsync([build]);

        return build;
    }

    protected static string GetTestBranchName([CallerMemberName] string testName = "", bool forwardFlow = true)
    {
        return $"{(forwardFlow ? "forward" : "backward")}/{testName}/{Guid.NewGuid().ToString().Substring(0, 16)}";
    }
}
