using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    [TestFixture]
    public class MergeSigningInfo
    {
        private PushMetadataToBuildAssetRegistry pushMetadata;

        public MergeSigningInfo()
        {
            pushMetadata = new PushMetadataToBuildAssetRegistry();
        }

        [Test]
        public void GivenCompatibleSigningInfo()
        {
            SigningInformation actualMerged = pushMetadata.MergeSigningInfo(SharedObjects.ExpectedSigningInfo.Concat(SharedObjects.ExpectedSigningInfo2).ToList());
            SharedObjects.CompareSigningInformation(actualMerged, SharedObjects.ExpectedMergedSigningInfo);
        }

        [Test]
        public void GivenDuplicateSigningInfo()
        {
            SigningInformation actualMerged = pushMetadata.MergeSigningInfo(SharedObjects.ExpectedSigningInfo.Concat(SharedObjects.ExpectedSigningInfo).ToList());
            SharedObjects.CompareSigningInformation(actualMerged, SharedObjects.ExpectedSigningInfo.First());
        }

        [Test]
        public void GivenTwoPartialSigningInfoMetadatas()
        {
            Action act = () => pushMetadata.MergeSigningInfo(new List<SigningInformation> { SharedObjects.PartialSigningInfo1, SharedObjects.PartialSigningInfo2 });
            act.Should().Throw<Exception>().WithMessage("Can't merge if one or more manifests have different build id, collection URI or project.");    
        }

        [Test]
        public void GivenTwoPartialSigningInfosWithEmptySections()
        {
            SigningInformation actualMerged = pushMetadata.MergeSigningInfo(new List<SigningInformation> { SharedObjects.PartialSigningInfo3, SharedObjects.PartialSigningInfo4 });
            SharedObjects.CompareSigningInformation(actualMerged, SharedObjects.MergedPartialSigningInfos);
        }

        [Test]
        public void GivenIncompatibleSigningInfos()
        {
            Action act = () => pushMetadata.MergeSigningInfo(new List<SigningInformation> { SharedObjects.PartialSigningInfo3, SharedObjects.IncompatibleSigningInfo });
            act.Should().Throw<Exception>().WithMessage("Can't merge if one or more manifests have different build id, collection URI or project.");
        }

        [Test]
        public void GivenNullSigningInfoList()
        {
            Action act = () => pushMetadata.MergeSigningInfo(null);
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void GivenEmptySigningInfoList()
        {
            pushMetadata.MergeSigningInfo(new List<SigningInformation>());
        }
    }
}
