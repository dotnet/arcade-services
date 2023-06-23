// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.Maestro.Tasks.Proxies;
using Microsoft.DotNet.Maestro.Tasks.Tests.Mocks;
using Microsoft.DotNet.VersionTools.BuildManifest.Model;
using Moq;
using NUnit.Framework;
using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks.Tests;

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
    private static readonly string isStable = "true";

    private static readonly string buildRepoName = "thisIsARepo";
    private static readonly string buildNumber = "azDevBuildNumber";
    private static readonly string sourceBranch = "thisIsASourceBranch";
    private static readonly string commitSourceVersion = "thisIsASourceVersion";
    private static readonly string id = "12345";
    private static string mergedManifestName = "MergedManifest.xml";
    private static string version = "6.0.0-beta.20516.5";

    private PackageArtifactModel package1 = new PackageArtifactModel
    {
        Attributes = new Dictionary<string, string>
        {
            { "NonShipping", "true" }
        },
        Id = "Microsoft.DotNet.ApiCompat",
        Version = version
    };

    private PackageArtifactModel nonShippingPackage = new PackageArtifactModel
    {
        Attributes = new Dictionary<string, string>
        {
            { "NonShipping", "true" }
        },
        Id = "Microsoft.Cci.Extensions",
        Version = version
    };

    private PackageArtifactModel packageWithNoVersion = new PackageArtifactModel
    {
        Attributes = new Dictionary<string, string>
        {
            { "NonShipping", "true" }
        },
        Id = "Microsoft.Cci.Extensions",
        Version = null
    };

    private PackageArtifactModel shippingPackage = new PackageArtifactModel
    {
        Attributes = new Dictionary<string, string>
        {
            { "NonShipping", "false" }
        },
        Id = "Microsoft.DotNet.ApiCompat",
        Version = version
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
        Id = "assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg"
    };

    private BlobArtifactModel mergedManifest = new BlobArtifactModel
    {
        Attributes = new Dictionary<string, string>
        {
            { "NonShipping", "true" }
        },
        Id = $"assets/manifests/{buildRepoName}/{id}/{mergedManifestName}"
    };

    private BlobArtifactModel nonShippingBlob = new BlobArtifactModel
    {
        Attributes = new Dictionary<string, string>
        {
            { "NonShipping", "true" },
            { "Category", "none" }
        },
        Id = "assets/symbols/Microsoft.DotNet.ApiCompat.6.0.0-beta.20516.5.symbols.nupkg"
    };

    private BlobArtifactModel shippingBlob = new BlobArtifactModel
    {
        Attributes = new Dictionary<string, string>
        {
            { "NonShipping", "false" },
            { "Category", "none" }
        },
        Id = "assets/symbols/Microsoft.DotNet.Maestro.Client.6.0.0-beta.20516.5.symbols.nupkg"
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
        IsReleaseOnlyPackageVersion = isReleasePackage,
        IsStable = isStable
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
        pushMetadata.versionIdentifier = new VersionIdentifierMock();
        return pushMetadata;
    }

    private BuildModel GetBuildModel()
    {
        BuildModel expectedBuildModel = new BuildModel(
            new BuildIdentity
            {
                Attributes = new Dictionary<string, string>()
                {
                    { "InitialAssetsLocation", initialAssetsLocation },
                    { "AzureDevOpsBuildId", azDoBuildId.ToString() },
                    { "AzureDevOpsBuildDefinitionId", azDoBuildDefId.ToString() },
                    { "AzureDevOpsAccount", azDoAccount },
                    { "AzureDevOpsProject", azDoProject },
                    { "AzureDevOpsBuildNumber", azDoBuildNumber },
                    { "AzureDevOpsRepository", azDoRepo },
                    { "AzureDevOpsBranch", azDoBranch }

                },
                Name = buildRepoName,
                BuildId = buildNumber,
                Branch = sourceBranch,
                Commit = commitSourceVersion,
                IsStable = bool.Parse(isStable),
                PublishingVersion = (PublishingInfraVersion)manifest.PublishingVersion,
                IsReleaseOnlyPackageVersion = bool.Parse(isReleasePackage)
            });

        return expectedBuildModel;
    }

    [Test]
    public void CheckIfTheDataIsPreservedAfterMerging()
    {
        BuildModel expectedBuildModel = GetBuildModel();
        PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();
        expectedBuildModel.Artifacts =
            new ArtifactSet
            {
                Packages = new List<PackageArtifactModel> { package1, nonShippingPackage },
                Blobs = new List<BlobArtifactModel> { blob1, nonShippingBlob }
            };
        expectedBuildModel.Identity.IsStable = true;

        packages = new List<PackageArtifactModel>() { package1, nonShippingPackage };
        blobs = new List<BlobArtifactModel>() { blob1, nonShippingBlob };

        BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(packages, blobs, manifest);

        actualModel.Should().BeEquivalentTo(expectedBuildModel);
    }

    [Test]
    public void CheckIfBlobDataIsPreservedAfterMerging()
    {
        BuildModel expectedBuildModel = GetBuildModel();
        PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

        packages = new List<PackageArtifactModel>();
        blobs = new List<BlobArtifactModel>();
        blobs.Add(blob1);

        expectedBuildModel.Artifacts =
            new ArtifactSet
            {
                Blobs = new List<BlobArtifactModel> { blob1 }
            };
        BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(packages, blobs, manifest);

        actualModel.Should().BeEquivalentTo(expectedBuildModel);
    }

    [Test]
    public void GivenShippingAndNonShippingPackagesBothShouldBeMergedIntoTheBuildModel()
    {
        BuildModel expectedBuildModel = GetBuildModel();
        PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

        expectedBuildModel.Artifacts =
            new ArtifactSet
            {
                Packages = new List<PackageArtifactModel> { shippingPackage, package1, packageWithNoVersion },
                Blobs = new List<BlobArtifactModel> { blob1 }
            };

        packages = new List<PackageArtifactModel>() { shippingPackage, package1, packageWithNoVersion };
        blobs = new List<BlobArtifactModel>() { blob1 };
        BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(packages, blobs, manifest);

        actualModel.Should().BeEquivalentTo(expectedBuildModel);
    }

    [Test]
    public void GivenMultipleBlobsAllShouldBeMergedIntoBuildModel()
    {
        BuildModel expectedBuildModel = GetBuildModel();
        PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

        expectedBuildModel.Artifacts =
            new ArtifactSet
            {
                Blobs = new List<BlobArtifactModel> { blob1, nonShippingBlob, mergedManifest, shippingBlob }
            };

        packages = new List<PackageArtifactModel>();
        blobs = new List<BlobArtifactModel>() { blob1, nonShippingBlob, mergedManifest, shippingBlob };

        BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(packages, blobs, manifest);

        actualModel.Should().BeEquivalentTo(expectedBuildModel);
    }
}
