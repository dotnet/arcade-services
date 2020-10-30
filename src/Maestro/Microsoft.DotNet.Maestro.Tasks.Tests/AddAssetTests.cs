// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using FluentAssertions;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    [TestFixture]
    public class AddAssetTests
    {
        [Test]
        public void EmptyAssetList_NewAssetIsOnlyAssetInList()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();
            List<AssetData> assetData = new List<AssetData>();
            AssetData expectedAssetData = new AssetData(true) { Name = "testName", Version = "12345", Locations = ImmutableList<AssetLocationData>.Empty.Add(new AssetLocationData(LocationType.None) { Location = "testLocation" }) };

            pushMetadata.AddAsset(assetData, expectedAssetData.Name, expectedAssetData.Version, "testLocation", LocationType.None, true);
            assetData.Count.Should().Be(1);
            assetData.Should().BeEquivalentTo(expectedAssetData);
        }

        [Test]
        public void AssetsAddedToList_NewAndPreviousAssetsInList()
        {
            AssetData existingAssetData = new AssetData(true)
            {
                Name = "ExistingAssetName",
                Version = "56789",
                Locations = ImmutableList<AssetLocationData>.Empty.Add(new AssetLocationData(LocationType.Container) { Location = "oldTestLocation" })
            };

            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();
            List<AssetData> assetData = new List<AssetData>() { existingAssetData };

            AssetData newAssetData = new AssetData(true) { Name = "testName", Version = "12345", Locations = ImmutableList<AssetLocationData>.Empty.Add(new AssetLocationData(LocationType.None) { Location = "testLocation" }) };

            pushMetadata.AddAsset(assetData, newAssetData.Name, newAssetData.Version, "testLocation", LocationType.None, true);
            assetData.Count.Should().Be(2);
            assetData[0].Should().BeEquivalentTo(existingAssetData);
            assetData[1].Should().BeEquivalentTo(newAssetData);
        }

        [Test]
        public void NullAssetList_ThrowsNullReferenceException()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();

            Action act = () =>
                pushMetadata.AddAsset(null, "testName", "12345", "testLocation", LocationType.None, true);
            act.Should().Throw<NullReferenceException>();
        }
    }
}
