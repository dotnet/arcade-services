// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;

[TestFixture]
public class AssetLocationResolverTests
{
    private const string AssetName = "Microsoft.Foo";
    private const string AssetVersion = "1.0.0";
    private const string Commit = "abc123";

    private Mock<IBasicBarClient> _barClient = null!;
    private AssetLocationResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        _resolver = new AssetLocationResolver(_barClient.Object);
    }

    [Test]
    public async Task AssetFromLatestBuildInChannelIsUsed()
    {
        var oldAsset = CreateAsset(assetId: 1, buildId: 10, location: "https://feed/old");
        var newAsset = CreateAsset(assetId: 2, buildId: 20, location: "https://feed/new");

        SetupAssets(oldAsset, newAsset);
        SetupBuild(buildId: 10, inChannel: true);
        SetupBuild(buildId: 20, inChannel: true);

        var dependency = CreateDependency();

        await _resolver.AddAssetLocationToDependenciesAsync([dependency]);

        dependency.Locations.Should().BeEquivalentTo(["https://feed/new"]);
    }

    [Test]
    public async Task LatestBuildNotInChannelIsSkippedAndPreviousIsUsed()
    {
        var oldAsset = CreateAsset(assetId: 1, buildId: 10, location: "https://feed/old");
        var newAsset = CreateAsset(assetId: 2, buildId: 20, location: "https://feed/new");

        SetupAssets(oldAsset, newAsset);
        SetupBuild(buildId: 10, inChannel: true);
        SetupBuild(buildId: 20, inChannel: false);

        var dependency = CreateDependency();

        await _resolver.AddAssetLocationToDependenciesAsync([dependency]);

        dependency.Locations.Should().BeEquivalentTo(["https://feed/old"]);
    }

    [Test]
    public async Task NoBuildInChannelLeavesLocationsUnset()
    {
        var oldAsset = CreateAsset(assetId: 1, buildId: 10, location: "https://feed/old");
        var newAsset = CreateAsset(assetId: 2, buildId: 20, location: "https://feed/new");

        SetupAssets(oldAsset, newAsset);
        SetupBuild(buildId: 10, inChannel: false);
        SetupBuild(buildId: 20, inChannel: false);

        var dependency = CreateDependency();

        await _resolver.AddAssetLocationToDependenciesAsync([dependency]);

        dependency.Locations.Should().BeEmpty();
    }

    private void SetupAssets(params Asset[] assets)
    {
        _barClient
            .Setup(x => x.GetAssetsAsync(AssetName, AssetVersion, null, null))
            .ReturnsAsync(assets);
    }

    private void SetupBuild(int buildId, bool inChannel)
    {
        List<Channel> channels = inChannel
            ? [new Channel(id: 1, name: "test-channel", classification: "product")]
            : [];

        Build build = new(
            id: buildId,
            dateProduced: DateTimeOffset.Now,
            staleness: 0,
            released: false,
            stable: false,
            commit: Commit,
            channels: channels,
            assets: [],
            dependencies: [],
            incoherencies: []);

        _barClient
            .Setup(x => x.GetBuildAsync(buildId))
            .ReturnsAsync(build);
    }

    private static Asset CreateAsset(int assetId, int buildId, string location) =>
        new(
            id: assetId,
            buildId: buildId,
            nonShipping: false,
            name: AssetName,
            version: AssetVersion,
            locations: [new AssetLocation(id: assetId, type: LocationType.NugetFeed, location: location)]);

    private static DependencyDetail CreateDependency() =>
        new()
        {
            Name = AssetName,
            Version = AssetVersion,
            Commit = Commit,
        };
}
