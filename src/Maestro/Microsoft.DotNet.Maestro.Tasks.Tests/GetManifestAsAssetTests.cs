// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.DotNet.Maestro.Tasks.Proxies;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    [TestFixture]
    public class GetManifestAsAssetTests
    {
        private PushMetadataToBuildAssetRegistry pushMetadata;
        private const string locationString = "https://url.test/accountName/ProjectName/_apis/build/builds/buildNumberHere/artifacts";
        private const string newManifestName = "NewManifest";

        internal static readonly AssetData PackageAsset1 =
            new AssetData(true)
            {
                Locations = ImmutableList.Create(
                    new AssetLocationData(LocationType.Container)
                    { Location = locationString }),
                Name = "PackageAsset1Name",
                Version = "1.2.3-beta.1235.6"
            };

        internal static readonly AssetData ExpectedPackageAsset1 =
            new AssetData(true)
            {
                Locations = ImmutableList.Create(
                    new AssetLocationData(LocationType.Container)
                    { Location = locationString }),
                Name = $"assets/manifests/thisIsARepo/1.2.3-beta.1235.6/{newManifestName}",
                Version = "1.2.3-beta.1235.6"
            };

        [SetUp]
        public void SetupGetManifestAsAssetTests()
        {
            Mock<IGetEnvProxy> getEnvMock = new Mock<IGetEnvProxy>();
            getEnvMock.Setup(s => s.GetEnv("BUILD_REPOSITORY_NAME")).Returns("thisIsARepo");

            pushMetadata = new PushMetadataToBuildAssetRegistry
            {
                getEnvProxy = getEnvMock.Object
            };
        }

        [Test]
        public void GivenExistentAssetDataWithLocationAndManifest()
        {
            AssetData parsedAssets = pushMetadata.GetManifestAsAsset(ImmutableList.Create(PackageAsset1), locationString, newManifestName);
            parsedAssets.Should().BeEquivalentTo(ExpectedPackageAsset1);
        }

        [Test]
        public void GivenMultipleExistentAssetData()
        {
            AssetData shippingPackageAsset =
                  new AssetData(false)
                  {
                      Locations = ImmutableList.Create(
                          new AssetLocationData(LocationType.Container)
                          { Location = locationString }),
                      Name = "PackageToShip",
                      Version = "VersionToBeShipped"
                  };

            AssetData blobAsset1 =
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                        new AssetLocationData(LocationType.Container)
                        { Location = locationString }),
                    Name = "assets/manifests/thisIsARepo/1.2.3-beta.1235.6/MergedManifest.xml",
                    Version = "BlobbyVersion"
                };

            AssetData parsedAssets = pushMetadata.GetManifestAsAsset(ImmutableList.Create(shippingPackageAsset, PackageAsset1, blobAsset1), locationString, newManifestName);
            parsedAssets.Should().BeEquivalentTo(ExpectedPackageAsset1);
        }

        [Test]
        public void GivenExistentAssetDataWithoutLocationOrManifest()
        {
            AssetData expectedPackageAssetNoLocationOrManifest =
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                        new AssetLocationData(LocationType.Container)
                        { Location = "" }),
                    Name = "assets/manifests/thisIsARepo/1.2.3-beta.1235.6/",
                    Version = "1.2.3-beta.1235.6"
                };

            AssetData parsedAssets = pushMetadata.GetManifestAsAsset(ImmutableList.Create(PackageAsset1), "", "");
            parsedAssets.Should().BeEquivalentTo(expectedPackageAssetNoLocationOrManifest);
        }

        [Test]
        public void GivenAssetVersionIsSet_VersionDoesntChange()
        {
            AssetData packageAsset1DifferentVersion =
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                        new AssetLocationData(LocationType.Container)
                        { Location = locationString }),
                    Name = "PackageAsset1Name",
                    Version = "123456"
                };

            // Version is set on the first pass through the function, then persisted in the class instance so changing it should have no effect on the generated AssetData
            pushMetadata.GetManifestAsAsset(ImmutableList.Create(PackageAsset1), "thisIsALocation", "FirstManifest");
            AssetData secondPassAssets = pushMetadata.GetManifestAsAsset(ImmutableList.Create(packageAsset1DifferentVersion), locationString, newManifestName);
            secondPassAssets.Should().BeEquivalentTo(ExpectedPackageAsset1);
        }

        [Test]
        public void GivenAssetVersionNotSetAndNonShippingAssetWithoutVersion()
        {
            AssetData packageAssetNoVersion =
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                        new AssetLocationData(LocationType.Container)
                        { Location = locationString }),
                    Name = "PackageAsset1Name"
                };

            AssetData expectedPackageAssetNoVersion =
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                        new AssetLocationData(LocationType.Container)
                        { Location = locationString }),
                    Name = $"assets/manifests/thisIsARepo//{newManifestName}"
                };

            AssetData parsedAssets = pushMetadata.GetManifestAsAsset(ImmutableList.Create(packageAssetNoVersion), locationString, newManifestName);
            parsedAssets.Should().BeEquivalentTo(expectedPackageAssetNoVersion);
        }

        [Test]
        public void GivenAssetVersionNotSetAndOnlyShippingAssets()
        {
            AssetData shippingPackageAsset =
                new AssetData(false)
                {
                    Locations = ImmutableList.Create(
                        new AssetLocationData(LocationType.Container)
                        { Location = locationString }),
                    Name = "PackageAsset1Name",
                    Version = "1.2.3-beta.1235.6"
                };

            AssetData shippingExpectedPackageAsset =
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                        new AssetLocationData(LocationType.Container)
                        { Location = locationString }),
                    Name = $"assets/manifests/thisIsARepo//{newManifestName}"
                };

            AssetData parsedAssets = pushMetadata.GetManifestAsAsset(ImmutableList.Create(shippingPackageAsset), locationString, newManifestName);
            parsedAssets.Should().BeEquivalentTo(shippingExpectedPackageAsset);
        }

        [Test]
        public void GivenEmptyAssetDataList()
        {
            AssetData expectedPackageAssetNoAssets =
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                        new AssetLocationData(LocationType.Container)
                        { Location = locationString }),
                    Name = $"assets/manifests/thisIsARepo//{newManifestName}",
                    NonShipping = true,
                    Version = null
                };

            AssetData parsedAssets = pushMetadata.GetManifestAsAsset(ImmutableList.Create<AssetData>(), locationString, newManifestName);
            parsedAssets.Should().BeEquivalentTo(expectedPackageAssetNoAssets);
        }

        [Test]
        public void GivenNullLocation()
        {
            AssetData packageAssetNoLocation =
                new AssetData(true)
                {
                    Name = "PackageAsset1Name",
                    Version = "1.2.3-beta.1235.6"
                };

            AssetData parsedAssets = pushMetadata.GetManifestAsAsset(ImmutableList.Create(packageAssetNoLocation), locationString, newManifestName);
            parsedAssets.Should().BeEquivalentTo(ExpectedPackageAsset1);
        }
    }
}
