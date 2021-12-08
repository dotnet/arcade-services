// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using FluentAssertions;
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
        private static readonly string id = "1234";
        private static readonly string version = "thisIsVersion";
        private static readonly string noVersion = "thisIsNoVersion";

        private PackageArtifactModel package1 = new PackageArtifactModel
        {
            Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" }
                },
            Id = id,
            Version = version
        };

        private PackageArtifactModel package2 = new PackageArtifactModel
        {
            Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" }
                },
            Id = id ,
            Version = noVersion
        };

        private PackageArtifactModel packageWithNoVersion = new PackageArtifactModel
        {
            Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" }
                },
            Id = id,
            Version = null
        };

        private PackageArtifactModel packageNonShipping = new PackageArtifactModel
        {
            Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "false" }
                },
            Id = id,
            Version = null
        }; 

        List<PackageArtifactModel> packages;
        List<BlobArtifactModel> blobs;

        private BlobArtifactModel blob1 = new BlobArtifactModel
        {
            Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" },
                    { "Category", "other" }
                },
            Id = id
        };

        private BlobArtifactModel blob2 = new BlobArtifactModel
        {
            Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" },
                    { "Category", "none" }
                },
            Id = id
        };

        private BlobArtifactModel blob3 = new BlobArtifactModel
        {
            Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "false" },
                    { "Category", "none" }
                },
            Id = id
        };

        private readonly Manifest manifest = new Manifest
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
            };

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
                    Attributes = new Dictionary< string, string>()
                    {
                        { "InitialAssetsLocation", manifest.InitialAssetsLocation },
                        { "AzureDevOpsBuildId", manifest.BuildId },
                        { "AzureDevOpsBuildDefinitionId", manifest.AzureDevOpsBuildDefinitionIdString },
                        { "AzureDevOpsAccount", manifest.AzureDevOpsAccount },
                        { "AzureDevOpsProject", manifest.AzureDevOpsProject },
                        { "AzureDevOpsBuildNumber", manifest.AzureDevOpsBuildNumber },
                        { "AzureDevOpsRepository", manifest.AzureDevOpsRepository},
                        { "AzureDevOpsBranch", manifest.AzureDevOpsBranch }

                    },
                    Name = buildRepoName,
                    BuildId = buildNumber,
                    Branch = sourceBranch,
                    Commit = commitSourceVersion,
                    IsStable = false,
                    PublishingVersion = (PublishingInfraVersion)manifest.PublishingVersion,
                    IsReleaseOnlyPackageVersion = bool.Parse(isReleasePackage)
                });

            return expectedBuildModel;
        }

        [Test]
        public void CheckMergedBuildModelData()
        {
            BuildModel expectedBuildModel = GetBuildModel();
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            PackageArtifactModel testPackage1 = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" }
                },
                Id = id,
                Version = version
            };

            PackageArtifactModel testPackage2 = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" }
                },
                Id = id,
                Version = noVersion
            };

            BlobArtifactModel testBlob1 = new BlobArtifactModel
            {
                Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" },
                    { "Category", "other" }
                },
                Id = id,
            };

            BlobArtifactModel testBlob2 = new BlobArtifactModel
            {
                Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" },
                    { "Category", "none" }
                },
                Id = id,
            };

            expectedBuildModel.Artifacts =
                new ArtifactSet
                {
                    Packages = new List<PackageArtifactModel> { testPackage1, testPackage2 },
                    Blobs = new List<BlobArtifactModel> { testBlob1, testBlob2 }
                };

            packages = new List<PackageArtifactModel>();
            blobs = new List<BlobArtifactModel>();
            packages.Add(package1);
            packages.Add(package2);
            blobs.Add(blob1);
            blobs.Add(blob2);

            BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(packages, blobs, manifest);

            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void CheckPackagesInMergedBuildModel()
        {
            BuildModel expectedBuildModel = GetBuildModel();
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            PackageArtifactModel packageArtifact = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", "true" },
                    },
                Id= id,
                Version = null
            };

            expectedBuildModel.Artifacts =
                new ArtifactSet
                {
                    Packages = new List<PackageArtifactModel> { packageArtifact }
                };

            packages = new List<PackageArtifactModel>();
            blobs = new List<BlobArtifactModel>();
            packages.Add(packageWithNoVersion);

            BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(packages, blobs, manifest);

            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void CheckBlobsInMergedBuildModel()
        {
            BuildModel expectedBuildModel = GetBuildModel();
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            BlobArtifactModel blobArtifactModel = new BlobArtifactModel
            {
                Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" },
                    { "Category", "other" }

                },
                Id = id
            };

            packages = new List<PackageArtifactModel>();
            blobs = new List<BlobArtifactModel>();
            blobs.Add(blob1);

            expectedBuildModel.Artifacts =
                new ArtifactSet
                {
                    Blobs = new List<BlobArtifactModel> { blobArtifactModel }
                };
            BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(packages, blobs, manifest);

            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void CheckShippingAndNonShippingPackages()
        {
            BuildModel expectedBuildModel = GetBuildModel();
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            PackageArtifactModel shippingPackageArtifact = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", "false" },
                    },
                Id = id,
                Version = null
            };

            PackageArtifactModel nonShippingPackage = new PackageArtifactModel
            {
                Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", "true" },
                    },
                Id = id,
                Version = null
            };

            BlobArtifactModel blobArtifactModel = new BlobArtifactModel
            {
                Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" },
                    { "Category", "other" }
                },
                Id = id
            };

            expectedBuildModel.Artifacts =
                new ArtifactSet
                {
                    Packages = new List<PackageArtifactModel> { shippingPackageArtifact, nonShippingPackage },
                    Blobs = new List<BlobArtifactModel> { blobArtifactModel }
                };

            packages = new List<PackageArtifactModel>();
            blobs = new List<BlobArtifactModel>();
            packages.Add(packageNonShipping);
            packages.Add(packageWithNoVersion);
            blobs.Add(blob1);
            BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(packages, blobs, manifest);

            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void GivenNonShippingAssets()
        {
            BuildModel expectedBuildModel = GetBuildModel();
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            BlobArtifactModel NonShippingBlob = new BlobArtifactModel
            {
                Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "true" },
                    { "Category", "other" }
                },
                Id = id
            };

            BlobArtifactModel ShippingBlob = new BlobArtifactModel
            {
                Attributes = new Dictionary<string, string>
                {
                    { "NonShipping", "false" },
                    { "Category", "none" }
                },
                Id = id
            };

            expectedBuildModel.Artifacts =
                new ArtifactSet
                {
                    Blobs = new List<BlobArtifactModel> { ShippingBlob, NonShippingBlob }
                };

            packages = new List<PackageArtifactModel>();
            blobs = new List<BlobArtifactModel>();
            blobs.Add(blob3);
            blobs.Add(blob1);
            BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(packages, blobs, manifest);

            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }

        [Test]
        public void GivenEmptyPackagesAndBlobsList()
        {
            BuildModel expectedBuildModel = GetBuildModel();
            PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

            packages = new List<PackageArtifactModel>();
            blobs = new List<BlobArtifactModel>();

            BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(packages, blobs, manifest);
            expectedBuildModel.Artifacts = new ArtifactSet { };

            actualModel.Should().BeEquivalentTo(expectedBuildModel);
        }
    }
}
