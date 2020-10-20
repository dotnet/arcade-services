using System.Collections.Generic;
using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.DotNet.Maestro.Client.Models;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    public class SharedObjects
    {
        public const string Commit = "e7a79ce64f0703c231e6da88b5279dd0bf681b3d";
        public const string AzureDevOpsAccount1 = "dnceng";
        public const int AzureDevOpsBuildDefinitionId1 = 6;
        public const int AzureDevOpsBuildId1 = 856354;
        public const string AzureDevOpsBranch1 = "refs/heads/master";
        public const string AzureDevOpsBuildNumber1 = "20201016.5";
        public const string AzureDevOpsProject1 = "internal";
        public const string AzureDevOpsRepository1 = "https://dnceng@dev.azure.com/dnceng/internal/_git/dotnet-arcade";

        #region SigningInformation
        // TODO: Why is this a list? Shouldn't things just get merged into the object? Or is this intended to be before merging & I'm mixed up?
        public static readonly List<SigningInformation> ExpectedSigningInfo = new List<SigningInformation>()
            {
                new SigningInformation()
                {
                    AzureDevOpsBuildId = AzureDevOpsBuildId1.ToString(),
                    AzureDevOpsCollectionUri = "https://dev.azure.com/dnceng/",
                    AzureDevOpsProject = AzureDevOpsProject1,
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

        public static readonly SigningInformation PartialSigningInfo1 = new SigningInformation()
        {
            AzureDevOpsBuildId = AzureDevOpsBuildId1.ToString(),
            AzureDevOpsCollectionUri = "https://dev.azure.com/dnceng/"
        };

        public static readonly SigningInformation PartialSigningInfo2 = new SigningInformation()
        {
            AzureDevOpsCollectionUri = "https://dev.azure.com/dnceng/",
            AzureDevOpsProject = AzureDevOpsProject1
        };

        public static readonly SigningInformation PartialSigningInfo3 = new SigningInformation()
        {
            AzureDevOpsBuildId = AzureDevOpsBuildId1.ToString(),
            AzureDevOpsCollectionUri = "https://dev.azure.com/dnceng/",
            AzureDevOpsProject = AzureDevOpsProject1,
            CertificatesSignInfo = new List<CertificatesSignInfo>()
                    {
                        new CertificatesSignInfo()
                        {
                            DualSigningAllowed = true,
                            Include = "ThisIsACert"
                        }
                    },

            FileExtensionSignInfos = new List<FileExtensionSignInfo>(),
            FileSignInfos = new List<FileSignInfo>(),
            StrongNameSignInfos = new List<StrongNameSignInfo>(),
            ItemsToSign = new List<ItemsToSign>()
        };

        public static readonly SigningInformation PartialSigningInfo4 = new SigningInformation()
        {
            AzureDevOpsBuildId = AzureDevOpsBuildId1.ToString(),
            AzureDevOpsCollectionUri = "https://dev.azure.com/dnceng/",
            AzureDevOpsProject = AzureDevOpsProject1,
            CertificatesSignInfo = new List<CertificatesSignInfo>(),
            FileExtensionSignInfos = new List<FileExtensionSignInfo>()
                    {
                        new FileExtensionSignInfo()
                        {
                            CertificateName = "ThisIsACert",
                            Include = ".dll"
                        }
                    },
            FileSignInfos = new List<FileSignInfo>(),
            StrongNameSignInfos = new List<StrongNameSignInfo>(),
            ItemsToSign = new List<ItemsToSign>()
        };

        public static readonly SigningInformation MergedPartialMetadataSigningInfos = new SigningInformation()
        {
            AzureDevOpsBuildId = AzureDevOpsBuildId1.ToString(),
            AzureDevOpsCollectionUri = "https://dev.azure.com/dnceng/",
            AzureDevOpsProject = AzureDevOpsProject1
        };

        public static readonly SigningInformation MergedPartialSigningInfos = new SigningInformation()
        {
            AzureDevOpsBuildId = AzureDevOpsBuildId1.ToString(),
            AzureDevOpsCollectionUri = "https://dev.azure.com/dnceng/",
            AzureDevOpsProject = AzureDevOpsProject1,
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
            FileSignInfos = new List<FileSignInfo>(),
            StrongNameSignInfos = new List<StrongNameSignInfo>(),
            ItemsToSign = new List<ItemsToSign>()
        };

        public static readonly SigningInformation IncompatibleSigningInfo = new SigningInformation()
        {
            AzureDevOpsBuildId = AzureDevOpsBuildId1.ToString(),
            AzureDevOpsCollectionUri = "https://dev.azure.com/newProject",
            AzureDevOpsProject = AzureDevOpsProject1,
        };

        public static readonly List<SigningInformation> ExpectedSigningInfo2 = new List<SigningInformation>()
            {
                new SigningInformation()
                {
                    AzureDevOpsBuildId = AzureDevOpsBuildId1.ToString(),
                    AzureDevOpsCollectionUri = "https://dev.azure.com/dnceng/",
                    AzureDevOpsProject = AzureDevOpsProject1,
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

        public static readonly SigningInformation ExpectedMergedSigningInfo =
                new SigningInformation()
                {
                    AzureDevOpsBuildId = AzureDevOpsBuildId1.ToString(),
                    AzureDevOpsCollectionUri = "https://dev.azure.com/dnceng/",
                    AzureDevOpsProject = AzureDevOpsProject1,
                    CertificatesSignInfo = new List<CertificatesSignInfo>()
                    {
                        new CertificatesSignInfo()
                        {
                            DualSigningAllowed = true,
                            Include = "ThisIsACert"
                        },
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
                            CertificateName = "ThisIsACert",
                            Include = ".dll"
                        },
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
                            CertificateName = "ThisIsACert",
                             Include = "ALibrary.dll"
                        },
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
                            CertificateName = "ThisIsACert",
                            Include = "IncludeMe",
                            PublicKeyToken = "123456789abcde12"
                        },
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

        #region AsssetData
        public static readonly IImmutableList<AssetData> ExpectedAssets1 =
            ImmutableList.Create(
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

        public static readonly IImmutableList<AssetData> ExpectedAssets2 =
             ImmutableList.Create(
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

        public static readonly IImmutableList<AssetData> ExpectedAssets3 =
             ImmutableList.Create(
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                { Location = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts" }),
                    Name = "Microsoft.DotNet.Arcade.Sdk",
                    Version = "6.0.0-beta.20516.5"
                },
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                { Location = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts" }),
                    Name = "assets/symbols/Microsoft.DotNet.Arcade.Sdk.6.0.0-beta.20516.5.symbols.nupkg",
                    Version = "6.0.0-beta.20516.5"
                });

        public static readonly IImmutableList<AssetData> ExpectedAssets1And2 =
            ImmutableList.Create(
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
                },
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

        public static readonly IImmutableList<AssetData> ThreeExpectedAssets =
            ImmutableList.Create(
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
                },
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
                },
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                { Location = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts" }),
                    Name = "Microsoft.DotNet.Arcade.Sdk",
                    Version = "6.0.0-beta.20516.5"
                },
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                { Location = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts" }),
                    Name = "assets/symbols/Microsoft.DotNet.Arcade.Sdk.6.0.0-beta.20516.5.symbols.nupkg",
                    Version = "6.0.0-beta.20516.5"
                });

        public static readonly IImmutableList<AssetData> NoBlobExpectedAssets =
            ImmutableList.Create(
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                { Location = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts" }),
                    Name = "Microsoft.Cci.Extensions",
                    Version = "6.0.0-beta.20516.5"
                });

        public static readonly IImmutableList<AssetData> NoPackageExpectedAssets =
                        ImmutableList.Create(
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                { Location = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts" }),
                    Name = "assets/manifests/dotnet-arcade/6.0.0-beta.20516.5/MergedManifest.xml",
                    Version = "6.0.0-beta.20516.5"
                });

        public static readonly IImmutableList<AssetData> ExpectedPartialAssets =
            ImmutableList.Create(
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

        public static readonly IImmutableList<AssetData> UnversionedPackageExpectedAssets =
            ImmutableList.Create(
                new AssetData(true)
                {
                    Locations = ImmutableList.Create(
                new AssetLocationData(LocationType.Container)
                { Location = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts" }),
                    Name = "Microsoft.Cci.Extensions"
                });
        #endregion

        #region Manifests
        public static readonly Manifest ExpectedManifest = new Manifest()
        {
            AzureDevOpsAccount = AzureDevOpsAccount1,
            AzureDevOpsBranch = AzureDevOpsBranch1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildNumber = AzureDevOpsBuildNumber1,
            AzureDevOpsProject = AzureDevOpsProject1,
            AzureDevOpsRepository = AzureDevOpsRepository1,
            InitialAssetsLocation = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts",
            PublishingVersion = 3
        };

        public static readonly ManifestBuildData ExpectedManifestBuildData = new ManifestBuildData(ExpectedManifest);

        public static readonly List<BuildData> ExpectedManifestMetadata = new List<BuildData>()
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

        public static readonly List<BuildData> ExpectedManifestMetadata2 = new List<BuildData>()
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

        public static readonly List<BuildData> ExpectedNoBlobManifestMetadata = new List<BuildData>()
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

        public static readonly List<BuildData> ExpectedNoPackagesManifestMetadata = new List<BuildData>()
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

        public static readonly List<BuildData> ExpectedUnversionedPackagedManifestMetadata = new List<BuildData>()
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

        #region BuildData
        public static readonly List<BuildData> ExpectedBuildDataList1 = new List<BuildData>()
            {
                new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "dotnet-arcade",
                    Assets = ExpectedAssets1,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                },
                new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "dotnet-arcade",
                    Assets = ExpectedAssets2,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                }
            };

        public static readonly List<BuildData> ExpectedThreeBuildDataList = new List<BuildData>()
            {
                new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "dotnet-arcade",
                    Assets = ExpectedAssets1,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                },
                new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "dotnet-arcade",
                    Assets = ExpectedAssets2,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                },
                new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "dotnet-arcade",
                    Assets = ExpectedAssets3,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                }
            };

        public static readonly List<BuildData> BuildDataWithoutAssetsList = new List<BuildData>()
            {
                new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "dotnet-arcade",
                    Assets = ImmutableList<AssetData>.Empty,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                },
                new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "dotnet-arcade",
                    Assets = null,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                }
            };

        public static readonly BuildData ExpectedMergedBuildDataWithoutAssets =
            new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
            {
                GitHubBranch = AzureDevOpsBranch1,
                GitHubRepository = "https://github.com/dotnet/arcade",
                Assets = ImmutableList<AssetData>.Empty,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
            };

        public static readonly BuildData ExpectedMergedBuildData =
            new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
            {
                GitHubBranch = AzureDevOpsBranch1,
                GitHubRepository = "https://github.com/dotnet/arcade",
                Assets = ExpectedAssets1And2,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
            };

        public static readonly BuildData ExpectedThreeAssetsBuildData =
            new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
            {
                GitHubBranch = AzureDevOpsBranch1,
                GitHubRepository = "https://github.com/dotnet/arcade",
                Assets = ThreeExpectedAssets,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
            };

        public static readonly BuildData ExpectedPartialAssetsBuildData =
            new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
            {
                GitHubBranch = AzureDevOpsBranch1,
                GitHubRepository = "https://github.com/dotnet/arcade",
                Assets = ExpectedPartialAssets,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
            };

        public static readonly List<BuildData> ExpectedBuildDataIncompatibleList = new List<BuildData>()
            {
                new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "https://github.com/dotnet/arcade",
                    Assets = ExpectedAssets1,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                },
                new BuildData("1234567", "newAccount", "newProject", "12345", "repositoryBranch", "azureDevOpsBranch", false, false)
                {
                    GitHubBranch = AzureDevOpsBranch1,
                    GitHubRepository = "dotnet-arcade",
                    Assets = ExpectedAssets2,
                    AzureDevOpsBuildId = AzureDevOpsBuildId1,
                    AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
                }
            };

        public static readonly List<BuildData> ExpectedDuplicatedAssetsBuildData = new List<BuildData>()
        {
            new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
            {
                GitHubBranch = AzureDevOpsBranch1,
                GitHubRepository = "https://github.com/dotnet/arcade",
                Assets = ExpectedAssets1,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
            },
            new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
            {
                GitHubBranch = AzureDevOpsBranch1,
                GitHubRepository = "https://github.com/dotnet/arcade",
                Assets = ExpectedAssets1,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
            }
        };
        #endregion

        public static void CompareManifestBuildData(ManifestBuildData actual, ManifestBuildData expected)
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

        public static void CompareSigningInformation(SigningInformation actualSigningInfo, SigningInformation expectedSigningInfo)
        {
            actualSigningInfo.CertificatesSignInfo.Should().BeEquivalentTo(expectedSigningInfo.CertificatesSignInfo);
            actualSigningInfo.FileExtensionSignInfos.Should().BeEquivalentTo(expectedSigningInfo.FileExtensionSignInfos);
            actualSigningInfo.FileSignInfos.Should().BeEquivalentTo(expectedSigningInfo.FileSignInfos);
            actualSigningInfo.ItemsToSign.Should().BeEquivalentTo(expectedSigningInfo.ItemsToSign);
        }

        public static void CompareBuildDataInformation(BuildData actualBuildData, BuildData expectedBuildData)
        {
            actualBuildData.Assets.Should().BeEquivalentTo(expectedBuildData.Assets);
            actualBuildData.AzureDevOpsAccount.Should().Be(expectedBuildData.AzureDevOpsAccount);
            actualBuildData.AzureDevOpsBranch.Should().Be(expectedBuildData.AzureDevOpsBranch);
            actualBuildData.AzureDevOpsBuildDefinitionId.Should().Be(expectedBuildData.AzureDevOpsBuildDefinitionId);
            actualBuildData.AzureDevOpsBuildId.Should().Be(expectedBuildData.AzureDevOpsBuildId);
            actualBuildData.AzureDevOpsBuildNumber.Should().Be(expectedBuildData.AzureDevOpsBuildNumber);
            actualBuildData.AzureDevOpsProject.Should().Be(expectedBuildData.AzureDevOpsProject);
            actualBuildData.AzureDevOpsRepository.Should().Be(expectedBuildData.AzureDevOpsRepository);
            actualBuildData.Commit.Should().Be(expectedBuildData.Commit);
            actualBuildData.Dependencies.Should().BeEquivalentTo(expectedBuildData.Dependencies);
            actualBuildData.GitHubBranch.Should().Be(expectedBuildData.GitHubBranch);
            actualBuildData.GitHubRepository.Should().Be(expectedBuildData.GitHubRepository);
            actualBuildData.Incoherencies.Should().BeEquivalentTo(expectedBuildData.Incoherencies);
            actualBuildData.IsValid.Should().Be(actualBuildData.IsValid);
            actualBuildData.Released.Should().Be(expectedBuildData.Released);
            actualBuildData.Stable.Should().Be(expectedBuildData.Stable);
        }
    }
}
