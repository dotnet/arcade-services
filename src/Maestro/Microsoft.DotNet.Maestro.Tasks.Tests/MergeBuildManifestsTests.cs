// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    [TestFixture]
    public class MergeBuildManifestsTests
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

        internal static readonly AssetData PackageAsset3 =
            new AssetData(true)
            {
                Locations = ImmutableList.Create(
                    new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
                Name = "Microsoft.DotNet.Arcade.Sdk",
                Version = "6.0.0-beta.20516.5"
            };

        internal static readonly AssetData BlobAsset3 =
            new AssetData(true)
            {
                Locations = ImmutableList.Create(
                    new AssetLocationData(LocationType.Container)
                    { Location = LocationString }),
                Name = "assets/symbols/Microsoft.DotNet.Arcade.Sdk.6.0.0-beta.20516.5.symbols.nupkg",
                Version = "6.0.0-beta.20516.5"
            };

        public static readonly IImmutableList<AssetData> ExpectedAssets1 =
            ImmutableList.Create(PackageAsset1, BlobAsset1);

        public static readonly IImmutableList<AssetData> ExpectedAssets2 =
             ImmutableList.Create(PackageAsset2, BlobAsset2);

        public static readonly IImmutableList<AssetData> ExpectedAssets3 =
             ImmutableList.Create(PackageAsset3, BlobAsset3);

        public static readonly IImmutableList<AssetData> ExpectedAssets1And2 =
            ImmutableList.Create(PackageAsset1, BlobAsset1, PackageAsset2, BlobAsset2);

        public static readonly IImmutableList<AssetData> ThreeExpectedAssets =
            ImmutableList.Create(PackageAsset1, BlobAsset1, PackageAsset2, BlobAsset2, PackageAsset3, BlobAsset3);

        public static readonly IImmutableList<AssetData> NoBlobExpectedAssets =
            ImmutableList.Create(PackageAsset1);

        public static readonly IImmutableList<AssetData> NoPackageExpectedAssets =
            ImmutableList.Create(BlobAsset1);

        public static readonly IImmutableList<AssetData> ExpectedPartialAssets =
            ImmutableList.Create(PackageAsset1, BlobAsset1);

        private static readonly BuildData Asset1BuildData =
            new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
            {
                GitHubBranch = AzureDevOpsBranch1,
                GitHubRepository = "dotnet-arcade",
                Assets = ExpectedAssets1,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
            };

        // Other BuildData isn't modified so it can be shared between tests
        private static readonly BuildData Asset2BuildData =
            new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
            {
                GitHubBranch = AzureDevOpsBranch1,
                GitHubRepository = "dotnet-arcade",
                Assets = ExpectedAssets2,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
            };

        private static readonly BuildData Asset3BuildData =
            new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
            {
                GitHubBranch = AzureDevOpsBranch1,
                GitHubRepository = "dotnet-arcade",
                Assets = ExpectedAssets3,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
            };

        private static readonly List<BuildData> twoBuildDataList =
            new List<BuildData>() { Asset1BuildData, Asset2BuildData };

        private static readonly List<BuildData> threeBuildDataList =
            new List<BuildData>() { Asset1BuildData, Asset2BuildData, Asset3BuildData };

        private static readonly BuildData ExpectedMergedBuildData =
            new BuildData(Commit, AzureDevOpsAccount1, AzureDevOpsProject1, AzureDevOpsBuildNumber1, AzureDevOpsRepository1, AzureDevOpsBranch1, false, false)
            {
                GitHubBranch = AzureDevOpsBranch1,
                GitHubRepository = "https://github.com/dotnet/arcade",
                Assets = ExpectedAssets1And2,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1
            };

        private static readonly BuildData ExpectedThreeAssetsBuildData =
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

        [SetUp]
        public void SetupMergeBuildManifestsTests()
        {
            pushMetadata = new PushMetadataToBuildAssetRegistry();
        }

        [Test]
        public void TwoCompatibleBuildData()
        {
            BuildData mergedData = pushMetadata.MergeBuildManifests(twoBuildDataList);
            mergedData.Should().BeEquivalentTo(ExpectedMergedBuildData);
        }

        [Test]
        public void ThreeCompatibleBuildData()
        {
            BuildData mergedData = pushMetadata.MergeBuildManifests(threeBuildDataList);
            mergedData.Should().BeEquivalentTo(ExpectedThreeAssetsBuildData);
        }

        [Test]
        public void BuildDataWithNullAndEmptyAssets()
        {
            Action act = () => pushMetadata.MergeBuildManifests(BuildDataWithoutAssetsList);
            act.Should().Throw<ArgumentNullException>().WithMessage("Value cannot be null. (Parameter 'items')");
        }

        [Test]
        public void BuildDataWithPartiallyEmptyAssets()
        {
            BuildData mergedData = pushMetadata.MergeBuildManifests(ExpectedNoBlobManifestMetadata.Concat(ExpectedNoPackagesManifestMetadata).ToList());
            mergedData.Should().BeEquivalentTo(ExpectedPartialAssetsBuildData);
        }

        [Test]
        public void IncompatibleBuildData()
        {
            Action act = () => pushMetadata.MergeBuildManifests(ExpectedBuildDataIncompatibleList);
            act.Should().Throw<Exception>().WithMessage("Can't merge if one or more manifests have different branch, build number, commit, or repository values.");
        }

        [Test]
        public void CompatibleBuildDataWithDuplicatedAssets()
        {
            Action act = () => pushMetadata.MergeBuildManifests(ExpectedBuildDataIncompatibleList);
            act.Should().Throw<Exception>().WithMessage("Can't merge if one or more manifests have different branch, build number, commit, or repository values.");
        }
    }
}
