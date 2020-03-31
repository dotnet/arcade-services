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
            var root = CreateId("root", "0");
            // The siblings should merge into the higher-versioned one.
            var a = CreateId("sibling", "0", new DependencyDetail { Version = "1.0.0" });
            var b = CreateId("sibling", "1", new DependencyDetail { Version = "1.0.1" });

            var incoherent = SourceBuildGraph.Create(new Dictionary<SourceBuildIdentity, SourceBuildIdentity[]>
            {
                [root] = new[] { a, b }
            });

            var coherent = MakeCoherent(incoherent);

            AssertEquivalentSet(
                new[] { root, b },
                coherent.Nodes);

            Assert.Equal(b, coherent.GetUpstreams(root).Single());
            Assert.Empty(coherent.GetDownstreams(root));

            Assert.Empty(coherent.GetUpstreams(b));
            Assert.Equal(root, coherent.GetDownstreams(b).Single());
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
            var root = CreateId("root", "0");
            var a = CreateId("a", "0");
            var b = CreateId("b", "0");
            var cousinA = CreateId("cousin", "0", new DependencyDetail { Version = "5.0.0-beta.0" });
            var cousinB = CreateId("cousin", "1", new DependencyDetail { Version = "5.0.0-beta.1" });

            var incoherent = SourceBuildGraph.Create(new Dictionary<SourceBuildIdentity, SourceBuildIdentity[]>
            {
                [root] = new[] { a, b },
                [a] = new[] { cousinA },
                [b] = new[] { cousinB }
            });

            var coherent = MakeCoherent(incoherent);

            AssertEquivalentSet(new[] { a, b }, coherent.GetDownstreams(cousinB));

            Assert.Equal(cousinB, coherent.GetUpstreams(a).Single());
            Assert.Equal(cousinB, coherent.GetUpstreams(b).Single());

            Assert.DoesNotContain(cousinA, coherent.Nodes);
        }

        [Fact]
        public void CommitDateBreaksVersionTies()
        {
            var root = CreateId("root", "0");
            // When building stable, we may build the same version multiple times. This results in
            // only the commit hash changing. Commit hashes don't sort, so we need to break the tie
            // somehow. The current algorithm uses commit date as a reasonable-seeming heuristic.
            var a = CreateId("sibling", "0", new DependencyDetail { Version = "1.0.0" });
            var b = CreateId("sibling", "1", new DependencyDetail { Version = "1.0.0" });
            var c = CreateId("sibling", "2", new DependencyDetail { Version = "1.0.0" });

            var incoherent = SourceBuildGraph.Create(new Dictionary<SourceBuildIdentity, SourceBuildIdentity[]>
            {
                [root] = new[] { a, b, c }
            });

            var coherent = MakeCoherent(incoherent, new Dictionary<SourceBuildIdentity, DateTimeOffset>
            {
                [a] = DateTimeOffset.FromUnixTimeSeconds(5000),
                [b] = DateTimeOffset.FromUnixTimeSeconds(6000),
                [c] = DateTimeOffset.FromUnixTimeSeconds(3000)
            });

            AssertEquivalentSet(new[] { root, b }, coherent.Nodes);
        }

        private SourceBuildGraph MakeCoherent(
            SourceBuildGraph graph,
            Dictionary<SourceBuildIdentity, DateTimeOffset> commitDateMap = null)
        {
            return new GraphCloneClient().CreateArtificiallyCoherentGraph(
                graph,
                node => commitDateMap?[node] ?? DateTimeOffset.MinValue);
        }

        private SourceBuildIdentity CreateId(
            string name,
            string commit,
            DependencyDetail detail = null)
        {
            return new SourceBuildIdentity
            {
                RepoUri = name,
                Commit = commit,
                Source = detail
            };
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
