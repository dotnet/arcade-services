// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.DotNet.Maestro.Tasks.Tests.Mocks;
using NUnit.Framework;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    [TestFixture]
    public class ParseBuildManifestMetadataTests
    {
        private PushMetadataToBuildAssetRegistry pushMetadata;

        public const string Commit = "e7a79ce64f0703c231e6da88b5279dd0bf681b3d";
        public const string AzureDevOpsAccount1 = "dnceng";
        public const int AzureDevOpsBuildDefinitionId1 = 6;
        public const int AzureDevOpsBuildId1 = 856354;
        public const string AzureDevOpsBranch1 = "refs/heads/master";
        public const string AzureDevOpsBuildNumber1 = "20201016.5";
        public const string AzureDevOpsProject1 = "internal";
        public const string AzureDevOpsRepository1 = "https://dnceng@dev.azure.com/dnceng/internal/_git/dotnet-arcade";
        public const string LocationString = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts";
        private const string GitHubRepositoryName = "dotnet-arcade";
        private const string GitHubBranch = "refs/heads/master";

        #region Assets
        internal static readonly AssetData PackageAsset1 =
           new AssetData(true)
           {
               Locations = ImmutableList.Create(
                   new AssetLocationData(LocationType.Container)
                   { Location = LocationString }),
               Name = "Microsoft.Cci.Extensions",
               Version = "12345"
           };

        internal static readonly AssetData BlobAsset1 =
            new AssetData(true)
            {
                Locations = ImmutableList.Create(
                    new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
                Name = "assets/manifests/dotnet-arcade/6.0.0-beta.20516.5/MergedManifest.xml",
                Version = "12345"
            };

        internal static readonly AssetData PackageAsset2 =
            new AssetData(true)
            {
                Locations = ImmutableList.Create(
                    new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
                Name = "Microsoft.DotNet.ApiCompat",
                Version = "12345"
            };

        internal static readonly AssetData BlobAsset2 =
            new AssetData(true)
            {
                Locations = ImmutableList.Create(
                    new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
                Name = "assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg",
                Version = "12345"
            };

        internal static readonly AssetData PackageAsset3 =
            new AssetData(true)
            {
                Locations = ImmutableList.Create(
                    new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
                Name = "Microsoft.DotNet.Arcade.Sdk",
                Version = "12345"
            };

        internal static readonly AssetData BlobAsset3 =
            new AssetData(true)
            {
                Locations = ImmutableList.Create(
                    new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
                Name = "assets/symbols/Microsoft.DotNet.Arcade.Sdk.6.0.0-beta.20516.5.symbols.nupkg",
                Version = "12345"
            };

        public static readonly IImmutableList<AssetData> ExpectedAssets1 =
            ImmutableList.Create(PackageAsset1, BlobAsset1);

        public static readonly IImmutableList<AssetData> ExpectedAssets2 =
             ImmutableList.Create(PackageAsset2, BlobAsset2);

        public static readonly IImmutableList<AssetData> ExpectedAssets3 =
             ImmutableList.Create(PackageAsset3, BlobAsset3);

        public static readonly IImmutableList<AssetData> NoBlobExpectedAssets =
           ImmutableList.Create(PackageAsset1);

        public static readonly IImmutableList<AssetData> NoPackageExpectedAssets =
            ImmutableList.Create(BlobAsset1);

        public static readonly IImmutableList<AssetData> UnversionedPackageExpectedAssets =
            ImmutableList.Create(
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                { Location = LocationString }),
                    Name = "Microsoft.Cci.Extensions"
                });

        public static readonly IImmutableList<AssetData> UnversionedBlobExpectedAssets =
            ImmutableList.Create(
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                    new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
                    Name = "assets/symbols/Microsoft.DotNet.Arcade.Sdk.6.0.0-beta.20516.5.symbols.nupkg"
                });
        #endregion

        #region IndividualAssets
        private static readonly Package package1 = new Package()
        {
            Id = "Microsoft.Cci.Extensions",
            NonShipping = true,
            Version = "12345"
        };

        private static readonly Package package2 = new Package()
        {
            Id = "Microsoft.DotNet.ApiCompat",
            NonShipping = true,
            Version = "12345"
        };

        private static readonly Package unversionedPackage = new Package()
        {
            Id = "Microsoft.Cci.Extensions",
            NonShipping = true
        };

        private static readonly Blob blob1 = new Blob()
        {
            Id = "assets/manifests/dotnet-arcade/6.0.0-beta.20516.5/MergedManifest.xml",
            NonShipping = true
        };

        private static readonly Blob blob2 = new Blob()
        {
            Id = "assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg",
            NonShipping = true
        };

        private static readonly Blob unversionedBlob = new Blob()
        {
            Id = "noVersionForThisBlob",
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

        #region SigningInfo
        public static readonly List<SigningInformation> ExpectedSigningInfo = new List<SigningInformation>()
        {
            signingInfo1
        };

        public static readonly List<SigningInformation> ExpectedSigningInfo2 = new List<SigningInformation>()
        {
            signingInfo2
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
            Packages = new List<Package> { package1 },
            Blobs = new List<Blob> { blob1 },
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

        private static readonly ManifestBuildData baseManifestBuildData = new ManifestBuildData(baseManifest);
        private static readonly ManifestBuildData manifest1BuildData = new ManifestBuildData(manifest1);

        private static readonly List<BuildData> buildData1 = new List<BuildData>()
            {
                new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "dotnet-arcade",
                    Assets = ExpectedAssets1,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                }
            };

        private static readonly List<BuildData> buildData2 = new List<BuildData>()
            {
                new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "dotnet-arcade",
                    Assets = ExpectedAssets2,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                }
            };

        private static readonly List<BuildData> noBlobManifestBuildData = new List<BuildData>()
            {
                new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "dotnet-arcade",
                    Assets = NoBlobExpectedAssets,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                }
            };

        private static readonly List<BuildData> noPackagesManifestBuildData = new List<BuildData>()
            {
                new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "dotnet-arcade",
                    Assets = NoPackageExpectedAssets,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                }
            };

        private static readonly List<BuildData> unversionedPackagedManifestBuildData = new List<BuildData>()
            {
                new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "dotnet-arcade",
                    Assets = UnversionedPackageExpectedAssets,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                }
            };
        #endregion

        [SetUp]
        public void SetupGetBuildManifestMetadataTests()
        {
            pushMetadata = new PushMetadataToBuildAssetRegistry();
            pushMetadata.versionIdentifier = new VersionIdentifierMock();
        }

        [Test]
        public void EmptyManifestListShouldReturnEmptyObjects()
        {
            var (buildData, signingInformation, manifestBuildData) = pushMetadata.ParseBuildManifestsMetadata(new List<Manifest>(), CancellationToken.None);
            buildData.Should().BeEmpty();
            signingInformation.Should().BeEmpty();
            manifestBuildData.Should().BeNull();
        }

        [Test]
        public void ParseBasicManifest()
        {
            List<Manifest> manifests = new List<Manifest>() { manifest1 };
            var (actualBuildData, actualSigningInformation, actualManifestBuildData) = pushMetadata.ParseBuildManifestsMetadata(manifests, CancellationToken.None);
            actualBuildData.Should().BeEquivalentTo(buildData1);
            actualSigningInformation.Should().BeEquivalentTo(ExpectedSigningInfo);
            actualManifestBuildData.Should().BeEquivalentTo(manifest1BuildData);
        }

        [Test]
        public void ParseTwoManifests()
        {
            List<Manifest> manifests = new List<Manifest>() { manifest1, manifest2 };
            var (actualBuildData, actualSigningInformation, actualManifestBuildData) = pushMetadata.ParseBuildManifestsMetadata(manifests, CancellationToken.None);
            actualBuildData.Should().BeEquivalentTo(buildData1.Concat(buildData2));
            actualSigningInformation.Should().BeEquivalentTo(ExpectedSigningInfo.Concat(ExpectedSigningInfo2));
            actualManifestBuildData.Should().BeEquivalentTo(baseManifestBuildData);
            
        }

        [Test]
        public void GivenAnEmptyManifest_ExceptionExpected()
        {
            Action act = () => pushMetadata.ParseBuildManifestsMetadata(new List<Manifest> { new Manifest() }, CancellationToken.None);
            act.Should().Throw<NullReferenceException>();
        }

        [Test]
        public void GivenManifestWithoutPackages()
        {
            Manifest manifestWithoutPackages = SharedMethods.GetCopyOfManifest(baseManifest);
            manifestWithoutPackages.Blobs = new List<Blob> { blob1 };
            var (actualBuildData, actualSigningInformation, actualManifestBuildData) = pushMetadata.ParseBuildManifestsMetadata(new List<Manifest> { manifestWithoutPackages }, CancellationToken.None);
            actualBuildData.Should().BeEquivalentTo(noPackagesManifestBuildData);
            actualManifestBuildData.Should().BeEquivalentTo(baseManifestBuildData);
        }

        [Test]
        public void GivenManifestWithUnversionedPackage()
        {
            Manifest manifestWithUnversionedPackage = SharedMethods.GetCopyOfManifest(baseManifest);
            manifestWithUnversionedPackage.Packages = new List<Package> { unversionedPackage };
            var (actualBuildData, actualSigningInformation, actualManifestBuildData) = pushMetadata.ParseBuildManifestsMetadata(new List<Manifest> { manifestWithUnversionedPackage }, CancellationToken.None);
            actualBuildData.Should().BeEquivalentTo(unversionedPackagedManifestBuildData);
            actualManifestBuildData.Should().BeEquivalentTo(baseManifestBuildData);
        }

        [Test]
        public void GivenManifestWithoutBlobs()
        {
            Manifest manifestWithoutBlobs = SharedMethods.GetCopyOfManifest(baseManifest);
            manifestWithoutBlobs.Packages = new List<Package> { package1 };
            var (actualBuildData, actualSigningInformation, actualManifestBuildData) = pushMetadata.ParseBuildManifestsMetadata(new List<Manifest> { manifestWithoutBlobs }, CancellationToken.None);
            actualBuildData.Should().BeEquivalentTo(noBlobManifestBuildData);
            actualManifestBuildData.Should().BeEquivalentTo(baseManifestBuildData);
        }

        [Test]
        public void GivenUnversionedBlob()
        {
            Manifest manifestWithUnversionedBlob = SharedMethods.GetCopyOfManifest(baseManifest);
            manifestWithUnversionedBlob.Blobs = new List<Blob> { unversionedBlob };
            Action act = () => pushMetadata.ParseBuildManifestsMetadata(new List<Manifest> { manifestWithUnversionedBlob }, CancellationToken.None);
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void GivenTwoManifestWithDifferentAttributes_ExceptionExpected()
        {
            Manifest differentAttributes = SharedMethods.GetCopyOfManifest(baseManifest);
            differentAttributes.AzureDevOpsAccount = "newAccount";
            Action act = () => pushMetadata.ParseBuildManifestsMetadata(new List<Manifest> { baseManifest, differentAttributes }, CancellationToken.None);
            act.Should().Throw<Exception>().WithMessage("Attributes should be the same in all manifests.");
        }
    }
}
