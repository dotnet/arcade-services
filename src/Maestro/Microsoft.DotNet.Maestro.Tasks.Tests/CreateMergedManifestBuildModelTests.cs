// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.DotNet.Maestro.Tasks.Proxies;
using Microsoft.DotNet.VersionTools.BuildManifest.Model;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    [TestFixture]
    class CreateMergedManifestBuildModelTests
    {
        private static readonly string initialAssetsLocation = "thisIsALocation";
        private static readonly int azDoBuildId = 12345;
        private static readonly int azDoBuildDefId = 67890;
        private static readonly string azDoAccount = "thisIstheAzDoAccount";
        private static readonly string azDoProject = "thisIsTheAzDoProject";
        private static readonly string azDoBuildNumber = "thisIsAnAzDoBuildNumberFromTheManifest";
        private static readonly string azDoRepo = "thisIsARepoFromTheManifest";
        private static readonly string azDoBranch = "thisIsAnAzDoBranch";
        private static readonly int publishingVersion = 1234567890;
        private static readonly string isReleasePackage = "false";

        private static readonly string buildRepoName = "thisIsARepo";
        private static readonly string buildNumber = "azDevBuildNumber";
        private static readonly string sourceBranch = "thisIsASourceBranch";
        private static readonly string commitSourceVersion = "thisIsASourceVersion";

        private PushMetadataToBuildAssetRegistry pushMetadata;
        private BuildModel expectedBuildModel;

        private AssetData assetDataWithoutName = new AssetData(true)
        { Version = "noNameVersion" };

        private AssetData assetDataWithoutVersion = new AssetData(true)
        { Name = "noVersionData" };

        private AssetData nonShippingAssetData = new AssetData(true)
        {
            Name = "NonShippingAssetData",
            Version = "nonShippingAsssetVersion"
        };

        private AssetData shippingAssetData = new AssetData(false)
        {
            Name = "ShippingAssetData",
            Version = "shippingAssetVersion"
        };

        private ManifestBuildData manifestBuildData = new ManifestBuildData(
            new Manifest
            {
                InitialAssetsLocation = initialAssetsLocation,
                AzureDevOpsBuildId = azDoBuildId,
                AzureDevOpsBuildDefinitionId = azDoBuildDefId,
                AzureDevOpsAccount = azDoAccount,
                AzureDevOpsProject = azDoProject,
                AzureDevOpsBuildNumber = azDoBuildNumber,
                AzureDevOpsRepository = azDoRepo,
                AzureDevOpsBranch = azDoBranch,
                PublishingVersion = publishingVersion,
                IsReleaseOnlyPackageVersion = isReleasePackage
            });

        [SetUp]
        public void TestCaseSetup()
        {
            Mock<IGetEnvProxy> getEnvMock = new Mock<IGetEnvProxy>();
            getEnvMock.Setup(a => a.GetEnv("BUILD_REPOSITORY_NAME")).Returns(buildRepoName);
            getEnvMock.Setup(b => b.GetEnv("BUILD_BUILDNUMBER")).Returns(buildNumber);
            getEnvMock.Setup(c => c.GetEnv("BUILD_SOURCEBRANCH")).Returns(sourceBranch);
            getEnvMock.Setup(d => d.GetEnv("BUILD_SOURCEVERSION")).Returns(commitSourceVersion);
            getEnvMock.SetReturnsDefault("MissingEnvVariableCheck!");

            pushMetadata = new PushMetadataToBuildAssetRegistry
            {
                getEnvProxy = getEnvMock.Object
            };

            expectedBuildModel = new BuildModel(
            new BuildIdentity
            {
                Attributes = manifestBuildData.ToDictionary(),
                Name = buildRepoName,
                BuildId = buildNumber,
                Branch = sourceBranch,
                Commit = commitSourceVersion,
                IsStable = false.ToString(),
                PublishingVersion = (PublishingInfraVersion)manifestBuildData.PublishingVersion,
                IsReleaseOnlyPackageVersion = isReleasePackage
            });
        }

        [Test]
        public void GivenAssetDataWithoutName()
        {
            PackageArtifactModel packageArtifact = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", true.ToString().ToLower() },
                    },
                Version = assetDataWithoutName.Version
            };

            expectedBuildModel.Artifacts =
                new ArtifactSet
                {
                    Packages = new List<PackageArtifactModel> { packageArtifact }
                };

            BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(
                ImmutableList.Create(assetDataWithoutName), manifestBuildData);

            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void GivenAssetWithoutVersion()
        {
            PackageArtifactModel packageArtifact = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", true.ToString().ToLower() },
                        { "Id", assetDataWithoutVersion.Name },
                        { "Version", null }
                    }
            };

            expectedBuildModel.Artifacts =
                new ArtifactSet
                {
                    Packages = new List<PackageArtifactModel> { packageArtifact }
                };

            BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(
                ImmutableList.Create(assetDataWithoutVersion), manifestBuildData);

            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void GivenAssetsInBlobSet()
        {
            AssetData dataInBlobSet = pushMetadata.GetManifestAsAsset(ImmutableList.Create(nonShippingAssetData), "thisIsALocation", "thisIsTheManifestFileName");
            BlobArtifactModel blobArtifactModel = new BlobArtifactModel
            {
                Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", true.ToString().ToLower() }
                },
                Id = nonShippingAssetData.Name
            };

            expectedBuildModel.Artifacts =
                new ArtifactSet
                {
                    Blobs = new List<BlobArtifactModel> { blobArtifactModel }
                };
            BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(
                ImmutableList.Create(dataInBlobSet), manifestBuildData);

            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void GivenSomeAssetsInBlobSetAndSomeNot()
        {
            AssetData dataInBlobSet = pushMetadata.GetManifestAsAsset(ImmutableList.Create(nonShippingAssetData), "thisIsALocation", "thisIsTheManifestFileName");

            PackageArtifactModel shippingPackageArtifact = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", false.ToString().ToLower() },
                    },
                Id = shippingAssetData.Name,
                Version = shippingAssetData.Version
            };

            BlobArtifactModel blobArtifactModel = new BlobArtifactModel
            {
                Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", true.ToString().ToLower() }
                },
                Id = dataInBlobSet.Name
            };

            expectedBuildModel.Artifacts =
                new ArtifactSet
                {
                    Packages = new List<PackageArtifactModel> { shippingPackageArtifact },
                    Blobs = new List<BlobArtifactModel> { blobArtifactModel }
                };

            BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(
                ImmutableList.Create(dataInBlobSet, shippingAssetData), manifestBuildData);

            // actualModel.IsSameOrEqualTo(expectedBuildModel);
            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void GivenNonShippingAssets()
        {
            PackageArtifactModel packageArtifact = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", true.ToString().ToLower() },
                    },
                Id = nonShippingAssetData.Name,
                Version = nonShippingAssetData.Version
            };

            expectedBuildModel.Artifacts =
                new ArtifactSet
                {
                    Packages = new List<PackageArtifactModel> { packageArtifact }
                };

            BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(
                ImmutableList.Create(nonShippingAssetData), manifestBuildData);
            actualModel.Artifacts.Packages[0].Id.Should().Be(expectedBuildModel.Artifacts.Packages[0].Id);
            actualModel.Artifacts.Packages[0].NonShipping.Should().Be(expectedBuildModel.Artifacts.Packages[0].NonShipping);
            actualModel.Artifacts.Packages[0].Version.Should().Be(expectedBuildModel.Artifacts.Packages[0].Version);
            actualModel.Artifacts.Packages[0].Attributes.Should().BeEquivalentTo(expectedBuildModel.Artifacts.Packages[0].Attributes);
            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void GivenShippingAssets()
        {
            PackageArtifactModel packageArtifact = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", false.ToString().ToLower() },
                    },
                Id = shippingAssetData.Name,
                Version = shippingAssetData.Version
            };

            expectedBuildModel.Artifacts =
                new ArtifactSet
                {
                    Packages = new List<PackageArtifactModel> { packageArtifact }
                };

            BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(
                ImmutableList.Create(shippingAssetData), manifestBuildData);

            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void GivenNullAssetsList_ExpectNullReferenceException()
        {

        }

        [Test]
        public void GivenEmptyAssetsList()
        {

        }

        [Test]
        public void GivenNullManifestBuildData_ExpectNullReferenceException()
        {

        }
    }
}
