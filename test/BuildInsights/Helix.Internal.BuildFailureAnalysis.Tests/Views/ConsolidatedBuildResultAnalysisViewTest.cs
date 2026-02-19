using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models.Views;
using Microsoft.Internal.Helix.GitHub.Models;
using Microsoft.Internal.Helix.KnownIssues.Models;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Tests.Views
{
    [TestFixture]
    public class ConsolidatedBuildResultAnalysisViewTest
    {
        [Test]
        public void ConsolidatedBuildResultAnalysisViewConstructionTest()
        {
            BuildResultAnalysis buildResultAnalysisA = MockBuildResultAnalysis(1,
                new List<TestResult> { MockTestResult(), MockTestResult(), MockTestResult() },
                new List<StepResult> { new StepResult() });
            BuildResultAnalysis buildResultAnalysisB = MockBuildResultAnalysis(1,
                new List<TestResult> { MockTestResult(), MockTestResult() },
                new List<StepResult> { new StepResult() });

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
                MockBuildResultAnalysis(1, new List<TestResult>(), new List<StepResult>());
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
            var issueList = new List<KnownIssue> { new KnownIssue(new GitHubIssue(), new List<string>(){ "testRepo" }, KnownIssueType.Infrastructure, new KnownIssueOptions()) };
            stepResult.KnownIssues = issueList.ToImmutableList();

            BuildResultAnalysis buildResultAnalysisA =
                MockBuildResultAnalysis(1, new List<TestResult>(), new List<StepResult> { stepResult });
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
            List<TestResult> testResults = new List<TestResult>()
            {
                MockTestResult(), testPassedOnRerun
            };

            BuildResultAnalysis buildResultAnalysisA =
                MockBuildResultAnalysis(1, testResults, new List<StepResult>());

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
            BuildResultAnalysis buildResultAnalysisA = MockBuildResultAnalysis(1, new List<TestResult>(), new List<StepResult>());
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
                new List<TestResult> { },
                new List<StepResult> { new StepResult() });
            buildResultAnalysisA.TotalTestFailures = 5;
            buildResultAnalysisA.TestKnownIssuesAnalysis = new TestKnownIssuesAnalysis(true,
                new List<TestResult>() { MockTestResult(isKnownIssue: true) });

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
            var issueList = new List<KnownIssue> { new KnownIssue(new GitHubIssue(id: 1234, repositoryWithOwner: "owner/repo"), new List<string>() { "testRepo" }, KnownIssueType.Infrastructure, new KnownIssueOptions()) };
            var stepResult = new StepResult
            {
                KnownIssues = issueList.ToImmutableList()
            };

            BuildResultAnalysis buildResultAnalysisA = MockBuildResultAnalysis(1, new List<TestResult>(), new List<StepResult> { stepResult, stepResult, stepResult });
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
