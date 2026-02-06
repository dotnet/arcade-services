using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.ApplicationInsights;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Services;
using Microsoft.Internal.Helix.GitHub.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Providers;

public class CheckResultProvider : ICheckResultService
{
    private readonly TelemetryClient _telemetryClient;

    public CheckResultProvider(TelemetryClient telemetryClient)
    {
        _telemetryClient = telemetryClient;
    }

    public CheckResult GetCheckResult(NamedBuildReference buildReference, ImmutableList<BuildResultAnalysis> buildResultAnalysis, int pendingBuildNames, bool reportSuccessWithKnownIssues)
    {
        CheckResult checkResult = GetCheckResult(buildResultAnalysis.Select(t => t.BuildStatus).ToList(), pendingBuildNames);
        CheckResult checkResultWithKnownIssues = GetCheckResult(buildResultAnalysis.Select(GetBuildStatusWithKnownIssues).ToList(), pendingBuildNames);

        _telemetryClient.TrackEvent(
            "BuildAnalysisCheckResult",
            new Dictionary<string, string>
            {
                {"repository", buildReference.RepositoryId},
                {"commit", buildReference.SourceSha},
                {"checkResult", checkResult.ToString()},
                {"checkResultWithKnownIssues", checkResultWithKnownIssues.ToString()}
            }
        );

        return reportSuccessWithKnownIssues ? checkResultWithKnownIssues : checkResult;
    }

    private static CheckResult GetCheckResult(List<BuildStatus> buildStatuses, int pendingPipelines)
    {
        if (buildStatuses.Any(buildStatus => buildStatus == BuildStatus.Failed))
        {
            return CheckResult.Failed;
        }

        if (pendingPipelines > 0)
        {
            return CheckResult.InProgress;
        }

        if (buildStatuses.Any() && buildStatuses.All(buildStatus => buildStatus is BuildStatus.Succeeded))
        {
            return CheckResult.Passed;
        }

        return CheckResult.InProgress;
    }


    private BuildStatus GetBuildStatusWithKnownIssues(BuildResultAnalysis buildResultAnalysis)
    {
        BuildStatus buildResult = buildResultAnalysis.BuildStatus;
        List<StepResult> stepResults = buildResultAnalysis.BuildStepsResult;
        List<TestResult> testResults = buildResultAnalysis.TestResults;

        bool hasUniqueBuildFailures = stepResults.Any(t => t.KnownIssues.Count == 0);
        bool hasUniqueTestFailures = testResults.Any(t => t.TestCaseResult.Outcome == TestOutcomeValue.Failed &&
                                                            t.KnownIssues.Count == 0 && !t.IsKnownIssueFailure);
        bool hasIssues = stepResults.Count > 0 || testResults.Count > 0;

        return buildResult switch
        {
            BuildStatus.Succeeded => BuildStatus.Succeeded,
            BuildStatus.InProgress => BuildStatus.InProgress,
            BuildStatus.Failed when !hasUniqueBuildFailures && !hasUniqueTestFailures && hasIssues =>
                BuildStatus.Succeeded,
            _ => BuildStatus.Failed
        };
    }
}
