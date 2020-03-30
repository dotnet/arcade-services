// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Actions.Clone;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Microsoft.DotNet.Darc.Tests.Actions.Clone
{
    public class CloneOperationTests
    {
        [Fact]
        public void IncoherentSiblingsMergeTest()
        {
            var root = new SourceBuildIdentity("root", "0", null);
            // The siblings should merge into the higher-versioned one.
            var a = new SourceBuildIdentity("sibling", "0", new DependencyDetail { Version = "1.0.0" });
            var b = new SourceBuildIdentity("sibling", "1", new DependencyDetail { Version = "1.0.1" });

            var incoherent = SourceBuildGraph.Create(new Dictionary<SourceBuildIdentity, SourceBuildIdentity[]>
            {
                [root] = new[] { a, b }
            });

            var coherent = incoherent.CreateArtificiallyCoherentGraph();

            AssertEquivalentSet(
                new[] { root, b },
                coherent.Nodes);

            Assert.Equal(b, coherent.GetUpstreams(root).Single());
            Assert.Empty(coherent.GetDownstreams(root));

            Assert.Empty(coherent.GetUpstreams(b));
            Assert.Equal(root, coherent.GetDownstreams(b).Single());
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
