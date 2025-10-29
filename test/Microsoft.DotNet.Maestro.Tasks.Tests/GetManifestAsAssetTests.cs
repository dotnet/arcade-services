// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.Maestro.Tasks.Proxies;
using Microsoft.DotNet.VersionTools.BuildManifest.Model;
using Moq;
using NUnit.Framework;
using System.Collections.Generic;

namespace Microsoft.DotNet.Maestro.Tasks.Tests;

[TestFixture]
public class GetManifestAsAssetTests
{
    private PushMetadataToBuildAssetRegistry _pushMetadata;
    private const string NewManifestName = "NewManifest";
    public const string LocationString = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts";
    public const string RepoName = "thisIsARepo";
    public const string AssetVersion = "6.0.0-beta.20516.5";

    internal BlobArtifactModel _blob = new()
    {
        Attributes = new Dictionary<string, string>()
        {
            {"NonShipping", "true"},
            {"Category", "NONE"}
        },
        Id = $"assets/symbols/Microsoft.Cci.Extensions.6.0.0-beta.20516.5.symbols.nupkg"
    };

    internal BlobArtifactModel _mergedManifestBlobWhenAssetVersionIsNotNull = new()
    {
        Attributes = new Dictionary<string, string>()
        {
            {"NonShipping", "true"},
            {"Category", "NONE"}
        },
        Id = $"assets/manifests/{RepoName}/{AssetVersion}/{NewManifestName}"
    };

    internal BlobArtifactModel _mergedManifestBlobWhenAssetVersionIsNull = new()
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
        var getEnvMock = new Mock<IGetEnvProxy>();
        getEnvMock.Setup(s => s.GetEnv("BUILD_REPOSITORY_NAME")).Returns(RepoName);

        _pushMetadata = new PushMetadataToBuildAssetRegistry
        {
            _getEnvProxy = getEnvMock.Object
        };
        return _pushMetadata;
    }

    [Test]
    public void AssetVersionIsNotNull()
    {
        _pushMetadata = SetupGetManifestAsAssetTests();
        _pushMetadata.AssetVersion = AssetVersion;
        List<BlobArtifactModel> blobs = [];
        var actualBlob = _pushMetadata.GetManifestAsAsset(blobs, NewManifestName);
        actualBlob.Id.Should().BeEquivalentTo(_mergedManifestBlobWhenAssetVersionIsNotNull.Id);
        actualBlob.NonShipping.Should().Be(_mergedManifestBlobWhenAssetVersionIsNotNull.NonShipping);
    }

    [Test]
    public void AssetVersionIsNull()
    {
        _pushMetadata = SetupGetManifestAsAssetTests();
        List<BlobArtifactModel> blobs = [_blob];
        var actualBlob = _pushMetadata.GetManifestAsAsset(blobs, NewManifestName);
        actualBlob.Id.Should().BeEquivalentTo(_mergedManifestBlobWhenAssetVersionIsNull.Id);
        actualBlob.NonShipping.Should().Be(_mergedManifestBlobWhenAssetVersionIsNull.NonShipping);
    }
}
