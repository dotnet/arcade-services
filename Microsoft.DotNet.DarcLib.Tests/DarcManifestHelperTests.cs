// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.Maestro.Client.Models;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Microsoft.DotNet.DarcLib.Tests
{
    public class DarcManifestHelperTests
    {
        const int FakeBuildCount = 10;

        [Test]
        public void GenerateManifestWithAbsolutePaths()
        {
            List<DownloadedBuild> downloadedBuilds = GetSomeShippingAssetsBuilds();
            JObject testManifest = ManifestHelper.GenerateDarcAssetJsonManifest(downloadedBuilds);
            var builds = testManifest["builds"].ToList();
            builds.Count.Should().Be(FakeBuildCount);

            for (int i = 0; i < FakeBuildCount; i++)
            {
                // Make sure everything has its distinct, correctly calculated target paths
                var targetPaths = builds[i]["assets"].First()["targets"];
                targetPaths[0].Value<string>().Should().Be(@$"F:\A\K\E\Path\SomeAsset.{i}.zip");
                targetPaths[1].Value<string>().Should().Be(@$"F:\A\KE\Other\Path\SomeAsset.{i}.zip");

                // Everything else is just passed-along values, so make sure they're present / not null.
                builds[i]["repo"].Value<string>().Should().NotBeNullOrEmpty();
                builds[i]["commit"].Value<string>().Should().NotBeNullOrEmpty();
                builds[i]["produced"].Value<DateTimeOffset>().Should().NotBe(DateTimeOffset.MinValue);
                builds[i]["buildNumber"].Value<string>().Should().NotBeNullOrEmpty();
                builds[i]["barBuildId"].Value<int>().Should().Be(i);
                builds[i]["channels"].Children().Count().Should().Be(2);
                builds[i]["assets"].Children().Count().Should().Be(1);
            }
        }


        [Test]
        public void GenerateManifestWithRelativePaths()
        {
            List<DownloadedBuild> downloadedBuilds = GetSomeShippingAssetsBuilds();
            JObject testManifest = ManifestHelper.GenerateDarcAssetJsonManifest(downloadedBuilds, @"F:\A");
            var builds = testManifest["builds"].ToList();
            builds.Count.Should().Be(FakeBuildCount);

            for (int i = 0; i < FakeBuildCount; i++)
            {
                // Make sure everything has its distinct, correctly calculated target paths
                var targetPaths = builds[i]["assets"].First()["targets"];
                targetPaths[0].Value<string>().Should().Be(@$"K\E\Path\SomeAsset.{i}.zip");
                targetPaths[1].Value<string>().Should().Be(@$"KE\Other\Path\SomeAsset.{i}.zip");

                // Everything else is just passed-along values, so make sure they're present / not null.
                builds[i]["repo"].Value<string>().Should().NotBeNullOrEmpty();
                builds[i]["commit"].Value<string>().Should().NotBeNullOrEmpty();
                builds[i]["produced"].Value<DateTimeOffset>().Should().NotBe(DateTimeOffset.MinValue);
                builds[i]["buildNumber"].Value<string>().Should().NotBeNullOrEmpty();
                builds[i]["barBuildId"].Value<int>().Should().Be(i);
                builds[i]["channels"].Children().Count().Should().Be(2);
                builds[i]["assets"].Children().Count().Should().Be(1);
            }
        }

        [Test]
        public void NoDownloadedBuildsProvided()
        {
            List<DownloadedBuild> downloadedBuilds = new List<DownloadedBuild>();
            JObject emptyManifest = ManifestHelper.GenerateDarcAssetJsonManifest(downloadedBuilds);
            var builds = emptyManifest["builds"].ToList();
            builds.Count.Should().Be(0);
        }

        private List<DownloadedBuild> GetSomeShippingAssetsBuilds()
        {
            List<DownloadedBuild> fakeDownloadedBuilds = new List<DownloadedBuild>();

            List<Channel> channels = new List<Channel>()
            {
                new Channel(123, "fake-channel-general", "general testing"),
                new Channel(456, "fake-channel-release", "release channel"),
            };

            for (int i = 0; i < FakeBuildCount; i++)
            {

                fakeDownloadedBuilds.Add(new DownloadedBuild()
                {
                    ReleaseLayoutOutputDirectory = @"F:\A",
                    AnyShippingAssets = true,
                    Build = new Build(i, DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(i)), i, true, true, "fakehash",
                                      ImmutableList<Channel>.Empty.AddRange(channels),
                                      ImmutableList<Asset>.Empty,
                                      ImmutableList<BuildRef>.Empty,
                                      ImmutableList<BuildIncoherence>.Empty)
                    {
                        GitHubRepository = i % 2 == 0 ? null : "https://github.com/dotnet/fakerepository",
                        AzureDevOpsRepository = i % 2 == 1 ? null : "https://fake.visualstudio.com/project",
                        AzureDevOpsBranch = "fake-azdo-branch",
                        AzureDevOpsBuildNumber = $"12345{i}"
                    },
                    DownloadedAssets = new List<DownloadedAsset>()
                    {
                        new DownloadedAsset()
                        {
                            Asset = new Asset(i, i + 10000, i % 2 == 0, $"DownloadedAsset{i}", $"{i}.0.0", ImmutableList<AssetLocation>.Empty),
                            SourceLocation = "https://github.com/dotnet/fakerepository",
                            ReleaseLayoutTargetLocation = @$"F:\A\K\E\Path\SomeAsset.{i}.zip",
                            UnifiedLayoutTargetLocation = @$"F:\A\KE\Other\Path\SomeAsset.{i}.zip",
                        }
                    },
                }); ;
            }
            return fakeDownloadedBuilds;
        }
    }
}
