using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.KnownIssues.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services;

public interface ITestResultService
{
    Task<TestKnownIssuesAnalysis> GetTestFailingWithKnownIssuesAnalysis(IReadOnlyList<TestRunDetails> failingTestCaseResults, IReadOnlyList<KnownIssue> knownIssues, string orgId);
}
