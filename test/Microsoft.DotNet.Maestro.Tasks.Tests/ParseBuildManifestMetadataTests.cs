// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.DotNet.Maestro.Tasks.Proxies;
using Microsoft.DotNet.Maestro.Tasks.Tests.Mocks;
using Microsoft.DotNet.VersionTools.BuildManifest.Model;
using Moq;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.DotNet.Maestro.Tasks.Tests;

[TestFixture]
public class ParseBuildManifestMetadataTests
{
    private PushMetadataToBuildAssetRegistry _pushMetadata;

    public const string Commit = "e7a79ce64f0703c231e6da88b5279dd0bf681b3d";
    public const string AzureDevOpsAccount1 = "dnceng";
    public const int AzureDevOpsBuildDefinitionId1 = 6;
    public const int AzureDevOpsBuildId1 = 856354;
    public const string AzureDevOpsBranch1 = "refs/heads/main";
    public const string AzureDevOpsBuildNumber1 = "20201016.5";
    public const string AzureDevOpsProject1 = "internal";
    public const string AzureDevOpsRepository1 = "https://dnceng@dev.azure.com/dnceng/internal/_git/dotnet-arcade";

    public const string LocationString =
        "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts";

    private const string GitHubRepositoryName = "dotnet-arcade";
    private const string GitHubBranch = "refs/heads/main";
    private const string RepoName = "dotnet-arcade";
    private const string SystemTeamCollectionUri = "https://dev.azure.com/dnceng/";
    private const string Version = "6.0.0-beta.20516.5";

    #region Assets

    internal static readonly AssetData PackageAsset1 =
        new(true)
        {
            Locations = [new AssetLocationData(LocationType.Container) { Location = LocationString }],
            Name = "Microsoft.Cci.Extensions",
            Version = "6.0.0-beta.20516.5"
        };

    internal static readonly AssetData BlobAsset1 =
        new(true)
        {
            Locations = [new AssetLocationData(LocationType.Container) { Location = LocationString }],
            Name = "assets/manifests/dotnet-arcade/6.0.0-beta.20516.5/MergedManifest.xml",
            Version = "6.0.0-beta.20516.5"
        };

    internal static readonly AssetData PackageAsset2 =
        new(true)
        {
            Locations = [new AssetLocationData(LocationType.Container) { Location = LocationString }],
            Name = "Microsoft.DotNet.ApiCompat",
            Version = "6.0.0-beta.20516.5"
        };

    internal static readonly AssetData BlobAsset2 =
        new(true)
        {
            Locations = [new AssetLocationData(LocationType.Container) { Location = LocationString }],
            Name = "assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg",
            Version = "6.0.0-beta.20516.5"
        };

    internal static readonly AssetData unversionedPackageAsset =
        new(true)
        {
            Locations = [new AssetLocationData(LocationType.Container) { Location = LocationString }],
            Name = "Microsoft.Cci.Extensions",
            Version = null
        };

    internal static readonly AssetData unversionedBlobAsset =
        new(true)
        {
            Locations = [new AssetLocationData(LocationType.Container) { Location = LocationString }],
            Name = "assets/symbols/Microsoft.DotNet.Arcade.Sdk.6.0.0-beta.20516.5.symbols.nupkg",
            Version = "assets/symbols/Microsoft.DotNet.Arcade.Sdk.6.0.0-beta.20516.5.symbols.nupkg"
        };

    #endregion

    #region Individual Assets

    private static readonly Package package1 = new()
    {
        Id = "Microsoft.Cci.Extensions",
        NonShipping = true,
        Version = Version,
        DotNetReleaseShipping = true
    };

    private static readonly Package package2 = new()
    {
        Id = "Microsoft.DotNet.ApiCompat",
        NonShipping = true,
        Version = Version
    };

    private static readonly PackageArtifactModel packageArtifactModel1 = new()
    {
        Attributes = new Dictionary<string, string>()
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "true" },
            { PushMetadataToBuildAssetRegistry.DotNetReleaseShippingAttributeName, "true" }
        },
        Id = "Microsoft.Cci.Extensions",
        Version = Version
    };

    private static readonly PackageArtifactModel packageArtifactModel2 = new()
    {
        Attributes = new Dictionary<string, string>()
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "true" },
            { PushMetadataToBuildAssetRegistry.DotNetReleaseShippingAttributeName, "false" }
        },
        Id = "Microsoft.DotNet.ApiCompat",
        Version = Version
    };

    private static readonly PackageArtifactModel unversionedPackageArtifactModel = new()
    {
        Attributes = new Dictionary<string, string>()
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "true" },
            { PushMetadataToBuildAssetRegistry.DotNetReleaseShippingAttributeName, "false" }
        },
        Id = "Microsoft.Cci.Extensions",
        Version = null
    };

    private static readonly Package unversionedPackage = new()
    {
        Id = "Microsoft.Cci.Extensions",
        NonShipping = true
    };

    private static readonly Blob manifestAsBlob = new()
    {
        Id = "assets/manifests/dotnet-arcade/6.0.0-beta.20516.5/MergedManifest.xml",
        NonShipping = true
    };

    private static readonly Blob blob2 = new()
    {
        Id = "assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg",
        NonShipping = true,
        Category = "Other"
    };

    private static readonly BlobArtifactModel manifestAsBlobArtifactModel = new()
    {
        Attributes = new Dictionary<string, string>()
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "true" },
            { PushMetadataToBuildAssetRegistry.CategoryAttributeName, "NONE" },
            { PushMetadataToBuildAssetRegistry.DotNetReleaseShippingAttributeName, "false" }
        },
        Id = "assets/manifests/dotnet-arcade/6.0.0-beta.20516.5/MergedManifest.xml"
    };

    private static readonly BlobArtifactModel blobArtifactModel2 = new()
    {
        Attributes = new Dictionary<string, string>()
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "true" },
            { PushMetadataToBuildAssetRegistry.CategoryAttributeName, "OTHER" },
            { PushMetadataToBuildAssetRegistry.DotNetReleaseShippingAttributeName, "false" }
        },
        Id = "assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg"
    };

    private static readonly BlobArtifactModel unversionedBlobArtifactModel = new()
    {
        Attributes = new Dictionary<string, string>()
        {
            { PushMetadataToBuildAssetRegistry.NonShippingAttributeName, "true" },
            { PushMetadataToBuildAssetRegistry.CategoryAttributeName, "NONE" },
            { PushMetadataToBuildAssetRegistry.DotNetReleaseShippingAttributeName, "false" }
        },
        Id = "assets/symbols/Microsoft.DotNet.Arcade.Sdk.6.0.0-beta.20516.5.symbols.nupkg"
    };

    private static readonly Blob unversionedBlob = new()
    {
        Id = "assets/symbols/Microsoft.DotNet.Arcade.Sdk.6.0.0-beta.20516.5.symbols.nupkg",
        NonShipping = true
    };

    private static readonly SigningInformation signingInfo1 = new()
    {
        CertificatesSignInfo =
        [
            new CertificatesSignInfo()
            {
                DualSigningAllowed = true,
                Include = "ThisIsACert"
            }
        ],

        FileExtensionSignInfos =
        [
            new FileExtensionSignInfo()
            {
                CertificateName = "ThisIsACert",
                Include = ".dll"
            }
        ],

        FileSignInfos =
        [
            new FileSignInfo()
            {
                CertificateName = "ThisIsACert",
                Include = "ALibrary.dll"
            },
            new FileSignInfo()
            {
                CertificateName = "ThisIsACertWithPKTAndTFM",
                Include = "ASecondLibrary.dll",
                PublicKeyToken = "4258675309abcdef",
                TargetFramework = ".NETFramework,Version=v2.0"
            }
        ],

        StrongNameSignInfos =
        [
            new StrongNameSignInfo()
            {
                CertificateName = "ThisIsACert",
                Include = "IncludeMe",
                PublicKeyToken = "123456789abcde12"
            }
        ],
        ItemsToSign = []
    };

    public static readonly SigningInformation signingInfo2 =
        new()
        {
            CertificatesSignInfo =
            [
                new CertificatesSignInfo()
                {
                    DualSigningAllowed = true,
                    Include = "AnotherCert"
                }
            ],

            FileExtensionSignInfos =
            [
                new FileExtensionSignInfo()
                {
                    CertificateName = "None",
                    Include = ".zip"
                }
            ],

            FileSignInfos =
            [
                new FileSignInfo()
                {
                    CertificateName = "AnotherCert",
                    Include = "AnotherLibrary.dll"
                },
                new FileSignInfo()
                {
                    CertificateName = "AnotherCertWithPKTAndTFM",
                    Include = "YetAnotherLibrary.dll",
                    PublicKeyToken = "4258675309abcdef",
                    TargetFramework = ".NETFramework,Version=v1.3"
                }
            ],

            StrongNameSignInfos =
            [
                new StrongNameSignInfo()
                {
                    CertificateName = "AnotherCert",
                    Include = "StrongName",
                    PublicKeyToken = "123456789abcde12"
                }
            ],
            ItemsToSign = []
        };

    #endregion

    #region Manifests

    private static readonly Manifest baseManifest = new()
    {
        AzureDevOpsAccount = AzureDevOpsAccount1,
        AzureDevOpsBranch = AzureDevOpsBranch1,
        AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
        AzureDevOpsBuildId = AzureDevOpsBuildId1,
        AzureDevOpsBuildNumber = AzureDevOpsBuildNumber1,
        AzureDevOpsProject = AzureDevOpsProject1,
        AzureDevOpsRepository = AzureDevOpsRepository1,
        InitialAssetsLocation = LocationString,
        PublishingVersion = 3,
        Commit = Commit,
        Name = GitHubRepositoryName,
        Branch = GitHubBranch,
        Packages = [],
        Blobs = [],
        SigningInformation = new SigningInformation()
    };

    private static readonly Manifest manifest1 = new()
    {
        AzureDevOpsAccount = AzureDevOpsAccount1,
        AzureDevOpsBranch = AzureDevOpsBranch1,
        AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
        AzureDevOpsBuildId = AzureDevOpsBuildId1,
        AzureDevOpsBuildNumber = AzureDevOpsBuildNumber1,
        AzureDevOpsProject = AzureDevOpsProject1,
        AzureDevOpsRepository = AzureDevOpsRepository1,
        InitialAssetsLocation = LocationString,
        PublishingVersion = 3,
        Commit = Commit,
        Name = GitHubRepositoryName,
        Branch = GitHubBranch,
        Packages = [package1, package2],
        Blobs = [manifestAsBlob, blob2],
        SigningInformation = signingInfo1
    };

    private readonly BuildModel _buildModel = new(
        new BuildIdentity
        {
            Attributes = new Dictionary<string, string>()
            {
                { "InitialAssetsLocation", LocationString },
                { "AzureDevOpsBuildId", AzureDevOpsBuildId1.ToString() },
                { "AzureDevOpsBuildDefinitionId", AzureDevOpsBuildDefinitionId1.ToString() },
                { "AzureDevOpsAccount", AzureDevOpsAccount1 },
                { "AzureDevOpsProject", AzureDevOpsProject1 },
                { "AzureDevOpsBuildNumber", AzureDevOpsBuildNumber1 },
                { "AzureDevOpsRepository", AzureDevOpsRepository1 },
                { "AzureDevOpsBranch", AzureDevOpsBranch1 }
            },
            Name = "dotnet-arcade",
            Commit = Commit,
            IsStable = false,
            PublishingVersion = (PublishingInfraVersion)3,
            IsReleaseOnlyPackageVersion = false

        });

    #endregion

    [SetUp]
    public void SetupGetBuildManifestMetadataTests()
    {
        var getEnvMock = new Mock<IGetEnvProxy>();
        getEnvMock.Setup(a => a.GetEnv("BUILD_REPOSITORY_NAME")).Returns(RepoName);
        getEnvMock.Setup(b => b.GetEnv("BUILD_BUILDNUMBER")).Returns(AzureDevOpsBuildNumber1);
        getEnvMock.Setup(c => c.GetEnv("BUILD_SOURCEBRANCH")).Returns(AzureDevOpsBranch1);
        getEnvMock.Setup(d => d.GetEnv("BUILD_SOURCEVERSION")).Returns(Commit);
        getEnvMock.Setup(d => d.GetEnv("SYSTEM_TEAMFOUNDATIONCOLLECTIONURI")).Returns(SystemTeamCollectionUri);
        getEnvMock.Setup(d => d.GetEnv("BUILD_BUILDID")).Returns(AzureDevOpsBuildId1.ToString());
        getEnvMock.SetReturnsDefault("MissingEnvVariableCheck!");

        _pushMetadata = new PushMetadataToBuildAssetRegistry
        {
            _getEnvProxy = getEnvMock.Object
        };
    }

    [Test]
    public void CheckPackagesAndBlobsTest()
    {
        List<PackageArtifactModel> expectedPackageArtifactModel = [packageArtifactModel1, packageArtifactModel2];

        List<BlobArtifactModel> expectedBlobArtifactModel = [manifestAsBlobArtifactModel, blobArtifactModel2];

        (List<PackageArtifactModel> packages, List<BlobArtifactModel> blobs) =
            PushMetadataToBuildAssetRegistry.GetPackagesAndBlobsInfo(manifest1);
        packages.Should().BeEquivalentTo(expectedPackageArtifactModel);
        blobs.Should().BeEquivalentTo(expectedBlobArtifactModel);
    }

    [Test]
    public void EmptyManifestShouldReturnEmptyObjects()
    {
        (List<PackageArtifactModel> packages, List<BlobArtifactModel> blobs) =
            PushMetadataToBuildAssetRegistry.GetPackagesAndBlobsInfo(baseManifest);
        packages.Should().BeEmpty();
        blobs.Should().BeEmpty();
    }

    [Test]
    public void GivenManifestWithoutPackages()
    {
        Manifest manifestWithoutPackages = SharedMethods.GetCopyOfManifest(baseManifest);
        manifestWithoutPackages.Blobs = [manifestAsBlob];

        var expectedBlobs = new List<BlobArtifactModel>() { manifestAsBlobArtifactModel };
        var (packages, blobs) = PushMetadataToBuildAssetRegistry.GetPackagesAndBlobsInfo(manifestWithoutPackages);
        packages.Should().BeEmpty();
        blobs.Should().BeEquivalentTo(expectedBlobs);
    }

    [Test]
    public void GivenManifestWithUnversionedPackage()
    {
        Manifest manifestWithUnversionedPackage = SharedMethods.GetCopyOfManifest(baseManifest);
        manifestWithUnversionedPackage.Packages = [unversionedPackage];
        var expectedPackages = new List<PackageArtifactModel>() { unversionedPackageArtifactModel };
        var (actualPackages, _) = PushMetadataToBuildAssetRegistry.GetPackagesAndBlobsInfo(manifestWithUnversionedPackage);
        actualPackages.Should().BeEquivalentTo(expectedPackages);
    }

    [Test]
    public void GivenManifestWithoutBlobs()
    {
        Manifest manifestWithoutBlobs = SharedMethods.GetCopyOfManifest(baseManifest);
        manifestWithoutBlobs.Packages = [package1];

        var expectedPackages = new List<PackageArtifactModel>() { packageArtifactModel1 };
        var (actualPackages, actualBlobs) = PushMetadataToBuildAssetRegistry.GetPackagesAndBlobsInfo(manifestWithoutBlobs);
        actualPackages.Should().BeEquivalentTo(expectedPackages);
        actualBlobs.Should().BeEmpty();
    }

    [Test]
    public void GivenUnversionedBlob()
    {
        Manifest manifestWithUnversionedBlob = SharedMethods.GetCopyOfManifest(baseManifest);
        manifestWithUnversionedBlob.Blobs = [unversionedBlob];

        var expectedBlobs = new List<BlobArtifactModel>() { unversionedBlobArtifactModel };
        var (actualPackages, actualBlobs) = PushMetadataToBuildAssetRegistry.GetPackagesAndBlobsInfo(manifestWithUnversionedBlob);
        actualBlobs.Should().BeEquivalentTo(expectedBlobs);
        actualPackages.Should().BeEmpty();
    }

    [Test]
    public void UpdateAssetAndBuildDataFromBuildModel()
    {
        _buildModel.Artifacts = new ArtifactSet();
        _buildModel.Artifacts.Blobs.Add(manifestAsBlobArtifactModel);
        _buildModel.Artifacts.Packages.Add(packageArtifactModel1);

        var expectedBuildData = new BuildData(
            commit: Commit,
            azureDevOpsAccount: AzureDevOpsAccount1,
            azureDevOpsProject: AzureDevOpsProject1,
            azureDevOpsBuildNumber: AzureDevOpsBuildNumber1,
            azureDevOpsRepository: AzureDevOpsRepository1,
            azureDevOpsBranch: AzureDevOpsBranch1,
            stable: false,
            released: false)
        {
            Assets = [PackageAsset1, BlobAsset1],
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            GitHubRepository = GitHubRepositoryName,
            GitHubBranch = GitHubBranch,
        };

        var buildData =
            _pushMetadata.GetMaestroBuildDataFromMergedManifest(_buildModel, manifest1, CancellationToken.None);

        buildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
        buildData.Should().BeEquivalentTo(expectedBuildData);
    }

    [Test]
    public void NoPackagesInBuildModel()
    {
        _buildModel.Artifacts = new ArtifactSet();
        _buildModel.Artifacts.Blobs.Add(manifestAsBlobArtifactModel);
        var expectedBuildData = new BuildData(
            commit: Commit,
            azureDevOpsAccount: AzureDevOpsAccount1,
            azureDevOpsProject: AzureDevOpsProject1,
            azureDevOpsBuildNumber: AzureDevOpsBuildNumber1,
            azureDevOpsRepository: AzureDevOpsRepository1,
            azureDevOpsBranch: AzureDevOpsBranch1,
            stable: false,
            released: false)
        {
            Assets = [],
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            GitHubRepository = GitHubRepositoryName,
            GitHubBranch = GitHubBranch,
        };
        expectedBuildData.Assets = [..expectedBuildData.Assets, BlobAsset1];
        var buildData =
            _pushMetadata.GetMaestroBuildDataFromMergedManifest(_buildModel, baseManifest, CancellationToken.None);
        buildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
        buildData.Should().BeEquivalentTo(expectedBuildData);
    }

    [Test]
    public void NoBlobsInBuildModel()
    {
        _buildModel.Artifacts = new ArtifactSet();
        _buildModel.Artifacts.Packages.Add(packageArtifactModel1);
        var expectedBuildData = new BuildData(
            commit: Commit,
            azureDevOpsAccount: AzureDevOpsAccount1,
            azureDevOpsProject: AzureDevOpsProject1,
            azureDevOpsBuildNumber: AzureDevOpsBuildNumber1,
            azureDevOpsRepository: AzureDevOpsRepository1,
            azureDevOpsBranch: AzureDevOpsBranch1,
            stable: false,
            released: false)
        {
            Assets = [],
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            GitHubRepository = GitHubRepositoryName,
            GitHubBranch = GitHubBranch,
        };

        expectedBuildData.Assets = [..expectedBuildData.Assets, PackageAsset1];
        var buildData =
            _pushMetadata.GetMaestroBuildDataFromMergedManifest(_buildModel, manifest1, CancellationToken.None);
        buildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
        buildData.Should().BeEquivalentTo(expectedBuildData);
    }


    [Test]
    public void MultipleBlobsAndPackagesToBuildData()
    {
        _buildModel.Artifacts = new ArtifactSet();
        _buildModel.Artifacts.Packages.Add(packageArtifactModel1);
        _buildModel.Artifacts.Packages.Add(packageArtifactModel2);
        _buildModel.Artifacts.Blobs.Add(manifestAsBlobArtifactModel);
        _buildModel.Artifacts.Blobs.Add(blobArtifactModel2);

        var expectedBuildData = new BuildData(
            commit: Commit,
            azureDevOpsAccount: AzureDevOpsAccount1,
            azureDevOpsProject: AzureDevOpsProject1,
            azureDevOpsBuildNumber: AzureDevOpsBuildNumber1,
            azureDevOpsRepository: AzureDevOpsRepository1,
            azureDevOpsBranch: AzureDevOpsBranch1,
            stable: false,
            released: false)
        {
            Assets = [],
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            GitHubRepository = GitHubRepositoryName,
            GitHubBranch = GitHubBranch,
        };
        expectedBuildData.Assets = [..expectedBuildData.Assets, PackageAsset1];
        expectedBuildData.Assets = [..expectedBuildData.Assets, PackageAsset2];
        expectedBuildData.Assets = [..expectedBuildData.Assets, BlobAsset1];
        expectedBuildData.Assets = [..expectedBuildData.Assets, BlobAsset2];

        var buildData =
            _pushMetadata.GetMaestroBuildDataFromMergedManifest(_buildModel, manifest1, CancellationToken.None);
        buildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
        buildData.Should().BeEquivalentTo(expectedBuildData);
    }


    [Test]
    public void NoAssetsInBuildData()
    {
        _buildModel.Artifacts = new ArtifactSet();
        var expectedBuildData = new BuildData(
            commit: Commit,
            azureDevOpsAccount: AzureDevOpsAccount1,
            azureDevOpsProject: AzureDevOpsProject1,
            azureDevOpsBuildNumber: AzureDevOpsBuildNumber1,
            azureDevOpsRepository: AzureDevOpsRepository1,
            azureDevOpsBranch: AzureDevOpsBranch1,
            stable: false,
            released: false)
        {
            Assets = [],
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            GitHubRepository = GitHubRepositoryName,
            GitHubBranch = GitHubBranch,
        };

        var buildData =
            _pushMetadata.GetMaestroBuildDataFromMergedManifest(_buildModel, manifest1, CancellationToken.None);
        buildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
        buildData.Should().BeEquivalentTo(expectedBuildData);
    }

    [Test]
    public void UnversionedPackagesToBuildData()
    {
        _pushMetadata._versionIdentifier = new VersionIdentifierMock();
        _buildModel.Artifacts = new ArtifactSet();
        _buildModel.Artifacts.Packages.Add(unversionedPackageArtifactModel);

        var expectedBuildData = new BuildData(
            commit: Commit,
            azureDevOpsAccount: AzureDevOpsAccount1,
            azureDevOpsProject: AzureDevOpsProject1,
            azureDevOpsBuildNumber: AzureDevOpsBuildNumber1,
            azureDevOpsRepository: AzureDevOpsRepository1,
            azureDevOpsBranch: AzureDevOpsBranch1,
            stable: false,
            released: false)
        {
            Assets = [],
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            GitHubRepository = GitHubRepositoryName,
            GitHubBranch = GitHubBranch,
        };
        expectedBuildData.Assets = [..expectedBuildData.Assets, unversionedPackageAsset];

        var buildData =
            _pushMetadata.GetMaestroBuildDataFromMergedManifest(_buildModel, manifest1, CancellationToken.None);
        buildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
        buildData.Should().BeEquivalentTo(expectedBuildData);
    }
}
