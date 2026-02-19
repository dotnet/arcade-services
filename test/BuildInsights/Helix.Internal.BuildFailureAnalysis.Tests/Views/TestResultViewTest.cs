using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models.Views;
using Microsoft.Internal.Helix.GitHub.Models;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Tests.Views
{
    [TestFixture]
    public class TestResultViewTest
    {
        [Test]
        public void BuildTestLogWebUriTest()
        {
            DateTimeOffset dateTimeOffset = new DateTimeOffset(2021,5,21,3,0,0,TimeSpan.Zero);
            var testCaseResult = new TestCaseResult("", dateTimeOffset, TestOutcomeValue.Failed, 123, 456, 789,
                new PreviousBuildRef("", dateTimeOffset), "", "", "TestProjectName", null, 55000);

            var testResult = new TestResult(testCaseResult, "https://example.test", new FailureRate());

            var testResultView = new TestResultView(testResult, 789, "LinkToBuild", MockMarkdownParameters());

            testResultView.TestLogs.Should().Contain("paneView=debug").And.Contain("buildId=789").And.Contain("runId=123&resultId=456");
            testResultView.ArtifactLink.Should().Contain("paneView=dotnet-dnceng.dnceng-build-release-tasks.helix-test-information-tab").And.Contain("buildId=789").And.Contain("runId=123&resultId=456");
            testResultView.HistoryLink.Should().Contain("paneView=history").And.Contain("buildId=789").And.Contain("runId=123&resultId=456");
        }

        [Test]
        public void TestResultViewConstructionTest()
        {
            var creationDate = new DateTimeOffset(2021, 5, 26, 5, 30, 0, TimeSpan.Zero);
            var testCaseCreationDate = new DateTimeOffset(2021, 6, 26, 5, 30, 0, TimeSpan.Zero);
            var testCaseResult = new TestCaseResult("AutomatedTestName", testCaseCreationDate, TestOutcomeValue.PassedOnRerun, 123, 456, 789,
                new PreviousBuildRef("", testCaseCreationDate), "TestErrorMessage", "", "TestProjectName", null, 55000, attempt:2);

            var testResult = new TestResult(testCaseResult, "https://example.test", new FailureRate())
            {
                FailingConfigurations = new List<FailingConfiguration>
                {
                    new FailingConfiguration
                    {
                        Configuration = MockConfiguration(""),
                        TestLogs = "",
                        HistoryLink = "",
                        ArtifactLink = ""
                    }
                },
                HelixWorkItem = new HelixWorkItem()
                {
                    HelixJobId = "HelixJobIdTest",
                    HelixWorkItemName = "HelixWorkItemTest",
                    ConsoleLogUrl = "ConsoleLogUrlTest",
                    ExitCode = 1,
                },
                IsRetry = true,
                FailureRate = new FailureRate
                {
                    FailedRuns = 5,
                    TotalRuns = 100
                },
                CreationDate = creationDate,
            };

            var testResultView = new TestResultView(testResult, 188, "LinkToBuild", MockMarkdownParameters());
            testResultView.BuildId.Should().Be(188);
            testResultView.CallStack.Should().BeEmpty();
            testResultView.ExceptionMessage.Should().Be("TestErrorMessage");
            testResultView.IsRetry.Should().BeTrue();
            testResultView.TestName.Should().Be("AutomatedTestName");
            testResultView.FailureRate.PercentageOfFailure.Should().Be(testResult.FailureRate.PercentageOfFailure);
            testResultView.FailingConfigurations.Count.Should().Be(1);
            testResultView.CreationDate.Should().Be(creationDate);
            testResultView.IsFlakyTest.Should().BeTrue();
            testResultView.Attempt.Should().Be(2);
            testResultView.ConsoleLogLink.Should().Be("ConsoleLogUrlTest");
            testResultView.IsHelixWorkItem.Should().BeTrue();
            testResultView.IsHelixWorkItemFailure.Should().BeTrue();
        }

        private MarkdownParameters MockMarkdownParameters(KnownIssueUrlOptions knownIssueUrlOptions = null, string pullRequest = "TEST-PULL-REQUEST")
        {
            return new MarkdownParameters(new MergedBuildResultAnalysis(), "TEST-REPO", pullRequest,
                new Repository("TEST-REPOSITORY", true), knownIssueUrlOptions);
        }

        private Configuration MockConfiguration(string name)
        {
            var testCaseResult = new TestCaseResult("", MockDateTimeOffset(), TestOutcomeValue.Failed, 0, 1, 2,
                new PreviousBuildRef(), "", "", "", null, 55000, 1);

            return new Configuration(name, "ANY_ORGANIZATION", "ANY_PROJECT", testCaseResult);
        }

        private static DateTimeOffset MockDateTimeOffset()
        {
            return new DateTimeOffset(2021, 5, 12, 0, 0, 0, TimeSpan.Zero);
        }
    }
}
