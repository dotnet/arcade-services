using System;
using System.Linq;
using FluentAssertions;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    [TestFixture]
    public class MergeBuildManifestsTests
    {
        PushMetadataToBuildAssetRegistry pushMetadata;

        public MergeBuildManifestsTests()
        {
            pushMetadata = new PushMetadataToBuildAssetRegistry();
        }

        [Test]
        public void TwoCompatibleBuildData()
        {
            BuildData mergedData = pushMetadata.MergeBuildManifests(SharedObjects.ExpectedBuildDataList1);
            SharedObjects.CompareBuildDataInformation(mergedData, SharedObjects.ExpectedMergedBuildData);
        }

        [Test]
        public void ThreeCompatibleBuildData()
        {
            BuildData mergedData = pushMetadata.MergeBuildManifests(SharedObjects.ExpectedThreeBuildDataList);
            SharedObjects.CompareBuildDataInformation(mergedData, SharedObjects.ExpectedThreeAssetsBuildData);
        }

        [Test]
        public void BuildDataWithNullAndEmptyAssets()
        {
            Action act = () => pushMetadata.MergeBuildManifests(SharedObjects.BuildDataWithoutAssetsList);
            act.Should().Throw<ArgumentNullException>().WithMessage("Value cannot be null. (Parameter 'items')");
        }

        [Test]
        public void BuildDataWithPartiallyEmptyAssets()
        {
            BuildData mergedData = pushMetadata.MergeBuildManifests(SharedObjects.ExpectedNoBlobManifestMetadata.Concat(SharedObjects.ExpectedNoPackagesManifestMetadata).ToList());
            SharedObjects.CompareBuildDataInformation(mergedData, SharedObjects.ExpectedPartialAssetsBuildData);
        }

        [Test]
        public void IncompatibleBuildData()
        {
            Action act = () => pushMetadata.MergeBuildManifests(SharedObjects.ExpectedBuildDataIncompatibleList);
            act.Should().Throw<Exception>().WithMessage("Can't merge if one or more manifests have different branch, build number, commit, or repository values.");
        }

        [Test]
        public void CompatibleBuildDataWithDuplicatedAssets()
        {
            Action act = () => pushMetadata.MergeBuildManifests(SharedObjects.ExpectedBuildDataIncompatibleList);
            act.Should().Throw<Exception>().WithMessage("");
        }
    }
}
