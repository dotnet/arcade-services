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
using System.Linq;
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
        public async Task CoherencyUpdateTests1()
        {
            Remote remote = new Remote(null, null, NullLogger.Instance);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false);

            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false) { Name = "depA", Version = "v2"}
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
        public async Task CoherencyUpdateTests2()
        {
            Remote remote = new Remote(null, null, NullLogger.Instance);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v3", "repoB", "commit1", pinned: false);

            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false) { Name = "depA", Version = "v2"},
                new AssetData(false) { Name = "depB", Version = "v5"}
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
        public async Task CoherencyUpdateTests3()
        {
            Remote remote = new Remote(null, null, NullLogger.Instance);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v3", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false) { Name = "depA", Version = "v2"},
                new AssetData(false) { Name = "depB", Version = "v5"}
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
        public async Task CoherencyUpdateTests4()
        {
            Remote remote = new Remote(null, null, NullLogger.Instance);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v3", "repoB", "commit1", pinned: true, coherentParent: "depA");
            DependencyDetail depC = AddDependency(existingDetails, "depC", "v7", "repoC", "commit1", pinned: false, coherentParent: "depB");

            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false) { Name = "depA", Version = "v2"},
                new AssetData(false) { Name = "depB", Version = "v5"},
                new AssetData(false) { Name = "depC", Version = "v7"}
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
        ///     Test a tree with a pinned head (nothing moves in non-coherency update)
        ///     Test different casings
        /// </summary>
        [Fact]
        public async Task CoherencyUpdateTests5()
        {
            Remote remote = new Remote(null, null, NullLogger.Instance);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: true);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v3", "repoB", "commit1", pinned: false, coherentParent: "depA");
            DependencyDetail depC = AddDependency(existingDetails, "depC", "v3", "repoC", "commit1", pinned: false, coherentParent: "depA");
            DependencyDetail depD = AddDependency(existingDetails, "depD", "v3", "REPOC", "commit1", pinned: false, coherentParent: "DEPA");

            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false) { Name = "depA", Version = "v2"},
                new AssetData(false) { Name = "depB", Version = "v5"},
                new AssetData(false) { Name = "depC", Version = "v7"},
                new AssetData(false) { Name = "depD", Version = "v11"}
            };

            List<DependencyUpdate> updates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            Assert.Empty(updates);
        }

        /// <summary>
        ///     Test a simple coherency update
        ///     B and C are tied to A, both should update
        /// </summary>
        [Fact]
        public async Task CoherencyUpdateTests6()
        {
            // Initialize
            var barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            var dependencyGraphRemoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(dependencyGraphRemoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v3", "repoB", "commit1", pinned: false, coherentParent: "depA");
            DependencyDetail depC = AddDependency(existingDetails, "depC", "v0", "repoC", "commit3", pinned: false, coherentParent: "depA");

            // Attempt to update all 3, only A should move.
            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false) { Name = "depA", Version = "v2"},
                new AssetData(false) { Name = "depB", Version = "v5"},
                new AssetData(false) { Name = "depC", Version = "v10324"}
            };

            BuildProducesAssets(barClientMock, "repoA", "commit2", new List<(string name, string version)>
            {
                ("depA", "v2")
            });

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, "depY", "v42", "repoB", "commit5", pinned: false);
            AddDependency(repoADeps, "depZ", "v43", "repoC", "commit6", pinned: false);
            RepoHasDependencies(dependencyGraphRemoteMock, "repoA", "commit2", repoADeps);

            BuildProducesAssets(barClientMock, "repoB", "commit5", new List<(string name, string version)>
            {
                ("depB", "v10"),
                ("depY", "v42"),
            });

            BuildProducesAssets(barClientMock, "repoC", "commit6", new List<(string name, string version)>
            {
                ("depC", "v1000"),
                ("depZ", "v43"),
            });

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
                Assert.Equal("v10", u.To.Version);
                Assert.Equal("commit5", u.To.Commit);
                Assert.Equal("repoB", u.To.RepoUri);
            },
            u =>
            {
                Assert.Equal(depC, u.From);
                Assert.Equal("v1000", u.To.Version);
                Assert.Equal("commit6", u.To.Commit);
                Assert.Equal("repoC", u.To.RepoUri);
            });
        }

        /// <summary>
        ///     Test a simple coherency update
        ///     B tied to A, but B is pinned. Nothing moves.
        /// </summary>
        [Fact]
        public async Task CoherencyUpdateTests7()
        {
            // Initialize
            var barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            var dependencyGraphRemoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(dependencyGraphRemoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v3", "repoB", "commit1", pinned: true, coherentParent: "depA");

            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false) { Name = "depA", Version = "v2"},
                new AssetData(false) { Name = "depB", Version = "v5"}
            };

            BuildProducesAssets(barClientMock, "repoA", "commit2", new List<(string name, string version)>
            {
                ("depA", "v2")
            });

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, "depC", "v10", "repoB", "commit5", pinned: false);
            RepoHasDependencies(dependencyGraphRemoteMock, "repoA", "commit2", repoADeps);

            BuildProducesAssets(barClientMock, "repoB", "commit5", new List<(string name, string version)>
            {
                ("depC", "v10"),
                ("depB", "v101"),
            });

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
        public async Task CoherencyUpdateTests8()
        {
            // Initialize
            var barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            var dependencyGraphRemoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(dependencyGraphRemoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v3", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false) { Name = "depA", Version = "v2"},
                new AssetData(false) { Name = "depB", Version = "v5"}
            };

            BuildProducesAssets(barClientMock, "repoA", "commit2", new List<(string name, string version)>
            {
                ("depA", "v2")
            });

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, "depC", "v10", "repoB", "commit5", pinned: false);
            RepoHasDependencies(dependencyGraphRemoteMock, "repoA", "commit2", repoADeps);

            BuildProducesAssets(barClientMock, "repoB", "commit5", new List<(string name, string version)>
            {
                ("depC", "v10")
            });

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
        ///     Coherent dependency test with a 3 repo chain
        /// </summary>
        /// <returns></returns>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CoherencyUpdateTests9(bool pinHead)
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> dependencyGraphRemoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(dependencyGraphRemoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: pinHead);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v3", "repoB", "commit2", pinned: false, coherentParent: "depA");
            DependencyDetail depC = AddDependency(existingDetails, "depC", "v0", "repoC", "commit3", pinned: false, coherentParent: "depB");

            BuildProducesAssets(barClientMock, "repoA", "commit1", new List<(string name, string version)>
            {
                ("depA", "v1")
            });

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, "depY", "v42", "repoB", "commit5", pinned: false);
            RepoHasDependencies(dependencyGraphRemoteMock, "repoA", "commit1", repoADeps);

            BuildProducesAssets(barClientMock, "repoB", "commit5", new List<(string name, string version)>
            {
                ("depB", "v10"),
                ("depY", "v42"),
            });

            List<DependencyDetail> repoBDeps = new List<DependencyDetail>();
            AddDependency(repoBDeps, "depZ", "v64", "repoC", "commit7", pinned: false);
            RepoHasDependencies(dependencyGraphRemoteMock, "repoB", "commit5", repoBDeps);

            BuildProducesAssets(barClientMock, "repoC", "commit7", new List<(string name, string version)>
            {
                ("depC", "v1000"),
                ("depZ", "v64"),
            });

            // This should bring B and C in line.
            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object);

            Assert.Collection(coherencyUpdates,
            u =>
            {
                Assert.Equal(depC, u.From);
                Assert.Equal("v1000", u.To.Version);
                Assert.Equal("commit7", u.To.Commit);
                Assert.Equal("repoC", u.To.RepoUri);
            },
            u =>
            {
                Assert.Equal(depB, u.From);
                Assert.Equal("v10", u.To.Version);
                Assert.Equal("commit5", u.To.Commit);
                Assert.Equal("repoB", u.To.RepoUri);
            });
        }

        private DependencyDetail AddDependency(List<DependencyDetail> details, string name,
            string version, string repo, string commit, bool pinned = false, string coherentParent = null)
        {
            DependencyDetail dep = new DependencyDetail
            {
                Name = name,
                Version = version,
                RepoUri = repo,
                Commit = commit,
                Pinned = pinned,
                Type = DependencyType.Product,
                CoherentParentDependencyName = coherentParent
            };
            details.Add(dep);
            return dep;
        }

        private void RepoHasDependencies(Mock<IRemote> dependencyGraphRemoteMock, 
            string repo, string commit, List<DependencyDetail> dependencies)
        {
            dependencyGraphRemoteMock.Setup(m => m.GetDependenciesAsync(repo, commit, null)).ReturnsAsync(dependencies);
        }

        private void BuildProducesAssets(Mock<IBarClient> barClientMock, string repo,
            string commit, List<(string name, string version)> assets)
        {
            int buildId = GetRandomId();
            var buildAssets = assets.Select<(string, string), Asset>(a =>
            new Asset(GetRandomId(), buildId, true, a.Item1, a.Item2, null));

            var build = new Build(buildId, DateTimeOffset.Now, commit, null, buildAssets.ToImmutableList(), null)
            {
                AzureDevOpsRepository = repo,
                GitHubRepository = repo
            };
            barClientMock.Setup(m => m.GetBuildsAsync(repo, commit)).ReturnsAsync(new List<Build> { build });
        }

        private Random _randomIdGenerator = new Random();
        private int GetRandomId()
        {
            return _randomIdGenerator.Next();
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
