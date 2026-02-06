using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.KnownIssues.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Providers
{
    public class KnownIssuesMatchHelper
    {
        public static List<KnownIssueMatch> GetKnownIssueMatchesInBuild(Build build, BuildResultAnalysis buildAnalysis)
        {
            List<KnownIssueMatch> knownIssueMatches = new List<KnownIssueMatch>();
            foreach (StepResult step in buildAnalysis.BuildStepsResult)
            {
                foreach (KnownIssue issue in step.KnownIssues)
                {
                    knownIssueMatches.Add(new KnownIssueMatch
                    {
                        BuildId = build.Id,
                        BuildRepository = build.Repository.Name,
                        IssueId = issue.GitHubIssue.Id,
                        IssueRepository = issue.GitHubIssue.RepositoryWithOwner,
                        IssueType = issue.IssueType.ToString(),
                        IssueLabels = string.Join(",", issue.GitHubIssue?.Labels ?? new List<string>()),
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
            List<TestKnownIssueMatch> knownIssueMatches = new List<TestKnownIssueMatch>();

            foreach (TestResult testResult in buildAnalysis.TestKnownIssuesAnalysis.TestResultWithKnownIssues)
            {
                knownIssueMatches.AddRange(testResult.KnownIssues.Select(issue => new TestKnownIssueMatch
                {
                    BuildId = build.Id,
                    BuildRepository = build.Repository.Name,
                    IssueId = issue.GitHubIssue.Id,
                    IssueRepository = issue.GitHubIssue.RepositoryWithOwner,
                    IssueType = KnownIssueType.Test.ToString(),
                    IssueLabels = string.Join(",", issue.GitHubIssue?.Labels ?? new List<string>()),
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
}
