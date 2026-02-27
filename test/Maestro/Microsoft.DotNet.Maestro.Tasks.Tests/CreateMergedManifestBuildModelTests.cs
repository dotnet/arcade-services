// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.Maestro.Tasks.Proxies;
using Microsoft.DotNet.Maestro.Tasks.Tests.Mocks;
using Microsoft.DotNet.VersionTools.BuildManifest.Model;
using Moq;
using NUnit.Framework;
using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks.Tests;

[TestFixture]
internal class CreateMergedManifestBuildModelTests
{
    private const string InitialAssetsLocation = "thisIsALocation";
    private const int AzDoBuildId = 12345;
    private const int AzDoBuildDefId = 67890;
    private const string AzDoAccount = "thisIstheAzDoAccount";
    private const string AzDoProject = "thisIsTheAzDoProject";
    private const string AzDoBuildNumber = "thisIsAnAzDoBuildNumberFromTheManifest";
    private const string AzDoRepo = "thisIsARepoFromTheManifest";
    private const string AzDoBranch = "thisIsAnAzDoBranch";
    private const int PublishingVersion = 1234567890;
    private const string IsReleasePackage = "false";
    private const string IsStable = "true";

    private const string BuildRepoName = "thisIsARepo";
    private const string BuildNumber = "azDevBuildNumber";
    private const string SourceBranch = "thisIsASourceBranch";
    private const string CommitSourceVersion = "thisIsASourceVersion";
    private const string Id = "12345";
    private const string MergedManifestName = "MergedManifest.xml";
    private const string Version = "6.0.0-beta.20516.5";

    private readonly PackageArtifactModel _package1 = new()
    {
        Attributes = new Dictionary<string, string>
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "true" }
        },
        Id = "Microsoft.DotNet.ApiCompat",
        Version = Version
    };

    private readonly PackageArtifactModel _nonShippingPackage = new()
    {
        Attributes = new Dictionary<string, string>
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "true" }
        },
        Id = "Microsoft.Cci.Extensions",
        Version = Version
    };

    private readonly PackageArtifactModel _packageWithNoVersion = new()
    {
        Attributes = new Dictionary<string, string>
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "true" }
        },
        Id = "Microsoft.Cci.Extensions",
        Version = null
    };

    private readonly PackageArtifactModel _shippingPackage = new()
    {
        Attributes = new Dictionary<string, string>
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "false" }
        },
        Id = "Microsoft.DotNet.ApiCompat",
        Version = Version
    };
    private List<PackageArtifactModel> _packages;
    private List<BlobArtifactModel> _blobs;

    private readonly BlobArtifactModel _blob1 = new()
    {
        Attributes = new Dictionary<string, string>
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "true" },
            { PushMetadataToBuildAssetRegistry.CategoryAttributeName, "other" }
        },
        Id = "assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg"
    };

    private readonly BlobArtifactModel _mergedManifest = new()
    {
        Attributes = new Dictionary<string, string>
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "true" }
        },
        Id = $"assets/manifests/{BuildRepoName}/{Id}/{MergedManifestName}"
    };

    private readonly BlobArtifactModel _nonShippingBlob = new()
    {
        Attributes = new Dictionary<string, string>
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "true" },
            { PushMetadataToBuildAssetRegistry.CategoryAttributeName, "none" }
        },
        Id = "assets/symbols/Microsoft.DotNet.ApiCompat.6.0.0-beta.20516.5.symbols.nupkg"
    };

    private readonly BlobArtifactModel _shippingBlob = new()
    {
        Attributes = new Dictionary<string, string>
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "false" },
            { PushMetadataToBuildAssetRegistry.CategoryAttributeName, "none" }
        },
        Id = "assets/symbols/Microsoft.DotNet.ProductConstructionService.Client.6.0.0-beta.20516.5.symbols.nupkg"
    };

    private readonly Manifest _manifest = new()
    {
        InitialAssetsLocation = InitialAssetsLocation,
        AzureDevOpsBuildId = AzDoBuildId,
        AzureDevOpsBuildDefinitionId = AzDoBuildDefId,
        AzureDevOpsAccount = AzDoAccount,
        AzureDevOpsProject = AzDoProject,
        AzureDevOpsBuildNumber = AzDoBuildNumber,
        AzureDevOpsRepository = AzDoRepo,
        AzureDevOpsBranch = AzDoBranch,
        PublishingVersion = PublishingVersion,
        IsReleaseOnlyPackageVersion = IsReleasePackage,
        IsStable = IsStable
    };

    private static PushMetadataToBuildAssetRegistry GetPushMetadata()
    {
        var getEnvMock = new Mock<IGetEnvProxy>();
        getEnvMock.Setup(a => a.GetEnv("BUILD_REPOSITORY_NAME")).Returns(BuildRepoName);
        getEnvMock.Setup(b => b.GetEnv("BUILD_BUILDNUMBER")).Returns(BuildNumber);
        getEnvMock.Setup(c => c.GetEnv("BUILD_SOURCEBRANCH")).Returns(SourceBranch);
        getEnvMock.Setup(d => d.GetEnv("BUILD_SOURCEVERSION")).Returns(CommitSourceVersion);
        getEnvMock.SetReturnsDefault("MissingEnvVariableCheck!");

        var pushMetadata = new PushMetadataToBuildAssetRegistry
        {
            _getEnvProxy = getEnvMock.Object,
            _versionIdentifier = new VersionIdentifierMock()
        };
        return pushMetadata;
    }

    private BuildModel GetBuildModel()
    {
        var expectedBuildModel = new BuildModel(
            new BuildIdentity
            {
                Attributes = new Dictionary<string, string>()
                {
                    { "InitialAssetsLocation", InitialAssetsLocation },
                    { "AzureDevOpsBuildId", AzDoBuildId.ToString() },
                    { "AzureDevOpsBuildDefinitionId", AzDoBuildDefId.ToString() },
                    { "AzureDevOpsAccount", AzDoAccount },
                    { "AzureDevOpsProject", AzDoProject },
                    { "AzureDevOpsBuildNumber", AzDoBuildNumber },
                    { "AzureDevOpsRepository", AzDoRepo },
                    { "AzureDevOpsBranch", AzDoBranch }

                },
                Name = BuildRepoName,
                BuildId = BuildNumber,
                Branch = SourceBranch,
                Commit = CommitSourceVersion,
                IsStable = bool.Parse(IsStable),
                PublishingVersion = (PublishingInfraVersion)_manifest.PublishingVersion,
                IsReleaseOnlyPackageVersion = bool.Parse(IsReleasePackage)
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
                Packages = [_package1, _nonShippingPackage],
                Blobs = [_blob1, _nonShippingBlob]
            };
        expectedBuildModel.Identity.IsStable = true;

        _packages = [_package1, _nonShippingPackage];
        _blobs = [_blob1, _nonShippingBlob];

        BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(_packages, _blobs, _manifest);

        actualModel.Should().BeEquivalentTo(expectedBuildModel);
    }

    [Test]
    public void CheckIfBlobDataIsPreservedAfterMerging()
    {
        BuildModel expectedBuildModel = GetBuildModel();
        PushMetadataToBuildAssetRegistry pushMetadata = GetPushMetadata();

        _packages = [];
        _blobs = [_blob1];

        expectedBuildModel.Artifacts =
            new ArtifactSet
            {
                Blobs = [_blob1]
            };
        BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(_packages, _blobs, _manifest);

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
                Packages = [_shippingPackage, _package1, _packageWithNoVersion],
                Blobs = [_blob1]
            };

        _packages = [_shippingPackage, _package1, _packageWithNoVersion];
        _blobs = [_blob1];
        BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(_packages, _blobs, _manifest);

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
                Blobs = [_blob1, _nonShippingBlob, _mergedManifest, _shippingBlob]
            };

        _packages = [];
        _blobs = [_blob1, _nonShippingBlob, _mergedManifest, _shippingBlob];

        BuildModel actualModel = pushMetadata.CreateMergedManifestBuildModel(_packages, _blobs, _manifest);

        actualModel.Should().BeEquivalentTo(expectedBuildModel);
    }
}
