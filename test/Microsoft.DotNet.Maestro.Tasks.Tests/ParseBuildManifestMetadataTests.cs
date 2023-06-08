// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.DotNet.Maestro.Tasks.Proxies;
using Microsoft.DotNet.Maestro.Tasks.Tests.Mocks;
using Microsoft.DotNet.VersionTools.BuildManifest.Model;
using Moq;
using NUnit.Framework;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace Microsoft.DotNet.Maestro.Tasks.Tests;

[TestFixture]
public class ParseBuildManifestMetadataTests
{
    private PushMetadataToBuildAssetRegistry pushMetadata;

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
    private const string version = "6.0.0-beta.20516.5";

    #region Assets

    internal static readonly AssetData PackageAsset1 =
        new AssetData(true)
        {
            Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
            Name = "Microsoft.Cci.Extensions",
            Version = "6.0.0-beta.20516.5"
        };

    internal static readonly AssetData BlobAsset1 =
        new AssetData(true)
        {
            Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
            Name = "assets/manifests/dotnet-arcade/6.0.0-beta.20516.5/MergedManifest.xml",
            Version = "6.0.0-beta.20516.5"
        };

    internal static readonly AssetData PackageAsset2 =
        new AssetData(true)
        {
            Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
            Name = "Microsoft.DotNet.ApiCompat",
            Version = "6.0.0-beta.20516.5"
        };

    internal static readonly AssetData BlobAsset2 =
        new AssetData(true)
        {
            Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
            Name = "assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg",
            Version = "6.0.0-beta.20516.5"
        };

    internal static readonly AssetData unversionedPackageAsset =
        new AssetData(true)
        {
            Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
            Name = "Microsoft.Cci.Extensions",
            Version = null
        };

    internal static readonly AssetData unversionedBlobAsset =
        new AssetData(true)
        {
            Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
            Name = "assets/symbols/Microsoft.DotNet.Arcade.Sdk.6.0.0-beta.20516.5.symbols.nupkg",
            Version = "assets/symbols/Microsoft.DotNet.Arcade.Sdk.6.0.0-beta.20516.5.symbols.nupkg"
        };

    #endregion

    #region Individual Assets

    private static readonly Package package1 = new Package()
    {
        Id = "Microsoft.Cci.Extensions",
        NonShipping = true,
        Version = version
    };

    private static readonly Package package2 = new Package()
    {
        Id = "Microsoft.DotNet.ApiCompat",
        NonShipping = true,
        Version = version
    };

    private static readonly PackageArtifactModel packageArtifactModel1 = new PackageArtifactModel()
    {
        Attributes = new Dictionary<string, string>()
        {
            { "NonShipping", "true" }
        },
        Id = "Microsoft.Cci.Extensions",
        Version = version
    };

    private static readonly PackageArtifactModel packageArtifactModel2 = new PackageArtifactModel()
    {
        Attributes = new Dictionary<string, string>()
        {
            { "NonShipping", "true" }
        },
        Id = "Microsoft.DotNet.ApiCompat",
        Version = version
    };

    private static readonly PackageArtifactModel unversionedPackageArtifactModel = new PackageArtifactModel()
    {
        Attributes = new Dictionary<string, string>()
        {
            { "NonShipping", "true" }
        },
        Id = "Microsoft.Cci.Extensions",
        Version = null
    };

    private static readonly Package unversionedPackage = new Package()
    {
        Id = "Microsoft.Cci.Extensions",
        NonShipping = true
    };

    private static readonly Blob manifestAsBlob = new Blob()
    {
        Id = "assets/manifests/dotnet-arcade/6.0.0-beta.20516.5/MergedManifest.xml",
        NonShipping = true
    };

    private static readonly Blob blob2 = new Blob()
    {
        Id = "assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg",
        NonShipping = true,
        Category = "Other"
    };

    private static readonly BlobArtifactModel manifestAsBlobArtifactModel = new BlobArtifactModel()
    {
        Attributes = new Dictionary<string, string>()
        {
            { "NonShipping", "true" },
            { "Category", "NONE" }
        },
        Id = "assets/manifests/dotnet-arcade/6.0.0-beta.20516.5/MergedManifest.xml"
    };

    private static readonly BlobArtifactModel blobArtifactModel2 = new BlobArtifactModel()
    {
        Attributes = new Dictionary<string, string>()
        {
            { "NonShipping", "true" },
            { "Category", "OTHER" }
        },
        Id = "assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg"
    };

    private static readonly BlobArtifactModel unversionedBlobArtifactModel = new BlobArtifactModel()
    {
        Attributes = new Dictionary<string, string>()
        {
            { "NonShipping", "true" },
            { "Category", "NONE" }
        },
        Id = "assets/symbols/Microsoft.DotNet.Arcade.Sdk.6.0.0-beta.20516.5.symbols.nupkg"
    };

    private static readonly Blob unversionedBlob = new Blob()
    {
        Id = "assets/symbols/Microsoft.DotNet.Arcade.Sdk.6.0.0-beta.20516.5.symbols.nupkg",
        NonShipping = true
    };

    private static readonly SigningInformation signingInfo1 = new SigningInformation()
    {
        CertificatesSignInfo = new List<CertificatesSignInfo>()
        {
            new CertificatesSignInfo()
            {
                DualSigningAllowed = true,
                Include = "ThisIsACert"
            }
        },

        FileExtensionSignInfos = new List<FileExtensionSignInfo>()
        {
            new FileExtensionSignInfo()
            {
                CertificateName = "ThisIsACert",
                Include = ".dll"
            }
        },

        FileSignInfos = new List<FileSignInfo>()
        {
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
        },

        StrongNameSignInfos = new List<StrongNameSignInfo>()
        {
            new StrongNameSignInfo()
            {
                CertificateName = "ThisIsACert",
                Include = "IncludeMe",
                PublicKeyToken = "123456789abcde12"
            }
        },
        ItemsToSign = new List<ItemsToSign>()
    };

    public static readonly SigningInformation signingInfo2 =
        new SigningInformation()
        {
            CertificatesSignInfo = new List<CertificatesSignInfo>()
            {
                new CertificatesSignInfo()
                {
                    DualSigningAllowed = true,
                    Include = "AnotherCert"
                }
            },

            FileExtensionSignInfos = new List<FileExtensionSignInfo>()
            {
                new FileExtensionSignInfo()
                {
                    CertificateName = "None",
                    Include = ".zip"
                }
            },

            FileSignInfos = new List<FileSignInfo>()
            {
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
            },

            StrongNameSignInfos = new List<StrongNameSignInfo>()
            {
                new StrongNameSignInfo()
                {
                    CertificateName = "AnotherCert",
                    Include = "StrongName",
                    PublicKeyToken = "123456789abcde12"
                }
            },
            ItemsToSign = new List<ItemsToSign>()
        };

    #endregion

    #region Manifests

    private static readonly Manifest baseManifest = new Manifest()
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
        Packages = new List<Package>(),
        Blobs = new List<Blob>(),
        SigningInformation = new SigningInformation()
    };

    private static readonly Manifest manifest1 = new Manifest()
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
        Packages = new List<Package> { package1, package2 },
        Blobs = new List<Blob> { manifestAsBlob, blob2 },
        SigningInformation = signingInfo1
    };

    private static readonly Manifest manifest2 = new Manifest()
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
        Packages = new List<Package> { package2 },
        Blobs = new List<Blob> { blob2 },
        SigningInformation = signingInfo2
    };

    BuildModel buildModel = new BuildModel(
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
        Mock<IGetEnvProxy> getEnvMock = new Mock<IGetEnvProxy>();
        getEnvMock.Setup(a => a.GetEnv("BUILD_REPOSITORY_NAME")).Returns(RepoName);
        getEnvMock.Setup(b => b.GetEnv("BUILD_BUILDNUMBER")).Returns(AzureDevOpsBuildNumber1);
        getEnvMock.Setup(c => c.GetEnv("BUILD_SOURCEBRANCH")).Returns(AzureDevOpsBranch1);
        getEnvMock.Setup(d => d.GetEnv("BUILD_SOURCEVERSION")).Returns(Commit);
        getEnvMock.Setup(d => d.GetEnv("SYSTEM_TEAMFOUNDATIONCOLLECTIONURI")).Returns(SystemTeamCollectionUri);
        getEnvMock.Setup(d => d.GetEnv("BUILD_BUILDID")).Returns(AzureDevOpsBuildId1.ToString());
        getEnvMock.SetReturnsDefault("MissingEnvVariableCheck!");

        pushMetadata = new PushMetadataToBuildAssetRegistry
        {
            getEnvProxy = getEnvMock.Object
        };
    }

    [Test]
    public void CheckPackagesAndBlobsTest()
    {
        List<PackageArtifactModel> expectedPackageArtifactModel = new List<PackageArtifactModel>()
            { packageArtifactModel1, packageArtifactModel2 };

        List<BlobArtifactModel> expectedBlobArtifactModel = new List<BlobArtifactModel>()
            { manifestAsBlobArtifactModel, blobArtifactModel2 };

        (List<PackageArtifactModel> packages, List<BlobArtifactModel> blobs) =
            pushMetadata.GetPackagesAndBlobsInfo(manifest1);
        packages.Should().BeEquivalentTo(expectedPackageArtifactModel);
        blobs.Should().BeEquivalentTo(expectedBlobArtifactModel);
    }

    [Test]
    public void EmptyManifestShouldReturnEmptyObjects()
    {
        (List<PackageArtifactModel> packages, List<BlobArtifactModel> blobs) =
            pushMetadata.GetPackagesAndBlobsInfo(baseManifest);
        packages.Should().BeEmpty();
        blobs.Should().BeEmpty();
    }

    [Test]
    public void GivenManifestWithoutPackages()
    {
        Manifest manifestWithoutPackages = SharedMethods.GetCopyOfManifest(baseManifest);
        manifestWithoutPackages.Blobs = new List<Blob> { manifestAsBlob };

        var expectedBlobs = new List<BlobArtifactModel>() { manifestAsBlobArtifactModel };
        var (packages, blobs) = pushMetadata.GetPackagesAndBlobsInfo(manifestWithoutPackages);
        packages.Should().BeEmpty();
        blobs.Should().BeEquivalentTo(expectedBlobs);
    }

    [Test]
    public void GivenManifestWithUnversionedPackage()
    {
        Manifest manifestWithUnversionedPackage = SharedMethods.GetCopyOfManifest(baseManifest);
        manifestWithUnversionedPackage.Packages = new List<Package> { unversionedPackage };
        var expectedPackages = new List<PackageArtifactModel>() { unversionedPackageArtifactModel };
        var (actualPackages, actualBlobs) = pushMetadata.GetPackagesAndBlobsInfo(manifestWithUnversionedPackage);
        actualPackages.Should().BeEquivalentTo(expectedPackages);
    }

    [Test]
    public void GivenManifestWithoutBlobs()
    {
        Manifest manifestWithoutBlobs = SharedMethods.GetCopyOfManifest(baseManifest);
        manifestWithoutBlobs.Packages = new List<Package> { package1 };

        var expectedPackages = new List<PackageArtifactModel>() { packageArtifactModel1 };
        var (actualPackages, actualBlobs) = pushMetadata.GetPackagesAndBlobsInfo(manifestWithoutBlobs);
        actualPackages.Should().BeEquivalentTo(expectedPackages);
        actualBlobs.Should().BeEmpty();
    }

    [Test]
    public void GivenUnversionedBlob()
    {
        Manifest manifestWithUnversionedBlob = SharedMethods.GetCopyOfManifest(baseManifest);
        manifestWithUnversionedBlob.Blobs = new List<Blob> { unversionedBlob };

        var expectedBlobs = new List<BlobArtifactModel>() { unversionedBlobArtifactModel };
        var (actualPackages, actualBlobs) = pushMetadata.GetPackagesAndBlobsInfo(manifestWithUnversionedBlob);
        actualBlobs.Should().BeEquivalentTo(expectedBlobs);
        actualPackages.Should().BeEmpty();
    }

    [Test]
    public void UpdateAssetAndBuildDataFromBuildModel()
    {
        buildModel.Artifacts = new ArtifactSet();
        buildModel.Artifacts.Blobs.Add(manifestAsBlobArtifactModel);
        buildModel.Artifacts.Packages.Add(packageArtifactModel1);

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
            Assets = new List<AssetData>().ToImmutableList(),
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            GitHubRepository = GitHubRepositoryName,
            GitHubBranch = GitHubBranch,
        };

        expectedBuildData.Assets = expectedBuildData.Assets.Add(PackageAsset1);
        expectedBuildData.Assets = expectedBuildData.Assets.Add(BlobAsset1);

        var buildData =
            pushMetadata.GetMaestroBuildDataFromMergedManifest(buildModel, manifest1, CancellationToken.None);

        buildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
        buildData.Should().BeEquivalentTo(expectedBuildData);
    }

    [Test]
    public void NoPackagesInBuildModel()
    {
        buildModel.Artifacts = new ArtifactSet();
        buildModel.Artifacts.Blobs.Add(manifestAsBlobArtifactModel);
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
            Assets = new List<AssetData>().ToImmutableList(),
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            GitHubRepository = GitHubRepositoryName,
            GitHubBranch = GitHubBranch,
        };
        expectedBuildData.Assets = expectedBuildData.Assets.Add(BlobAsset1);
        var buildData =
            pushMetadata.GetMaestroBuildDataFromMergedManifest(buildModel, baseManifest, CancellationToken.None);
        buildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
        buildData.Should().BeEquivalentTo(expectedBuildData);
    }

    [Test]
    public void NoBlobsInBuildModel()
    {
        buildModel.Artifacts = new ArtifactSet();
        buildModel.Artifacts.Packages.Add(packageArtifactModel1);
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
            Assets = new List<AssetData>().ToImmutableList(),
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            GitHubRepository = GitHubRepositoryName,
            GitHubBranch = GitHubBranch,
        };

        expectedBuildData.Assets = expectedBuildData.Assets.Add(PackageAsset1);
        var buildData =
            pushMetadata.GetMaestroBuildDataFromMergedManifest(buildModel, manifest1, CancellationToken.None);
        buildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
        buildData.Should().BeEquivalentTo(expectedBuildData);
    }


    [Test]
    public void MultipleBlobsAndPackagesToBuildData()
    {
        buildModel.Artifacts = new ArtifactSet();
        buildModel.Artifacts.Packages.Add(packageArtifactModel1);
        buildModel.Artifacts.Packages.Add(packageArtifactModel2);
        buildModel.Artifacts.Blobs.Add(manifestAsBlobArtifactModel);
        buildModel.Artifacts.Blobs.Add(blobArtifactModel2);

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
            Assets = new List<AssetData>().ToImmutableList(),
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            GitHubRepository = GitHubRepositoryName,
            GitHubBranch = GitHubBranch,
        };
        expectedBuildData.Assets = expectedBuildData.Assets.Add(PackageAsset1);
        expectedBuildData.Assets = expectedBuildData.Assets.Add(PackageAsset2);
        expectedBuildData.Assets = expectedBuildData.Assets.Add(BlobAsset1);
        expectedBuildData.Assets = expectedBuildData.Assets.Add(BlobAsset2);

        var buildData =
            pushMetadata.GetMaestroBuildDataFromMergedManifest(buildModel, manifest1, CancellationToken.None);
        buildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
        buildData.Should().BeEquivalentTo(expectedBuildData);
    }


    [Test]
    public void NoAssetsInBuildData()
    {
        buildModel.Artifacts = new ArtifactSet();
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
            Assets = new List<AssetData>().ToImmutableList(),
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            GitHubRepository = GitHubRepositoryName,
            GitHubBranch = GitHubBranch,
        };

        var buildData =
            pushMetadata.GetMaestroBuildDataFromMergedManifest(buildModel, manifest1, CancellationToken.None);
        buildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
        buildData.Should().BeEquivalentTo(expectedBuildData);
    }

    [Test]
    public void UnversionedPackagesToBuildData()
    {
        pushMetadata.versionIdentifier = new VersionIdentifierMock();
        buildModel.Artifacts = new ArtifactSet();
        buildModel.Artifacts.Packages.Add(unversionedPackageArtifactModel);

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
            Assets = new List<AssetData>().ToImmutableList(),
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            GitHubRepository = GitHubRepositoryName,
            GitHubBranch = GitHubBranch,
        };
        expectedBuildData.Assets = expectedBuildData.Assets.Add(unversionedPackageAsset);

        var buildData =
            pushMetadata.GetMaestroBuildDataFromMergedManifest(buildModel, manifest1, CancellationToken.None);
        buildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
        buildData.Should().BeEquivalentTo(expectedBuildData);
    }
}
