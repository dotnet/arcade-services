using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models.Views;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Providers;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Services;
using Microsoft.Internal.Helix.GitHub.Models;
using Microsoft.Internal.Helix.KnownIssues.Models;
using Microsoft.Internal.Helix.Utility.UserSentiment;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Tests
{
    [TestFixture]
    public class MarkdownGeneratorTests
    {
        private class TestData : IDisposable, IAsyncDisposable
        {
            private readonly ServiceProvider _provider;
            public MarkdownGenerator Generator { get; }

            private TestData(ServiceProvider provider, MarkdownGenerator generator)
            {
                _provider = provider;
                Generator = generator;
            }

            public void Dispose()
            {
                _provider.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                return _provider.DisposeAsync();
            }

            private class Builder
            {
                public TestData Build()
                {
                    ServiceCollection collection = new ServiceCollection();
                    collection.AddLogging(l => l.AddProvider(new NUnitLogger()));
                    collection.AddUserSentiment(o => o.Host = "https://sentiment.test");
                    collection.Configure<KnownIssueUrlOptions>(o => {
                        o.Host = "https://knownissue.example/new";
                        o.InfrastructureIssueParameters = new IssueParameters { GithubTemplateName = "InfrastructureTemplate", };
                        o.RepositoryIssueParameters = new IssueParameters { GithubTemplateName = "RepositoryTemplate", };
                    });
                    collection.AddMarkdownGenerator();
                    var services = collection.BuildServiceProvider();
                    return new TestData(services, (MarkdownGenerator) services.GetRequiredService<IMarkdownGenerator>());
                }
            }

            public static TestData Default()
            {
                return new Builder().Build();
            }
        }


        [Test]
        public void GenerateMarkdownCompileTemplate()
        {
            using var testData = TestData.Default();
            var result = testData.Generator.GenerateMarkdown(new ConsolidatedBuildResultAnalysisView());
            result.Should().NotBeEmpty();
        }

        [Test]
        public void GenerateMarkdownNullBuildResultAnalysis()
        {
            Func<MarkdownParameters> a = () => new MarkdownParameters(
                null,
                "TEST-SNAPSHOT",
                "TEST-PULL-REQUEST",
                new Repository("TEST-REPOSITORY", true)
            );
            a.Should().Throw<ArgumentException>();
        }
        
        [Test]
        public void GenerateMarkdownNoPipelines()
        {
            using TestData testData = TestData.Default();
            var mergedBuildResultAnalysis = new MarkdownParameters(
                new MergedBuildResultAnalysis("COMMIT-HASH", null, CheckResult.Passed, null, null, null),
                "TEST-SNAPSHOT",
                "TEST-PULL-REQUEST",
                new Repository("TEST-REPOSITORY", true)
            );
            string result = testData.Generator.GenerateMarkdown(mergedBuildResultAnalysis);
            result.Should().NotContain("will be analyzed once they finish");
            result.Should().Contain("Introduction.md", because: "with no pipelines, we should show preliminary check with documentation links");
        }
        
        [Test]
        public void GenerateEmtpyMarkdown()
        {
            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateEmptyMarkdown(new UserSentimentParameters{Repository = "TEST-REPO"});
            result.Should().NotContain("will be analyzed once they finish");
            result.Should().Contain("Introduction.md", because: "with no pipelines, we should show preliminary check with documentation links");
            result.Should().Contain("TEST-REPO");
        }
        
        [Test]
        public void GenerateMarkdownNoFailuresPipelines()
        {
            using TestData testData = TestData.Default();
            var mergedBuildResultAnalysis = new MarkdownParameters(
                new MergedBuildResultAnalysis("COMMIT-HASH",
                    new[]
                    {
                        new BuildResultAnalysis
                        {
                            BuildStepsResult = new List<StepResult>(),
                            TestResults = new List<TestResult>(),
                            TestKnownIssuesAnalysis = new TestKnownIssuesAnalysis()
                        }
                    }, CheckResult.Passed, null, null, null),
                "TEST-SNAPSHOT",
                "TEST-PULL-REQUEST",
                new Repository("TEST-REPOSITORY", true)
            );
            string result = testData.Generator.GenerateMarkdown(mergedBuildResultAnalysis);
            result.Should().Contain("completed");
            result.Should().NotContain("will be analyzed once they finish");
        }

        [Test]
        public void GenerateMarkdownPendingPipelines()
        {
            using TestData testData = TestData.Default();
            var mergedBuildResultAnalysis = new MarkdownParameters(
                new MergedBuildResultAnalysis(
                    "COMMIT-HASH",
                    null,
                    CheckResult.Passed,
                    new[] {new Link("PENDING-PIPELINE-1", "https://dev.azure.text/link/to/build/777"),new Link("PENDING-PIPELINE-2", "https://dev.azure.text/link/to/build/888")},
                    null,
                    null
                ),
                "TEST-SNAPSHOT",
                "TEST-PULL-REQUEST",
                new Repository("TEST-REPOSITORY", true)
            );
            string result = testData.Generator.GenerateMarkdown(mergedBuildResultAnalysis);
            result.Should().Contain("will be analyzed once they finish");
            result.Should().Contain("PENDING-PIPELINE-1");
            result.Should().Contain("777");
            result.Should().Contain("PENDING-PIPELINE-2");
            result.Should().Contain("888");
        }

        [Test]
        public void GenerateMarkdownPendingPipelinesAndCompletedPipelines()
        {
            using TestData testData = TestData.Default();
            var mergedBuildResultAnalysis = new MarkdownParameters(
                new MergedBuildResultAnalysis(
                    "COMMIT-HASH",
                    new List<BuildResultAnalysis>
                    {
                        new()
                        {
                            BuildStepsResult = new List<StepResult>(), TestResults = new List<TestResult>(),
                            TestKnownIssuesAnalysis = new TestKnownIssuesAnalysis(),
                            PipelineName = "PENDING-PIPELINE-1",
                            LinkToBuild = "https://dev.azure.text/link/to/build/777"
                        }
                    },
                    CheckResult.Passed,
                    new[] { new Link("PENDING-PIPELINE-2", "https://dev.azure.text/link/to/build/888") },
                    null,
                    null
                ),
                "TEST-SNAPSHOT",
                "TEST-PULL-REQUEST",
                new Repository("TEST-REPOSITORY", true)
            );
            string result = testData.Generator.GenerateMarkdown(mergedBuildResultAnalysis);
            result.Should().Contain("will be analyzed once they finish");
            result.Should().Contain("PENDING-PIPELINE-1");
            result.Should().Contain("777");
            result.Should().Contain("PENDING-PIPELINE-2");
            result.Should().Contain("888");
        }

        [Test]
        public void GenerateMarkdownCompletedPipelines()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                BuildFailuresUnique = new List<StepResultView>(),
                TestFailuresUnique = new List<TestResultsGroupView>(),
                SucceededPipelinesLinks = new List<Link>() { new Link("COMPLETED-PIPELINE-1", "https://dev.azure.text/link/to/build/1234") }.ToImmutableList(),
                FailingPipelinesLinks = new List<Link>() { new Link("COMPLETED-PIPELINE-2", "https://dev.azure.text/link/to/build/5678") }.ToImmutableList(),
                HasData = true,
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("completed");
            result.Should().Contain("COMPLETED-PIPELINE-1");
            result.Should().Contain("1234");
            result.Should().Contain("COMPLETED-PIPELINE-2");
            result.Should().Contain("5678");
        }

        [Test]
        public void GenerateMarkdownWithMergedBuildResultAnalysis()
        {
            var mergedBuildResultAnalysis = new MergedBuildResultAnalysis(
                "COMMIT-HASH",
                ImmutableList.Create(
                    new BuildResultAnalysis
                    {
                        PipelineName = "",
                        BuildId = 0,
                        BuildNumber = "",
                        TargetBranch = Branch.Parse("fakeTargetBranchName"),
                        LinkToBuild = "",
                        LinkAllTestResults = "",
                        IsRerun = false,
                        BuildStatus = BuildStatus.Failed,
                        TestResults = new List<TestResult>(),
                        BuildStepsResult = new List<StepResult>(),
                        LatestAttempt = new Attempt(),
                        TestKnownIssuesAnalysis = new TestKnownIssuesAnalysis()
                    }
                ),
                CheckResult.Failed,
                null,
                null,
                null
            );

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(
                new MarkdownParameters(mergedBuildResultAnalysis, "TEST-SNAPSHOT", "TEST-PULL-REQUEST", new Repository("TEST-REPOSITORY", true))
            );

            result.Should().NotBeEmpty();
        }

        private static StepResultView BuildStep(
            IEnumerable<Error> errors,
            FailureRate failureRate = null)
        {
            return new StepResultView(
                stepName: "StepNameTest",
                pipelineBuildName: null,
                linkToBuild: null,
                linkToStep:null,
                errors: errors.ToImmutableList(),
                failureRate: failureRate,
                stepHierarchy: ImmutableList<string>.Empty,
                knownIssues: ImmutableList.Create<KnownIssue>(),
                parameters: new MarkdownParameters(new MergedBuildResultAnalysis(), "TEST-SNAPSHOT", "TEST-PULL-REQUEST", new Repository("TEST-REPOSITORY", true))
            );
        }

        [Test]
        public void GenerateMarkDownWithLinksToTestFailuresTest()
        {
            List<TestResultView> fakeTest = new List<TestResultView>()
            {
                new()
                {
                    TestName = "FakeTestName",
                    ExceptionMessage = "FakeMessage",
                    FailureRate = new FailureRate()
                }
            };

            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView()
            {
                TestFailuresUnique = new List<TestResultsGroupView>()
                {
                    new("https://example.test", "example-test",fakeTest, 1 ),
                    new("https://example2.test", "example-test-special",fakeTest, 1 ),
                },
                HasData = true,
            };

            using var testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("example-test").And.Contain("https://example.test");
            result.Should().Contain("example-test-special").And.Contain("https://example2.test");
        }

        [Test]
        public void GenerateMarkdownMultipleErrorMessageInOneTaskTest()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                BuildFailuresUnique = new List<StepResultView>
                {
                    BuildStep(
                        new[]
                        {
                            new Error() {ErrorMessage = "TestErrorMessage01"},
                            new Error() {ErrorMessage = "TestErrorMessage02"}
                        }
                    )
                },
                HasData = true,
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("StepNameTest");
            result.Should().Contain("TestErrorMessage01").And.Contain("TestErrorMessage02");
        }

        [Test]
        public void GenerateMarkDownBuildFailurePassOnRetryTest()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                IsRerun = true,
                LatestAttempt = new List<AttemptView>
                {
                    new AttemptView
                    {
                        BuildStepsResult = new List<StepResultView>
                        {
                            BuildStep(
                                new[] {new Error {ErrorMessage = "ErrorPassedOnRetry"}}
                            )
                        }
                    }
                },
                HasData = true,
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("StepNameTest").And.Contain("ErrorPassedOnRetry");
        }

        [Test]
        public void GenerateMarkDownLatestAttemptLinkTest()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                IsRerun = true,
                LatestAttempt = new List<AttemptView>
                {
                    new AttemptView
                    {
                        LinkToBuild = "example.link.test",
                    }
                },
                HasData = true,
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("example.link.test");
        }

        [Test]
        public void GenerateMarkDownBuildFailedOnRetryTest()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                IsRerun = true,
                BuildFailuresUnique = new List<StepResultView>
                {
                    BuildStep(
                        new[] {new Error {ErrorMessage = "TestErrorMessage"}}
                    )
                },
                LatestAttempt = new List<AttemptView>()
                {
                    new AttemptView()
                    {
                        BuildStepsResult = new List<StepResultView>
                        {
                            BuildStep(
                                new[] {new Error {ErrorMessage = "ErrorPassedOnRetry"}}
                            )
                        }
                    }
                },
                HasData = true,
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("StepNameTest", "TestErrorMessage");
            result.Should().NotContain("TaskFailedInPreviousBuild").And.NotContain("ErrorPassedOnRetry");
        }

        [Test]
        public void GenerateMarkdownInfrastructureBuildBreaks()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                InfrastructureBuildBreaks = new List<KnownIssueView>
                {
                    new KnownIssueView("DisplayStepNameOfKnownIssue", "LinkToBuildKnownIssue", "IssueRepositoryTest",
                        "IssueIdTest", "LinkToGitHubIssue", "TitleGitHubIssue")
                },
                HasData = true
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("DisplayStepNameOfKnownIssue").And.Contain("LinkToBuildKnownIssue")
                .And.Contain("TitleGitHubIssue").And.Contain("LinkToGitHubIssue");
        }

        [Test]
        public void GenerateMarkdownRepoBuildBreaks()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                RepoBuildBreaks = new List<KnownIssueView>
                {
                    new KnownIssueView("DisplayStepNameOfKnownIssue", "LinkToBuildKnownIssue", "IssueRepositoryTest",
                        "IssueIdTest", "LinkToGitHubIssue", "TitleGitHubIssue")
                },
                HasData = true
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("DisplayStepNameOfKnownIssue").And.Contain("LinkToBuildKnownIssue")
                .And.Contain("TitleGitHubIssue").And.Contain("LinkToGitHubIssue");
        }

        [Test]
        public void UserSentiment()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                SentimentParameters = new UserSentimentParameters
                {
                    Repository = "TEST-REPO",
                    CommitHash = "abcdefghijklmnopqrstuvwxyz",
                    BuildId = 999,
                    HasUniqueBuildFailures = true,
                    HasUniqueTestFailures = true,
                    SnapshotId = "TEST-SNAPSHOT",
                },
                HasData = true,
            };

            using var testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("abcdefghijkl").And.NotContain("abcdefghijklmnopqrstuvwxyz");
            result.Should().Contain(SentimentFeature.DeveloperWorkflowGitHubCheckTab.ToString("D"));
            result.Should().Contain("ub=1");
            result.Should().Contain("ut=1");
            result.Should().Contain("TEST-REPO");
        }

        [Test]
        public void GenerateMarkDownTestFailureRateTotalRunsZero()
        {
            var testResultView = new List<TestResultView>
            {
                new()
                {
                    TestName = "TestResultViewRetryTest",
                    ExceptionMessage = "ExceptionMessageAfterRetryTest",
                    IsRetry = true,
                    FailureRate = new FailureRate
                    {
                        TotalRuns = 0
                    }
                }
            };

            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView()
            {
                TestFailuresUnique  = new List<TestResultsGroupView>()
                {
                    new("https://example.test", "example-test",testResultView, 1 ),
                },
                HasData = true,
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("0").And.Contain("0").And.NotContain("0%");
        }

        [Test]
        public void GenerateMarkDownTestFailureRate()
        {
            var testResultView  = new List<TestResultView>
            {
                new()
                {
                    TestName = "TestResultViewRetryTest",
                    ExceptionMessage = "ExceptionMessageAfterRetryTest",
                    IsRetry = true,
                    FailureRate = new FailureRate
                    {
                        TotalRuns = 10,
                        FailedRuns = 5
                    },
                    HistoryLink = "FAKE HISTORY LINK"
                }
            };

            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView()
            {
                TestFailuresUnique = new List<TestResultsGroupView>()
                {
                    new("https://example.test", "example-test",testResultView, 1 ) { DisplayTestsCount = 1 }
                },
                HasData = true,
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("50").And.Contain("%");
        }

        [Test]
        public void GenerateMarkDownTestLinkToOpenNewKnownIssue()
        {
            List<TestResult> testResults = new List<TestResult>()
            {
                MockTestResult("Test-A")
            };

            MergedBuildResultAnalysis mergedBuildResultAnalysis = MockMergedBuildResultAnalysis(testResults: testResults);

            var knownIssueUrlOptions = new KnownIssueUrlOptions
            {
                Host = "https://example.test-new-infra.com",
                RepositoryIssueParameters = new IssueParameters { Labels = new List<string> { "RepositoryIssueLabel" } },
                InfrastructureIssueParameters = new IssueParameters { Labels = new List<string> { "InfrastructureIssueLabel" }, Repository = "INFRA-REPO" }
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(new MarkdownParameters(mergedBuildResultAnalysis, "TEST-SNAPSHOT",
                "PULL-REQUEST", new Repository("TEST-REPOSITORY", true), knownIssueUrlOptions));

            Regex.IsMatch(result, $"{knownIssueUrlOptions.Host}.*INFRA-REPO.").Should().BeTrue();
            Regex.IsMatch(result, $"{knownIssueUrlOptions.Host}.*TEST-REPOSITORY").Should().BeTrue();
        }

        [Test]
        public void GenerateMarkDownTessPassOnRetryTest()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                IsRerun = true,
                LatestAttempt = new List<AttemptView>
                {
                    new AttemptView
                    {
                        TestResults = new List<TestResultView>
                        {
                            new TestResultView
                            {
                                TestName = "TestResultViewRetryTest",
                                ExceptionMessage = "ExceptionMessageAfterRetryTest",
                                IsRetry = true,
                                FailureRate = new FailureRate
                                {
                                    TotalRuns = 100,
                                    FailedRuns = 2
                                }
                            }
                        }
                    }
                },
                HasData = true,
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("TestResultViewRetryTest").And.Contain("ExceptionMessageAfterRetryTest");
        }

        [Test]
        public void GenerateMarkDownUniqueBuildFailureTest()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView()
            {
                BuildFailuresUnique = new List<StepResultView>
                {
                    BuildStep(
                        new[] {new Error() {ErrorMessage = "TestErrorMessage"}}
                    )
                },
                HasData = true,
            };
            using var testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("StepNameTest").And.Contain("TestErrorMessage");
        }

        [Test]
        public void GenerateMarkDownUniqueTestFailureTest()
        {
            var testResultView  = new List<TestResultView>
            {
                new()
                {
                    TestName = "TestTestName",
                    ExceptionMessage = "TestExceptionMessage",
                    FailureRate = new FailureRate {TotalRuns = 0}
                }
            };

            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView()
            {
                TestFailuresUnique = new List<TestResultsGroupView>()
                {
                    new("https://example.test", "example-test",testResultView, 1 ) { DisplayTestsCount = 1 }
                },
                HasData = true,
            };

            using var testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("TestTestName").And.Contain("TestExceptionMessage");
        }

        [Test]
        public void GenerateMarkDownTestAndBuildFailureTest()
        {
            var testResultView  = new List<TestResultView>
            {
                new TestResultView
                {
                    TestName = "TestTestName",
                    ExceptionMessage = "TestExceptionMessage",
                    FailureRate = new FailureRate {TotalRuns = 0}
                }
            };

            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView()
            {
                BuildFailuresUnique = new List<StepResultView>()
                {
                    BuildStep(
                        new[] {new Error{ErrorMessage = "BuildErrorMessage"}}
                    )
                },
                TestFailuresUnique = new List<TestResultsGroupView>()
                {
                    new("https://example.test", "example-test",testResultView, 1 ) { DisplayTestsCount = 1 }
                },
                HasData = true,
            };

            using var testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("StepNameTest").And.Contain("BuildErrorMessage");
            result.Should().Contain("TestTestName").And.Contain("TestExceptionMessage");
        }

        [Test]
        public void GenerateMarkdownBuildAutomaticallyRetry()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                BuildFailuresUnique = new List<StepResultView> {BuildStep(Enumerable.Empty<Error>())},
                BuildRetryAutomatically = new List<RetryInformationView>
                {
                    new RetryInformationView("PipelineRetryNameTest", 1, "BuildNumberRetry", "linkToBuildRetry", new GitHubIssue())
                },
                HasData = true,
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("BuildNumberRetry").And.Contain("linkToBuildRetry");
        }

        [Test]
        public void GenerateMarkdownTestResultsWithSubResults()
        {
            var testResultView = new List<TestResultView>()
            {
                new TestResultView()
                {
                    TestName = "TestNameTest",
                    FailureRate = new FailureRate {TotalRuns = 0},
                    TestSubResults = new List<TestSubResultView>()
                    {
                        new TestSubResultView(
                            "SubResultTestNameTest", "SubResultErrorMessageTest", "SubResultStackTrace"
                        )
                    }
                }
            };

            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                BuildFailuresUnique = new List<StepResultView> {BuildStep(Enumerable.Empty<Error>())},
                TestFailuresUnique = new List<TestResultsGroupView>()
                {
                    new("https://example.test", "example-test",testResultView, 1 ) { DisplayTestsCount = 1 }
                },
                HasData = true,
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("TestNameTest");
            result.Should().Contain("SubResultTestNameTest").And.Contain("SubResultErrorMessageTest");
        }

        [Test]
        public void GenerateMarkdownTestsPassedOnRerun()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                BuildFailuresUnique = new List<StepResultView> (),
                TestFailuresUnique = new List<TestResultsGroupView>(),
                FlakyTests = new List<TestResultView>()
                {
                    new TestResultView()
                    {
                        TestName = "TestNameFailOnceThenPass ",
                        FailureRate = new FailureRate {TotalRuns = 6},
                        TestSubResults = new List<TestSubResultView>()
                        {
                            new TestSubResultView(
                                "Attempt", "SubResultErrorMessage_Test", "StackTrace"
                            )
                        },
                        Attempt = 12
                    }
                },
                HasData = true,
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("TestNameFailOnceThenPass");
            result.Should().Contain("Attempt", "SubResultErrorMessage_Test");
            result.Should().Contain("12");
        }

        [Test]
        public void GenerateMarkdownSummarizeResult()
        {
            var testResultView = new List<TestResultView>()
            {
                new()
                {
                    TestName = "TestNameA",
                    FailureRate = new FailureRate() {TotalRuns = 0},
                    ExceptionMessage = "A2B4C6D8"
                },
                new()
                {
                    TestName = "TestNameB",
                    FailureRate = new FailureRate {TotalRuns = 0},
                    TestSubResults = new List<TestSubResultView>()
                    {
                        new TestSubResultView(
                            "SubResultTestNameTest", "2A4B6C8D", "SubResultStackTrace"
                        )
                    }
                }
            };

            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                BuildFailuresUnique = new List<StepResultView> { BuildStep(Enumerable.Empty<Error>()) },
                TestFailuresUnique = new List<TestResultsGroupView>()
                {
                    new("https://example.test", "example-test",testResultView, 1 )
                    {
                        DisplayTestsCount = 2
                    }
                },
                HasData = true,
                SummarizeInstructions = new MarkdownSummarizeInstructions(true, 5, 100)
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);

            result.Should().Contain("TestNameA").And.Contain("A2B4C").And.NotContain("A2B4C6D8");
            result.Should().Contain("TestNameB").And.Contain("2A4B6").And.NotContain("2A4B6C8D");
        }


        [Test]
        public void GenerateMarkdownSummarizeResultWithPreviousAttempt()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                BuildFailuresUnique = new List<StepResultView>(),
                TestFailuresUnique = new List<TestResultsGroupView>(),
                IsRerun = true,
                LatestAttempt = new List<AttemptView>()
                {
                    new AttemptView()
                    {
                        TestResults = new List<TestResultView>()
                        {
                            new TestResultView()
                            {
                                TestName = "TestNameA",
                                FailureRate = new FailureRate() {TotalRuns = 0},
                                ExceptionMessage = "A2B4C6D8",
                            },
                            new TestResultView()
                            {
                                TestName = "TestNameB",
                                FailureRate = new FailureRate {TotalRuns = 0},
                                TestSubResults = new List<TestSubResultView>()
                                {
                                    new TestSubResultView(
                                        "SubResultTestNameTest", "2A4B6C8D", "SubResultStackTrace"
                                    )
                                }
                            }
                        }
                    }
                },
                FlakyTests = new List<TestResultView>()
                {
                    new TestResultView()
                    {
                        TestName = "FlakyTestNameA",
                        FailureRate = new FailureRate(){TotalRuns = 0},
                        IsFlakyTest = true
                    },
                    new TestResultView()
                    {
                        TestName = "FlakyTestNameB",
                        FailureRate = new FailureRate {TotalRuns = 0},
                        TestSubResults = new List<TestSubResultView>()
                        {
                            new TestSubResultView(
                                "SubResultTestNameFlakyTest", "NOPQRSTUW", "SubResultStackTrace"
                            )
                        }
                    }
                },
                HasData = true,
                SummarizeInstructions = new MarkdownSummarizeInstructions(true, 5, 100)
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);

            result.Should().Contain("TestNameA").And.Contain("A2B4C").And.NotContain("A2B4C6D8");
            result.Should().Contain("TestNameB").And.Contain("2A4B6").And.NotContain("2A4B6C8D");
            result.Should().Contain("FlakyTestNameB").And.Contain("NOPQR").And.NotContain("NOPQRSTUW");
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        public void GenerateMarkdownRepositoryReportItButton(bool hasRepositoryIssues, bool expectedResult)
        {
            List<StepResult> stepResults = MockStepResults();

            MergedBuildResultAnalysis mergedBuildResultAnalysis = MockMergedBuildResultAnalysis(stepResults);

            var knownIssueUrlOptions = new KnownIssueUrlOptions
            {
                Host = "example.host/ki/new",
                RepositoryIssueParameters = new IssueParameters {Labels = new List<string> {"RepositoryIssueLabel"}},
                InfrastructureIssueParameters = new IssueParameters {Labels = new List<string> {"InfrastructureIssueLabel"}, Repository = "INFRA-REPO"}
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(new MarkdownParameters(mergedBuildResultAnalysis, "TEST-SNAPSHOT",
                "PULL-REQUEST", new Repository("TEST-REPOSITORY", hasRepositoryIssues), knownIssueUrlOptions));

            Regex.IsMatch(result, $"{knownIssueUrlOptions.Host}.*INFRA-REPO").Should().BeTrue();
            Regex.IsMatch(result, $"{knownIssueUrlOptions.Host}.*TEST-REPOSITORY").Should().Be(expectedResult);
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        public void GenerateMarkdownRepositoryReportItButtonInBuildPreviousAttempt(bool hasRepositoryIssues, bool expectedResult)
        {
            List<StepResult> stepResults = MockStepResults();
            var latestAttempt = new Attempt
            {
                LinkBuild = "null",
                AttemptId = 1,
                BuildStepsResult = stepResults,
                TestResults = new List<TestResult>()
            };

            MergedBuildResultAnalysis mergedBuildResultAnalysis = MockMergedBuildResultAnalysis(latestAttempt: latestAttempt);

            var knownIssueUrlOptions = new KnownIssueUrlOptions
            {
                Host = "example.host/ki/new",
                RepositoryIssueParameters = new IssueParameters {Labels = new List<string> {"RepositoryIssueLabel"}},
                InfrastructureIssueParameters = new IssueParameters {Labels = new List<string> {"InfrastructureIssueLabel"}, Repository = "INFRA-REPO"}
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(new MarkdownParameters(mergedBuildResultAnalysis, "TEST-SNAPSHOT",
                "PULL-REQUEST", new Repository("TEST-REPOSITORY", hasRepositoryIssues), knownIssueUrlOptions));

            Regex.IsMatch(result, $"{knownIssueUrlOptions.Host}.*INFRA-REPO").Should().BeTrue();
            Regex.IsMatch(result, $"{knownIssueUrlOptions.Host}.*TEST-REPOSITORY").Should().Be(expectedResult);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void GenerateMarkDownWithArtifactWhenHelixWorkItem(bool isHelixWorkItem)
        {
            var testResultView = new List<TestResultView>()
            {
                new()
                {
                    TestName = "FakeTestName",
                    ExceptionMessage = "FakeMessage",
                    HelixWorkItem = isHelixWorkItem ? new HelixWorkItem() : null,
                    FailureRate = new FailureRate(),
                    ArtifactLink = "ArtifactLink"
                }
            };

            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView()
            {
                TestFailuresUnique = new List<TestResultsGroupView>()
                {
                    new("https://example.test", "example-test",testResultView, 1 ) { DisplayTestsCount = 1 }
                },
                HasData = true,
            };

            using var testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);

            if (isHelixWorkItem)
            {
                result.Should().Contain("ArtifactLink");
            }
            else
            {
                result.Should().NotContain("ArtifactLink");
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void GenerateMarkDownHelixWorkItem(int exitCode)
        {

            HelixWorkItem helixWorkItem = new HelixWorkItem
            {
                HelixJobId = "ABC-DEG",
                HelixWorkItemName = "HelixWorkItemTest",
                ConsoleLogUrl = "https://example.org/workitemlog",
                ExitCode = exitCode
            };

            var testResultView = new List<TestResultView>()
            {
                new TestResultView()
                {
                    TestName = "FakeTestName",
                    ExceptionMessage = "FakeMessage",
                    HelixWorkItem = helixWorkItem,
                    FailureRate = new FailureRate(),
                    ArtifactLink = "ArtifactLink",
                    ConsoleLogLink = helixWorkItem?.ConsoleLogUrl
                }
            };

            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView()
            {
                TestFailuresUnique = new List<TestResultsGroupView>()
                {
                    new("https://example.test", "example-test",testResultView, 1 ) { DisplayTestsCount = 1 }
                },
                HasData = true,
            };

            using var testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("https://example.org/workitemlog").And.Contain("ArtifactLink");

            if (exitCode == 0)
            {
                result.Should().NotContain("work item crash");
                result.Should().Contain("FakeMessage");
            }
            else
            {
                result.Should().Contain("work item crash");
                result.Should().NotContain("FakeMessage");
            }
        }


        [TestCase(true, true)]
        [TestCase(false, false)]
        public void GenerateMarkdownWithHelixWorkItemConsoleLog(bool hasRepositoryIssues, bool expectedResult)
        {
            var testResultView =  new List<TestResultView>()
            {
                new TestResultView()
                {
                    TestName = "TestNameA",
                    FailureRate = new FailureRate() {TotalRuns = 0},
                    ExceptionMessage = "A2B4C6D8",
                    ConsoleLogLink = "TestConsoleLogLink"
                },
            };

            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                BuildFailuresUnique = new List<StepResultView> { BuildStep(Enumerable.Empty<Error>()) },
                TestFailuresUnique = new List<TestResultsGroupView>()
                {
                    new("https://example.test", "example-test",testResultView, 1 ) { DisplayTestsCount = 1 }
                },
                HasData = true
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);

            result.Should().Contain("TestConsoleLogLink");
        }

        [Test]
        public void GenerateMarkdownWithCriticalIssues()
        {
            var knownIssueListBuilder = ImmutableList.CreateBuilder<KnownIssueView>();
            knownIssueListBuilder.Add(new KnownIssueView("IssueNameCritical", "https://helix.example", "test/repo", "issueId123", "", ""));
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                BuildFailuresUnique = new List<StepResultView> { BuildStep(Enumerable.Empty<Error>()) },
                TestFailuresUnique = new List<TestResultsGroupView>() { },
                CriticalIssues = knownIssueListBuilder.ToImmutable(),
                HasData = true
            };

            using TestData testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);

            result.Should().Contain("IssueNameCritical");
            result.Should().Contain("https://helix.example");
        }


        [Test]
        public void GenerateMarkDownWithTesKnownIssue()
        {
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView()
            {
                TestKnownIssues = new List<KnownIssueView>() {new KnownIssueView("test-known-issue-name", "https://helix.example", "issue-repository-tes", "1234", "link", "https://helix.example") },
                HasData = true,
            };

            using var testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("test-known-issue-name").And.Contain("https://helix.example");
        }


        [Test]
        public void GenerateMarkDownSummary()
        {
            var markdownParameters = new MarkdownParameters(MockMergedBuildResultAnalysis(), "ANY_SNAPSHOT",
                "ANY_PULL_REQUEST", new Repository("TEST-REPOSITORY", true));
            var buildResultAnalysis = new ConsolidatedBuildResultAnalysisView
            {
                RenderSummary = true,
                BuildAnalysisSummaries = new List<BuildAnalysisSummaryView>(),
                BuildFailuresUnique = new List<StepResultView>(){ new (MockStepResults().First(), "PIPELINE_TEST", "BUILD_TEST", markdownParameters)},
                HasData = true
            };

            using var testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildResultAnalysis);
            result.Should().Contain("Build Summary");
        }

        [Test]
        public void GenerateMarkDownOverrideResult()
        {
            string reason = "TESTING MARKDOWN GENERATOR";
            string previousResult = "FailedTest";
            string newResult = "PassedTest";
            string checkResultBody = "CheckResultBody";

            BuildAnalysisUpdateOverridenResult buildAnalysisUpdateOverridenResult = new BuildAnalysisUpdateOverridenResult(reason, previousResult, newResult, checkResultBody);

            using var testData = TestData.Default();
            string result = testData.Generator.GenerateMarkdown(buildAnalysisUpdateOverridenResult);
            result.Should().Contain("Build Analysis Check Result has been manually overridden");
            result.Should().Contain(reason);
            result.Should().Contain(previousResult);
            result.Should().Contain(newResult);
            result.Should().Contain(checkResultBody);
        }

        private static List<StepResult> MockStepResults()
        {
            return new List<StepResult>
            {
                new StepResult
                {
                    StepName = "StepNameError",
                    Errors = new List<Error>
                    {
                        new Error {ErrorMessage = "StepErrorMessage"}
                    },
                    FailureRate = new FailureRate {TotalRuns = 0}
                }
            };
        }

        private static MergedBuildResultAnalysis MockMergedBuildResultAnalysis(
            List<StepResult> stepResults = null,
            List<TestResult> testResults = null,
            Attempt latestAttempt = null)
        {
            return new MergedBuildResultAnalysis(
                "COMMIT-HASH",
                ImmutableList.Create(
                    new BuildResultAnalysis
                    {
                        PipelineName = "",
                        BuildId = 0,
                        BuildNumber = "",
                        TargetBranch = Branch.Parse("fakeTargetBranchName"),
                        LinkToBuild = "",
                        LinkAllTestResults = "",
                        IsRerun = false,
                        BuildStatus = BuildStatus.Failed,
                        TestResults = testResults ?? new List<TestResult>(),
                        BuildStepsResult = stepResults ?? new List<StepResult>(),
                        LatestAttempt = latestAttempt ?? new Attempt(),
                        TestKnownIssuesAnalysis = new TestKnownIssuesAnalysis()
                    }
                ),
                CheckResult.Failed,
                null,
                null,
                null
            );
        }

        private TestResult MockTestResult(string testName)
        {
            return new TestResult(
                new TestCaseResult(testName, new DateTimeOffset(2021, 5, 28, 11, 0, 0, TimeSpan.Zero),
                    TestOutcomeValue.Failed, 0, 0, 0, new PreviousBuildRef(), "", "", "", null, 55000), "", new FailureRate());
        }
    }
}
