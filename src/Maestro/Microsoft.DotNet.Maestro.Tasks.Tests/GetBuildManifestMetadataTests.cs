using System;
using System.Linq;
using System.Threading;
using FluentAssertions;
using NUnit.Framework;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    [TestFixture]
    public class GetBuildManifestMetadataTests
    {
        private PushMetadataToBuildAssetRegistry pushMetadata;
        private string testManifestFolderPath = @".\TestManifests\"; 

        public GetBuildManifestMetadataTests()
        {
            pushMetadata = new PushMetadataToBuildAssetRegistry();
        }
        [Test]
        public void EmptyManifestFolderPath()
        {
            Action act = () => pushMetadata.GetBuildManifestsMetadata("", new CancellationToken());
            act.Should().Throw<ArgumentException>().WithMessage("The path is empty. (Parameter 'path')");
        }

        [Test]
        public void ParseBasicManifest()
        {
            var data = pushMetadata.GetBuildManifestsMetadata(testManifestFolderPath + "OneManifest", new CancellationToken());
            data.Item1.Should().BeEquivalentTo(SharedObjects.ExpectedManifestMetadata);
            data.Item2.Should().BeEquivalentTo(SharedObjects.ExpectedSigningInfo);
            SharedObjects.CompareManifestBuildData(data.Item3, SharedObjects.ExpectedManifestBuildData);
        }

        [Test]
        public void ParseTwoManifests()
        {
            var data = pushMetadata.GetBuildManifestsMetadata(testManifestFolderPath + "TwoManifests", new CancellationToken());
            data.Item1.Should().BeEquivalentTo(SharedObjects.ExpectedManifestMetadata.Concat(SharedObjects.ExpectedManifestMetadata2));
            data.Item2.Should().BeEquivalentTo(SharedObjects.ExpectedSigningInfo.Concat(SharedObjects.ExpectedSigningInfo2));
            SharedObjects.CompareManifestBuildData(data.Item3, SharedObjects.ExpectedManifestBuildData);
        }

        [Test]
        public void GivenFileThatIsNotAManifest_ExceptionExpected()
        {
            Action act = () => pushMetadata.GetBuildManifestsMetadata(testManifestFolderPath + "NonManifest", new CancellationToken());
            act.Should().Throw<InvalidOperationException>().WithMessage("There is an error in XML document (1, 1).");
        }

        [Test]
        public void GivenBadlyFormattedXml_ExceptionExpected()
        {
            Action act = () => pushMetadata.GetBuildManifestsMetadata(testManifestFolderPath + "BadXml", new CancellationToken());
            act.Should().Throw<InvalidOperationException>().WithMessage("There is an error in XML document (2, 1).");
        }

        [Test]
        public void GivenAnEmptyManifest_ExceptionExpected()
        {
            Action act = () => pushMetadata.GetBuildManifestsMetadata(testManifestFolderPath + "EmptyManifest", new CancellationToken());
            act.Should().Throw<NullReferenceException>();
        }

        [Test]
        public void GivenManifestWithoutPackages()
        {
            var data = pushMetadata.GetBuildManifestsMetadata(testManifestFolderPath + "ManifestWithoutPackages", new CancellationToken());
            data.Item1.Should().BeEquivalentTo(SharedObjects.ExpectedNoPackagesManifestMetadata);
            data.Item2.Should().BeEquivalentTo(SharedObjects.ExpectedSigningInfo);
            SharedObjects.CompareManifestBuildData(data.Item3, SharedObjects.ExpectedManifestBuildData);
        }

        [Test]
        public void GivenManifestWithUnversionedPackage()
        {
            var data = pushMetadata.GetBuildManifestsMetadata(testManifestFolderPath + "UnversionedPackage", new CancellationToken());
            data.Item1.Should().BeEquivalentTo(SharedObjects.ExpectedUnversionedPackagedManifestMetadata);
        }

        [Test]
        public void GivenManifestWithoutBlobs()
        {
            var data = pushMetadata.GetBuildManifestsMetadata(testManifestFolderPath + "ManifestWithoutBlobs", new CancellationToken());
            data.Item1.Should().BeEquivalentTo(SharedObjects.ExpectedNoBlobManifestMetadata);
            data.Item2.Should().BeEquivalentTo(SharedObjects.ExpectedSigningInfo);
            SharedObjects.CompareManifestBuildData(data.Item3, SharedObjects.ExpectedManifestBuildData);
        }

        // TODO: This gets a versionId from another method, which needs to be overwritten with DI. Turning it off while I get DI set up for the project.
        [Test]
        [Ignore("Requires DI that isn't set up for the project yet.")]
        public void GivenUnversionedBlob_ExceptionExpected()
        {
            Action act = () => pushMetadata.GetBuildManifestsMetadata(testManifestFolderPath + "UnversionedBlob", new CancellationToken());
            act.Should().Throw<NullReferenceException>();
        }

        [Test]
        public void GivenBlobWithoutPackages()
        { 
            var data = pushMetadata.GetBuildManifestsMetadata(testManifestFolderPath + "ManifestWithoutPackages", new CancellationToken());
            data.Item1.Should().BeEquivalentTo(SharedObjects.ExpectedNoPackagesManifestMetadata);
            data.Item2.Should().BeEquivalentTo(SharedObjects.ExpectedSigningInfo);
            SharedObjects.CompareManifestBuildData(data.Item3, SharedObjects.ExpectedManifestBuildData);
        }

        [Test]
        public void GivenTwoManifestWithDifferentAttributes_ExceptionExpected()
        {
            Action act = () => pushMetadata.GetBuildManifestsMetadata(testManifestFolderPath + "DifferentAttributes", new CancellationToken());
            act.Should().Throw<Exception>().WithMessage("Attributes should be the same in all manifests.");
        }
    }
}
