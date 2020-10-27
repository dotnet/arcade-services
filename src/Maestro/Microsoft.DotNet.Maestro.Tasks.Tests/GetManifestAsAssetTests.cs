using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using FluentAssertions;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    [TestFixture]
    public class GetManifestAsAssetTests
    {
        private PushMetadataToBuildAssetRegistry pushMetadata;
        private const string locationString = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts";

        internal static readonly AssetData PackageAsset1 =
            new AssetData(true)
            {
                Locations = ImmutableList.Create(
                    new AssetLocationData(LocationType.Container)
                    { Location = locationString }),
                Name = "Microsoft.Cci.Extensions",
                Version = "6.0.0-beta.20516.5"
            };

        internal static readonly AssetData BlobAsset1 =
            new AssetData(true)
            {
                Locations = ImmutableList.Create(
                    new AssetLocationData(LocationType.Container)
                    { Location = locationString }),
                Name = "assets/manifests/dotnet-arcade/6.0.0-beta.20516.5/MergedManifest.xml",
                Version = "6.0.0-beta.20516.5"
            };

        public static readonly IImmutableList<AssetData> ExpectedAssets1 =
            ImmutableList.Create(PackageAsset1, BlobAsset1);

        public GetManifestAsAssetTests()
        {
            pushMetadata = new PushMetadataToBuildAssetRegistry();
        }

        // TODO: Requires DI
        [Test]
        public void GivenExistentAssetDataWithLocationAndManifest()
        {
            AssetData parsedAssets = pushMetadata.GetManifestAsAsset(ExpectedAssets1, locationString, "NewManifest");
            parsedAssets.Should().Be(ExpectedAssets1);
        }

        [Test]
        public void GivenExistentAssetDataWithoutLocationOrManifest()
        {
            AssetData parsedAssets = pushMetadata.GetManifestAsAsset(ExpectedAssets1, "", "");
            parsedAssets.Should().Be(ExpectedAssets1);
        }

        [Test]
        public void GivenAssetVersionIsSet()
        {
            // TODO: Check where version is set so it can be set up here
            AssetData parsedAssets = pushMetadata.GetManifestAsAsset(ExpectedAssets1, locationString, "NewManifest");
            parsedAssets.Should().Be(ExpectedAssets1);
        }

        [Test]
        public void GivenAssetVersionNotSetButNonShippingAssetHasVersion()
        {
            
        }

        [Test]
        public void GivenAssetVersionNotSetAndNonShippingAssetWithoutVersion()
        {

        }

        [Test]
        public void GivenEmptyAssetDataList()
        {

        }

        [Test]
        public void GivenNullLocation()
        {

        }

        [Test]
        public void GivenNullSigningInformation_ExceptionExpected()
        {

        }

        [Test]
        public void GivenSigningInfoWithNullValues_ExceptionExpected()
        {

        }

        [Test]
        public void GivenBlobSetWithADuplicateKey_ExceptionExpected()
        {

        }
    }
}
