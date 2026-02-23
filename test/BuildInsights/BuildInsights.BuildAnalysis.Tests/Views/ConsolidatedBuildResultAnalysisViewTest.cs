// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Models.Views;
using BuildInsights.GitHub.Models;
using BuildInsights.KnownIssues.Models;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests.Views
{
    [TestFixture]
    public class ConsolidatedBuildResultAnalysisViewTest
    {
        [Test]
        public void ConsolidatedBuildResultAnalysisViewConstructionTest()
        {
            BuildResultAnalysis buildResultAnalysisA = MockBuildResultAnalysis(1,
                [MockTestResult(), MockTestResult(), MockTestResult()],
                [new StepResult()]);
            BuildResultAnalysis buildResultAnalysisB = MockBuildResultAnalysis(1,
                [MockTestResult(), MockTestResult()],
                [new StepResult()]);

            var mergedBuildResultAnalysis = new MergedBuildResultAnalysis(
                "abcdeghijklmnopqrstuwxyz",
                ImmutableList.Create(buildResultAnalysisA, buildResultAnalysisB),
                CheckResult.Failed,
                null,
                null,
                null
            );

            var consolidatedBuildResult = new ConsolidatedBuildResultAnalysisView(
                new MarkdownParameters(mergedBuildResultAnalysis, "TEST-SNAPSHOT", "TEST-PULL-REQUEST", new Repository("TEST-REPOSITORY", true))
            );
            consolidatedBuildResult.BuildFailuresUnique.Should().HaveCount(2);
            consolidatedBuildResult.TestFailuresUnique.Sum(t => t.TestResults.Count).Should().Be(5);
            consolidatedBuildResult.CheckSig.Should().Be("abcdeghijklm");
        }

        [Test]
        public void ConsolidatedBuildResultAnalysisViewLatestAttempt()
        {
            BuildResultAnalysis buildResultAnalysisA =
                MockBuildResultAnalysis(1, [], []);
            buildResultAnalysisA.LatestAttempt = new Attempt();

            var mergedBuildResultAnalysis = new MergedBuildResultAnalysis(
                "abcdeghijklmnopqrstuwxyz",
                ImmutableList.Create(buildResultAnalysisA),
                CheckResult.Failed,
                null,
                null,
                null
            );

            var consolidatedBuildResult = new ConsolidatedBuildResultAnalysisView(
                new MarkdownParameters(mergedBuildResultAnalysis, "TEST-SNAPSHOT", "TEST-PULL-REQUEST", new Repository("TEST-REPOSITORY", true))
            );
            consolidatedBuildResult.LatestAttempt.Count.Should().Be(1);
            consolidatedBuildResult.IsRerun.Should().BeTrue();
            consolidatedBuildResult.LatestAttempt.First().CheckSig.Should().Be("abcdeghijklm");
        }

        [Test]
        public void ConsolidatedBuildResultAnalysisViewKnownIssues()
        {
            var stepResult = new StepResult();
            var issueList = new List<KnownIssue> { new(new GitHubIssue(), ["testRepo"], KnownIssueType.Infrastructure, new KnownIssueOptions()) };
            stepResult.KnownIssues = issueList.ToImmutableList();

            BuildResultAnalysis buildResultAnalysisA =
                MockBuildResultAnalysis(1, [], [stepResult]);
            buildResultAnalysisA.LatestAttempt = new Attempt();

            var mergedBuildResultAnalysis = new MergedBuildResultAnalysis(
                "abcdeghijklmnopqrstuwxyz",
                ImmutableList.Create(buildResultAnalysisA),
                CheckResult.Failed,
                null,
                null,
                null
            );

            var consolidatedBuildResult = new ConsolidatedBuildResultAnalysisView(
                new MarkdownParameters(mergedBuildResultAnalysis, "TEST-SNAPSHOT", "TEST-PULL-REQUEST", new Repository("TEST-REPOSITORY", true))
            );
            consolidatedBuildResult.SentimentParameters.KnownIssues.Should().Be(1);
        }

        [Test]
        public void ConsolidatedBuildResultAnalysisWithFailedTestsAndPassedOnRerunTests()
        {
            TestResult testPassedOnRerun = MockTestResultFlaky();
            List<TestResult> testResults =
            [
                MockTestResult(), testPassedOnRerun
            ];

            BuildResultAnalysis buildResultAnalysisA =
                MockBuildResultAnalysis(1, testResults, []);

            var mergedBuildResultAnalysis = new MergedBuildResultAnalysis(
                "abcdeghijklmnopqrstuwxyz",
                ImmutableList.Create(buildResultAnalysisA),
                CheckResult.Passed,
                null,
                null,
                null
            );

            var consolidatedBuildResult = new ConsolidatedBuildResultAnalysisView(
                new MarkdownParameters(mergedBuildResultAnalysis, "TEST-SNAPSHOT", "TEST-PULL-REQUEST", new Repository("TEST-REPOSITORY", true))
            );

            consolidatedBuildResult.HasFlakyTests.Should().Be(true);
            consolidatedBuildResult.FlakyTests.Should().HaveCount(1);
            consolidatedBuildResult.HasTestFailures.Should().Be(true);
            consolidatedBuildResult.TestFailuresUnique.Should().HaveCount(1);
        }

        [Test]
        public void ConsolidatedBuildResultAnalysisCompletedPipelines()
        {
            BuildResultAnalysis buildResultAnalysisA = MockBuildResultAnalysis(1, [], []);
            buildResultAnalysisA.PipelineName = "COMPLETED-PIPELINE-1";
            buildResultAnalysisA.LinkToBuild = "https://dev.azure.text/link/to/build/1234";

            var mergedBuildResultAnalysis = new MergedBuildResultAnalysis(
                "abcdeghijklmnopqrstuwxyz",
                ImmutableList.Create(buildResultAnalysisA),
                CheckResult.Failed,
                null,
                null,
                null
            );

            var consolidatedBuildResult = new ConsolidatedBuildResultAnalysisView(
                new MarkdownParameters(mergedBuildResultAnalysis, "TEST-SNAPSHOT", "TEST-PULL-REQUEST", new Repository("TEST-REPOSITORY", true)));
            consolidatedBuildResult.CompletedPipelinesLinks.Should().HaveCount(1);
            consolidatedBuildResult.CompletedPipelinesLinks.First().Name.Should().Be("COMPLETED-PIPELINE-1");
            consolidatedBuildResult.CompletedPipelinesLinks.First().Url.Should().Be("https://dev.azure.text/link/to/build/1234");
        }



        [Test]
        public void ConsolidatedBuildResultAnalysisUniqueTestFailuresTest()
        {
            BuildResultAnalysis buildResultAnalysisA = MockBuildResultAnalysis(1,
                [],
                [new StepResult()]);
            buildResultAnalysisA.TotalTestFailures = 5;
            buildResultAnalysisA.TestKnownIssuesAnalysis = new TestKnownIssuesAnalysis(true,
                [MockTestResult(isKnownIssue: true)]);

            var mergedBuildResultAnalysis = new MergedBuildResultAnalysis(
                "abcdeghijklmnopqrstuwxyz",
                ImmutableList.Create(buildResultAnalysisA),
                CheckResult.Failed,
                null,
                null,
                null);

            var consolidatedBuildResult = new ConsolidatedBuildResultAnalysisView(
                new MarkdownParameters(mergedBuildResultAnalysis, "TEST-SNAPSHOT", "TEST-PULL-REQUEST", new Repository("TEST-REPOSITORY", true))
            );
            consolidatedBuildResult.UniqueTestFailures.Should().Be(4);
        }


        [Test]
        public void ConsolidatedBuildResultAnalysisSummarizeKnownIssues()
        {
            var issueList = new List<KnownIssue> { new(new GitHubIssue(id: 1234, repositoryWithOwner: "owner/repo"), ["testRepo"], KnownIssueType.Infrastructure, new KnownIssueOptions()) };
            var stepResult = new StepResult
            {
                KnownIssues = issueList.ToImmutableList()
            };

            BuildResultAnalysis buildResultAnalysisA = MockBuildResultAnalysis(1, [], [stepResult, stepResult, stepResult]);
            buildResultAnalysisA.LatestAttempt = new Attempt();

            var mergedBuildResultAnalysis = new MergedBuildResultAnalysis(
                "abcdeghijklmnopqrstuwxyz",
                ImmutableList.Create(buildResultAnalysisA),
                CheckResult.Failed,
                null,
                null,
                null
            );

            var consolidatedBuildResult = new ConsolidatedBuildResultAnalysisView(
                new MarkdownParameters(mergedBuildResultAnalysis, "TEST-SNAPSHOT", "TEST-PULL-REQUEST", new Repository("TEST-REPOSITORY", true)));
            consolidatedBuildResult.InfrastructureBuildBreaks.Should().HaveCount(1);
            consolidatedBuildResult.InfrastructureBuildBreaks.First().DuplicateHits.Should().Be(2);
        }


        private BuildResultAnalysis MockBuildResultAnalysis(int buildId, List<TestResult> testResults,
            List<StepResult> stepResults)
        {
            return new BuildResultAnalysis
            {
                PipelineName = "",
                BuildId = buildId,
                BuildNumber = "",
                TargetBranch = Branch.Parse("fakeTargetBranchName"),
                LinkToBuild = "",
                LinkAllTestResults = "",
                IsRerun = false,
                BuildStatus = BuildStatus.Failed,
                TestResults = testResults,
                BuildStepsResult = stepResults,
                LatestAttempt = null,
                TestKnownIssuesAnalysis = new TestKnownIssuesAnalysis()
            };
        }

        private TestResult MockTestResult(bool isKnownIssue = false)
        {
            return new TestResult(
                new TestCaseResult("", new DateTimeOffset(2021, 5, 28, 11, 0, 0, TimeSpan.Zero),
                    TestOutcomeValue.Failed, 0, 0, 0, new PreviousBuildRef(), "", "", "", null, 55000), "", new FailureRate())
            {
                IsKnownIssueFailure = isKnownIssue,
            };
        }

        private TestResult MockTestResultFlaky()
        {
            return new TestResult(
                new TestCaseResult("", new DateTimeOffset(2021, 5, 28, 11, 0, 0, TimeSpan.Zero),
                    TestOutcomeValue.PassedOnRerun, 0, 0, 0, new PreviousBuildRef(), "", "", "", null, 55000), "", new FailureRate());
        }
    }
}
