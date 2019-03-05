// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.DotNet.Darc.Tests
{
    public class DependencyCoherencyTests
    {
        /// <summary>
        ///     Test that a simple set of non-coherency updates works.
        /// </summary>
        [Fact]
        public async void CoherencyUpdateTests1()
        {
            Remote remote = new Remote(null, null, NullLogger.Instance);

            DependencyDetail depA = new DependencyDetail
            {
                Name = "depA",
                Version = "v1",
                Commit = "commit1",
                Pinned = false,
                RepoUri = "repoA",
                Type = DependencyType.Product
            };

            DependencyDetail depB = new DependencyDetail
            {
                Name = "depB",
                Version = "v1",
                Commit = "commit1",
                Pinned = false,
                RepoUri = "repoA",
                Type = DependencyType.Product
            };

            List<DependencyDetail> existingDetails = new List<DependencyDetail>
            { depA, depB };

            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData
                {
                    Name = "depA",
                    Version = "v2"
                }
            };

            List<DependencyUpdate> updates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            Assert.Collection(updates, u =>
            {
                Assert.Equal(depA, updates[0].From);
                Assert.Equal("v2", updates[0].To.Version);
            });
        }

        /// <summary>
        ///     Test that a simple set of non-coherency updates works.
        /// </summary>
        [Fact]
        public async void CoherencyUpdateTests2()
        {
            Remote remote = new Remote(null, null, NullLogger.Instance);

            DependencyDetail depA = new DependencyDetail
            {
                Name = "depA",
                Version = "v1",
                Commit = "commit1",
                Pinned = false,
                RepoUri = "repoA",
                Type = DependencyType.Product
            };

            DependencyDetail depB = new DependencyDetail
            {
                Name = "depB",
                Version = "v3",
                Commit = "commit1",
                Pinned = false,
                RepoUri = "repoA",
                Type = DependencyType.Product
            };

            List<DependencyDetail> existingDetails = new List<DependencyDetail>
            { depA, depB };

            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData
                {
                    Name = "depA",
                    Version = "v2"
                },
                new AssetData
                {
                    Name = "depB",
                    Version = "v5"
                }
            };

            List<DependencyUpdate> updates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            Assert.Collection(updates, u =>
            {
                Assert.Equal(depA, updates[0].From);
                Assert.Equal("v2", updates[0].To.Version);
            },
            u =>
            {
                Assert.Equal(depB, updates[0].From);
                Assert.Equal("v5", updates[0].To.Version);
            });
        }
    }
}
