// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Shouldly;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.DarcLib.Tests.Helpers;

public class DarcManifestHelperTests
{
    private const int FakeBuildCount = 10;
    private const string FakeOutputPath = @"F:\A\";

    [TestCase(true)]
    [TestCase(false)]
    public void GenerateManifestWithAbsolutePaths(bool includeExtraAssets)
    {
        JObject testManifest;
        if (includeExtraAssets)
        {
            testManifest = ManifestHelper.GenerateDarcAssetJsonManifest(GetSomeShippingAssetsBuilds(), GetSomeExtraDownloadedAssets(),
                FakeOutputPath, false, NullLogger.Instance);
        }
        else
        {
            testManifest = ManifestHelper.GenerateDarcAssetJsonManifest(GetSomeShippingAssetsBuilds(),
                FakeOutputPath, false, NullLogger.Instance);
        }

        var builds = testManifest["builds"].ToList();
        builds.Count.ShouldBe(FakeBuildCount);

        for (var i = 0; i < FakeBuildCount; i++)
        {
            // Make sure everything has its distinct, correctly calculated target paths
            var targetPaths = builds[i]["assets"].First()["targets"];
            targetPaths[0].Value<string>().ShouldBe(@$"{FakeOutputPath}K\E\Path\SomeAsset.{i}.zip");
            targetPaths[1].Value<string>().ShouldBe(@$"{FakeOutputPath}KE\Other\Path\SomeAsset.{i}.zip");

            // Everything else is just passed-along values, so make sure they're present / not null.
            builds[i]["repo"].Value<string>().ShouldNotBeNullOrEmpty();
            builds[i]["commit"].Value<string>().ShouldNotBeNullOrEmpty();
            builds[i]["produced"].Value<DateTimeOffset>().ShouldNotBe(DateTimeOffset.MinValue);
            builds[i]["buildNumber"].Value<string>().ShouldNotBeNullOrEmpty();
            builds[i]["barBuildId"].Value<int>().ShouldBe(i);
            builds[i]["channels"].Children().Count().ShouldBe(2);
            builds[i]["assets"].Children().Count().ShouldBe(1);

            if (i == 0) // Dependencies included in test data
            {
                CheckExpectedDependencies(builds[i]["dependencies"]);
            }
            else
            {
                // Non-root builds won't populate these so expect to not even find the node.
                builds[i]["dependencies"].ShouldBeNull();
            }
        }
        testManifest["outputPath"].Value<string>().ShouldBe(FakeOutputPath);
        if (includeExtraAssets)
        {
            CheckExpectedExtraAssets(testManifest["extraAssets"], false);
        }
        else
        {
            testManifest["extraAssets"].ShouldBeNull(); // Don't generate the extra Assets entry unless we have some.
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void GenerateManifestWithRelativePaths(bool includeExtraAssets)
    {
        JObject testManifest;
        if (includeExtraAssets)
        {
            testManifest = ManifestHelper.GenerateDarcAssetJsonManifest(GetSomeShippingAssetsBuilds(), GetSomeExtraDownloadedAssets(),
                FakeOutputPath, true, NullLogger.Instance);
        }
        else
        {
            testManifest = ManifestHelper.GenerateDarcAssetJsonManifest(GetSomeShippingAssetsBuilds(),
                FakeOutputPath, true, NullLogger.Instance);
        }

        var builds = testManifest["builds"].ToList();
        builds.Count.ShouldBe(FakeBuildCount);

        for (var i = 0; i < FakeBuildCount; i++)
        {
            // Make sure everything has its distinct, correctly calculated target paths
            var targetPaths = builds[i]["assets"].First()["targets"];
            targetPaths[0].Value<string>().ShouldBe(@$"K\E\Path\SomeAsset.{i}.zip");
            targetPaths[1].Value<string>().ShouldBe(@$"KE\Other\Path\SomeAsset.{i}.zip");

            // Everything else is just passed-along values, so make sure they're present / not null.
            builds[i]["repo"].Value<string>().ShouldNotBeNullOrEmpty();
            builds[i]["commit"].Value<string>().ShouldNotBeNullOrEmpty();
            builds[i]["produced"].Value<DateTimeOffset>().ShouldNotBe(DateTimeOffset.MinValue);
            builds[i]["buildNumber"].Value<string>().ShouldNotBeNullOrEmpty();
            builds[i]["barBuildId"].Value<int>().ShouldBe(i);
            builds[i]["channels"].Children().Count().ShouldBe(2);
            builds[i]["assets"].Children().Count().ShouldBe(1);

            if (i == 0) // Dependencies included in test data
            {
                CheckExpectedDependencies(builds[i]["dependencies"]);
            }
            else
            {
                // Non-root builds won't populate these so expect to not even find the node.
                builds[i]["dependencies"].ShouldBeNull();
            }
        }
        testManifest["outputPath"].Value<string>().ShouldBe(FakeOutputPath);
        if (includeExtraAssets)
        {
            CheckExpectedExtraAssets(testManifest["extraAssets"], true);
        }
        else
        {
            testManifest["extraAssets"].ShouldBeNull(); // Don't generate the extra Assets entry unless we have some.
        }
    }

    private static void CheckExpectedExtraAssets(JToken extraAssetsNode, bool relativePaths)
    {
        extraAssetsNode.Children().Count().ShouldBe(2);
        extraAssetsNode[0]["name"].Value<string>().ShouldBe("FakeExtraAssetOne");
        extraAssetsNode[0]["version"].Value<string>().ShouldBe("1.0.0");
        extraAssetsNode[0]["nonShipping"].Value<bool>().ShouldBe(false);
        extraAssetsNode[0]["source"].Value<string>().ShouldBe("https://fakeplace.blob.core.windows.net/dotnet/assets/fake-blob-one.zip");
        extraAssetsNode[0]["barAssetId"].Value<int>().ShouldBe(123);
        extraAssetsNode[1]["name"].Value<string>().ShouldBe("FakeExtraAssetTwo");
        extraAssetsNode[1]["version"].Value<string>().ShouldBe("1.2.0");
        extraAssetsNode[1]["nonShipping"].Value<bool>().ShouldBe(true);
        extraAssetsNode[1]["source"].Value<string>().ShouldBe("https://fakeplace.blob.core.windows.net/dotnet/assets/fake-blob-two.zip");
        extraAssetsNode[1]["barAssetId"].Value<int>().ShouldBe(789);

        if (relativePaths)
        {
            extraAssetsNode[0]["targets"][0].Value<string>().ShouldBe(@$"KE\Path\FakeExtraAssetOne.blob");
            extraAssetsNode[0]["targets"][1].Value<string>().ShouldBe(@$"K\E\OtherPath\FakeExtraAssetOne.blob");
            extraAssetsNode[1]["targets"][0].Value<string>().ShouldBe(@$"KE\Path\FakeExtraAssetTwo.blob");
            extraAssetsNode[1]["targets"][1].Value<string>().ShouldBe(@$"K\E\OtherPath\FakeExtraAssetTwo.blob");
        }
        else
        {
            extraAssetsNode[0]["targets"][0].Value<string>().ShouldBe(@$"F:\A\KE\Path\FakeExtraAssetOne.blob");
            extraAssetsNode[0]["targets"][1].Value<string>().ShouldBe(@$"F:\A\K\E\OtherPath\FakeExtraAssetOne.blob");
            extraAssetsNode[1]["targets"][0].Value<string>().ShouldBe(@$"F:\A\KE\Path\FakeExtraAssetTwo.blob");
            extraAssetsNode[1]["targets"][1].Value<string>().ShouldBe(@$"F:\A\K\E\OtherPath\FakeExtraAssetTwo.blob");
        }
    }

    private static void CheckExpectedDependencies(JToken dependenciesNode)
    {
        dependenciesNode.Children().Count().ShouldBe(2);
        dependenciesNode.Children().ToArray()[0]["commit"].Value<string>().ShouldBe("fakehash1");
        dependenciesNode.Children().ToArray()[0]["name"].Value<string>().ShouldBe("Fake.Dependency.One");
        dependenciesNode.Children().ToArray()[0]["repoUri"].Value<string>().ShouldBe("https://github.com/dotnet/fakerepository1");
        dependenciesNode.Children().ToArray()[0]["version"].Value<string>().ShouldBe("1.2.3-prerelease");

        dependenciesNode.Children().ToArray()[1]["commit"].Value<string>().ShouldBe("fakehash2");
        dependenciesNode.Children().ToArray()[1]["name"].Value<string>().ShouldBe("Fake.Dependency.Two");
        dependenciesNode.Children().ToArray()[1]["repoUri"].Value<string>().ShouldBe("https://github.com/dotnet/fakerepository2");
        dependenciesNode.Children().ToArray()[1]["version"].Value<string>().ShouldBe("4.5.6");
    }


    [Test]
    public void NoDownloadedBuildsProvided()
    {
        List<DownloadedBuild> downloadedBuilds = [];
        JObject emptyManifest = ManifestHelper.GenerateDarcAssetJsonManifest(downloadedBuilds,
            FakeOutputPath, false, NullLogger.Instance);
        var builds = emptyManifest["builds"].ToList();
        builds.Count.ShouldBe(0);
        emptyManifest["outputPath"].Value<string>().ShouldBe(FakeOutputPath);
    }

    private static List<DownloadedAsset> GetSomeExtraDownloadedAssets()
    {
        List<DownloadedAsset> fakeAssets =
        [
            new DownloadedAsset()
            {
                Asset = new Asset(123, 456, false, "FakeExtraAssetOne", "1.0.0", null),
                LocationType = LocationType.Container,
                ReleaseLayoutTargetLocation = @"F:\A\KE\Path\FakeExtraAssetOne.blob",
                UnifiedLayoutTargetLocation = @"F:\A\K\E\OtherPath\FakeExtraAssetOne.blob",
                SourceLocation = "https://fakeplace.blob.core.windows.net/dotnet/assets/fake-blob-one.zip"
            },
            new DownloadedAsset()
            {
                Asset = new Asset(789, 101, true, "FakeExtraAssetTwo", "1.2.0", null),
                LocationType = LocationType.Container,
                ReleaseLayoutTargetLocation = @"F:\A\KE\Path\FakeExtraAssetTwo.blob",
                UnifiedLayoutTargetLocation = @"F:\A\K\E\OtherPath\FakeExtraAssetTwo.blob",
                SourceLocation = "https://fakeplace.blob.core.windows.net/dotnet/assets/fake-blob-two.zip"
            },
        ];
        return fakeAssets;
    }

    private static List<DownloadedBuild> GetSomeShippingAssetsBuilds()
    {
        List<DownloadedBuild> fakeDownloadedBuilds = [];

        List<Channel> channels =
        [
            new Channel(123, "fake-channel-general", "general testing"),
            new Channel(456, "fake-channel-release", "release channel"),
        ];

        for (var i = 0; i < FakeBuildCount; i++)
        {
            var buildToAdd = new DownloadedBuild()
            {
                ReleaseLayoutOutputDirectory = @"F:\A",
                AnyShippingAssets = true,
                Build = new Build(i, DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(i)), i, true, true, "fakehash", channels, [], [], [])
                {
                    GitHubRepository = i % 2 == 0 ? null : "https://github.com/dotnet/fakerepository",
                    AzureDevOpsRepository = i % 2 == 1 ? null : "https://fake.visualstudio.com/project",
                    AzureDevOpsBranch = "fake-azdo-branch",
                    AzureDevOpsBuildNumber = $"12345{i}"
                },
                DownloadedAssets = new List<DownloadedAsset>()
                {
                    new()
                    {
                        Asset = new Asset(i, i + 10000, i % 2 == 0, $"DownloadedAsset{i}", $"{i}.0.0", []),
                        SourceLocation = "https://github.com/dotnet/fakerepository",
                        ReleaseLayoutTargetLocation = @$"F:\A\K\E\Path\SomeAsset.{i}.zip",
                        UnifiedLayoutTargetLocation = @$"F:\A\KE\Other\Path\SomeAsset.{i}.zip",
                    }
                },
            };

            if (i == 0) // Include some dependency details if it's the first build, so we test both the null and has-values possibilities
            {
                buildToAdd.Dependencies = new List<DependencyDetail>()
                {
                    new()
                    {
                        Commit = "fakehash1",
                        Name = "Fake.Dependency.One",
                        RepoUri = "https://github.com/dotnet/fakerepository1",
                        Version = "1.2.3-prerelease"
                    },
                    new()
                    {
                        Commit = "fakehash2",
                        Name = "Fake.Dependency.Two",
                        RepoUri = "https://github.com/dotnet/fakerepository2",
                        Version = "4.5.6"
                    }
                };
            }
            fakeDownloadedBuilds.Add(buildToAdd);
        }
        return fakeDownloadedBuilds;
    }
}
