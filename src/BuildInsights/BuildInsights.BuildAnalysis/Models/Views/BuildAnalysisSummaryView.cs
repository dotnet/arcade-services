// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BuildInsights.GitHub.Models;
using BuildInsights.KnownIssues;

namespace BuildInsights.BuildAnalysis.Models.Views;

public class BuildAnalysisStatusView
{
    public static string Failed => ":x:";
    public static string Succeeded => ":white_check_mark:";
    public static string InProgress => ":clock9:";
    public static string FailedWithKnownIssues => ":warning:";
}

public class BuildAnalysisSummaryView
{
    public int UniqueBuildFailuresCount { get; }
    public string UniqueBuildFailureUrl { get; }
    public bool HasUniqueBuildFailures => UniqueBuildFailuresCount > 0;

    public int BuildRepositoryKnownFailuresCount { get; }
    public string RepositoryKnownFailureUrl { get; }
    public bool HasBuildRepositoryKnownFailures => BuildRepositoryKnownFailuresCount > 0;

    public int BuildInfrastructureKnownFailureCount { get; }
    public string InfrastructureKnownFailureUrl { get; }
    public bool HasBuildInfrastructureKnownFailures => BuildInfrastructureKnownFailureCount > 0;

    public int UniqueTestIssuesCount { get; }
    public string UniqueTestFailureUrl { get; }
    public bool HasUniqueTestIssues => UniqueTestIssuesCount > 0;

    public int TestKnownIssueCount { get; }
    public bool HasTestKnownIssues => TestKnownIssueCount > 0;

    public int BuildKnownIssuesCount { get; }
    public string BuildUrl { get; }
    public string BuildId { get; }
    public string BuildStatusEmoji { get; }
    public BuildStatus BuildStatus { get; set; }

    public string CreateInfraIssueLink { get;  }
    public string CreateRepoIssueLink { get;  }

    public BuildAnalysisSummaryView(BuildResultAnalysis buildResultAnalysis,
        ImmutableList<StepResultView> pipelineUniqueBuildFailures,
        ImmutableList<KnownIssueView> pipelineKnownInfrastructureBuildBreaks,
        ImmutableList<KnownIssueView> pipelineKnownRepositoryBuildBreaks,
        MarkdownParameters parameters)
    {
        UniqueBuildFailuresCount = pipelineUniqueBuildFailures.Count;
        UniqueBuildFailureUrl = pipelineUniqueBuildFailures.Select(t => t.LinkToFirstLogError).FirstOrDefault();

        BuildRepositoryKnownFailuresCount = pipelineKnownRepositoryBuildBreaks.Count;
        RepositoryKnownFailureUrl = pipelineKnownRepositoryBuildBreaks.Select(t => t.LinkToGitHubIssue).FirstOrDefault();

        BuildInfrastructureKnownFailureCount = pipelineKnownInfrastructureBuildBreaks.Count();
        InfrastructureKnownFailureUrl = pipelineKnownInfrastructureBuildBreaks.Select(t => t.LinkToGitHubIssue).FirstOrDefault();

        BuildKnownIssuesCount = buildResultAnalysis.BuildStepsResult.Sum(t => t.KnownIssues.Count);
        BuildStatus = buildResultAnalysis.BuildStatus;

        TestKnownIssueCount = buildResultAnalysis.TestKnownIssuesAnalysis?.TestResultWithKnownIssues.Count ?? 0;
        UniqueTestFailureUrl = GetTestResultsTabUri(buildResultAnalysis.LinkToBuild);
        UniqueTestIssuesCount = buildResultAnalysis.TotalTestFailures - TestKnownIssueCount;

        BuildId = buildResultAnalysis.PipelineName;
        BuildUrl = buildResultAnalysis.LinkToBuild;
        BuildStatusEmoji = GetBuildStatus(BuildStatus);

        KnownIssueUrlOptions urlOptions = parameters.KnownIssueUrlOptions ?? new KnownIssueUrlOptions();
        CreateInfraIssueLink = GetReportIssueUrl(urlOptions.InfrastructureIssueParameters,
            urlOptions.Host, parameters.Repository.Id, parameters.PullRequest);
        CreateRepoIssueLink = GetReportIssueUrl(urlOptions.RepositoryIssueParameters,
            urlOptions.Host, parameters.Repository.Id, parameters.PullRequest);
    }

    private string GetBuildStatus(BuildStatus buildStatus)
    {
        return buildStatus switch
        {
            BuildStatus.Succeeded => BuildAnalysisStatusView.Succeeded,
            BuildStatus.InProgress => BuildAnalysisStatusView.InProgress,
            BuildStatus.Failed when UniqueBuildFailuresCount == 0 && UniqueTestIssuesCount == 0 && BuildKnownIssuesCount > 0 => BuildAnalysisStatusView
                .FailedWithKnownIssues,
            _ => BuildAnalysisStatusView.Failed
        };
    }

    private string GetTestResultsTabUri(string buildUrl)
    {
        return $"{buildUrl}&view=ms.vss-test-web.build-test-results-tab";
    }

    private string GetReportIssueUrl(IssueParameters issueParameters, string host, string repository, string pullRequest)
    {
        var parameters = new Dictionary<string, string>
        {
            {"build", BuildUrl ?? ""},
            {"repository", issueParameters?.Repository ?? repository},
            {"pr", pullRequest ?? "N/A"}
        };

        return KnownIssueHelper.GetReportIssueUrl(parameters, issueParameters, host, repository, pullRequest);
    }
}
