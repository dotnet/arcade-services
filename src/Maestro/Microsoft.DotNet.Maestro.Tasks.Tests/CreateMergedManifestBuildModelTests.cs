// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
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

        private readonly AssetData assetDataWithoutName = new AssetData(true)
        { Version = "noNameVersion" };

        private readonly AssetData assetDataWithoutVersion = new AssetData(true)
        { Name = "noVersionData" };

        private readonly AssetData nonShippingAssetData = new AssetData(true)
        {
            Name = "NonShippingAssetData",
            Version = "nonShippingAsssetVersion"
        };

        private readonly AssetData shippingAssetData = new AssetData(false)
        {
            Name = "ShippingAssetData",
            Version = "shippingAssetVersion"
        };

        private readonly ManifestBuildData manifestBuildData = new ManifestBuildData(
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

        private PushMetadataToBuildAssetRegistry GetPushMetadata()
        {
            Mock<IGetEnvProxy> getEnvMock = new Mock<IGetEnvProxy>();
            getEnvMock.Setup(a => a.GetEnv("BUILD_REPOSITORY_NAME")).Returns(buildRepoName);
            getEnvMock.Setup(b => b.GetEnv("BUILD_BUILDNUMBER")).Returns(buildNumber);
            getEnvMock.Setup(c => c.GetEnv("BUILD_SOURCEBRANCH")).Returns(sourceBranch);
            getEnvMock.Setup(d => d.GetEnv("BUILD_SOURCEVERSION")).Returns(commitSourceVersion);
            getEnvMock.SetReturnsDefault("MissingEnvVariableCheck!");

            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry
            {
                getEnvProxy = getEnvMock.Object
            };

            return pushMetadata;
        }

        private BuildModel GetBuildModel()
        {
            BuildModel expectedBuildModel = new BuildModel(
                new BuildIdentity
                {
                    Attributes = manifestBuildData.ToDictionary(),
                    Name = buildRepoName,
                    BuildId = buildNumber,
                    Branch = sourceBranch,
                    Commit = commitSourceVersion,
                    IsStable = false,
                    PublishingVersion = (PublishingInfraVersion)manifestBuildData.PublishingVersion,
                    IsReleaseOnlyPackageVersion = bool.Parse(isReleasePackage)
                });

            return expectedBuildModel;
        }

        [Test]
        [Ignore("Fails due to bug https://github.com/dotnet/arcade/issues/6677")]
        public void GivenAssetDataWithoutName()
        {
            BuildModel expectedBuildModel = GetBuildModel();
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            PackageArtifactModel packageArtifact = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" }
                },
                Id = null,
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
        [Ignore("Fails due to bug https://github.com/dotnet/arcade/issues/6677")]
        public void GivenAssetWithoutVersion()
        {
            BuildModel expectedBuildModel = GetBuildModel();
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            PackageArtifactModel packageArtifact = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", "true" },
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
        [Ignore("Fails due to bug https://github.com/dotnet/arcade/issues/6677")]
        public void GivenAssetsInBlobSet()
        {
            BuildModel expectedBuildModel = GetBuildModel();
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            AssetData dataInBlobSet = pushMetadata.GetManifestAsAsset(ImmutableList.Create(nonShippingAssetData), "thisIsALocation", "thisIsTheManifestFileName");
            BlobArtifactModel blobArtifactModel = new BlobArtifactModel
            {
                Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" }
                },
                Id = dataInBlobSet.Name
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
        [Ignore("Fails due to bug https://github.com/dotnet/arcade/issues/6677")]
        public void GivenSomeAssetsInBlobSetAndSomeNot()
        {
            BuildModel expectedBuildModel = GetBuildModel();
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            AssetData dataInBlobSet = pushMetadata.GetManifestAsAsset(ImmutableList.Create(nonShippingAssetData), "thisIsALocation", "thisIsTheManifestFileName");

            PackageArtifactModel shippingPackageArtifact = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", "false" },
                    },
                Id = shippingAssetData.Name,
                Version = shippingAssetData.Version
            };

            BlobArtifactModel blobArtifactModel = new BlobArtifactModel
            {
                Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" }
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

            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void GivenNonShippingAssets()
        {
            BuildModel expectedBuildModel = GetBuildModel();
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            PackageArtifactModel packageArtifact = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", "true" },
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

            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void GivenShippingAssets()
        {
            BuildModel expectedBuildModel = GetBuildModel();
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            PackageArtifactModel packageArtifact = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", "false" },
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
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            Action act = () => pushMetadata.CreateMergedManifestBuildModel(null, manifestBuildData);
            act.Should().Throw<NullReferenceException>();
        }

        [Test]
        public void GivenEmptyAssetsList()
        {
            BuildModel expectedBuildModel = GetBuildModel();
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(ImmutableList.Create<AssetData>(), manifestBuildData);
            expectedBuildModel.Artifacts = new ArtifactSet { };

            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void GivenNullManifestBuildData_ExpectNullReferenceException()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            Action act = () => pushMetadata.CreateMergedManifestBuildModel(ImmutableList.Create<AssetData>(), null);
            act.Should().Throw<NullReferenceException>();
        }
    }
}
