// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.Maestro.Tasks.Tests;

[TestFixture]
public class AddAssetTests
{
    [Test]
    public void EmptyAssetList_NewAssetIsOnlyAssetInList()
    {
        List<AssetData> assetData = [];
        var expectedAssetData = new AssetData(true) { Name = "testName", Version = "12345", Locations = [new AssetLocationData(LocationType.None) { Location = "testLocation" }] };

        PushMetadataToBuildAssetRegistry.AddAsset(assetData, expectedAssetData.Name, expectedAssetData.Version, "testLocation", LocationType.None, true);
        assetData.Count.Should().Be(1);
        assetData.First().Should().BeEquivalentTo(expectedAssetData);
    }

    [Test]
    public void AssetsAddedToList_NewAndPreviousAssetsInList()
    {
        var existingAssetData = new AssetData(true)
        {
            Name = "ExistingAssetName",
            Version = "56789",
            Locations = [new AssetLocationData(LocationType.Container) { Location = "oldTestLocation" }]
        };

        List<AssetData> assetData = [existingAssetData];

        var newAssetData = new AssetData(true) { Name = "testName", Version = "12345", Locations = [new AssetLocationData(LocationType.None) { Location = "testLocation" }] };

        PushMetadataToBuildAssetRegistry.AddAsset(assetData, newAssetData.Name, newAssetData.Version, "testLocation", LocationType.None, true);
        assetData.Count.Should().Be(2);
        assetData[0].Should().BeEquivalentTo(existingAssetData);
        assetData[1].Should().BeEquivalentTo(newAssetData);
    }

    [Test]
    public void NullAssetList_ThrowsNullReferenceException()
    {
        var pushMetadata = new PushMetadataToBuildAssetRegistry();

        Action act = () =>
            PushMetadataToBuildAssetRegistry.AddAsset(null, "testName", "12345", "testLocation", LocationType.None, true);
        act.Should().Throw<NullReferenceException>();
    }
}
