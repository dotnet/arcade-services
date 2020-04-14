// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Actions.Clone;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Services.OAuth;
using Xunit;

namespace Microsoft.DotNet.Darc.Tests.Actions.Clone
{
    public class CloneOperationTests
    {
        [Fact]
        public void IncoherentSiblingsMerge()
        {
            var root = Identity("root", "0");
            // The siblings should merge into the higher-versioned one.
            var a = Identity("sibling", "0");
            var b = Identity("sibling", "1");

            var incoherent = Graph(Node(root, Edge(a, "1.0.0"), Edge(b, "1.0.1")));

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
            var root = Identity("root", "0");
            var a = Identity("a", "0");
            var b = Identity("b", "0");
            var cousinA = Identity("cousin", "0");
            var cousinB = Identity("cousin", "1");

            var incoherent = Graph(
                Node(root, Edge(a), Edge(b)),
                Node(a, Edge(cousinA, "5.0.0-beta.0")),
                Node(b, Edge(cousinB, "5.0.0-beta.1")));

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
            var root = Identity("root", "0");
            // When building stable, we may build the same version multiple times. This results in
            // only the commit hash changing. Commit hashes don't sort, so we need to break the tie
            // somehow. The current algorithm uses commit date as a reasonable-seeming heuristic.
            var a = Identity("sibling", "0");
            var b = Identity("sibling", "1");
            var c = Identity("sibling", "2");

            var incoherent = Graph(Node(root, Edge(a), Edge(b), Edge(c)));

            var coherent = MakeCoherent(incoherent, new Dictionary<SourceBuildIdentity, DateTimeOffset>
            {
                [a] = DateTimeOffset.FromUnixTimeSeconds(5000),
                [b] = DateTimeOffset.FromUnixTimeSeconds(6000),
                [c] = DateTimeOffset.FromUnixTimeSeconds(3000)
            });

            AssertEquivalentSet(new[] { root, b }, coherent.Nodes.Select(n => n.Identity));
        }

        [Fact]
        public void TooManyProductCriticalAttributesCausesFailure()
        {
            var root = Identity("root", "0");
            var a = Identity("a", "0");
            var b0 = Identity("b", "0");
            var b1 = Identity("b", "1");

            Assert.Throws<RepositoryMarkedCriticalMoreThanOnceException>(() =>
            {
                MakeCoherent(Graph(
                    Node(
                        root,
                        Edge(a, productCritical: true),
                        Edge(a, productCritical: true)
                    )
                ));
            });

            Assert.Throws<RepositoryMarkedCriticalMoreThanOnceException>(() =>
            {
                MakeCoherent(Graph(
                    Node(
                        root,
                        Edge(a),
                        Edge(b1, productCritical: true)
                    ),
                    Node(
                        a,
                        Edge(b0, productCritical: true)
                    )
                ));
            });
        }

        [Fact]
        public void ProductCriticalDuplicationIsOkWhenTraversalPicksOne()
        {
            var root = Identity("root", "0");
            var a0 = Identity("a", "0");
            var a1 = Identity("a", "1");
            var b0 = Identity("b", "0");
            var b1 = Identity("b", "1");

            // When both a0 => b0 and a1 => b1 are critical, this is fine because we already
            // eliminated a1 by saying root => a0 is critical and root => a1 is not. This happens
            // all the time in the real SDK graph, because dependency criticality doesn't change
            // much (if at all) from one commit to the next, and runtime versions are a very common
            // source of incoherency.
            MakeCoherent(Graph(
                Node(a0, Edge(b0, productCritical: true)),
                Node(a1, Edge(b1, productCritical: true)),
                Node(
                    root,
                    Edge(a0),
                    Edge(a1, productCritical: true)
                )
            ));
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

        private static SourceBuildGraph Graph(params SourceBuildNode[] nodes)
        {
            return SourceBuildGraph.CreateAndAddMissingLeafNodes(nodes);
        }

        private static SourceBuildNode Node(
            SourceBuildIdentity id,
            params SourceBuildEdge[] edges)
        {
            // Fix up back-references here since they're clunky to write in by hand.
            foreach (var e in edges)
            {
                e.Downstream = id;
            }

            return new SourceBuildNode
            {
                Identity = id,
                UpstreamEdges = edges,
            };
        }

        private static SourceBuildEdge Edge(
            SourceBuildIdentity upstream,
            string version = "1.0.0",
            bool productCritical = false)
        {
            return new SourceBuildEdge
            {
                Upstream = upstream,
                ProductCritical = productCritical,
                Source = new DependencyDetail
                {
                    Version = version,
                }
            };
        }
        private static SourceBuildIdentity Identity(string repoUri, string commit)
        {
            return new SourceBuildIdentity
            {
                RepoUri = repoUri,
                Commit = commit,
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
