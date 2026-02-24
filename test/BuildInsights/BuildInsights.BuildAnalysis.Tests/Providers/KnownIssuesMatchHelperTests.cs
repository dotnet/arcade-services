// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.GitHub.Models;
using BuildInsights.KnownIssues.Models;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests.Providers;

[TestFixture]
public class KnownIssuesMatchHelperTests
{
    [Test]
    public void GetKnownIssueMatchesInBuildTest()
    {
        Build build = new Build(id: 12345);
        BuildResultAnalysis analysis = new BuildResultAnalysis
        {
            BuildStepsResult =
            [
                new StepResult
                {
                    KnownIssues = CreateImmutableKnownIssuesList(new[]{ 1234, 5678})
                },
                new StepResult
                {
                    KnownIssues = CreateImmutableKnownIssuesList(new[]{ 4356, 7891})
                }
            ]
        };

        List<KnownIssueMatch> knownIssuesMatch = KnownIssuesMatchHelper.GetKnownIssueMatchesInBuild(build, analysis);
        knownIssuesMatch.Should().HaveCount(4);
    }

    [Test]
    public void GetKnownIssueMatchesInTests()
    {
        Build build = new Build(id: 12345);
        BuildResultAnalysis analysis = new BuildResultAnalysis
        {
            BuildStepsResult = [],
            TestKnownIssuesAnalysis = new TestKnownIssuesAnalysis(true,
            [
                MockTestResult(CreateImmutableKnownIssuesList(new[]{ 9876, 54321}))
            ])
        };

        List<TestKnownIssueMatch> testKnownIssuesMatch = KnownIssuesMatchHelper.GetKnownIssueMatchesInTests(build, analysis);
        testKnownIssuesMatch.Should().HaveCount(2);
    }


    private static IImmutableList<KnownIssue> CreateImmutableKnownIssuesList(IEnumerable<int> issueIds)
    {
        var matchListBuilder = ImmutableList.CreateBuilder<KnownIssue>();
        foreach (int issueId in issueIds)
        {
            var githubIssue = new GitHubIssue(id: issueId);
            var knownIssue = new KnownIssue(githubIssue, ["String to match"], KnownIssueType.Infrastructure, new KnownIssueOptions());
            matchListBuilder.Add(knownIssue);
        }
        return matchListBuilder.ToImmutable();
    }

    private static TestResult MockTestResult(IImmutableList<KnownIssue> knownIssues)
    {
        return new TestResult(
            new TestCaseResult("", new DateTimeOffset(2021, 5, 28, 11, 0, 0, TimeSpan.Zero),
                TestOutcomeValue.Failed, 0, 0, 0, new PreviousBuildRef(), "", "", "", null, 55000), "", new FailureRate())
        {
            KnownIssues = knownIssues
        };
    }
}
