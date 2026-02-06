using System.Collections.Generic;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    /// <summary>
    /// TestCaseResult equality test using only the Automated Test Name
    /// </summary>
    public class TestCaseResultNameComparer : IEqualityComparer<TestCaseResult>
    {
        public bool Equals(TestCaseResult x, TestCaseResult y)
        {
            return x?.Name.Equals(y?.Name) ?? false;
        }

        public int GetHashCode(TestCaseResult obj)
        {
            return obj?.Name.GetHashCode() ?? 0;
        }
    }
}
