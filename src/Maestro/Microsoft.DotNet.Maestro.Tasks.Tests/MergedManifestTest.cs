// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    internal class MergedManifestTest
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
            public const string Id = "Id";
            public const string version = "version";

            Package package1 = new Package()
            {
                Id = Id,
                Version = version,
                NonShipping = true,
            };

            Package package2 = new Package()
            {
                Id = "123",
                Version = "version1",
                NonShipping = false
            };

            private Blob blob1 = new Blob()
            {
                Id = Id,
                Category = "None",
                NonShipping = true
            };

            private Blob blob2 = new Blob()
            {
                Id = "1",
                Category = "Other",
                NonShipping = false
            };

            Manifest manifest1 = new Manifest() {
                AzureDevOpsBranch = AzureDevOpsBranch1,
                AzureDevOpsAccount = AzureDevOpsAccount1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildNumber = AzureDevOpsBuildNumber1,
                AzureDevOpsProject = AzureDevOpsProject1,
                AzureDevOpsRepository = AzureDevOpsRepository1,
                Packages = new List<Package>(),
                Blobs = new List<Blob>()
            };

            Manifest manifest2 = new Manifest() {
                AzureDevOpsAccount = "devdiv",
                AzureDevOpsBranch = "refs/heads/test",
                AzureDevOpsBuildDefinitionId = 9,
                AzureDevOpsBuildId = 123345,
                AzureDevOpsBuildNumber = "20201016.8",
                AzureDevOpsProject = "internal",
                AzureDevOpsRepository = "https://dnceng@dev.azure.com/dnceng/internal/_git/dotnet-arcade",
                Packages = new List<Package>(),
                Blobs = new List<Blob>()
            };

            Manifest manifest3 = new Manifest()
            {
                AzureDevOpsBranch = AzureDevOpsBranch1,
                AzureDevOpsAccount = AzureDevOpsAccount1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildNumber = AzureDevOpsBuildNumber1,
                AzureDevOpsProject = AzureDevOpsProject1,
                AzureDevOpsRepository = AzureDevOpsRepository1,
                Packages = new List<Package>(),
                Blobs = new List<Blob>()
            };

            Manifest manifest4 = new Manifest()
            {
                AzureDevOpsBranch = AzureDevOpsBranch1,
                AzureDevOpsAccount = AzureDevOpsAccount1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildNumber = AzureDevOpsBuildNumber1,
                AzureDevOpsProject = AzureDevOpsProject1,
                AzureDevOpsRepository = AzureDevOpsRepository1,
                Packages = new List<Package>(),
                Blobs = new List<Blob>()
            };

            Manifest manifest = new Manifest()
            {
                AzureDevOpsBranch = AzureDevOpsBranch1,
                AzureDevOpsAccount = AzureDevOpsAccount1,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId1,
                AzureDevOpsBuildId = AzureDevOpsBuildId1,
                AzureDevOpsBuildNumber = AzureDevOpsBuildNumber1,
                AzureDevOpsProject = AzureDevOpsProject1,
                AzureDevOpsRepository = AzureDevOpsRepository1,
                Packages = new List<Package>(),
                Blobs = new List<Blob>()
            };

            [SetUp]
            public void SetupMergeBuildManifestsTests()
            {
                pushMetadata = new PushMetadataToBuildAssetRegistry();
            }

            [Test]
            public void ErrorWhenManifestDoesNotMatch()
            {
                List<Manifest> manifests = new List<Manifest>() { manifest1, manifest2 };

                Action act = () => pushMetadata.CheckIfManifestCanBeMerged(manifests);
                act.Should().Throw<Exception>().WithMessage("Can't merge if one or more manifests have different branch, build number, commit, or repository values.");

            }

            [Test]
            public void TwoCompatibleManifest()
            {
                
                manifest1.Packages = new List<Package>() { package1 };
                manifest3.Packages = new List<Package>() { package2 };
                manifest1.Blobs = new List<Blob>() { blob1 };
                manifest3.Blobs = new List<Blob>() { blob2 };

                manifest.Packages = new List<Package>() { package1, package2 };
                manifest.Blobs = new List<Blob>() { blob1, blob2 };
                List<Manifest> manifests = new List<Manifest>() { manifest1, manifest3 };
                Manifest expectedManifest =  pushMetadata.CheckIfManifestCanBeMerged(manifests);
                expectedManifest.Should().BeEquivalentTo(manifest);
            }

            [Test]
            public void ThreeCompatibleBuildData()
            {

                manifest1.Packages.Add(package1);
                manifest3.Packages.Add(package2);
                manifest4.Blobs.Add(blob1);
                manifest3.Blobs.Add(blob2);

                manifest.Packages.Add(package1);
                manifest.Packages.Add(package2);
                manifest.Blobs.Add(blob1);
                manifest.Blobs.Add(blob2);
                List<Manifest> manifests = new List<Manifest>() { manifest1, manifest3, manifest4 };
                Manifest expectedManifest = pushMetadata.CheckIfManifestCanBeMerged(manifests);
                expectedManifest.Should().BeEquivalentTo(manifest);
            }

            [Test]
            public void ManifestWithPartiallyEmptyAssets()
            {
                List<Manifest> manifests = new List<Manifest>() { manifest1, manifest3 };
                Manifest expectedManifest = pushMetadata.CheckIfManifestCanBeMerged(manifests);
                expectedManifest.Should().BeEquivalentTo(manifest);

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
                string actualRepo = pushMetadata.GetGithubRepoName(azdoRepoUrl);
                Assert.AreEqual(expectedRepo, actualRepo);
            }
        }
    }
}
