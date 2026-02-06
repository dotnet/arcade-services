using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    /// <summary>
    /// Collection of TestCaseR
    /// </summary>
    public class TestHistoryByBranch
    {
        public GitRef RefName { get; }
        public ImmutableList<TestCaseResult> Results { get; }

        public TestHistoryByBranch(GitRef refName, ImmutableList<TestCaseResult> results)
        {
            RefName = refName;
            Results = results;
        }

        public TestHistoryByBranch(GitRef refName, IEnumerable<TestCaseResult> results)
            : this(refName, results.ToImmutableList())
        {
        }
    }
}
