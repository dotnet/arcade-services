// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class HelixMetadata
    {
        public ImmutableList<TestCaseResult> RerunTests { get; }
        public ImmutableDictionary<string, string> TestLists { get;  }
        public int Partitions { get; }
        public ImmutableDictionary<string, int> ResultCounts { get; }

        public HelixMetadata(ImmutableList<TestCaseResult> rerunTests, ImmutableDictionary<string, string> testLists, int partitions, ImmutableDictionary<string, int> resultCounts)
        {
            RerunTests = rerunTests;
            TestLists = testLists;
            Partitions = partitions;
            ResultCounts = resultCounts;
        }

        public HelixMetadata(IEnumerable<TestCaseResult> rerunTests, IEnumerable<KeyValuePair<string, string>> testLists, int partitions, IEnumerable<KeyValuePair<string, int>> resultCounts)
        {
            RerunTests = rerunTests.ToImmutableList();
            TestLists = testLists.ToImmutableDictionary(p => p.Key, p => p.Value);
            Partitions = partitions;
            ResultCounts = resultCounts.ToImmutableDictionary(p => p.Key, p => p.Value);;
        }
    }
}
