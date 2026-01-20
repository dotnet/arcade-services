// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NuGet.Versioning;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

public class BackflowConflictResolverTests
{
    private const string MappingName = "test-repo";
    private const string CurrentVmrSha = "current flow VMR SHA";
    private const string CurrentRepoSha = "current flow repo SHA";
    private const string LastVmrSha = "last flow VMR SHA";
    private const string LastRepoSha = "last flow repo SHA";
    private const string TargetBranch = "main";
    private const string PrBranch = "pr-branch";
    private const string VmrUri = "https://github.com/dotnet/dotnet";

    private readonly NativePath _vmrPath = new("/data/vmr");
    private readonly NativePath _repoPath = new("/data/repo");

    private readonly Mock<IVmrInfo> _vmrInfo = new();
    private readonly Mock<IVmrPatchHandler> _patchHandler = new();
    private readonly Mock<ILocalLibGit2Client> _libGit2Client = new();
    private readonly Mock<ILocalGitRepoFactory> _localGitRepoFactory = new();
    private readonly Mock<IVersionDetailsParser> _versionDetailsParser = new();
    private readonly Mock<IAssetLocationResolver> _assetLocationResolver = new();
    private readonly Mock<IDependencyFileManager> _dependencyFileManager = new();
    private readonly Mock<IJsonFileMerger> _jsonMergerMock = new();
    private readonly Mock<IVersionDetailsFileMerger> _versionDetailsFileMergerMock = new();
    private readonly Mock<IFileSystem> _fileSystem = new();

    private readonly Mock<ILocalGitRepo> _localRepo = new();
    private readonly Mock<ILocalGitRepo> _localVmr = new();

    BackflowConflictResolver _conflictResolver = null!;

    // Mapping of SHA -> Content of version details
    Dictionary<string, VersionDetails> _versionDetails = [];
    private int _buildId = 1;

    [SetUp]
    public void SetUp()
    {
        _vmrInfo.Reset();
        _vmrInfo
            .SetupGet(x => x.VmrPath)
            .Returns(_vmrPath);
        _vmrInfo
            .Setup(x => x.GetRepoSourcesPath(It.IsAny<SourceMapping>()))
            .Returns((SourceMapping mapping) => _vmrPath / VmrInfo.SourcesDir / mapping.Name);

        _libGit2Client.Reset();
        _localRepo.Reset();
        _localRepo
            .SetupGet(x => x.Path)
            .Returns(_repoPath);
        _localRepo
            .Setup(x => x.HasWorkingTreeChangesAsync())
            .ReturnsAsync(true);
        _localRepo
            .Setup(x => x.GetFileFromGitAsync(It.Is<string>(s => s.Contains(VersionFiles.VersionDetailsXml)), It.IsAny<string>(), null))
            .ReturnsAsync((string _, string _, string __) => $"repo/{TargetBranch}");
        _localRepo
            .Setup(x => x.GetStagedFilesAsync())
            .ReturnsAsync(Array.Empty<string>());
        _localRepo
            .Setup(x => x.GetConflictedFilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UnixPath>());
        _localRepo
            .SetReturnsDefault(Task.CompletedTask);
        _localRepo
            .SetReturnsDefault(Task.FromResult(new ProcessExecutionResult()));

        _localVmr.Reset();
        _localVmr
            .SetupGet(x => x.Path)
            .Returns(_vmrPath);
        _localVmr
            .Setup(x => x.GetFileFromGitAsync(It.Is<string>(s => s.Contains(VersionFiles.VersionDetailsXml)), It.IsAny<string>(), null))
            .ReturnsAsync((string _, string commit, string __) => $"vmr/{commit}");
        _localVmr
            .Setup(x => x.GetStagedFilesAsync())
            .ReturnsAsync(Array.Empty<string>());
        _localVmr
            .Setup(x => x.GetConflictedFilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UnixPath>());

        _localGitRepoFactory.Reset();
        _localGitRepoFactory
            .Setup(x => x.Create(_vmrPath))
            .Returns(_localVmr.Object);

        _versionDetailsParser.Reset();
        _versionDetails = [];
        _versionDetailsParser
            .Setup(x => x.ParseVersionDetailsXml(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string content, bool _) => _versionDetails[content]);

        _assetLocationResolver.Reset();
        _assetLocationResolver.SetReturnsDefault(Task.CompletedTask);

        _patchHandler.Reset();
        _patchHandler.SetReturnsDefault(Task.CompletedTask);

        _dependencyFileManager.Reset();
        _dependencyFileManager
            .Setup(x => x.AddDependencyAsync(It.IsAny<DependencyDetail>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UnixPath>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Callback((DependencyDetail dep, string repo, string commit, UnixPath? _, bool _, bool? _, bool _) =>
            {
                var key = (repo == _vmrPath ? "vmr" : "repo") + "/" + commit;
                VersionDetails versionDetails = _versionDetails.TryGetValue(key, out var vd) ? vd : new([], null);

                if (versionDetails.Dependencies.Any(d => d.Name == dep.Name))
                {
                    return;
                }

                _versionDetails[key] = new VersionDetails(
                    versionDetails.Dependencies.Append(dep).ToArray(),
                    versionDetails.Source);
            })
            .Returns(Task.CompletedTask);

        _dependencyFileManager
            .Setup(x => x.RemoveDependencyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UnixPath>(), It.IsAny<bool>()))
            .Callback((string name, string repo, string commit, UnixPath? _, bool? _) =>
            {
                var key = (repo == _vmrPath ? "vmr" : "repo") + "/" + commit;
                var versionDetails = _versionDetails[key];
                _versionDetails[key] = new VersionDetails(
                    [..versionDetails.Dependencies.Where(d => d.Name != name)],
                    versionDetails.Source);
            })
            .Returns(Task.CompletedTask);

        _fileSystem.Reset();

        _versionDetailsFileMergerMock.Reset();
        _jsonMergerMock.Reset();

        _conflictResolver = new(
            _vmrInfo.Object,
            new SourceManifest([new RepositoryRecord(MappingName, "https://github/repo1", "abcdef", -1)], []),
            _patchHandler.Object,
            _libGit2Client.Object,
            _localGitRepoFactory.Object,
            _versionDetailsParser.Object,
            _assetLocationResolver.Object,
            new CoherencyUpdateResolver(Mock.Of<IBasicBarClient>(), Mock.Of<IRemoteFactory>(), new NullLogger<CoherencyUpdateResolver>()),
            _dependencyFileManager.Object,
            _jsonMergerMock.Object,
            _versionDetailsFileMergerMock.Object,
            _fileSystem.Object,
            new NullLogger<BackflowConflictResolver>());
    }

    // Tests a case when packages were updated in the repo as well as in VMR and some created during the build.
    // Tests that the versions are merged correctly.
    [Test]
    public async Task VersionsAreMergedInBackflowAfterForwardFlowTest()
    {
        var lastBackflow = new Backflow(LastVmrSha, LastRepoSha);
        var lastFlow = new ForwardFlow(LastRepoSha, LastVmrSha);
        var lastFlows = new LastFlows(lastFlow, lastBackflow, lastFlow, null);
        var currentFlow = new Backflow(CurrentVmrSha, CurrentRepoSha);

        // This represents a package being updated to a new version in the repo after the last flow
        var repoDependencyAtLastFlow = CreateDependency("Package.Updated.In.Repo.Before.Current.Flow", "2.0.0", LastVmrSha);
        var repoDependencyAtCurrentFlow = CreateDependency("Package.Updated.In.Repo.Before.Current.Flow", "2.0.2", "unspecifiedSha");
        var repoDependencyUpdate = new DependencyUpdate
        {
            From = repoDependencyAtLastFlow,
            To = repoDependencyAtCurrentFlow,
        };

        // Version details looks like this in the target branch
        _versionDetails[$"repo/{TargetBranch}"] = new VersionDetails(
            [
                CreateDependency("Package.from.build", "1.0.1", LastVmrSha),
                CreateDependency("Another.Package.From.Build", "1.0.1", LastVmrSha),
                CreateDependency("Yet.Another.Package.From.Build", "1.0.1", LastVmrSha),
                CreateDependency("Package.Excluded.From.Backflow", "1.0.0", LastVmrSha),
                CreateDependency("Package.Also.Excluded.From.Backflow", "1.0.0", LastVmrSha),
                CreateDependency("Package.Updated.In.Both", "3.0.0", LastVmrSha),
                CreateDependency("Package.Added.In.VMR", "2.0.0", LastVmrSha),
                CreateDependency("Package.Added.In.VMR2", "2.0.0", LastVmrSha),
                CreateDependency("Package.Added.In.Both", "2.2.2", LastVmrSha),
                repoDependencyAtCurrentFlow,
            ],
            new SourceDependency(VmrUri, MappingName, LastVmrSha, 123456));

        // Set up the version details for the unspecified repository reference
        _versionDetails["repo/"] = new VersionDetails(
            _versionDetails[$"repo/{TargetBranch}"].Dependencies.ToArray(),
            _versionDetails[$"repo/{TargetBranch}"].Source);

        var build = CreateNewBuild(CurrentVmrSha,
        [
            ("Package.From.Build", "1.0.5"),
            ("Package.Excluded.From.Backflow", "1.0.2"),
            ("Package.Also.Excluded.From.Backflow", "1.0.2"),
            ("Another.Package.From.Build", "1.0.5"),
            ("Yet.Another.Package.From.Build", "1.0.5"),
            ("Package.Removed.In.Repo", "1.0.5"),
            ("Package.Added.In.vmr2", "2.1.0"), // newer version than was just added to the VMR, but has a different indentation
            ("Package.Added.In.Repo", "4.0.0") // package was added at some point, but then a newer version was produced in the build
        ]);

        // The following packages are not updated:
        //   - Package.Removed.In.Repo - removed in repo (not getting updated)
        //   - Package.Removed.In.VMR - removed in VMR (and thus in repo)
        //   - Package.Added.In.VMR - added in VMR, so it was just added in the repo (not getting updated)
        //   - Package.Excluded.From.Backflow - excluded from backflow
        //   - Package.Also.Excluded.From.Backflow - excluded from backflow
        // Need to add Package.Added.In.Repo to emulate what the repo version would already have
        _versionDetails[$"repo/{TargetBranch}"] = new VersionDetails(
            _versionDetails[$"repo/{TargetBranch}"].Dependencies
                .Append(CreateDependency("Package.Added.In.Repo", "1.0.0", LastVmrSha))
                .ToArray(),
            _versionDetails[$"repo/{TargetBranch}"].Source);

        // Also update repo/ to match
        _versionDetails["repo/"] = new VersionDetails(
            _versionDetails[$"repo/{TargetBranch}"].Dependencies.ToArray(),
            _versionDetails[$"repo/{TargetBranch}"].Source);

        Dictionary<string, DependencyUpdate> additions = new()
        {
            { "Package.Added.In.Repo", new DependencyUpdate() { From = null, To = new DependencyDetail { Name = "Package.Added.In.Repo", Version = "1.0.1" }}},
            { "Package.Added.In.VMR2", new DependencyUpdate() { From = null, To = new DependencyDetail { Name = "Package.Added.In.VMR2", Version = "2.0.0" }} }
        };

        _versionDetailsFileMergerMock.Setup(x => x.MergeVersionDetails(
                It.IsAny<ILocalGitRepo>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ILocalGitRepo>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new VersionFileChanges<DependencyUpdate>(
                [],
                additions,
                new Dictionary<string, DependencyUpdate>()
                {
                    // this represents the repo-side update to Version.Details.xml prior to the current codeflow
                    { repoDependencyUpdate.From.Name, repoDependencyUpdate } 
                }));

        // Simulate dependency manager
        _assetLocationResolver.Setup(a => a.AddAssetLocationToDependenciesAsync(It.IsAny<IEnumerable<DependencyDetail>>()))
            .Callback((IEnumerable<DependencyDetail> deps) =>
            {
                // This would normally set locations, but we don't need that for the test
            })
            .Returns(Task.CompletedTask);
            
        await TestConflictResolver(
            build,
            lastFlows,
            currentFlow,
            expectedDependencies:
            [
                ("Package.From.Build", "1.0.5"),
                ("Package.Excluded.From.Backflow", "1.0.0"),
                ("Package.Also.Excluded.From.Backflow", "1.0.0"),
                ("Package.Updated.In.Both", "3.0.0"),
                ("Package.Added.In.Repo", "4.0.0"),
                ("Package.Added.In.VMR", "2.0.0"),
                ("Package.Added.In.VMR2", "2.1.0"),
                ("Package.Added.In.Both", "2.2.2"),
                ("Another.Package.From.Build", "1.0.5"),
                ("Yet.Another.Package.From.Build", "1.0.5"),
                ("Package.Updated.In.Repo.Before.Current.Flow", "2.0.2"),
                // Note: Package.Removed.In.Repo is not included as it was removed in repo
            ],
            expectedUpdates:
            [
                new("Package.From.Build", "1.0.1", "1.0.5"),
                new("Another.Package.From.Build", "1.0.1", "1.0.5"),
                new("Yet.Another.Package.From.Build", "1.0.1", "1.0.5"),
                new("Package.Added.In.Repo", null, "4.0.0"),
                new("Package.Added.In.VMR2", null, "2.1.0"),
            ],
            headBranchExisted: false,
            excludedAssets: ["Package.Excluded.From.Backflow", "Package.Also.*"]);
    }

    [Test]
    public void TestCodeflowDependencyUpdateCommitMessage()
    {
        DependencyUpdate dep1 = new()
        {
            From = new DependencyDetail()
            {
                Name = "Foo",
                Version = "2.0.0"
            },
            To = new DependencyDetail()
            {
                Name = "Foo",
                Version = "3.0.0"
            },
        };

        DependencyUpdate dep2 = new()
        {
            From = new DependencyDetail()
            {
                Name = "Bar",
                Version = "2.0.0"
            },
            To = new DependencyDetail()
            {
                Name = "Bar",
                Version = "3.0.0"
            },
        };

        DependencyUpdate dep3 = new()
        {
            From = new DependencyDetail()
            {
                Name = "Boz",
                Version = "1.0.0"
            },
            To = new DependencyDetail()
            {
                Name = "Boz",
                Version = "4.0.0"
            },
        };
        DependencyUpdate dep4 = new()
        {
            To = new DependencyDetail()
            {
                Name = "Bop",
                Version = "3.0.0"
            },
        };
        DependencyUpdate dep5 = new()
        {
            From = new DependencyDetail()
            {
                Name = "Bam",
                Version = "2.0.0"
            },
        };

        BackflowConflictResolver.BuildDependencyUpdateCommitMessage([dep1, dep2, dep3, dep4, dep5]).Should().BeEquivalentTo(
            """
            Updated Dependencies:
            Foo, Bar (Version 2.0.0 -> 3.0.0)
            Boz (Version 1.0.0 -> 4.0.0)

            Added Dependencies:
            Bop (Version 3.0.0)

            Removed Dependencies:
            Bam (Version 2.0.0)
            """.Trim()
            );

    }

    private async Task TestConflictResolver(
        Build build,
        LastFlows lastFlows,
        Backflow currentFlow,
        (string Name, string Version)[] expectedDependencies,
        ExpectedUpdate[] expectedUpdates,
        bool headBranchExisted,
        string[] excludedAssets)
    {
        var gitFileChanges = new GitFileContentContainer
        {
            VersionDetailsProps = new GitFile(VersionFiles.VersionDetailsProps, "<Project />"),
            VersionDetailsXml = new GitFile(VersionFiles.VersionDetailsXml, "<Project />"),
            NugetConfig = new GitFile(VersionFiles.NugetConfigNames.First(), "<configuration />"),
            GlobalJson = new GitFile(VersionFiles.GlobalJson, "{}"),
        };

        _dependencyFileManager
            .Setup(x => x.UpdateDependencyFiles(
                It.IsAny<IEnumerable<DependencyDetail>>(),
                It.IsAny<SourceDependency>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<DependencyDetail>>(),
                null,
                It.IsAny<bool?>(),
                It.IsAny<UnixPath>()))
            .Callback((IEnumerable<DependencyDetail> itemsToUpdate,
                       SourceDependency? sourceDependency,
                       string repo,
                       string? commit,
                       IEnumerable<DependencyDetail> oldDependencies,
                       SemanticVersion? incomingDotNetSdkVersion,
                       bool? _,
                       UnixPath __) =>
            {
                // Update dependencies in-memory
                var key = (repo == _vmrPath ? "vmr" : "repo") + "/" + commit;
                _versionDetails[key] = new VersionDetails(
                    oldDependencies
                        .Select(dep => itemsToUpdate.FirstOrDefault(d => d.Name.Equals(dep.Name, StringComparison.OrdinalIgnoreCase)) ?? dep)
                        .ToArray(),
                    new SourceDependency(build, MappingName));

            })
            .ReturnsAsync(gitFileChanges);

        var cancellationToken = new CancellationToken();
        VersionFileUpdateResult mergeResult = await _conflictResolver.TryMergingBranchAndUpdateDependencies(
            new CodeflowOptions(
                new SourceMapping(MappingName, "https://github/repo1", "main", [], [], false),
                currentFlow,
                TargetBranch,
                PrBranch,
                build,
                excludedAssets,
                EnableRebase: true,
                ForceUpdate: false,
                UnsafeFlow: false),
            lastFlows,
            _localRepo.Object,
            headBranchExisted,
            cancellationToken);

        mergeResult.DependencyUpdates
            .Select(update => new ExpectedUpdate(
                update.To.Name,
                update.From?.Version,
                update.To?.Version))
            .Should().BeEquivalentTo(expectedUpdates, options => options.WithoutStrictOrdering());

        // Test the final state of V.D.xml (from the working tree)
        _versionDetails["repo/"].Dependencies
            .Select(x => (x.Name, x.Version))
            .Should().BeEquivalentTo(expectedDependencies, options => options.WithoutStrictOrdering()
                .Using<string>(ctx => ctx.Subject.Equals(ctx.Expectation, StringComparison.OrdinalIgnoreCase))
                .WhenTypeIs<string>());

        _libGit2Client.Verify(x => x.CommitFilesAsync(It.IsAny<List<GitFile>>(), _repoPath.Path, null, null), Times.AtLeastOnce);
        _localRepo.Verify(x => x.StageAsync(It.IsAny<IEnumerable<string>>(), cancellationToken), Times.AtLeastOnce);
        _localRepo.Verify(x => x.CommitAsync(It.IsAny<string>(), false, null, cancellationToken), Times.AtLeastOnce);
    }

    private Build CreateNewBuild(string commit, (string name, string version)[] assets)
    {
        var assetId = 1;
        _buildId++;

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
            GitHubRepository = VmrUri,
        };

        return build;
    }

    private static DependencyDetail CreateDependency(string name, string version, string commit, DependencyType type = DependencyType.Product)
        => new()
        {
            Name = name,
            Version = version,
            Commit = commit,
            RepoUri = VmrUri,
            Type = type,
        };

    private record ExpectedUpdate(string Name, string? From, string? To);
}
