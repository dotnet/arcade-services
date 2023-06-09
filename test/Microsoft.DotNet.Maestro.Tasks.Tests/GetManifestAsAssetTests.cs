// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.Maestro.Tasks.Proxies;
using Microsoft.DotNet.VersionTools.BuildManifest.Model;
using Moq;
using NUnit.Framework;
using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks.Tests;

[TestFixture]
public class GetManifestAsAssetTests
{
    private PushMetadataToBuildAssetRegistry pushMetadata;
    private const string newManifestName = "NewManifest";
    public const string LocationString = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts";
    public const string repoName ="thisIsARepo";
    public const string assetVersion = "6.0.0-beta.20516.5";

    internal BlobArtifactModel blob = new BlobArtifactModel()
    {
        Attributes = new Dictionary<string, string>()
        {
            {"NonShipping", "true"},
            {"Category", "NONE"}
        },
        Id = $"assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg"
    };

    internal BlobArtifactModel mergedManifestBlobWhenAssetVersionIsNotNull = new BlobArtifactModel()
    {
        Attributes = new Dictionary<string, string>()
        {
            {"NonShipping", "true"},
            {"Category", "NONE"}
        },
        Id = $"assets/manifests/{repoName}/{assetVersion}/{newManifestName}"
    };

    internal BlobArtifactModel mergedManifestBlobWhenAssetVersionIsNull = new BlobArtifactModel()
    {
        Attributes = new Dictionary<string, string>()
        {
            {"NonShipping", "true"},
            {"Category", "NONE"}
        },
        Id = "assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg"
    };

    public PushMetadataToBuildAssetRegistry SetupGetManifestAsAssetTests()
    {
        Mock<IGetEnvProxy> getEnvMock = new Mock<IGetEnvProxy>();
        getEnvMock.Setup(s => s.GetEnv("BUILD_REPOSITORY_NAME")).Returns(repoName);

        pushMetadata = new PushMetadataToBuildAssetRegistry
        {
            getEnvProxy = getEnvMock.Object
        };
        return pushMetadata;
    }

    [Test]
    public void AssetVersionIsNotNull()
    {
        pushMetadata = SetupGetManifestAsAssetTests();
        pushMetadata.AssetVersion = assetVersion;
        List<BlobArtifactModel> blobs = new List<BlobArtifactModel>();
        var actualBlob = pushMetadata.GetManifestAsAsset(blobs, newManifestName);
        actualBlob.Id.Should().BeEquivalentTo(mergedManifestBlobWhenAssetVersionIsNotNull.Id);
        actualBlob.NonShipping.Should().Be(mergedManifestBlobWhenAssetVersionIsNotNull.NonShipping);
    }

    [Test]
    public void AssetVersionIsNull()
    {
        pushMetadata = SetupGetManifestAsAssetTests();
        List<BlobArtifactModel> blobs = new List<BlobArtifactModel>() { blob };
        var actualBlob = pushMetadata.GetManifestAsAsset(blobs, newManifestName);
        actualBlob.Id.Should().BeEquivalentTo(mergedManifestBlobWhenAssetVersionIsNull.Id);
        actualBlob.NonShipping.Should().Be(mergedManifestBlobWhenAssetVersionIsNull.NonShipping);
    }
}
