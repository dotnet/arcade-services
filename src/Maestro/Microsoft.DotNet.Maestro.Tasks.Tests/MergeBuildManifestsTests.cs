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
        // TODO: Find whatever is making this not work concurrently.
        [Test]
        public void TwoCompatibleBuildData()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();
            BuildData mergedData = pushMetadata.MergeBuildManifests(SharedObjects.ExpectedTwoBuildDataList);
            SharedObjects.CompareBuildDataInformation(mergedData, SharedObjects.ExpectedMergedBuildData);
        }

        [Test]
        public void ThreeCompatibleBuildData()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();
            BuildData mergedData = pushMetadata.MergeBuildManifests(SharedObjects.ExpectedThreeBuildDataList);
            SharedObjects.CompareBuildDataInformation(mergedData, SharedObjects.ExpectedThreeAssetsBuildData);
        }

        [Test]
        public void BuildDataWithNullAndEmptyAssets()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();
            Action act = () => pushMetadata.MergeBuildManifests(SharedObjects.BuildDataWithoutAssetsList);
            act.Should().Throw<ArgumentNullException>().WithMessage("Value cannot be null. (Parameter 'items')");
        }

        [Test]
        public void BuildDataWithPartiallyEmptyAssets()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();
            BuildData mergedData = pushMetadata.MergeBuildManifests(SharedObjects.ExpectedNoBlobManifestMetadata.Concat(SharedObjects.ExpectedNoPackagesManifestMetadata).ToList());
            SharedObjects.CompareBuildDataInformation(mergedData, SharedObjects.ExpectedPartialAssetsBuildData);
        }

        [Test]
        public void IncompatibleBuildData()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();
            Action act = () => pushMetadata.MergeBuildManifests(SharedObjects.ExpectedBuildDataIncompatibleList);
            act.Should().Throw<Exception>().WithMessage("Can't merge if one or more manifests have different branch, build number, commit, or repository values.");
        }

        [Test]
        public void CompatibleBuildDataWithDuplicatedAssets()
        {
            PushMetadataToBuildAssetRegistry pushMetadata = new PushMetadataToBuildAssetRegistry();
            Action act = () => pushMetadata.MergeBuildManifests(SharedObjects.ExpectedBuildDataIncompatibleList);
            act.Should().Throw<Exception>().WithMessage("Can't merge if one or more manifests have different branch, build number, commit, or repository values.");
        }
    }
}
