// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
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
                new AssetData(false)
                {
                    Name = "depA",
                    Version = "v2"
                }
            };

            List<DependencyUpdate> updates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            Assert.Collection(updates, u =>
            {
                Assert.Equal(depA, u.From);
                Assert.Equal("v2", u.To.Version);
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
                new AssetData(false)
                {
                    Name = "depA",
                    Version = "v2"
                },
                new AssetData(false)
                {
                    Name = "depB",
                    Version = "v5"
                }
            };

            List<DependencyUpdate> updates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            Assert.Collection(updates,
            u =>
            {
                Assert.Equal(depA, u.From);
                Assert.Equal("v2", u.To.Version);
            },
            u =>
            {
                Assert.Equal(depB, u.From);
                Assert.Equal("v5", u.To.Version);
            });
        }

        /// <summary>
        ///     Test that a simple set of non-coherency updates works.
        ///     
        ///     depB is tied to depA and should not move.
        /// </summary>
        [Fact]
        public async void CoherencyUpdateTests3()
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
                Type = DependencyType.Product,
                CoherentParentDependencyName = "depA"
            };

            List<DependencyDetail> existingDetails = new List<DependencyDetail>
            { depA, depB };

            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false)
                {
                    Name = "depA",
                    Version = "v2"
                },
                new AssetData(false)
                {
                    Name = "depB",
                    Version = "v5"
                }
            };

            List<DependencyUpdate> updates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            Assert.Collection(updates,
            u =>
            {
                Assert.Equal(depA, u.From);
                Assert.Equal("v2", u.To.Version);
            });
        }

        /// <summary>
        ///     Test a chain with a pinned middle.
        /// </summary>
        [Fact]
        public async void CoherencyUpdateTests4()
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

            // depB is tied to A, but pinned (head of coherent tree)
            DependencyDetail depB = new DependencyDetail
            {
                Name = "depB",
                Version = "v3",
                Commit = "commit1",
                Pinned = true,
                RepoUri = "repoB",
                Type = DependencyType.Product,
                CoherentParentDependencyName = "depA"
            };

            // depC is tied to depB
            DependencyDetail depC = new DependencyDetail
            {
                Name = "depC",
                Version = "v7",
                Commit = "commit1",
                Pinned = false,
                RepoUri = "repoC",
                Type = DependencyType.Product,
                CoherentParentDependencyName = "depB"
            };

            List<DependencyDetail> existingDetails = new List<DependencyDetail>
            { depA, depB, depC };

            // Attempt to update all 3, only A should move.
            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false)
                {
                    Name = "depA",
                    Version = "v2"
                },
                new AssetData(false)
                {
                    Name = "depB",
                    Version = "v5"
                },
                new AssetData(false)
                {
                    Name = "depC",
                    Version = "v7"
                }
            };

            List<DependencyUpdate> updates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            Assert.Collection(updates,
            u =>
            {
                Assert.Equal(depA, u.From);
                Assert.Equal("v2", u.To.Version);
            });
        }

        /// <summary>
        ///     Test a tree with a pinned head (nothing moves)
        ///     Test different casings
        /// </summary>
        [Fact]
        public async void CoherencyUpdateTests5()
        {
            Remote remote = new Remote(null, null, NullLogger.Instance);

            // Pin head.
            DependencyDetail depA = new DependencyDetail
            {
                Name = "depA",
                Version = "v1",
                Commit = "commit1",
                Pinned = true,
                RepoUri = "repoA",
                Type = DependencyType.Product
            };

            DependencyDetail depB = new DependencyDetail
            {
                Name = "depB",
                Version = "v3",
                Commit = "commit1",
                Pinned = false,
                RepoUri = "repoB",
                Type = DependencyType.Product,
                CoherentParentDependencyName = "depA"
            };

            DependencyDetail depC = new DependencyDetail
            {
                Name = "depC",
                Version = "v3",
                Commit = "commit1",
                Pinned = false,
                RepoUri = "repoC",
                Type = DependencyType.Product,
                CoherentParentDependencyName = "depA"
            };

            DependencyDetail depD = new DependencyDetail
            {
                Name = "depD",
                Version = "v3",
                Commit = "commit1",
                Pinned = false,
                RepoUri = "REPOC",
                Type = DependencyType.Product,
                CoherentParentDependencyName = "DEPA"
            };

            List<DependencyDetail> existingDetails = new List<DependencyDetail>
            { depA, depB, depC, depD };

            // Attempt to update all 3, only A should move.
            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false)
                {
                    Name = "depA",
                    Version = "v2"
                },
                new AssetData(false)
                {
                    Name = "depB",
                    Version = "v5"
                },
                new AssetData(false)
                {
                    Name = "depC",
                    Version = "v7"
                },
                new AssetData(false)
                {
                    Name = "depD",
                    Version = "v11"
                }
            };

            List<DependencyUpdate> updates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            Assert.Empty(updates);
        }

        /// <summary>
        ///     Test a simple coherency update
        ///     B tied to A.
        /// </summary>
        [Fact]
        public async void CoherencyUpdateTests6()
        {
            // Initialize
            var barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            var dependencyGraphRemoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemote(It.IsAny<string>(), It.IsAny<ILogger>())).Returns(dependencyGraphRemoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemote(It.IsAny<ILogger>())).Returns(remote);

            // Pin head.
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
                RepoUri = "repoB",
                Type = DependencyType.Product,
                CoherentParentDependencyName = "depA"
            };

            List<DependencyDetail> existingDetails = new List<DependencyDetail>
            { depA, depB };
            
            // Attempt to update all 3, only A should move.
            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false)
                {
                    Name = "depA",
                    Version = "v2"
                },
                new AssetData(false)
                {
                    Name = "depB",
                    Version = "v5"
                }
            };

            // Builds
            var repoABuildAssets = new List<Asset>
            {
                new Asset(1, 1, true, "depA", "v2", null)
            };
            var repoABuild = new Build(1, DateTimeOffset.Now, "commit2", null, repoABuildAssets.ToImmutableList(), null);
            barClientMock.Setup(m => m.GetBuildsAsync("repoA", "commit2")).ReturnsAsync(new List<Build> { repoABuild });

            // This should return dependencies from repoB, but not a depB directly
            // which should be looked up in the assets.
            DependencyDetail depC = new DependencyDetail
            {
                Name = "depC",
                Version = "v10",
                Commit = "commit5",
                Pinned = false,
                RepoUri = "repoB",
                Type = DependencyType.Product
            };
            dependencyGraphRemoteMock.Setup(m => m.GetDependenciesAsync("repoA", "commit2", null)).ReturnsAsync(new List<DependencyDetail> { depC });

            var repoBBuildAssets = new List<Asset>
            {
                new Asset(2, 2, true, "depC", "v10", null),
                // This is the asset that should be pulled forward
                new Asset(2, 2, true, "depB", "v101", null)
            };
            var repoBBuild = new Build(2, DateTimeOffset.Now, "commit64", null, repoBBuildAssets.ToImmutableList(), null)
            {
                AzureDevOpsRepository = "repoB",
                GitHubRepository = "repoB"
            };
            barClientMock.Setup(m => m.GetBuildsAsync("repoB", "commit5")).ReturnsAsync(new List<Build> { repoBBuild });

            List<DependencyUpdate> nonCoherencyUpdates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            Assert.Collection(nonCoherencyUpdates,
            u =>
            {
                Assert.Equal(depA, u.From);
                Assert.Equal("v2", u.To.Version);
            });

            // Update the current dependency details with the non coherency updates
            UpdateCurrentDependencies(existingDetails, nonCoherencyUpdates);

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object);

            Assert.Collection(coherencyUpdates,
            u =>
            {
                Assert.Equal(depB, u.From);
                Assert.Equal("v101", u.To.Version);
                Assert.Equal("commit64", u.To.Commit);
                Assert.Equal("repoB", u.To.RepoUri);
            });
        }

        /// <summary>
        ///     Test a simple coherency update
        ///     B tied to A, but B is pinned.
        /// </summary>
        [Fact]
        public async void CoherencyUpdateTests7()
        {
            // Initialize
            var barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            var dependencyGraphRemoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemote(It.IsAny<string>(), It.IsAny<ILogger>())).Returns(dependencyGraphRemoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemote(It.IsAny<ILogger>())).Returns(remote);

            // Pin head.
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
                Pinned = true,
                RepoUri = "repoB",
                Type = DependencyType.Product,
                CoherentParentDependencyName = "depA"
            };

            List<DependencyDetail> existingDetails = new List<DependencyDetail>
            { depA, depB };

            // Attempt to update all 3, only A should move.
            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false)
                {
                    Name = "depA",
                    Version = "v2"
                },
                new AssetData(false)
                {
                    Name = "depB",
                    Version = "v5"
                }
            };

            // Builds
            var repoABuildAssets = new List<Asset>
            {
                new Asset(1, 1, true, "depA", "v2", null)
            };
            var repoABuild = new Build(1, DateTimeOffset.Now, "commit2", null, repoABuildAssets.ToImmutableList(), null);
            barClientMock.Setup(m => m.GetBuildsAsync("repoA", "commit2")).ReturnsAsync(new List<Build> { repoABuild });

            // This should return dependencies from repoB, but not a depB directly
            // which should be looked up in the assets.
            DependencyDetail depC = new DependencyDetail
            {
                Name = "depC",
                Version = "v10",
                Commit = "commit5",
                Pinned = false,
                RepoUri = "repoB",
                Type = DependencyType.Product
            };
            dependencyGraphRemoteMock.Setup(m => m.GetDependenciesAsync("repoA", "commit2", null)).ReturnsAsync(new List<DependencyDetail> { depC });

            var repoBBuildAssets = new List<Asset>
            {
                new Asset(2, 2, true, "depC", "v10", null),
                // This is the asset that should be pulled forward
                new Asset(2, 2, true, "depB", "v101", null)
            };
            var repoBBuild = new Build(2, DateTimeOffset.Now, "commit64", null, repoBBuildAssets.ToImmutableList(), null)
            {
                AzureDevOpsRepository = "repoB",
                GitHubRepository = "repoB"
            };
            barClientMock.Setup(m => m.GetBuildsAsync("repoB", "commit5")).ReturnsAsync(new List<Build> { repoBBuild });

            List<DependencyUpdate> nonCoherencyUpdates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            Assert.Collection(nonCoherencyUpdates,
            u =>
            {
                Assert.Equal(depA, u.From);
                Assert.Equal("v2", u.To.Version);
            });

            // Update the current dependency details with the non coherency updates
            UpdateCurrentDependencies(existingDetails, nonCoherencyUpdates);

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object);

            Assert.Empty(coherencyUpdates);
        }

        /// <summary>
        ///     Test a simple coherency update
        ///     B tied to A, but no B asset is produced.
        ///     Should throw.
        /// </summary>
        [Fact]
        public async void CoherencyUpdateTests8()
        {
            // Initialize
            var barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            var dependencyGraphRemoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemote(It.IsAny<string>(), It.IsAny<ILogger>())).Returns(dependencyGraphRemoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemote(It.IsAny<ILogger>())).Returns(remote);

            // Pin head.
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
                Pinned = true,
                RepoUri = "repoB",
                Type = DependencyType.Product,
                CoherentParentDependencyName = "depA"
            };

            List<DependencyDetail> existingDetails = new List<DependencyDetail>
            { depA, depB };

            // Attempt to update all 3, only A should move.
            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false)
                {
                    Name = "depA",
                    Version = "v2"
                },
                new AssetData(false)
                {
                    Name = "depB",
                    Version = "v5"
                }
            };

            // Builds
            var repoABuildAssets = new List<Asset>
            {
                new Asset(1, 1, true, "depA", "v2", null)
            };
            var repoABuild = new Build(1, DateTimeOffset.Now, "commit2", null, repoABuildAssets.ToImmutableList(), null);
            barClientMock.Setup(m => m.GetBuildsAsync("repoA", "commit2")).ReturnsAsync(new List<Build> { repoABuild });

            // This should return dependencies from repoB, but not a depB directly
            // which should be looked up in the assets.
            DependencyDetail depC = new DependencyDetail
            {
                Name = "depC",
                Version = "v10",
                Commit = "commit5",
                Pinned = false,
                RepoUri = "repoB",
                Type = DependencyType.Product
            };
            dependencyGraphRemoteMock.Setup(m => m.GetDependenciesAsync("repoA", "commit2", null)).ReturnsAsync(new List<DependencyDetail> { depC });

            var repoBBuildAssets = new List<Asset>
            {
                new Asset(2, 2, true, "depC", "v10", null)
            };
            var repoBBuild = new Build(2, DateTimeOffset.Now, "commit64", null, repoBBuildAssets.ToImmutableList(), null)
            {
                AzureDevOpsRepository = "repoB",
                GitHubRepository = "repoB"
            };
            barClientMock.Setup(m => m.GetBuildsAsync("repoB", "commit5")).ReturnsAsync(new List<Build> { repoBBuild });

            List<DependencyUpdate> nonCoherencyUpdates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            Assert.Collection(nonCoherencyUpdates,
            u =>
            {
                Assert.Equal(depA, u.From);
                Assert.Equal("v2", u.To.Version);
            });

            // Update the current dependency details with the non coherency updates
            UpdateCurrentDependencies(existingDetails, nonCoherencyUpdates);

            await Assert.ThrowsAsync<DarcException>(() => remote.GetRequiredCoherencyUpdatesAsync(
                existingDetails, remoteFactoryMock.Object));
        }

        /// <summary>
        ///     Updates <paramref name="currentDependencies"/> with <paramref name="updates"/>
        /// </summary>
        /// <param name="currentDependencies">Full set of existing dependency details</param>
        /// <param name="updates">Updates.</param>
        private void UpdateCurrentDependencies(List<DependencyDetail> currentDependencies,
            List<DependencyUpdate> updates)
        {
            foreach (DependencyUpdate update in updates)
            {
                DependencyDetail from = update.From;
                DependencyDetail to = update.To;
                // Replace in the current dependencies list so the correct data is fed into the coherency pass.
                currentDependencies.Remove(from);
                currentDependencies.Add(to);
            }
        }
    }
}
