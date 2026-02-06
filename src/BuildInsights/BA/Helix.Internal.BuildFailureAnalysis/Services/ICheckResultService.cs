using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.GitHub.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services;

public interface ICheckResultService
{
    CheckResult GetCheckResult(NamedBuildReference buildReference, ImmutableList<BuildResultAnalysis> buildResultAnalysis, int pendingBuildNames, bool reportSuccessWithKnownIssues);
}
