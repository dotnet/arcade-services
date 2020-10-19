using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using FluentAssertions;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    [TestFixture]
    public class GetBuildManifestMetadataTests
    {
        private readonly ManifestBuildData expectedManifest;
        private readonly List<BuildData> expectedManifestMetadata;
        private readonly List<SigningInformation> expectedSigningInfo;
        private readonly List<BuildData> expectedManifestMetadata2;
        private readonly List<SigningInformation> expectedSigningInfo2;
        private readonly IImmutableList<AssetData> expectedAssets1;
        private readonly IImmutableList<AssetData> expectedAssets2;

        private const string commit = "e7a79ce64f0703c231e6da88b5279dd0bf681b3d";
        private const string azureDevOpsAccount = "dnceng";
        private const int azureDevOpsBuildDefinitionId = 6;
        private const int azureDevOpsBuildId = 856354;
        private const string azureDevOpsBranch = "refs/heads/master";
        private const string azureDevOpsBuildNumber = "20201016.5";
        private const string azureDevOpsProject = "internal";
        private const string azureDevOpsRepository = "https://dnceng@dev.azure.com/dnceng/internal/_git/dotnet-arcade";

        public GetBuildManifestMetadataTests()
        {
            Manifest baseManifestData = new Manifest()
            {
                AzureDevOpsAccount = azureDevOpsAccount,
                AzureDevOpsBranch = azureDevOpsBranch,
                AzureDevOpsBuildDefinitionId = azureDevOpsBuildDefinitionId,
                AzureDevOpsBuildId = azureDevOpsBuildId,
                AzureDevOpsBuildNumber = azureDevOpsBuildNumber,
                AzureDevOpsProject = azureDevOpsProject,
                AzureDevOpsRepository = azureDevOpsRepository,
                InitialAssetsLocation = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts",
                PublishingVersion = 3
            };

            expectedManifest = new ManifestBuildData(baseManifestData);

            expectedAssets1 = ImmutableList.Create(
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                       new AssetLocationData(LocationType.Container)
                       { Location = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts" }),
                    Name = "Microsoft.Cci.Extensions",
                    Version = "6.0.0-beta.20516.5"
                },
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                        new AssetLocationData(LocationType.Container)
                        { Location = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts" }),
                    Name = "assets/manifests/dotnet-arcade/6.0.0-beta.20516.5/MergedManifest.xml",
                    Version = "6.0.0-beta.20516.5"
                });

            expectedAssets2 = ImmutableList.Create(
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                       new AssetLocationData(LocationType.Container)
                       { Location = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts" }),
                    Name = "Microsoft.DotNet.ApiCompat",
                    Version = "6.0.0-beta.20516.5"
                },
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                        new AssetLocationData(LocationType.Container)
                        { Location = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts" }),
                    Name = "assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg",
                    Version = "6.0.0-beta.20516.5"
                });

            expectedManifestMetadata = new List<BuildData>()
            {
                new BuildData(commit, azureDevOpsAccount, azureDevOpsProject, azureDevOpsBuildNumber, azureDevOpsRepository, azureDevOpsBranch, false, false)
                {
                    GitHubBranch = azureDevOpsBranch,
                    GitHubRepository = "dotnet-arcade",
                    Assets = expectedAssets1,
                    AzureDevOpsBuildId = azureDevOpsBuildId,
                    AzureDevOpsBuildDefinitionId = azureDevOpsBuildDefinitionId
                }
            };

            expectedManifestMetadata2 = new List<BuildData>()
            {
                new BuildData(commit, azureDevOpsAccount, azureDevOpsProject, azureDevOpsBuildNumber, azureDevOpsRepository, azureDevOpsBranch, false, false)
                {
                    GitHubBranch = azureDevOpsBranch,
                    GitHubRepository = "dotnet-arcade",
                    Assets = expectedAssets2,
                    AzureDevOpsBuildId = azureDevOpsBuildId,
                    AzureDevOpsBuildDefinitionId = azureDevOpsBuildDefinitionId
                }
            };

            expectedSigningInfo = new List<SigningInformation>()
            {
                new SigningInformation()
                {
                    AzureDevOpsBuildId = azureDevOpsBuildId.ToString(),
                    AzureDevOpsCollectionUri = "https://dev.azure.com/dnceng/",
                    AzureDevOpsProject = azureDevOpsProject,
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
                }
            };
            expectedSigningInfo2 = new List<SigningInformation>()
            {
                new SigningInformation()
                {
                    AzureDevOpsBuildId = azureDevOpsBuildId.ToString(),
                    AzureDevOpsCollectionUri = "https://dev.azure.com/dnceng/",
                    AzureDevOpsProject = azureDevOpsProject,
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
                }
            };
        }

        [Test]
        public void EmptyManifestFolderPath()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();

            Action act = () => pushMetadata.GetBuildManifestsMetadata("", new CancellationToken());
            act.Should().Throw<ArgumentException>().WithMessage("The path is empty. (Parameter 'path')");
        }

        [Test]
        public void ParseBasicManifest()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();

            var data = pushMetadata.GetBuildManifestsMetadata(@"C:\Users\mquinn\Downloads\Manifests\OneManifest", new CancellationToken());
            data.Item1.Should().BeEquivalentTo(expectedManifestMetadata);
            data.Item2.Should().BeEquivalentTo(expectedSigningInfo);
            CompareManifestBuildData(data.Item3, expectedManifest);
        }

        [Test]
        public void ParseTwoManifests()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();

            var data = pushMetadata.GetBuildManifestsMetadata(@"C:\Users\mquinn\Downloads\Manifests\TwoManifests", new CancellationToken());
            data.Item1.Should().BeEquivalentTo(expectedManifestMetadata.Concat(expectedManifestMetadata2));
            data.Item2.Should().BeEquivalentTo(expectedSigningInfo.Concat(expectedSigningInfo2));
            CompareManifestBuildData(data.Item3, expectedManifest);
        }

        [Test]
        public void GivenFileThatIsNotAManifest_ExceptionExpected()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();

            Action act = () => pushMetadata.GetBuildManifestsMetadata(@"C:\Users\mquinn\Downloads\Manifests\NonManifest", new CancellationToken());
            act.Should().Throw<InvalidOperationException>().WithMessage("There is an error in XML document (1, 1).");
        }

        [Test]
        public void GivenBadlyFormattedXml_ExceptionExpected()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();

            Action act = () => pushMetadata.GetBuildManifestsMetadata(@"C:\Users\mquinn\Downloads\Manifests\BadXml", new CancellationToken());
            act.Should().Throw<InvalidOperationException>().WithMessage("There is an error in XML document (2, 1).");
        }

        [Test]
        public void GivenAnEmptyManifest_ExceptionExpected()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();

            Action act = () => pushMetadata.GetBuildManifestsMetadata(@"C:\Users\mquinn\Downloads\Manifests\EmptyManifest", new CancellationToken());
            act.Should().Throw<NullReferenceException>();
        }

        // TODO: This doesn't throw an exception - is it valid to have a manifest with only signing data?
        [Test]
        public void GivenManifestWithoutAssets_ExceptionExpected()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();

            Action act = () => pushMetadata.GetBuildManifestsMetadata(@"C:\Users\mquinn\Downloads\Manifests\ManifestWithoutAssets", new CancellationToken());
            act.Should().Throw<NullReferenceException>();
        }

        // TODO: T his doesn't throw either, is it valid to have a package with no version parameter specified?
        [Test]
        public void GivenManifestWithUnversionedPackage_ExceptionExpected()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();

            Action act = () => pushMetadata.GetBuildManifestsMetadata(@"C:\Users\mquinn\Downloads\Manifests\UnversionedPackage", new CancellationToken());
            act.Should().Throw<NullReferenceException>();
        }

        // TODO: This isn't going to throw either, should it?
        [Test]
        public void GivenManifestWithoutBlobs_ExceptionExpected()
        {

        }

        // TODO: This doesn't actually appear to be a value in the manifest, so why did I add a test? is it in the code?
        [Test]
        public void GivenUnversionedBlob_ExceptionExpected()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();

            Action act = () => pushMetadata.GetBuildManifestsMetadata(@"C:\Users\mquinn\Downloads\Manifests\UnversionedBlob", new CancellationToken());
            act.Should().Throw<NullReferenceException>();
        }

        // TODO: still doesn't care, should it?
        [Test]
        public void GivenBlobWithoutAssets_ExceptionExpected()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();

            Action act = () => pushMetadata.GetBuildManifestsMetadata(@"C:\Users\mquinn\Downloads\Manifests\BlobWithoutAssets", new CancellationToken());
            act.Should().Throw<NullReferenceException>();
        }

        [Test]
        public void GivenTwoManifestWithDifferentAttributes_ExceptionExpected()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();

            Action act = () => pushMetadata.GetBuildManifestsMetadata(@"C:\Users\mquinn\Downloads\Manifests\DifferentAttributes", new CancellationToken());
            act.Should().Throw<Exception>().WithMessage("Attributes should be the same in all manifests.");
        }

        private void CompareManifestBuildData(ManifestBuildData actual, ManifestBuildData expected)
        {
            actual.AzureDevOpsAccount.Should().Be(expected.AzureDevOpsAccount);
            actual.AzureDevOpsBranch.Should().Be(expected.AzureDevOpsBranch);
            actual.AzureDevOpsBuildDefinitionId.Should().Be(expected.AzureDevOpsBuildDefinitionId);
            actual.AzureDevOpsBuildId.Should().Be(expected.AzureDevOpsBuildId);
            actual.AzureDevOpsBuildNumber.Should().Be(expected.AzureDevOpsBuildNumber);
            actual.AzureDevOpsProject.Should().Be(expected.AzureDevOpsProject);
            actual.AzureDevOpsRepository.Should().Be(expected.AzureDevOpsRepository);
            actual.InitialAssetsLocation.Should().Be(expected.InitialAssetsLocation);
            actual.PublishingVersion.Should().Be(expected.PublishingVersion);
        }
    }
}
