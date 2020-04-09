// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Actions.Clone;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Microsoft.DotNet.Darc.Tests.Actions.Clone
{
    public class CloneOperationTests
    {
        [Fact]
        public void IncoherentSiblingsMerge()
        {
            var root = CreateIdentity("root", "0", null);
            // The siblings should merge into the higher-versioned one.
            var a = CreateIdentity("sibling", "0", new DependencyDetail { Version = "1.0.0" });
            var b = CreateIdentity("sibling", "1", new DependencyDetail { Version = "1.0.1" });

            var incoherent = SourceBuildGraph.Create(new[]
            {
                //new SourceBuildNode{Identity = root, Upstream = new [] {a, b}},
                new SourceBuildNode{Identity = a},
                new SourceBuildNode{Identity = b}
            }, null);

            var coherent = MakeCoherent(incoherent);

            AssertEquivalentSet(
                new[] { root, b },
                coherent.Nodes.Select(n => n.Identity));

            Assert.Equal(b, coherent.GetUpstreams(root).Single().Identity);
            Assert.Empty(coherent.GetDownstreams(root));

            Assert.Empty(coherent.GetUpstreams(b));
            Assert.Equal(root, coherent.GetDownstreams(b).Single().Identity);
        }

        [Fact]
        public void IncoherentCousinsMerge()
        {
            // If a great-grandparent depends on separate parents which depend on incoherent
            // cousins, the cousins should merge and form a diamond.
            //   root => a => cousinA
            //       \=> b => cousinB
            // to
            //   root => a => cousinB
            //       \=> b ===^
            var root = CreateIdentity("root", "0", null);
            var a = CreateIdentity("a", "0", null);
            var b = CreateIdentity("b", "0", null);
            var cousinA = CreateIdentity("cousin", "0", new DependencyDetail { Version = "5.0.0-beta.0" });
            var cousinB = CreateIdentity("cousin", "1", new DependencyDetail { Version = "5.0.0-beta.1" });

            var incoherent = SourceBuildGraph.Create(new[]
            {
                //new SourceBuildNode{Identity = root, Upstreams = new [] {a, b}},
                //new SourceBuildNode{Identity = a, Upstreams = new [] {cousinA}},
                //new SourceBuildNode{Identity = b, Upstreams = new [] {cousinB}},
                new SourceBuildNode{Identity = cousinA},
                new SourceBuildNode{Identity = cousinB}
            }, null);

            var coherent = MakeCoherent(incoherent);

            AssertEquivalentSet(
                new[] { a, b },
                coherent.GetDownstreams(cousinB).Select(n => n.Identity));

            Assert.Equal(cousinB, coherent.GetUpstreams(a).Single().Identity);
            Assert.Equal(cousinB, coherent.GetUpstreams(b).Single().Identity);

            Assert.DoesNotContain(cousinA, coherent.Nodes.Select(n => n.Identity));
        }

        [Fact]
        public void CommitDateBreaksVersionTies()
        {
            var root = CreateIdentity("root", "0", null);
            // When building stable, we may build the same version multiple times. This results in
            // only the commit hash changing. Commit hashes don't sort, so we need to break the tie
            // somehow. The current algorithm uses commit date as a reasonable-seeming heuristic.
            var a = CreateIdentity("sibling", "0", new DependencyDetail { Version = "1.0.0" });
            var b = CreateIdentity("sibling", "1", new DependencyDetail { Version = "1.0.0" });
            var c = CreateIdentity("sibling", "2", new DependencyDetail { Version = "1.0.0" });

            var incoherent = SourceBuildGraph.Create(new[]
            {
                //new SourceBuildNode{Identity = root, Upstreams = new [] {a, b, c}},
                new SourceBuildNode{Identity = a},
                new SourceBuildNode{Identity = b},
                new SourceBuildNode{Identity = c}
            }, null);

            var coherent = MakeCoherent(incoherent, new Dictionary<SourceBuildIdentity, DateTimeOffset>
            {
                [a] = DateTimeOffset.FromUnixTimeSeconds(5000),
                [b] = DateTimeOffset.FromUnixTimeSeconds(6000),
                [c] = DateTimeOffset.FromUnixTimeSeconds(3000)
            });

            AssertEquivalentSet(new[] { root, b }, coherent.Nodes.Select(n => n.Identity));
        }

        private SourceBuildIdentity CreateIdentity(
            string repoUri,
            string commit,
            DependencyDetail detail)
        {
            return new SourceBuildIdentity
            {
                RepoUri = repoUri,
                Commit = commit,
                //Sources = detail == null ? null : new[] { detail }
            };
        }

        private SourceBuildGraph MakeCoherent(
            SourceBuildGraph graph,
            Dictionary<SourceBuildIdentity, DateTimeOffset> commitDateMap = null)
        {
            var client = new GraphCloneClient
            {
                GetCommitDate = node => commitDateMap?[node] ?? DateTimeOffset.MinValue
            };

            return client.CreateArtificiallyCoherentGraph(graph);
        }

        private static void AssertEquivalentSet<T>(
            IEnumerable<T> expected,
            IEnumerable<T> actual,
            IEqualityComparer<T> comparer = null)
        {
            comparer ??= EqualityComparer<T>.Default;

            var unexpected = actual.Except(expected, comparer).ToArray();
            var missing = expected.Except(actual, comparer).ToArray();

            Assert.False(unexpected.Any(), $"Unexpected elements: {string.Join(", ", unexpected)}");
            Assert.False(missing.Any(), $"Missing elements: {string.Join(", ", missing)}");
        }
    }
}
