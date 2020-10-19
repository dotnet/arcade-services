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
            data.Item1.Should().BeEquivalentTo(SharedObjects.ExpectedManifestMetadata);
            data.Item2.Should().BeEquivalentTo(SharedObjects.ExpectedSigningInfo);
            SharedObjects.CompareManifestBuildData(data.Item3, SharedObjects.ExpectedManifestBuildData);
        }

        [Test]
        public void ParseTwoManifests()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();

            var data = pushMetadata.GetBuildManifestsMetadata(@"C:\Users\mquinn\Downloads\Manifests\TwoManifests", new CancellationToken());
            data.Item1.Should().BeEquivalentTo(SharedObjects.ExpectedManifestMetadata.Concat(SharedObjects.ExpectedManifestMetadata2));
            data.Item2.Should().BeEquivalentTo(SharedObjects.ExpectedSigningInfo.Concat(SharedObjects.ExpectedSigningInfo2));
            SharedObjects.CompareManifestBuildData(data.Item3, SharedObjects.ExpectedManifestBuildData);
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
    }
}
