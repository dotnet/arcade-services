// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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

public class VersionFileConflictResolverTests
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
    private readonly Mock<ILocalLibGit2Client> _libGit2Client = new();
    private readonly Mock<ILocalGitRepoFactory> _localGitRepoFactory = new();
    private readonly Mock<IVersionDetailsParser> _versionDetailsParser = new();
    private readonly Mock<IAssetLocationResolver> _assetLocationResolver = new();
    private readonly Mock<IDependencyFileManager> _dependencyFileManager = new();
    private readonly Mock<IFileSystem> _fileSystem = new();

    private readonly Mock<ILocalGitRepo> _localRepo = new();
    private readonly Mock<ILocalGitRepo> _localVmr = new();

    VersionFileConflictResolver _versionFileConflictResolver = null!;

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
            .Setup(x => x.PatchesPath)
            .Returns(_vmrPath + "/patches");
        _vmrInfo
            .Setup(x => x.AdditionalMappings)
            .Returns(Array.Empty<(string, string?)>());
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
            .ReturnsAsync((string _, string commit, string __) => $"repo/{commit}");
        _localRepo
            .SetReturnsDefault(Task.CompletedTask);

        _localVmr.Reset();
        _localVmr
            .SetupGet(x => x.Path)
            .Returns(_vmrPath);
        _localVmr
            .Setup(x => x.GetFileFromGitAsync(It.Is<string>(s => s.Contains(VersionFiles.VersionDetailsXml)), It.IsAny<string>(), null))
            .ReturnsAsync((string _, string commit, string __) => $"vmr/{commit}");

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

        _dependencyFileManager.Reset();
        _dependencyFileManager
            .Setup(x => x.AddDependencyAsync(It.IsAny<DependencyDetail>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback((DependencyDetail dep, string repo, string commit) =>
            {
                var key = (repo == _vmrPath ? "vmr" : "repo") + "/" + commit;
                VersionDetails versionDetails = _versionDetails.TryGetValue(key, out var vd) ? vd : new([], null);
                _versionDetails[key] = new VersionDetails(
                    versionDetails.Dependencies.Append(dep).ToArray(),
                    versionDetails.Source);
            })
            .Returns(Task.CompletedTask);

        _dependencyFileManager
            .Setup(x => x.RemoveDependencyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback((string name, string repo, string commit, bool _) =>
            {
                var key = (repo == _vmrPath ? "vmr" : "repo") + "/" + commit;
                var versionDetails = _versionDetails[key];
                _versionDetails[key] = new VersionDetails(
                    [..versionDetails.Dependencies.Where(d => d.Name != name)],
                    versionDetails.Source);
            })
            .Returns(Task.CompletedTask);

        _fileSystem.Reset();

        _versionFileConflictResolver = new(
            _vmrInfo.Object,
            _libGit2Client.Object,
            _localGitRepoFactory.Object,
            _versionDetailsParser.Object,
            _assetLocationResolver.Object,
            new CoherencyUpdateResolver(Mock.Of<IBasicBarClient>(), new NullLogger<CoherencyUpdateResolver>()),
            _dependencyFileManager.Object,
            _fileSystem.Object,
            new NullLogger<VersionFileConflictResolver>());
    }

    // Tests a case when packages were updated in the repo as well as in VMR and some created during the build.
    // Tests that the versions are merged correctly.
    [Test]
    public async Task VersionsAreMergedInBackflowAfterForwardFlowTest()
    {
        var lastFlow = new ForwardFlow(LastRepoSha, LastVmrSha);
        var currentFlow = new Backflow(CurrentVmrSha, CurrentRepoSha);

        // Dependencies in the repo after last flow
        _versionDetails[$"repo/{lastFlow.RepoSha}"] = new VersionDetails(
            [
                CreateDependency("Package.From.Build", "1.0.0", LastVmrSha),
                CreateDependency("Package.Removed.In.Repo", "1.0.0", LastVmrSha),
                CreateDependency("Package.Updated.In.Both", "1.0.0", LastVmrSha),
                CreateDependency("Package.Removed.In.VMR", "1.0.0", LastVmrSha), // Will be removed in VMR
            ],
            new SourceDependency(VmrUri, LastVmrSha, 123456));

        // Dependencies in the target branch of the repo (what we are flowing to)
        _versionDetails[$"repo/{TargetBranch}"] = new VersionDetails(
            [
                CreateDependency("Package.From.Build", "1.0.1", LastVmrSha), // Updated
                CreateDependency("Package.Updated.In.Both", "1.0.3", LastVmrSha), // Updated (vmr updated to 3.0.0)
                CreateDependency("Package.Added.In.Repo", "1.0.0", LastVmrSha), // Added
            ],
            new SourceDependency(VmrUri, LastVmrSha, 123456));

        // The PR branch was just created so it has the same dependencies as the target branch
        _versionDetails[$"repo/{PrBranch}"] = _versionDetails["repo/main"];
        _versionDetails["repo/"] = _versionDetails["repo/main"];

        // Dependencies in the VMR after last flow (same as repo's - let's assume we forward flowed those)
        _versionDetails[$"vmr/{lastFlow.VmrSha}"] = _versionDetails["repo/main"];

        // Dependencies in the VMR commit we're flowing
        _versionDetails[$"vmr/{CurrentVmrSha}"] = new VersionDetails(
            [
                CreateDependency("Package.From.Build", "1.0.0", LastVmrSha),
                CreateDependency("Package.Removed.In.Repo", "1.0.0", LastVmrSha),
                CreateDependency("Package.Updated.In.Both", "3.0.0", LastVmrSha), // Updated (repo updated to 1.0.3)
                CreateDependency("Package.Added.In.VMR", "2.0.0", LastVmrSha), // Added
                // Package.Removed.In.VMR removed
            ],
            new SourceDependency(VmrUri, LastVmrSha, 123456));

        var build = CreateNewBuild(CurrentVmrSha,
        [
            ("Package.From.Build", "1.0.5"),
            ("Another.Package.From.Build", "1.0.5"),
            ("Yet.Another.Package.From.Build", "1.0.5")
        ]);

        // The final set of updates should be following
        // Package.From.Build 1.0.5 - Coming from the build
        // Package.Updated.In.Both 3.0.0 - Updated in repo and VMR but VMR's update is higher
        // The following packages are not updated:
        //   - Package.Removed.In.Repo - removed in repo (not getting updated)
        //   - Package.Removed.In.VMR - removed in VMR (and thus in repo)
        //   - Package.Added.In.Repo: 1.0.0 - added in repo, so already there
        //   - Package.Added.In.VMR - added in VMR, so it was just added in the repo (not getting updated)
        await TestConflictResolver(
            build,
            lastFlow,
            currentFlow,
            expectedDependencies:
            [
                ("Package.From.Build", "1.0.5"),
                ("Package.Updated.In.Both", "3.0.0"),
                ("Package.Added.In.Repo", "1.0.0"),
                ("Package.Added.In.VMR", "2.0.0"),
            ],
            expectedUpdates:
            [
                new("Package.Added.In.VMR", null, "2.0.0"),
                new("Package.From.Build", "1.0.1", "1.0.5"),
                new("Package.Updated.In.Both", "1.0.3", "3.0.0"),
            ]);

        // Now we will add a new dependency to the PR branch
        // We will change a dependency in the repo too
        // And we will flow a new build from the VMR to the existing PR
        var newVmrSha = "new flow VMR SHA";
        var newFlow = new Backflow(newVmrSha, CurrentRepoSha);
        var newRepoDependency = CreateDependency("New.Package.In.Repo", "4.0.0", "sha does not matter");
        var newPrDependency = CreateDependency("New.Package.In.Pr", "4.0.0", "sha does not matter");
        var newVmrDependency = CreateDependency("New.Package.In.Vmr", "4.0.0", "sha does not matter");

        // Current flow will become last flow for the next test
        _versionDetails[$"repo/{currentFlow.RepoSha}"] = _versionDetails[$"repo/{LastRepoSha}"];

        // Main branch will have the new package
        await _dependencyFileManager.Object.AddDependencyAsync(newRepoDependency, _repoPath, TargetBranch);

        // PR branch will have its own new package
        await _dependencyFileManager.Object.AddDependencyAsync(newPrDependency, _repoPath, null);
        _versionDetails[$"repo/{PrBranch}"] = _versionDetails["repo/"];

        // VMR content will be the same as before plus the new VMR package
        _versionDetails[$"vmr/{newVmrSha}"] = _versionDetails[$"vmr/{CurrentVmrSha}"];
        await _dependencyFileManager.Object.AddDependencyAsync(newVmrDependency, _vmrPath, newVmrSha);

        build = CreateNewBuild(newVmrSha, [..build.Assets.Select(a => (a.Name, "1.0.6"))]);

        await TestConflictResolver(
            build,
            currentFlow,
            newFlow,
            expectedDependencies:
            [
                // Same as before
                ("Package.From.Build", "1.0.6"),
                ("Package.Updated.In.Both", "3.0.0"),
                ("Package.Added.In.Repo", "1.0.0"),
                ("Package.Added.In.VMR", "2.0.0"),
                // New packages
                ("New.Package.In.Repo", "4.0.0"),
                ("New.Package.In.Pr", "4.0.0"),
                ("New.Package.In.Vmr", "4.0.0"),
            ],
            expectedUpdates:
            [
                new ExpectedUpdate("New.Package.In.Vmr", null, To: "4.0.0"),
                new ExpectedUpdate("New.Package.In.Repo", null, To: "4.0.0"),
                new ExpectedUpdate("Package.From.Build", "1.0.5", To: "1.0.6"),
            ]);
    }

    // Tests a case when conflicting updates were made in the repo and VMR.
    // There are two backflows in a row and in between those,
    // a package was added in the VMR while at the same time removed in the repo.
    [Test]
    public async Task ConflictingChangesThrowTest()
    {
        var lastFlow = new Backflow(LastRepoSha, LastVmrSha);
        var currentFlow = new Backflow(CurrentVmrSha, CurrentRepoSha);

        var withPackage = new VersionDetails(
        [
            CreateDependency("Package", "1.0.0", LastVmrSha),
            CreateDependency("Conflicting.Package", "1.0.0", LastVmrSha),
        ],
        Source: null);

        var withoutPackage = new VersionDetails(
        [
            CreateDependency("Package", "1.0.0", LastVmrSha),
        ],
        Source: null);

        // Package removed in the repo
        _versionDetails[$"repo/{lastFlow.RepoSha}"] = withPackage;
        _versionDetails[$"repo/{TargetBranch}"] = withoutPackage;
        _versionDetails[$"repo/{PrBranch}"] = withoutPackage;
        _versionDetails["repo/"] = withoutPackage;

        // Package added in the VMR
        _versionDetails[$"vmr/{lastFlow.VmrSha}"] = withoutPackage;
        _versionDetails[$"vmr/{CurrentVmrSha}"] = withPackage;

        var action = async () => await TestConflictResolver(
            CreateNewBuild(CurrentVmrSha,
            [
                ("Package", "1.0.5")
            ]),
            lastFlow,
            currentFlow,
            [],
            []);

        await action.Should().ThrowAsync<ConflictingDependencyUpdateException>();
    }

    private async Task TestConflictResolver(
        Build build,
        Codeflow lastFlow,
        Backflow currentFlow,
        (string Name, string Version)[] expectedDependencies,
        ExpectedUpdate[] expectedUpdates)
    {
        var gitFileChanges = new GitFileContentContainer();
        _dependencyFileManager
            .Setup(x => x.UpdateDependencyFiles(
                It.IsAny<IEnumerable<DependencyDetail>>(),
                It.IsAny<SourceDependency>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<DependencyDetail>>(),
                null))
            .Callback((IEnumerable<DependencyDetail> itemsToUpdate,
                       SourceDependency? sourceDependency,
                       string repo,
                       string? commit,
                       IEnumerable<DependencyDetail> oldDependencies,
                       SemanticVersion? incomingDotNetSdkVersion) =>
            {
                // Update dependencies in-memory
                var key = (repo == _vmrPath ? "vmr" : "repo") + "/" + commit;
                _versionDetails[key] = new VersionDetails(
                    oldDependencies
                        .Select(dep => itemsToUpdate.FirstOrDefault(d => d.Name == dep.Name) ?? dep)
                        .ToArray(),
                    new SourceDependency(build));

            })
            .ReturnsAsync(gitFileChanges);

        var cancellationToken = new CancellationToken();
        List<DependencyUpdate> updates = await _versionFileConflictResolver.BackflowDependenciesAndToolset(
            MappingName,
            _localRepo.Object,
            TargetBranch,
            build,
            excludedAssets: [],
            lastFlow,
            currentFlow,
            cancellationToken);

        updates
            .Select(update => new ExpectedUpdate(
                update.From?.Name ?? update.To.Name,
                update.From?.Version,
                update.To?.Version))
            .Should().BeEquivalentTo(expectedUpdates, options => options.WithoutStrictOrdering());

        // Test the final state of V.D.xml (from the working tree)
        _versionDetails["repo/"].Dependencies
            .Select(x => (x.Name, x.Version))
            .Should().BeEquivalentTo(expectedDependencies, options => options.WithoutStrictOrdering());

        _libGit2Client.Verify(x => x.CommitFilesAsync(It.IsAny<List<GitFile>>(), _repoPath.Path, null, null), Times.AtLeastOnce);
        _localRepo.Verify(x => x.StageAsync(new[] { "." }, cancellationToken), Times.AtLeastOnce);
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
