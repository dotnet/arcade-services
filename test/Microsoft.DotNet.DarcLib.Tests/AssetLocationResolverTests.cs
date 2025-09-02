// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;




public class AssetLocationResolverTests
{
    /// <summary>
    /// Validates that when assets exist from multiple builds with matching and non-matching SHAs,
    /// the resolver selects the latest asset (by BuildId) whose producing build's commit equals the dependency's commit,
    /// and assigns only NuGet feed locations to the dependency.
    /// Inputs:
    ///  - A dependency with Name, Version, Commit.
    ///  - BAR client returning multiple assets from different build IDs with mixed Location types.
    /// Expected:
    ///  - Dependency.Locations becomes the set of locations from the latest matching asset where LocationType == NugetFeed.
    /// Notes:
    ///  - This test is marked ignored because constructing instances of Asset, Build, and related location model types
    ///    is not feasible without relying on unverified constructors or virtual members. Replace the TODOs with real instances
    ///    of Microsoft.DotNet.ProductConstructionService.Client.Models types and unignore the test.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddAssetLocationToDependenciesAsync_LocationsFromLatestAssetApplied()
    {
        // Arrange
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);

        var assetMismatched = new Asset(
            id: 10,
            buildId: 100,
            nonShipping: false,
            name: "Package.X",
            version: "1.2.3",
            locations: new List<AssetLocation>
            {
                new AssetLocation(id: 101, type: LocationType.NugetFeed, location: "nuget-feed-mismatch")
            });

        var assetOlder = new Asset(
            id: 11,
            buildId: 200,
            nonShipping: false,
            name: "Package.X",
            version: "1.2.3",
            locations: new List<AssetLocation>
            {
                new AssetLocation(id: 201, type: LocationType.Container, location: "container://old"),
                new AssetLocation(id: 202, type: LocationType.NugetFeed, location: "nuget-feed-1")
            });

        var assetNewer = new Asset(
            id: 12,
            buildId: 300,
            nonShipping: false,
            name: "Package.X",
            version: "1.2.3",
            locations: new List<AssetLocation>
            {
                new AssetLocation(id: 301, type: LocationType.Container, location: "container://new"),
                new AssetLocation(id: 302, type: LocationType.NugetFeed, location: "nuget-feed-2")
            });

        barClientMock
            .Setup(m => m.GetAssetsAsync("Package.X", "1.2.3", null, null))
            .ReturnsAsync(new List<Asset> { assetMismatched, assetOlder, assetNewer });

        var build100 = new Build(100, DateTimeOffset.UtcNow, 0, false, false, "sha-y",
            new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());
        var build200 = new Build(200, DateTimeOffset.UtcNow, 0, false, false, "sha-x",
            new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());
        var build300 = new Build(300, DateTimeOffset.UtcNow, 0, false, false, "sha-x",
            new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());

        barClientMock.Setup(m => m.GetBuildAsync(100)).ReturnsAsync(build100);
        barClientMock.Setup(m => m.GetBuildAsync(200)).ReturnsAsync(build200);
        barClientMock.Setup(m => m.GetBuildAsync(300)).ReturnsAsync(build300);

        var resolver = new AssetLocationResolver(barClientMock.Object);

        var dependency = new DependencyDetail
        {
            Name = "Package.X",
            Version = "1.2.3",
            Commit = "sha-x",
            RepoUri = "https://repo/x",
        };

        // Act
        await resolver.AddAssetLocationToDependenciesAsync(new[] { dependency });

        // Assert
        dependency.Locations.Should().BeEquivalentTo(new[] { "nuget-feed-2" });
    }

    /// <summary>
    /// Verifies that non-NuGet feed locations are ignored and only NuGet feed locations are assigned.
    /// Inputs:
    ///  - A dependency with matching commit.
    ///  - BAR client returns an asset whose Locations include both NugetFeed and other types.
    /// Expected:
    ///  - Dependency.Locations includes only the NugetFeed locations.
    /// Notes:
    ///  - Ignored until concrete Asset/Location model instances can be created.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddAssetLocationToDependenciesAsync_IgnoresNonNugetFeedLocations()
    {
        // Arrange
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);

        var assetWithMixedLocations = new Asset(
            id: 9,
            buildId: 700,
            nonShipping: false,
            name: "Package.Y",
            version: "2.0.0",
            locations: new List<AssetLocation>
            {
                new AssetLocation(id: 31, type: LocationType.Container, location: "container://ignore"),
                new AssetLocation(id: 32, type: LocationType.NugetFeed, location: "nuget-feed-only")
            });

        barClientMock
            .Setup(m => m.GetAssetsAsync("Package.Y", "2.0.0", null, null))
            .ReturnsAsync(new List<Asset> { assetWithMixedLocations });

        var producingBuild = new Build(700, DateTimeOffset.UtcNow, 0, false, false, "sha-y",
            new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());

        barClientMock.Setup(m => m.GetBuildAsync(assetWithMixedLocations.BuildId)).ReturnsAsync(producingBuild);

        var resolver = new AssetLocationResolver(barClientMock.Object);

        var dependency = new DependencyDetail
        {
            Name = "Package.Y",
            Version = "2.0.0",
            Commit = "sha-y",
            RepoUri = "https://repo/y",
        };

        // Act
        await resolver.AddAssetLocationToDependenciesAsync(new[] { dependency });

        // Assert
        dependency.Locations.Should().BeEquivalentTo(new[] { "nuget-feed-only" });
    }

    /// <summary>
    /// Ensures that build retrieval is cached per BuildId so that the BAR client is not called
    /// multiple times for the same BuildId across assets/dependencies.
    /// Inputs:
    ///  - Assets for two dependencies sharing the same BuildId.
    /// Expected:
    ///  - IBasicBarClient.GetBuildAsync is called only once per unique BuildId.
    /// Notes:
    ///  - Ignored until concrete Asset/Build model instances can be constructed.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddAssetLocationToDependenciesAsync_CachesBuildLookupPerBuildId()
    {
        // Arrange
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);

        var sharedBuildId = 900;
        var sharedBuildAsset = new Asset(
            id: 101,
            buildId: sharedBuildId,
            nonShipping: false,
            name: "Package.A",
            version: "1.0.0",
            locations: new List<AssetLocation>
            {
                new AssetLocation(id: 11, type: LocationType.NugetFeed, location: "nuget-feed-shared")
            });

        barClientMock
            .Setup(m => m.GetAssetsAsync("Package.A", "1.0.0", null, null))
            .ReturnsAsync(new List<Asset> { sharedBuildAsset });
        barClientMock
            .Setup(m => m.GetAssetsAsync("Package.B", "1.1.0", null, null))
            .ReturnsAsync(new List<Asset> { sharedBuildAsset });

        var producingBuild = new Build(sharedBuildId, DateTimeOffset.UtcNow, 0, false, false, "sha-shared",
            new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());

        barClientMock.Setup(m => m.GetBuildAsync(sharedBuildId)).ReturnsAsync(producingBuild);

        var resolver = new AssetLocationResolver(barClientMock.Object);

        var dep1 = new DependencyDetail { Name = "Package.A", Version = "1.0.0", Commit = "sha-shared", RepoUri = "https://repo/a" };
        var dep2 = new DependencyDetail { Name = "Package.B", Version = "1.1.0", Commit = "sha-shared", RepoUri = "https://repo/b" };

        // Act
        await resolver.AddAssetLocationToDependenciesAsync(new[] { dep1, dep2 });

        // Assert
        barClientMock.Verify(m => m.GetBuildAsync(sharedBuildId), Times.Once);
    }

    /// <summary>
    /// Verifies that the constructor of AssetLocationResolver accepts both a null and a valid IBasicBarClient,
    /// does not throw an exception, and returns a non-null instance.
    /// Inputs:
    /// - passNull: when true, passes null as IBasicBarClient; when false, passes a mocked IBasicBarClient.
    /// Expected:
    /// - No exception is thrown.
    /// - The constructed instance is not null.
    /// </summary>
    /// <param name="passNull">Indicates whether to pass null or a mocked IBasicBarClient to the constructor.</param>
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_NullOrValidBarClient_InstanceCreated(bool passNull)
    {
        // Arrange
        IBasicBarClient barClient = passNull ? null : new Mock<IBasicBarClient>().Object;
        AssetLocationResolver created = null;

        // Act
        Action act = () => { created = new AssetLocationResolver(barClient); };

        // Assert
        act.Should().NotThrow();
        created.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that when no assets are returned for a dependency, no locations are set.
    /// Input: A dependency with Name/Version/Commit where GetAssetsAsync returns an empty list.
    /// Expected: Dependency.Locations remains empty and GetBuildAsync is never called.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddAssetLocationToDependenciesAsync_NoAssets_DoesNotSetLocations()
    {
        // Arrange
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var dependency = new DependencyDetail { Name = "Pkg.A", Version = "1.0.0", Commit = "abc123" };

        barClient
            .Setup(m => m.GetAssetsAsync("Pkg.A", "1.0.0", null, null))
            .ReturnsAsync(new List<Asset>());

        var resolver = new AssetLocationResolver(barClient.Object);
        var dependencies = new List<DependencyDetail> { dependency };

        // Act
        await resolver.AddAssetLocationToDependenciesAsync(dependencies);

        // Assert
        dependency.Locations.Should().NotBeNull();
        dependency.Locations.Should().BeEmpty();
        barClient.Verify(m => m.GetAssetsAsync("Pkg.A", "1.0.0", null, null), Times.Once);
        barClient.Verify(m => m.GetBuildAsync(It.IsAny<int>()), Times.Never);
    }

    /// <summary>
    /// Verifies that assets whose producing builds do not have the same commit as the dependency are ignored.
    /// Input: Two assets with different BuildIds; builds' commits do not match dependency.Commit.
    /// Expected: No locations set; GetBuildAsync called once per asset.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddAssetLocationToDependenciesAsync_AssetsWithMismatchedCommit_IgnoresAssets()
    {
        // Arrange
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var dependency = new DependencyDetail { Name = "Pkg.B", Version = "2.0.0", Commit = "dep-sha" };

        var assets = new List<Asset>
            {
                new Asset(id: 1, buildId: 100, nonShipping: false, name: "Pkg.B", version: "2.0.0", locations: new List<AssetLocation>()),
                new Asset(id: 2, buildId: 200, nonShipping: true, name: "Pkg.B", version: "2.0.0", locations: new List<AssetLocation>())
            };

        barClient
            .Setup(m => m.GetAssetsAsync("Pkg.B", "2.0.0", null, null))
            .ReturnsAsync(assets);

        var build100 = new Build(100, DateTimeOffset.UtcNow, 0, false, false, "other-sha-1", new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());
        var build200 = new Build(200, DateTimeOffset.UtcNow, 0, false, false, "other-sha-2", new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());

        barClient.Setup(m => m.GetBuildAsync(100)).ReturnsAsync(build100);
        barClient.Setup(m => m.GetBuildAsync(200)).ReturnsAsync(build200);

        var resolver = new AssetLocationResolver(barClient.Object);
        var dependencies = new List<DependencyDetail> { dependency };

        // Act
        await resolver.AddAssetLocationToDependenciesAsync(dependencies);

        // Assert
        dependency.Locations.Should().NotBeNull();
        dependency.Locations.Should().BeEmpty();
        barClient.Verify(m => m.GetAssetsAsync("Pkg.B", "2.0.0", null, null), Times.Once);
        barClient.Verify(m => m.GetBuildAsync(100), Times.Once);
        barClient.Verify(m => m.GetBuildAsync(200), Times.Once);
    }

    /// <summary>
    /// Verifies that when a matching asset is found but it has null Locations, the dependency remains unchanged.
    /// Input: A single asset whose producing build matches the dependency commit; asset.Locations is null.
    /// Expected: Dependency.Locations remains empty; GetBuildAsync hits only once.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddAssetLocationToDependenciesAsync_MatchingCommit_LocationsNull_DoesNotSetLocations()
    {
        // Arrange
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var dependency = new DependencyDetail { Name = "Pkg.C", Version = "3.0.0", Commit = "same-sha" };

        var asset = new Asset(id: 3, buildId: 300, nonShipping: false, name: "Pkg.C", version: "3.0.0", locations: null);
        barClient
            .Setup(m => m.GetAssetsAsync("Pkg.C", "3.0.0", null, null))
            .ReturnsAsync(new List<Asset> { asset });

        var build300 = new Build(300, DateTimeOffset.UtcNow, 0, false, false, "same-sha", new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());
        barClient.Setup(m => m.GetBuildAsync(300)).ReturnsAsync(build300);

        var resolver = new AssetLocationResolver(barClient.Object);
        var dependencies = new List<DependencyDetail> { dependency };

        // Act
        await resolver.AddAssetLocationToDependenciesAsync(dependencies);

        // Assert
        dependency.Locations.Should().NotBeNull();
        dependency.Locations.Should().BeEmpty();
        barClient.Verify(m => m.GetAssetsAsync("Pkg.C", "3.0.0", null, null), Times.Once);
        barClient.Verify(m => m.GetBuildAsync(300), Times.Once);
    }

    /// <summary>
    /// Verifies that among multiple matching assets, the latest by BuildId is chosen and only NugetFeed locations are assigned.
    /// Input: Two assets with matching commit and different BuildIds (older and newer), each with mixed location types.
    /// Expected: Dependency.Locations equals the NugetFeed locations from the newer asset only.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddAssetLocationToDependenciesAsync_MultipleMatchingAssets_ChoosesLatestAndFiltersNugetFeed()
    {
        // Arrange
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var dependency = new DependencyDetail { Name = "Pkg.D", Version = "4.0.0", Commit = "commit-123" };

        var assetOld = new Asset(
            id: 4,
            buildId: 400,
            nonShipping: false,
            name: "Pkg.D",
            version: "4.0.0",
            locations: new List<AssetLocation>
            {
                    new AssetLocation(id: 1, type: LocationType.Container, location: "container://old"),
                    new AssetLocation(id: 2, type: LocationType.NugetFeed, location: "https://nuget.old/index.json")
            });

        var assetNew = new Asset(
            id: 5,
            buildId: 500,
            nonShipping: true,
            name: "Pkg.D",
            version: "4.0.0",
            locations: new List<AssetLocation>
            {
                    new AssetLocation(id: 3, type: LocationType.NugetFeed, location: "https://nuget.new/index.json"),
                    new AssetLocation(id: 4, type: LocationType.Container, location: "container://new")
            });

        barClient
            .Setup(m => m.GetAssetsAsync("Pkg.D", "4.0.0", null, null))
            .ReturnsAsync(new List<Asset> { assetOld, assetNew });

        var build400 = new Build(400, DateTimeOffset.UtcNow, 0, false, false, "commit-123", new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());
        var build500 = new Build(500, DateTimeOffset.UtcNow, 0, false, false, "commit-123", new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());

        barClient.Setup(m => m.GetBuildAsync(400)).ReturnsAsync(build400);
        barClient.Setup(m => m.GetBuildAsync(500)).ReturnsAsync(build500);

        var resolver = new AssetLocationResolver(barClient.Object);
        var dependencies = new List<DependencyDetail> { dependency };

        // Act
        await resolver.AddAssetLocationToDependenciesAsync(dependencies);

        // Assert
        dependency.Locations.Should().NotBeNull();
        dependency.Locations.Should().BeEquivalentTo(new[] { "https://nuget.new/index.json" });
        barClient.Verify(m => m.GetAssetsAsync("Pkg.D", "4.0.0", null, null), Times.Once);
        barClient.Verify(m => m.GetBuildAsync(400), Times.Once);
        barClient.Verify(m => m.GetBuildAsync(500), Times.Once);
    }

    /// <summary>
    /// Verifies that when matching asset has only non-NugetFeed locations, the dependency ends up with an empty locations list.
    /// Input: One asset whose producing build commit matches; asset has only Container locations.
    /// Expected: Dependency.Locations is set to an empty enumeration (not null).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddAssetLocationToDependenciesAsync_MatchingCommit_NoNugetLocations_SetsEmptyLocations()
    {
        // Arrange
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var dependency = new DependencyDetail { Name = "Pkg.E", Version = "5.0.0", Commit = "same" };

        var asset = new Asset(
            id: 6,
            buildId: 600,
            nonShipping: false,
            name: "Pkg.E",
            version: "5.0.0",
            locations: new List<AssetLocation>
            {
                    new AssetLocation(id: 10, type: LocationType.Container, location: "container://only")
            });

        barClient
            .Setup(m => m.GetAssetsAsync("Pkg.E", "5.0.0", null, null))
            .ReturnsAsync(new List<Asset> { asset });

        var build600 = new Build(600, DateTimeOffset.UtcNow, 0, false, false, "same", new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());
        barClient.Setup(m => m.GetBuildAsync(600)).ReturnsAsync(build600);

        var resolver = new AssetLocationResolver(barClient.Object);
        var dependencies = new List<DependencyDetail> { dependency };

        // Act
        await resolver.AddAssetLocationToDependenciesAsync(dependencies);

        // Assert
        dependency.Locations.Should().NotBeNull();
        dependency.Locations.Should().BeEmpty();
        barClient.Verify(m => m.GetAssetsAsync("Pkg.E", "5.0.0", null, null), Times.Once);
        barClient.Verify(m => m.GetBuildAsync(600), Times.Once);
    }

    /// <summary>
    /// Verifies build caching across multiple dependencies within a single call:
    /// when assets from multiple dependencies share the same BuildId, the producing build is fetched only once.
    /// Input: Two dependencies whose assets reference the same BuildId and match the same commit.
    /// Expected: GetBuildAsync called exactly once for the shared BuildId; both dependencies get their respective NugetFeed locations.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddAssetLocationToDependenciesAsync_MultipleDependencies_SharedBuild_UsesBuildCache()
    {
        // Arrange
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);

        var d1 = new DependencyDetail { Name = "Pkg.F1", Version = "1.1.1", Commit = "cache-sha" };
        var d2 = new DependencyDetail { Name = "Pkg.F2", Version = "2.2.2", Commit = "cache-sha" };

        var sharedBuildId = 777;

        var asset1 = new Asset(
            id: 7,
            buildId: sharedBuildId,
            nonShipping: false,
            name: "Pkg.F1",
            version: "1.1.1",
            locations: new List<AssetLocation>
            {
                    new AssetLocation(id: 21, type: LocationType.NugetFeed, location: "https://nuget.pkgf1/index.json")
            });

        var asset2 = new Asset(
            id: 8,
            buildId: sharedBuildId,
            nonShipping: true,
            name: "Pkg.F2",
            version: "2.2.2",
            locations: new List<AssetLocation>
            {
                    new AssetLocation(id: 22, type: LocationType.NugetFeed, location: "https://nuget.pkgf2/index.json")
            });

        barClient
            .Setup(m => m.GetAssetsAsync("Pkg.F1", "1.1.1", null, null))
            .ReturnsAsync(new List<Asset> { asset1 });
        barClient
            .Setup(m => m.GetAssetsAsync("Pkg.F2", "2.2.2", null, null))
            .ReturnsAsync(new List<Asset> { asset2 });

        var sharedBuild = new Build(sharedBuildId, DateTimeOffset.UtcNow, 0, false, false, "cache-sha", new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());
        barClient.Setup(m => m.GetBuildAsync(sharedBuildId)).ReturnsAsync(sharedBuild);

        var resolver = new AssetLocationResolver(barClient.Object);
        var dependencies = new List<DependencyDetail> { d1, d2 };

        // Act
        await resolver.AddAssetLocationToDependenciesAsync(dependencies);

        // Assert
        d1.Locations.Should().BeEquivalentTo(new[] { "https://nuget.pkgf1/index.json" });
        d2.Locations.Should().BeEquivalentTo(new[] { "https://nuget.pkgf2/index.json" });

        barClient.Verify(m => m.GetAssetsAsync("Pkg.F1", "1.1.1", null, null), Times.Once);
        barClient.Verify(m => m.GetAssetsAsync("Pkg.F2", "2.2.2", null, null), Times.Once);
        barClient.Verify(m => m.GetBuildAsync(sharedBuildId), Times.Once);
    }

    /// <summary>
    /// Verifies that when the dependencies collection is empty, the resolver makes no BAR client calls.
    /// Inputs:
    /// - An empty dependencies array.
    /// Expected:
    /// - IBasicBarClient.GetAssetsAsync is never called.
    /// - IBasicBarClient.GetBuildAsync is never called.
    /// - No exception is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddAssetLocationToDependenciesAsync_EmptyDependencies_NoBarCalls()
    {
        // Arrange
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var resolver = new AssetLocationResolver(barClientMock.Object);
        var dependencies = Array.Empty<DependencyDetail>();

        // Act
        await resolver.AddAssetLocationToDependenciesAsync(dependencies);

        // Assert
        barClientMock.Verify(m => m.GetAssetsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool?>()), Times.Never());
        barClientMock.Verify(m => m.GetBuildAsync(It.IsAny<int>()), Times.Never());
    }


    /// <summary>
    /// Verifies that the constructor of AssetLocationResolver accepts both a null and a valid IBasicBarClient,
    /// does not throw an exception, returns a non-null instance, and implements IAssetLocationResolver.
    /// Inputs:
    /// - passNull: when true, passes null as IBasicBarClient; when false, passes a mocked IBasicBarClient.
    /// Expected:
    /// - No exception is thrown.
    /// - The constructed instance is not null and implements IAssetLocationResolver.
    /// </summary>
    /// <param name="passNull">Indicates whether to pass null or a mocked IBasicBarClient to the constructor.</param>
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_NullOrValidBarClient_InstanceCreatedAndImplementsInterface(bool passNull)
    {
        // Arrange
        IBasicBarClient barClient = passNull ? null : new Mock<IBasicBarClient>(MockBehavior.Strict).Object;

        // Act
        var instance = new AssetLocationResolver(barClient);

        // Assert
        // Using NUnit assertions to validate outcome. If an assertion library is preferred,
        // replace the following with equivalent assertions from the allowed framework.
        Assert.That(instance, Is.Not.Null);
        Assert.That(instance, Is.InstanceOf<IAssetLocationResolver>());
        Assert.That(instance, Is.TypeOf<AssetLocationResolver>());
    }

    /// <summary>
    /// Ensures that the constructor does not invoke any members of the provided IBasicBarClient dependency.
    /// Inputs:
    /// - A strictly mocked IBasicBarClient with no setups.
    /// Expected:
    /// - Instance is created successfully.
    /// - No interactions occur on the IBasicBarClient mock.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithValidBarClient_DoesNotInvokeBarClient()
    {
        // Arrange
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);

        // Act
        var instance = new AssetLocationResolver(barClientMock.Object);

        // Assert
        instance.Should().NotBeNull();
        barClientMock.VerifyNoOtherCalls();
    }
}
