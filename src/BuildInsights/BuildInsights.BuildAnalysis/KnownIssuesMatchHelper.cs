// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.Models;
using BuildInsights.KnownIssues.Models;

namespace BuildInsights.BuildAnalysis;

public class KnownIssuesMatchHelper
{
    public static List<KnownIssueMatch> GetKnownIssueMatchesInBuild(Build build, BuildResultAnalysis buildAnalysis)
    {
        List<KnownIssueMatch> knownIssueMatches = [];
        foreach (StepResult step in buildAnalysis.BuildStepsResult)
        {
            foreach (KnownIssue issue in step.KnownIssues)
            {
                knownIssueMatches.Add(new KnownIssueMatch
                {
                    BuildId = build.Id,
                    BuildRepository = build.Repository.Name,
                    IssueId = issue.GitHubIssue?.Id ?? 0,
                    IssueRepository = issue.GitHubIssue?.RepositoryWithOwner ?? string.Empty,
                    IssueType = issue.IssueType.ToString(),
                    IssueLabels = string.Join(",", issue.GitHubIssue?.Labels ?? []),
                    JobId = step.JobId,
                    StepName = step.StepName,
                    StepStartTime = step.StepStartTime ?? DateTimeOffset.UtcNow,
                    LogURL = step.LinkLog,
                    PullRequest = build.PullRequest,
                    Organization = build.OrganizationName,
                    Project = build.ProjectName
                });
            }
        }

        return knownIssueMatches;
    }

    public static List<TestKnownIssueMatch> GetKnownIssueMatchesInTests(Build build, BuildResultAnalysis buildAnalysis)
    {
        List<TestKnownIssueMatch> knownIssueMatches = [];

        foreach (TestResult testResult in buildAnalysis.TestKnownIssuesAnalysis.TestResultWithKnownIssues)
        {
            knownIssueMatches.AddRange(testResult.KnownIssues.Select(issue => new TestKnownIssueMatch
            {
                BuildId = build.Id,
                BuildRepository = build.Repository.Name,
                IssueId = issue.GitHubIssue.Id,
                IssueRepository = issue.GitHubIssue.RepositoryWithOwner,
                IssueType = KnownIssueType.Test.ToString(),
                IssueLabels = string.Join(",", issue.GitHubIssue?.Labels ?? []),
                TestResultName = testResult.TestCaseResult.Name,
                TestRunId = testResult.TestCaseResult.TestRunId,
                Url = testResult.Url,
                PullRequest = build.PullRequest,
                CompletedDate = testResult.TestCaseResult.CompletedDate,
                Organization = build.OrganizationName,
                Project = build.ProjectName
            }));
        }

        return knownIssueMatches;
    }
}
