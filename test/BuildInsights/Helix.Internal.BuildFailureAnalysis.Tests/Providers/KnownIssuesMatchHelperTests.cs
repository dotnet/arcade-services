using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Providers;
using Microsoft.Internal.Helix.GitHub.Models;
using Microsoft.Internal.Helix.KnownIssues.Models;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Tests.Providers
{
    [TestFixture]
    public class KnownIssuesMatchHelperTests
    {
        [Test]
        public void GetKnownIssueMatchesInBuildTest()
        {
            Build build = new Build(id: 12345);
            BuildResultAnalysis analysis = new BuildResultAnalysis
            {
                BuildStepsResult = new List<StepResult>
                {
                    new StepResult
                    {
                        KnownIssues = CreateImmutableKnownIssuesList(new[]{ 1234, 5678})
                    },
                    new StepResult
                    {
                        KnownIssues = CreateImmutableKnownIssuesList(new[]{ 4356, 7891})
                    }
                }
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
                BuildStepsResult = new List<StepResult>(),
                TestKnownIssuesAnalysis = new TestKnownIssuesAnalysis(true, new List<TestResult>()
                {
                    MockTestResult(CreateImmutableKnownIssuesList(new[]{ 9876, 54321}))
                })
            };

            List<TestKnownIssueMatch> testKnownIssuesMatch = KnownIssuesMatchHelper.GetKnownIssueMatchesInTests(build, analysis);
            testKnownIssuesMatch.Should().HaveCount(2);
        }


        private IImmutableList<KnownIssue> CreateImmutableKnownIssuesList(IEnumerable<int> issueIds)
        {
            var matchListBuilder = ImmutableList.CreateBuilder<KnownIssue>();
            foreach (int issueId in issueIds)
            {
                var githubIssue = new GitHubIssue(id: issueId);
                var knownIssue = new KnownIssue(githubIssue, new List<string>(){"String to match"}, KnownIssueType.Infrastructure, new KnownIssueOptions());
                matchListBuilder.Add(knownIssue);
            }
            return matchListBuilder.ToImmutable();
        }

        private TestResult MockTestResult(IImmutableList<KnownIssue> knownIssues)
        {
            return new TestResult(
                new TestCaseResult("", new DateTimeOffset(2021, 5, 28, 11, 0, 0, TimeSpan.Zero),
                    TestOutcomeValue.Failed, 0, 0, 0, new PreviousBuildRef(), "", "", "", null, 55000), "", new FailureRate())
            {
                KnownIssues = knownIssues
            };
        }
    }
}
