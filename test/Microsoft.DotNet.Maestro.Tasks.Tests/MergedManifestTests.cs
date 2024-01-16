// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks.Tests;

internal class MergedManifestTests
{
    [TestFixture]
    public class MergeBuildManifestsTests
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
        public const string LocationString = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts";
        public const string PackageId = "PackageId";
        public const string BlobId = "BlobId";
        public const string version = "version";

        readonly Package package1 = new()
        {
            Id = PackageId,
            Version = version,
            NonShipping = true,
        };

        readonly Package package2 = new()
        {
            Id = "123",
            Version = "version1",
            NonShipping = false
        };

        readonly Blob blob1 = new()
        {
            Id = BlobId,
            Category = "None",
            NonShipping = true
        };

        readonly Blob blob2 = new()
        {
            Id = "1",
            Category = "Other",
            NonShipping = false
        };

        Manifest Manifest1() => new()
        {
            AzureDevOpsBranch = AzureDevOpsBranch1,
            AzureDevOpsAccount = AzureDevOpsAccount1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildNumber = AzureDevOpsBuildNumber1,
            AzureDevOpsProject = AzureDevOpsProject1,
            AzureDevOpsRepository = AzureDevOpsRepository1,
            Packages = [],
            Blobs = []
        };

        Manifest Manifest2() => new()
        {
            AzureDevOpsAccount = "devdiv",
            AzureDevOpsBranch = "refs/heads/test",
            AzureDevOpsBuildDefinitionId = 9,
            AzureDevOpsBuildId = 123345,
            AzureDevOpsBuildNumber = "20201016.8",
            AzureDevOpsProject = "internal",
            AzureDevOpsRepository = "https://dnceng@dev.azure.com/dnceng/internal/_git/dotnet-arcade",
            Packages = [],
            Blobs = []
        };

        Manifest Manifest3() => new()
        {
            AzureDevOpsBranch = AzureDevOpsBranch1,
            AzureDevOpsAccount = AzureDevOpsAccount1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildNumber = AzureDevOpsBuildNumber1,
            AzureDevOpsProject = AzureDevOpsProject1,
            AzureDevOpsRepository = AzureDevOpsRepository1,
            Packages = [],
            Blobs = []
        };

        Manifest Manifest4() => new()
        {
            AzureDevOpsBranch = AzureDevOpsBranch1,
            AzureDevOpsAccount = AzureDevOpsAccount1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildNumber = AzureDevOpsBuildNumber1,
            AzureDevOpsProject = AzureDevOpsProject1,
            AzureDevOpsRepository = AzureDevOpsRepository1,
            Packages = [],
            Blobs = []
        };

        Manifest OutputManifest() => new()
        {
            AzureDevOpsBranch = AzureDevOpsBranch1,
            AzureDevOpsAccount = AzureDevOpsAccount1,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
            AzureDevOpsBuildId = AzureDevOpsBuildId1,
            AzureDevOpsBuildNumber = AzureDevOpsBuildNumber1,
            AzureDevOpsProject = AzureDevOpsProject1,
            AzureDevOpsRepository = AzureDevOpsRepository1,
            Packages = [],
            Blobs = []
        };

        [SetUp]
        public void SetupMergeBuildManifestsTests()
        {
            var buildEngine = new Mocks.MockBuildEngine();
            pushMetadata = new PushMetadataToBuildAssetRegistry()
            {
                BuildEngine = buildEngine,
            };
        }

        [Test]
        public void ErrorWhenManifestDoesNotMatch()
        {
            List<Manifest> manifests = [Manifest1(), Manifest2()];

            Action act = () => pushMetadata.MergeManifests(manifests);
            act.Should().Throw<Exception>().WithMessage("Can't merge if one or more manifests have different branch, build number, commit, or repository values.");
        }

        [Test]
        public void TwoCompatibleManifest()
        {
            var manifest1 = Manifest1();
            var manifest3 = Manifest3();
            manifest1.Packages = [package1];
            manifest3.Packages = [package2];
            manifest1.Blobs = [blob1];
            manifest3.Blobs = [blob2];

            var outputManifest = OutputManifest();

            outputManifest.Packages = [package1, package2];
            outputManifest.Blobs = [blob1, blob2];
            List<Manifest> manifests = [manifest1, manifest3];
            Manifest expectedManifest =  pushMetadata.MergeManifests(manifests);
            expectedManifest.Should().BeEquivalentTo(outputManifest);
        }

        [Test]
        public void ThreeCompatibleBuildData()
        {
            var manifest1 = Manifest1();
            var manifest3 = Manifest3();
            var manifest4 = Manifest4();

            manifest1.Packages.Add(package1);
            manifest3.Packages.Add(package2);
            manifest4.Blobs.Add(blob1);
            manifest3.Blobs.Add(blob2);

            var outputManifest = OutputManifest();

            outputManifest.Packages.Add(package1);
            outputManifest.Packages.Add(package2);
            outputManifest.Blobs.Add(blob1);
            outputManifest.Blobs.Add(blob2);

            List<Manifest> manifests = [manifest1, manifest3, manifest4];
            Manifest expectedManifest = pushMetadata.MergeManifests(manifests);
            expectedManifest.Should().BeEquivalentTo(outputManifest);
        }

        [Test]
        public void ManifestWithPartiallyEmptyAssets()
        {
            List<Manifest> manifests = [Manifest1(), Manifest3()];
            Manifest expectedManifest = pushMetadata.MergeManifests(manifests);
            expectedManifest.Should().BeEquivalentTo(OutputManifest());
        }

        [TestCase("https://github.com/dotnet/trusted-packages", "trusted/packages")]
        [TestCase("https://devdiv.visualstudio.com/DevDiv/_git/MSTest", "mstest")]
        [TestCase("https://dev.azure.com/devdiv/DevDiv/_git/MSTest", "mstest")]
        [TestCase("https://github.com/microsoft/vstest", "vstest")]
        [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-trusted-packages", "dotnet/trusted-packages")]
        [TestCase("https://dev.azure.com/devdiv/DevDiv/_git/dotnet-trusted-packages-trusted", "dotnet/trusted-packages")]
        [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-images-trusted", "dotnet/images-trusted")]
        [TestCase("https://dev.azure.com/devdiv/DevDiv/_git/dotnet-images-trusted-trusted", "dotnet/images-trusted")]
        [TestCase("https://dev.azure.com/devdiv/DevDiv/_git/NuGet-NuGet.Client-Trusted", "nuget/nuget.client")]
        public void GetGithubRepoNameTest(string azdoRepoUrl, string expectedRepo)
        {
            string actualRepo = PushMetadataToBuildAssetRegistry.GetGithubRepoName(azdoRepoUrl);
            actualRepo.Should().Be(expectedRepo);
        }

        [Test]
        public void MergingShouldNotAllowDuplicatedPackages()
        {
            var manifest1 = Manifest1();
            var manifest3 = Manifest3();

            manifest1.Packages = [package1];
            manifest3.Packages = [package1];

            List<Manifest> manifests = [manifest1, manifest3];
            Action act = () => pushMetadata.MergeManifests(manifests);
            act.Should().Throw<Exception>().WithMessage("Duplicate package entries are not allowed for publishing to BAR, as this can cause race conditions and unexpected behavior");
            pushMetadata.Log.HasLoggedErrors.Should().BeTrue();
        }

        [Test]
        public void MergingShouldNotAllowDuplicatedBlobs()
        {
            var manifest1 = Manifest1();
            var manifest3 = Manifest3();

            manifest1.Blobs = [blob1];
            manifest3.Blobs = [blob1];

            List<Manifest> manifests = [manifest1, manifest3];
            Action act = () => pushMetadata.MergeManifests(manifests);
            act.Should().Throw<Exception>().WithMessage("Duplicate blob entries are not allowed for publishing to BAR, as this can cause race conditions and unexpected behavior");
            pushMetadata.Log.HasLoggedErrors.Should().BeTrue();
        }
    }
}
