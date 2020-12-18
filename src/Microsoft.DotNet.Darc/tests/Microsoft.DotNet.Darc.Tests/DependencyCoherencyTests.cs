// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Octokit;

namespace Microsoft.DotNet.Darc.Tests
{
    [TestFixture]
    public class DependencyCoherencyTests
    {
        /// <summary>
        ///     Test that a simple set of non-coherency updates works.
        /// </summary>
        [Test]
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

            updates.Should().SatisfyRespectively(u =>
            {
                u.From.Should().Be(depA);
                u.To.Version.Should().Be("v2");
            });
        }

        /// <summary>
        ///     Test that a simple set of non-coherency updates works.
        /// </summary>
        [Test]
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

            updates.Should().SatisfyRespectively(            u =>
            {
                u.From.Should().Be(depA);
                u.To.Version.Should().Be("v2");
            }, u =>
            {
                u.From.Should().Be(depB);
                u.To.Version.Should().Be("v5");
            });
        }

        /// <summary>
        ///     Test that a simple set of non-coherency updates works.
        ///     
        ///     depB is tied to depA and should not move.
        ///     depA should have its case-corrected.
        /// </summary>
        [Test]
        public async Task CoherencyUpdateTests3()
        {
            Remote remote = new Remote(null, null, NullLogger.Instance);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v3", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false) { Name = "depa", Version = "v2"},
                new AssetData(false) { Name = "depB", Version = "v5"}
            };

            List<DependencyUpdate> updates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            updates.Should().SatisfyRespectively(            u =>
            {
                u.From.Should().Be(depA);
                u.To.Version.Should().Be("v2");
                u.To.Name.Should().Be("depa");
            });
        }

        /// <summary>
        ///     Test a chain with a pinned middle.
        /// </summary>
        [Test]
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

            updates.Should().SatisfyRespectively(            u =>
            {
                u.From.Should().Be(depA);
                u.To.Version.Should().Be("v2");
            });
        }

        /// <summary>
        ///     Test a tree with a pinned head (nothing moves in non-coherency update)
        ///     Test different casings
        /// </summary>
        [Test]
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

            updates.Should().BeEmpty();
        }

        /// <summary>
        ///     Test a simple coherency update
        ///     B and C are tied to A, both should update
        /// </summary>
        [Test]
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

            BuildProducesAssets(barClientMock, "repoA", "commit2", new List<(string name, string version, string[] locations)>
            {
                ("depA", "v2", null)
            });

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, "depY", "v42", "repoB", "commit5", pinned: false);
            AddDependency(repoADeps, "depZ", "v43", "repoC", "commit6", pinned: false);
            RepoHasDependencies(dependencyGraphRemoteMock, "repoA", "commit2", repoADeps);

            BuildProducesAssets(barClientMock, "repoB", "commit5", new List<(string name, string version, string[] locations)>
            {
                ("depB", "v10", null),
                ("depY", "v42", null),
            });

            BuildProducesAssets(barClientMock, "repoC", "commit6", new List<(string name, string version, string[] locations)>
            {
                ("depC", "v1000", null),
                ("depZ", "v43", null),
            });;

            List<DependencyUpdate> nonCoherencyUpdates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            nonCoherencyUpdates.Should().SatisfyRespectively(            u =>
            {
                u.From.Should().Be(depA);
                u.To.Version.Should().Be("v2");
            });

            // Update the current dependency details with the non coherency updates
            UpdateCurrentDependencies(existingDetails, nonCoherencyUpdates);

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Legacy);

            coherencyUpdates.Should().SatisfyRespectively(            u =>
            {
                u.From.Should().Be(depB);
                u.To.Version.Should().Be("v10");
                u.To.Commit.Should().Be("commit5");
                u.To.RepoUri.Should().Be("repoB");
            }, u =>
            {
                u.From.Should().Be(depC);
                u.To.Version.Should().Be("v1000");
                u.To.Commit.Should().Be("commit6");
                u.To.RepoUri.Should().Be("repoC");
            });
        }

        /// <summary>
        ///     Test a simple coherency update
        ///     B tied to A, but B is pinned. Nothing moves.
        /// </summary>
        [Test]
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

            BuildProducesAssets(barClientMock, "repoA", "commit2", new List<(string name, string version, string[] locations)>
            {
                ("depA", "v2", null)
            });

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, "depC", "v10", "repoB", "commit5", pinned: false);
            RepoHasDependencies(dependencyGraphRemoteMock, "repoA", "commit2", repoADeps);

            BuildProducesAssets(barClientMock, "repoB", "commit5", new List<(string name, string version, string[] locations)>
            {
                ("depC", "v10", null),
                ("depB", "v101", null),
            });

            List<DependencyUpdate> nonCoherencyUpdates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            nonCoherencyUpdates.Should().SatisfyRespectively(            u =>
            {
                u.From.Should().Be(depA);
                u.To.Version.Should().Be("v2");
            });

            // Update the current dependency details with the non coherency updates
            UpdateCurrentDependencies(existingDetails, nonCoherencyUpdates);

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Legacy);

            coherencyUpdates.Should().BeEmpty();
        }

        /// <summary>
        ///     Test a simple coherency update
        ///     B tied to A, but no B asset is produced.
        ///     Should throw.
        /// </summary>
        [Test]
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

            BuildProducesAssets(barClientMock, "repoA", "commit2", new List<(string name, string version, string[] locations)>
            {
                ("depA", "v2", null)
            });

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, "depC", "v10", "repoB", "commit5", pinned: false);
            RepoHasDependencies(dependencyGraphRemoteMock, "repoA", "commit2", repoADeps);

            BuildProducesAssets(barClientMock, "repoB", "commit5", new List<(string name, string version, string[] locations)>
            {
                ("depC", "v10", null)
            });

            List<DependencyUpdate> nonCoherencyUpdates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);

            nonCoherencyUpdates.Should().SatisfyRespectively(            u =>
            {
                u.From.Should().Be(depA);
                u.To.Version.Should().Be("v2");
            });

            // Update the current dependency details with the non coherency updates
            UpdateCurrentDependencies(existingDetails, nonCoherencyUpdates);

            await (((Func<Task>)(() => remote.GetRequiredCoherencyUpdatesAsync(
                existingDetails, remoteFactoryMock.Object, CoherencyMode.Legacy)))).Should().ThrowExactlyAsync<DarcCoherencyException>();
        }

        /// <summary>
        ///     Coherent dependency test with a 3 repo chain
        /// </summary>
        /// <returns></returns>
        [TestCase(true)]
        [TestCase(false)]
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

            BuildProducesAssets(barClientMock, "repoA", "commit1", new List<(string name, string version, string[] locations)>
            {
                ("depA", "v1", null)
            });

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, "depY", "v42", "repoB", "commit5", pinned: false);
            RepoHasDependencies(dependencyGraphRemoteMock, "repoA", "commit1", repoADeps);

            BuildProducesAssets(barClientMock, "repoB", "commit5", new List<(string name, string version, string[] locations)>
            {
                ("depB", "v10", null),
                ("depY", "v42", null),
            });

            List<DependencyDetail> repoBDeps = new List<DependencyDetail>();
            AddDependency(repoBDeps, "depZ", "v64", "repoC", "commit7", pinned: false);
            RepoHasDependencies(dependencyGraphRemoteMock, "repoB", "commit5", repoBDeps);

            BuildProducesAssets(barClientMock, "repoC", "commit7", new List<(string name, string version, string[] locations)>
            {
                ("depC", "v1000", null),
                ("depZ", "v64", null),
            });

            // This should bring B and C in line.
            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Legacy);

            coherencyUpdates.Should().SatisfyRespectively(            u =>
            {
                u.From.Should().Be(depC);
                u.To.Version.Should().Be("v1000");
                u.To.Commit.Should().Be("commit7");
                u.To.RepoUri.Should().Be("repoC");
            }, u =>
            {
                u.From.Should().Be(depB);
                u.To.Version.Should().Be("v10");
                u.To.Commit.Should().Be("commit5");
                u.To.RepoUri.Should().Be("repoB");
            });
        }

        /// <summary>
        ///     Coherent dependency test with two 3 repo chains that have a common element.
        ///     This should show only a single update for each element.
        /// </summary>
        /// <returns></returns>
        [TestCase(true)]
        [TestCase(false)]
        public async Task CoherencyUpdateTests10(bool pinHead)
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
            // Both C and D depend on B
            DependencyDetail depC = AddDependency(existingDetails, "depC", "v0", "repoC", "commit3", pinned: false, coherentParent: "depB");
            DependencyDetail depD = AddDependency(existingDetails, "depD", "v50", "repoD", "commit5", pinned: false, coherentParent: "depB");

            BuildProducesAssets(barClientMock, "repoA", "commit1", new List<(string name, string version, string[] locations)>
            {
                ("depA", "v1", null)
            });

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, "depY", "v42", "repoB", "commit5", pinned: false);
            RepoHasDependencies(dependencyGraphRemoteMock, "repoA", "commit1", repoADeps);

            BuildProducesAssets(barClientMock, "repoB", "commit5", new List<(string name, string version, string[] locations)>
            {
                ("depB", "v10", null),
                ("depY", "v42", null),
            });

            List<DependencyDetail> repoBDeps = new List<DependencyDetail>();
            AddDependency(repoBDeps, "depQ", "v66", "repoD", "commit35", pinned: false);
            AddDependency(repoBDeps, "depZ", "v64", "repoC", "commit7", pinned: false);
            RepoHasDependencies(dependencyGraphRemoteMock, "repoB", "commit5", repoBDeps);

            BuildProducesAssets(barClientMock, "repoC", "commit7", new List<(string name, string version, string[] locations)>
            {
                ("depC", "v1000", null),
                ("depZ", "v64", null),
            });

            BuildProducesAssets(barClientMock, "repoD", "commit35", new List<(string name, string version, string[] locations)>
            {
                ("depD", "v1001", null),
                ("depQ", "v66", null),
            });

            // This should bring B and C in line.
            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Legacy);

            coherencyUpdates.Should().SatisfyRespectively(            u =>
            {
                u.From.Should().Be(depC);
                u.To.Version.Should().Be("v1000");
                u.To.Commit.Should().Be("commit7");
                u.To.RepoUri.Should().Be("repoC");
            }, u =>
            {
                u.From.Should().Be(depB);
                u.To.Version.Should().Be("v10");
                u.To.Commit.Should().Be("commit5");
                u.To.RepoUri.Should().Be("repoB");
            }, u =>
            {
                u.From.Should().Be(depD);
                u.To.Version.Should().Be("v1001");
                u.To.Commit.Should().Be("commit35");
                u.To.RepoUri.Should().Be("repoD");
            });
        }

        /// <summary>
        ///     Test that a simple set of non-coherency updates works,
        ///     and that a file with no coherency updates does nothing
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests1()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false);

            List<AssetData> assets = new List<AssetData>()
            {
                new AssetData(false) { Name = "depA", Version = "v2"}
            };

            List<DependencyUpdate> nonCoherencyUpdates =
                await remote.GetRequiredNonCoherencyUpdatesAsync("repoA", "commit2", assets, existingDetails);
            
            nonCoherencyUpdates.Should().SatisfyRespectively(u =>
            {
                u.From.Should().Be(depA);
                u.To.Version.Should().Be("v2");
            });

            // Update the current dependency details with the non coherency updates
            UpdateCurrentDependencies(existingDetails, nonCoherencyUpdates);

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            // Should have no coherency updates
            coherencyUpdates.Should().BeEmpty();
        }

        /// <summary>
        ///     Test that a simple strict coherency update fails because depA does not have a dependency on depB.
        ///     Strict coherency does not involve any graph build, and only
        ///     looks one level deep.
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests2()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");

            DarcCoherencyException coherencyException = (await (((Func<Task>)(async () =>
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict)))).Should().ThrowAsync<DarcCoherencyException>()).Which;

            // Coherency exception should be for depB, saying that repoA @ commit1 has no such dependency
            coherencyException.Errors.Should().SatisfyRespectively(                e =>
                {
                    e.Dependency.Name.Should().Be(depB.Name);
                    e.Error.Should().Be($"{depA.RepoUri} @ {depA.Commit} does not contain dependency {depB.Name}");
                });
        }

        /// <summary>
        ///     Test that a simple strict coherency update fails because depA does not have a dependency on depB.
        ///     Strict coherency does not involve any graph build, and only
        ///     looks one level deep. This test adds another layer where depB appears. That version should
        ///     not be chosen.
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests3()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, "depY", "v42", "repoB", "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            DarcCoherencyException coherencyException = (await (((Func<Task>)(async () =>
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict)))).Should().ThrowAsync<DarcCoherencyException>()).Which;

            // Coherency exception should be for depB, saying that repoA @ commit1 has no such dependency
            coherencyException.Errors.Should().SatisfyRespectively(                e =>
                {
                    e.Dependency.Name.Should().Be(depB.Name);
                    e.Error.Should().Be($"{depA.RepoUri} @ {depA.Commit} does not contain dependency {depB.Name}");
                });
        }

        /// <summary>
        ///     Test that a simple strict coherency passes and chooses the right version for depB.
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests4()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, depB.Name, "v42", depB.RepoUri, "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            List<DependencyUpdate> coherencyUpdates = 
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            coherencyUpdates.Should().SatisfyRespectively(                u =>
                {
                    u.From.Should().Be(depB);
                    u.To.Version.Should().Be("v42");
                    u.To.Commit.Should().Be("commit5");
                    u.To.RepoUri.Should().Be(depB.RepoUri);
                    u.To.Name.Should().Be(depB.Name);
                });
        }

        /// <summary>
        ///     Test that a strict update on a dependency chain works.
        ///     The dependency chain means the head of the chain moves first,
        ///     potentially affecting other parts of the chain.
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests5()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");
            DependencyDetail depC = AddDependency(existingDetails, "depC", "v1", "repoC", "commit1", pinned: false, coherentParent: "depB");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, depB.Name, "v42", depB.RepoUri, "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            // This set of deps should not be used because B should move before C gets updated
            List<DependencyDetail> repoBAtCommit1Deps = new List<DependencyDetail>();
            AddDependency(repoBAtCommit1Deps, depC.Name, "v101", depC.RepoUri, "commit100", pinned: false);
            RepoHasDependencies(remoteMock, "repoB", "commit1", repoBAtCommit1Deps);

            List<DependencyDetail> repoBAtCommit5Deps = new List<DependencyDetail>();
            AddDependency(repoBAtCommit5Deps, depC.Name, "v1000", depC.RepoUri, "commit1000", pinned: false);
            RepoHasDependencies(remoteMock, "repoB", "commit5", repoBAtCommit5Deps);

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            coherencyUpdates.Should().SatisfyRespectively(                u =>
                {
                    u.From.Should().Be(depB);
                    u.To.Version.Should().Be("v42");
                    u.To.Commit.Should().Be("commit5");
                    u.To.RepoUri.Should().Be(depB.RepoUri);
                    u.To.Name.Should().Be(depB.Name);
                }, u =>
                {
                    u.From.Should().Be(depC);
                    u.To.Version.Should().Be("v1000");
                    u.To.Commit.Should().Be("commit1000");
                    u.To.RepoUri.Should().Be(depC.RepoUri);
                    u.To.Name.Should().Be(depC.Name);
                });
        }

        /// <summary>
        ///     Test that a strict update on a dependency chain with some pinning works
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests6()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");
            // This is pinned and so should not move, meaning that D should update based on C @ commit1
            DependencyDetail depC = AddDependency(existingDetails, "depC", "v1", "repoC", "commit1", pinned: true, coherentParent: "depB");
            DependencyDetail depD = AddDependency(existingDetails, "depD", "v1", "repoD", "commit1", pinned: false, coherentParent: "depC");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, depB.Name, "v42", depB.RepoUri, "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            // This set of deps should not be used because B should move before C gets updated
            List<DependencyDetail> repoBAtCommit1Deps = new List<DependencyDetail>();
            AddDependency(repoBAtCommit1Deps, depC.Name, "v101", depC.RepoUri, "commit100", pinned: false);
            RepoHasDependencies(remoteMock, "repoB", "commit1", repoBAtCommit1Deps);

            List<DependencyDetail> repoBAtCommit5Deps = new List<DependencyDetail>();
            AddDependency(repoBAtCommit5Deps, depC.Name, "v1000", depC.RepoUri, "commit1000", pinned: false);
            RepoHasDependencies(remoteMock, "repoB", "commit5", repoBAtCommit5Deps);

            List<DependencyDetail> repoCAtCommit1Deps = new List<DependencyDetail>();
            AddDependency(repoCAtCommit1Deps, depD.Name, "v2.5", depD.RepoUri, "commit2.5", pinned: false);
            RepoHasDependencies(remoteMock, "repoC", "commit1", repoCAtCommit1Deps);

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            coherencyUpdates.Should().SatisfyRespectively(                u =>
                {
                    u.From.Should().Be(depB);
                    u.To.Version.Should().Be("v42");
                    u.To.Commit.Should().Be("commit5");
                    u.To.RepoUri.Should().Be(depB.RepoUri);
                    u.To.Name.Should().Be(depB.Name);
                }, u =>
                {
                    u.From.Should().Be(depD);
                    u.To.Version.Should().Be("v2.5");
                    u.To.Commit.Should().Be("commit2.5");
                    u.To.RepoUri.Should().Be(depD.RepoUri);
                    u.To.Name.Should().Be(depD.Name);
                });
        }

        /// <summary>
        ///     Test that disambiguation with build info works if it is available.
        ///     This test has the asset we are going to update to being generated by
        ///     a build of the CPD parents commit. No location
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests7()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, depB.Name, "v42", depB.RepoUri, "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            BuildProducesAssets(barClientMock, "repoB", "commit5", new List<(string name, string version, string[] locations)>
            {
                ("depB", "v42", null)
            });

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            coherencyUpdates.Should().SatisfyRespectively(                u =>
                {
                    u.From.Should().Be(depB);
                    u.To.Version.Should().Be("v42");
                    u.To.Commit.Should().Be("commit5");
                    u.To.RepoUri.Should().Be(depB.RepoUri);
                    u.To.Name.Should().Be(depB.Name);
                    u.To.Locations.Should().BeNull();
                });
        }

        /// <summary>
        ///     Test that disambiguation with build info works if it is available.
        ///     This test has the asset we are going to update to being generated by
        ///     a build of the CPD parents commit. This asset has a location.
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests8()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, depB.Name, "v42", depB.RepoUri, "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            BuildProducesAssets(barClientMock, "repoB", "commit5", new List<(string name, string version, string[])>
            {
                ("depB", "v42", new string[] { "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json" } )
            });

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            coherencyUpdates.Should().SatisfyRespectively(                u =>
                {
                    u.From.Should().Be(depB);
                    u.To.Version.Should().Be("v42");
                    u.To.Commit.Should().Be("commit5");
                    u.To.RepoUri.Should().Be(depB.RepoUri);
                    u.To.Name.Should().Be(depB.Name);
                    u.To.Locations.Should().SatisfyRespectively(                        u =>
                        {
                            u.Should().Be("https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json");
                        }
);
                });
        }

        /// <summary>
        ///     Test that disambiguation with build info works if it is available.
        ///     This test has the asset we are going to update to being generated by
        ///     a build of the CPD parents commit. This asset has two locations.
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests9()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, depB.Name, "v42", depB.RepoUri, "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            BuildProducesAssets(barClientMock, "repoB", "commit5", new List<(string name, string version, string[])>
            {
                ("depB", "v42", new string[] {
                    "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json",
                    "https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json"
                } )
            });

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            coherencyUpdates.Should().SatisfyRespectively(                u =>
                {
                    u.From.Should().Be(depB);
                    u.To.Version.Should().Be("v42");
                    u.To.Commit.Should().Be("commit5");
                    u.To.RepoUri.Should().Be(depB.RepoUri);
                    u.To.Name.Should().Be(depB.Name);
                    u.To.Locations.Should().SatisfyRespectively(                        u =>
                        {
                            u.Should().Be("https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json");
                        }, u =>
                        {
                            u.Should().Be("https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json");
                        }
);
                });
        }

        /// <summary>
        ///     Test that disambiguation with build info works if it is available.
        ///     This test has the asset we are going to update to being generated by
        ///     two separate builds of the CPD parents commit. Only one asset has a location,
        ///     but doesn't match the nuget.config. In this case, the lateset build should be returned.
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests10()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Mock<IGitRepo> gitRepoMock = new Mock<IGitRepo>();
            Remote remote = new Remote(gitRepoMock.Object, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, depB.Name, "v42", depB.RepoUri, "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            RepoHadBuilds(barClientMock, "repoB", "commit5",
                new List<Build>
                {
                    CreateBuild("repoB", "commit5", new List<(string name, string version, string[])>
                    {
                        ("depB", "v42", new string[] {
                            "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json",
                            "https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json"
                        } )
                    }, 13),

                    CreateBuild("repoB", "commit5", new List<(string name, string version, string[])>
                    {
                        ("depB", "v42", null )
                    }, 1)
                });

            // Repo has no feeds
            RepositoryHasFeeds(gitRepoMock, "repoA", "commit1", new string[] { });

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            coherencyUpdates.Should().SatisfyRespectively(                u =>
                {
                    u.From.Should().Be(depB);
                    u.To.Version.Should().Be("v42");
                    u.To.Commit.Should().Be("commit5");
                    u.To.RepoUri.Should().Be(depB.RepoUri);
                    u.To.Name.Should().Be(depB.Name);
                    u.To.Locations.Should().SatisfyRespectively(                        u =>
                        {
                            u.Should().Be("https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json");
                        }, u =>
                        {
                            u.Should().Be("https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json");
                        }
);
                });
        }

        /// <summary>
        ///     Test that disambiguation with build info works if it is available.
        ///     This test has the asset we are going to update to being generated by
        ///     two separate builds of the CPD parents commit. Only one asset has a location
        ///     and matches what is in the feed list.
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests11()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Mock<IGitRepo> gitRepoMock = new Mock<IGitRepo>();
            Remote remote = new Remote(gitRepoMock.Object, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();
            remoteMock.Setup(m => m.GetPackageSourcesAsync("repoA", "commit1")).ReturnsAsync(
                new String[] { "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json" });

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, depB.Name, "v42", depB.RepoUri, "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            RepoHadBuilds(barClientMock, "repoB", "commit5",
                new List<Build>
                {
                    CreateBuild("repoB", "commit5", new List<(string name, string version, string[])>
                    {
                        ("depB", "v42", null )
                    }),
                    CreateBuild("repoB", "commit5", new List<(string name, string version, string[])>
                    {
                        ("depB", "v42", new string[] {
                            "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json",
                            "https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json"
                        } )
                    })
                });

            List <DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            coherencyUpdates.Should().SatisfyRespectively(                u =>
                {
                    u.From.Should().Be(depB);
                    u.To.Version.Should().Be("v42");
                    u.To.Commit.Should().Be("commit5");
                    u.To.RepoUri.Should().Be(depB.RepoUri);
                    u.To.Name.Should().Be(depB.Name);
                    u.To.Locations.Should().SatisfyRespectively(                        u =>
                        {
                            u.Should().Be("https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json");
                        }, u =>
                        {
                            u.Should().Be("https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json");
                        }
);
                });
        }

        /// <summary>
        ///     Test that disambiguation with build info works if it is available.
        ///     This test has the asset we are going to update to being generated by
        ///     two separate builds of the CPD parents commit. Both builds have the assets,
        ///     and neither matches. The later build should be returned.
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests12()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Mock<IGitRepo> gitRepoMock = new Mock<IGitRepo>();
            Remote remote = new Remote(gitRepoMock.Object, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, depB.Name, "v42", depB.RepoUri, "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            RepoHadBuilds(barClientMock, "repoB", "commit5",
                new List<Build>
                {
                    CreateBuild("repoB", "commit5", new List<(string name, string version, string[])>
                    {
                        ("depB", "v42", new string[] {
                            "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5/nuget/v3/index.json",
                            "https://dotnetfeed.blob.core.windows.net/dotnet-core2/index.json"
                        } )
                    }, 11),
                    CreateBuild("repoB", "commit5", new List<(string name, string version, string[])>
                    {
                        ("depB", "v42", new string[] {
                            "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json",
                            "https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json"
                        } )
                    }, 10)
                });

            RepositoryHasFeeds(gitRepoMock, "repoA", "commit1",
                new string[] { "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet566/nuget/v3/index.json" });

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            coherencyUpdates.Should().SatisfyRespectively(                u =>
                {
                    u.From.Should().Be(depB);
                    u.To.Version.Should().Be("v42");
                    u.To.Commit.Should().Be("commit5");
                    u.To.RepoUri.Should().Be(depB.RepoUri);
                    u.To.Name.Should().Be(depB.Name);
                    u.To.Locations.Should().SatisfyRespectively(                        u =>
                        {
                            u.Should().Be("https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5/nuget/v3/index.json");
                        }, u =>
                        {
                            u.Should().Be("https://dotnetfeed.blob.core.windows.net/dotnet-core2/index.json");
                        }
);
                });
        }

        /// <summary>
        ///     Test that disambiguation with build info works if it is available.
        ///     This test has the asset we are going to update to being generated by
        ///     two separate builds of the CPD parents commit. Both builds have the assets,
        ///     and one matches. The matching build should be returned
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests13()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Mock<IGitRepo> gitRepoMock = new Mock<IGitRepo>();
            Remote remote = new Remote(gitRepoMock.Object, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, depB.Name, "v42", depB.RepoUri, "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            // Older build has matching feeds.
            RepoHadBuilds(barClientMock, "repoB", "commit5",
                new List<Build>
                {
                    CreateBuild("repoB", "commit5", new List<(string name, string version, string[])>
                    {
                        ("depB", "v42", new string[] {
                            "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet566/nuget/v3/index.json",
                            "https://dotnetfeed.blob.core.windows.net/dotnet-core2/index.json"
                        } )
                    }, 10),
                    CreateBuild("repoB", "commit5", new List<(string name, string version, string[])>
                    {
                        ("depB", "v42", new string[] {
                            "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json",
                            "https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json"
                        } )
                    }, 11)
                });

            RepositoryHasFeeds(gitRepoMock, "repoA", "commit1",
                new string[] { "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet566/nuget/v3/index.json" });

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            coherencyUpdates.Should().SatisfyRespectively(                u =>
                {
                    u.From.Should().Be(depB);
                    u.To.Version.Should().Be("v42");
                    u.To.Commit.Should().Be("commit5");
                    u.To.RepoUri.Should().Be(depB.RepoUri);
                    u.To.Name.Should().Be(depB.Name);
                    u.To.Locations.Should().SatisfyRespectively(                        u =>
                        {
                            u.Should().Be("https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json");
                        }, u =>
                        {
                            u.Should().Be("https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json");
                        }
);
                });
        }

        /// <summary>
        ///     Test that disambiguation with build info works if it is available.
        ///     This test has the asset we are going to update to being generated by
        ///     two separate builds of the CPD parents commit. Both builds have the assets,
        ///     and both match. The later build id should be returned
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests14()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Mock<IGitRepo> gitRepoMock = new Mock<IGitRepo>();
            Remote remote = new Remote(gitRepoMock.Object, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, depB.Name, "v42", depB.RepoUri, "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            // Older build has matching feeds.
            RepoHadBuilds(barClientMock, "repoB", "commit5",
                new List<Build>
                {
                    CreateBuild("repoB", "commit5", new List<(string name, string version, string[])>
                    {
                        ("depB", "v42", new string[] {
                            "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet566/nuget/v3/index.json",
                            "https://dotnetfeed.blob.core.windows.net/dotnet-core2/index.json"
                        } )
                    }, 10),
                    CreateBuild("repoB", "commit5", new List<(string name, string version, string[])>
                    {
                        ("depB", "v42", new string[] {
                            "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet566/nuget/v3/index.json",
                            "https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json"
                        } )
                    }, 11)
                });

            RepositoryHasFeeds(gitRepoMock, "repoA", "commit1",
                new string[] { "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet566/nuget/v3/index.json" });

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            coherencyUpdates.Should().SatisfyRespectively(                u =>
                {
                    u.From.Should().Be(depB);
                    u.To.Version.Should().Be("v42");
                    u.To.Commit.Should().Be("commit5");
                    u.To.RepoUri.Should().Be(depB.RepoUri);
                    u.To.Name.Should().Be(depB.Name);
                    u.To.Locations.Should().SatisfyRespectively(                        u =>
                        {
                            u.Should().Be("https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet566/nuget/v3/index.json");
                        }, u =>
                        {
                            u.Should().Be("https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json");
                        }
);
                });
        }

        /// <summary>
        ///     Test that disambiguation with build info works if it is available.
        ///     This test has the asset we are going to update not being generated
        ///     by the CPD parent's build.
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests15()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Remote remote = new Remote(null, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, depB.Name, "v42", depB.RepoUri, "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            BuildProducesAssets(barClientMock, "repoB", "commit5", new List<(string name, string version, string[])> {});

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            coherencyUpdates.Should().SatisfyRespectively(                u =>
                {
                    u.From.Should().Be(depB);
                    u.To.Version.Should().Be("v42");
                    u.To.Commit.Should().Be("commit5");
                    u.To.RepoUri.Should().Be(depB.RepoUri);
                    u.To.Name.Should().Be(depB.Name);
                    u.To.Locations.Should().BeNull();
                });
        }

        /// <summary>
        ///     Test that disambiguation with build info works if it is available.
        ///     This test has the the asset only generated by one of the builds.
        /// </summary>
        [Test]
        public async Task StrictCoherencyUpdateTests16()
        {
            // Initialize
            Mock<IBarClient> barClientMock = new Mock<IBarClient>();
            Mock<IGitRepo> gitRepoMock = new Mock<IGitRepo>();
            Remote remote = new Remote(gitRepoMock.Object, barClientMock.Object, NullLogger.Instance);

            // Mock the remote used by build dependency graph to gather dependency details.
            Mock<IRemote> remoteMock = new Mock<IRemote>();

            // Always return the main remote.
            var remoteFactoryMock = new Mock<IRemoteFactory>();
            remoteFactoryMock.Setup(m => m.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>())).ReturnsAsync(remoteMock.Object);
            remoteFactoryMock.Setup(m => m.GetBarOnlyRemoteAsync(It.IsAny<ILogger>())).ReturnsAsync(remote);

            List<DependencyDetail> existingDetails = new List<DependencyDetail>();
            DependencyDetail depA = AddDependency(existingDetails, "depA", "v1", "repoA", "commit1", pinned: false);
            DependencyDetail depB = AddDependency(existingDetails, "depB", "v1", "repoB", "commit1", pinned: false, coherentParent: "depA");

            List<DependencyDetail> repoADeps = new List<DependencyDetail>();
            AddDependency(repoADeps, depB.Name, "v42", depB.RepoUri, "commit5", pinned: false);
            RepoHasDependencies(remoteMock, "repoA", "commit1", repoADeps);

            RepoHadBuilds(barClientMock, "repoB", "commit5",
                new List<Build>
                {
                    CreateBuild("repoB", "commit5", new List<(string name, string version, string[])>
                    {
                        ("depB", "v42", new string[] {
                            "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json",
                            "https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json"
                        } )
                    }, 13),

                    CreateBuild("repoB", "commit5", new List<(string name, string version, string[])>
                    {
                        ("depB", "v43", null )
                    }, 1)
                });

            // Repo has no feeds
            RepositoryHasFeeds(gitRepoMock, "repoA", "commit1", new string[] { });

            List<DependencyUpdate> coherencyUpdates =
                await remote.GetRequiredCoherencyUpdatesAsync(existingDetails, remoteFactoryMock.Object, CoherencyMode.Strict);

            coherencyUpdates.Should().SatisfyRespectively(                u =>
                {
                    u.From.Should().Be(depB);
                    u.To.Version.Should().Be("v42");
                    u.To.Commit.Should().Be("commit5");
                    u.To.RepoUri.Should().Be(depB.RepoUri);
                    u.To.Name.Should().Be(depB.Name);
                    u.To.Locations.Should().SatisfyRespectively(                        u =>
                        {
                            u.Should().Be("https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json");
                        }, u =>
                        {
                            u.Should().Be("https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json");
                        }
);
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

        private void RepoHasDependencies(Mock<IRemote> remoteMock, 
            string repo, string commit, List<DependencyDetail> dependencies)
        {
            remoteMock.Setup(m => m.GetDependenciesAsync(repo, commit, null, false)).ReturnsAsync(dependencies);
        }

        private void RepoHadBuilds(Mock<IBarClient> barClientMock, string repo, string commit, IEnumerable<Build> builds)
        {
            barClientMock.Setup(m => m.GetBuildsAsync(repo, commit)).ReturnsAsync(builds);
        }

        private Build CreateBuild(string repo, string commit,
            List<(string name, string version, string[] locations)> assets, int buildId = -1)
        {
            if (buildId == -1)
            {
                buildId = GetRandomId();
            }
            var buildAssets = assets.Select<(string, string, string[]), Asset>(a =>
                new Asset(
                    GetRandomId(),
                    buildId,
                    true,
                    a.Item1,
                    a.Item2,
                    a.Item3?.Select(location => new AssetLocation(GetRandomId(), LocationType.NugetFeed, location)).ToImmutableList()));

            return new Build(buildId, DateTimeOffset.Now, 0, false, false, commit, null, buildAssets.ToImmutableList(), null, null)
            {
                AzureDevOpsRepository = repo,
                GitHubRepository = repo
            };
        }

        private void BuildProducesAssets(Mock<IBarClient> barClientMock, string repo,
            string commit, List<(string name, string version, string[] locations)> assets, int buildId = -1)
        {
            Build build = CreateBuild(repo, commit, assets, buildId);
            barClientMock.Setup(m => m.GetBuildsAsync(repo, commit)).ReturnsAsync(new List<Build> { build });
        }

        private void RepositoryHasFeeds(Mock<IGitRepo> barClientMock,
            string repo, string commit, string[] feeds)
        {
            const string baseNugetConfig = @"<?xml version=""1.0"" encoding=""utf - 8""?>
<configuration>
<packageSources>
<clear/>
FEEDSHERE
</packageSources>
</configuration>";
            string nugetConfig = baseNugetConfig.Replace("FEEDSHERE",
                string.Join(Environment.NewLine, feeds.Select(feed => $@"<add key = ""{GetRandomId()}"" value = ""{feed}"" />")));

            barClientMock.Setup(m => m.GetFileContentsAsync(VersionFiles.NugetConfig, repo, commit)).ReturnsAsync(nugetConfig);
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
