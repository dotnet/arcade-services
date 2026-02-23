using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using Moq;
using NUnit.Framework;

namespace BuildInsights.BuildResultProcessor.Tests
{
    [TestFixture]
    public class BuildAnalysisTests
    {
        private static readonly BuildReferenceIdentifier _buildReference = new BuildReferenceIdentifier("dnceng-public", "public", 12345, "",686, null, null, null, null);
        private static readonly Branch TargetBranch = Branch.Parse("main");

        private class TestData : IDisposable, IAsyncDisposable
        {
            private readonly ServiceProvider _provider;
            public BuildAnalysisProvider BuildAnalysis { get; }
            public Mock<IBuildDataService> BuildDataService { get; }

            private TestData(ServiceProvider provider, BuildAnalysisProvider buildAnalysis, Mock<IBuildDataService> buildDataService)
            {
                _provider = provider;
                BuildAnalysis = buildAnalysis;
                BuildDataService = buildDataService;
            }

            public void Dispose()
            {
                _provider.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                return _provider.DisposeAsync();
            }

            public class Builder
            {
                private readonly ImmutableDictionary<Guid, ImmutableList<TimelineRecord>> _previousTimelines = ImmutableDictionary<Guid, ImmutableList<TimelineRecord>>.Empty;
                private readonly ImmutableList<TimelineRecord> _timeline = ImmutableList<TimelineRecord>.Empty;
                private readonly ImmutableList<TestRunDetails> _testCaseResultFailuresBuild = ImmutableList<TestRunDetails>.Empty;
                private readonly ImmutableList<TestRunDetails> _testCaseResultsLatestRun = ImmutableList<TestRunDetails>.Empty;
                private readonly ImmutableList<TestRunDetails> _testPassedOnRerun = ImmutableList<TestRunDetails>.Empty;
                private readonly ImmutableDictionary<int, TestCaseResult> _testResultsHistory = ImmutableDictionary<int, TestCaseResult>.Empty;
                private readonly ImmutableDictionary<string, ImmutableList<TestHistoryByBranch>> _testHistory = ImmutableDictionary<string, ImmutableList<TestHistoryByBranch>>.Empty;
                private readonly ImmutableList<Build> _latestBuilds = ImmutableList<Build>.Empty;
                private readonly ImmutableList<Build> _builds = ImmutableList<Build>.Empty;
                private readonly ImmutableList<KnownIssue> _knownIssues = ImmutableList<KnownIssue>.Empty;

                public Builder()
                {
                }

                private Builder(
                    ImmutableList<Build> builds,
                    ImmutableList<TimelineRecord> timeline,
                    ImmutableList<TestRunDetails> testCaseResultFailuresBuild,
                    ImmutableList<TestRunDetails> testCaseResultsLatestRun,
                    ImmutableList<TestRunDetails> testPassedOnRerun,
                    ImmutableDictionary<int, TestCaseResult> testResultsHistory,
                    ImmutableDictionary<string, ImmutableList<TestHistoryByBranch>> testHistory,
                    ImmutableDictionary<Guid, ImmutableList<TimelineRecord>> previousTimelines,
                    ImmutableList<Build> latestBuilds,
                    ImmutableList<KnownIssue> knownIssues)
                {
                    _builds = builds;
                    _timeline = timeline;
                    _testCaseResultFailuresBuild = testCaseResultFailuresBuild;
                    _testCaseResultsLatestRun = testCaseResultsLatestRun;
                    _testPassedOnRerun = testPassedOnRerun;
                    _testResultsHistory = testResultsHistory;
                    _testHistory = testHistory;
                    _previousTimelines = previousTimelines;
                    _latestBuilds = latestBuilds;
                    _knownIssues = knownIssues;
                }

                private Builder With(
                    ImmutableList<Build> builds = null,
                    ImmutableList<TestRunDetails> testCaseResultFailuresBuild = null,
                    ImmutableList<TestRunDetails> testCaseResultsLatestRun = null,
                    ImmutableList<TestRunDetails> testPassedOnRerun = null,
                    ImmutableList<TimelineRecord> timeline = null,
                    ImmutableDictionary<int, TestCaseResult> testResultsHistory = null,
                    ImmutableDictionary<string, ImmutableList<TestHistoryByBranch>> testHistory = null,
                    ImmutableDictionary<Guid, ImmutableList<TimelineRecord>> previousTimelines = null,
                    ImmutableList<Build> latestBuilds = null,
                    ImmutableList<KnownIssue> knownIssues = null)
                {
                    return new Builder(
                        builds ?? _builds,
                        timeline ?? _timeline,
                        testCaseResultFailuresBuild ?? _testCaseResultFailuresBuild,
                        testCaseResultsLatestRun ?? _testCaseResultsLatestRun,
                        testPassedOnRerun ?? _testPassedOnRerun,
                        testResultsHistory ?? _testResultsHistory,
                        testHistory ?? _testHistory,
                        previousTimelines ?? _previousTimelines,
                        latestBuilds ?? _latestBuilds,
                        knownIssues ?? _knownIssues);
                }

                public Builder WithTestCaseResultsLatestRun(List<TestRunDetails> testCaseResult) => With(testCaseResultsLatestRun: testCaseResult.ToImmutableList());

                public Builder WithTimeline(List<TimelineRecord> timeline) => With(timeline: timeline.ToImmutableList());

                public Builder WithTestCaseResultFailuresBuild(List<TestRunDetails> testCaseResults) => With(testCaseResultFailuresBuild: testCaseResults.ToImmutableList());

                public Builder WithTestPassedOnRerun(List<TestRunDetails> testPassedOnRerun) => With(testPassedOnRerun: testPassedOnRerun.ToImmutableList());

                public Builder WithTestResultsHistory(int key, TestCaseResult result) => With(testResultsHistory: _testResultsHistory.Add(key, result));

                public Builder WithTestHistory(string key, List<TestHistoryByBranch> testHistory) => With(testHistory: _testHistory.Add(key, testHistory.ToImmutableList()));

                public Builder WithKnownIssues(ImmutableList<KnownIssue> knownIssues) => With(knownIssues: knownIssues);

                public Builder WithPreviousTimeline(string key, List<TimelineRecord> timeline) =>
                    With(previousTimelines: _previousTimelines.Add(CreateGuid(key), timeline.ToImmutableList()));

                public Builder WithLatestBuilds(List<Build> builds) => With(latestBuilds: builds.ToImmutableList());

                public Builder AddBuild(Build build) => With(builds: _builds.Add(build));
                public Builder AddBuilds(IEnumerable<Build> builds) => With(builds: _builds.AddRange(builds));

                public TestData Build()
                {
                    var buildRetryServiceMock = new Mock<IBuildRetryService>();
                    var buildDataServiceMock = new Mock<IBuildDataService>();
                    buildDataServiceMock.Setup(m => m.GetBuildAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                        .Returns<string, string, int, CancellationToken>((org, definitionName, buildId, cancellationToken) =>
                            Task.FromResult(_builds.FirstOrDefault(b => b.Id == buildId)));
                    buildDataServiceMock.Setup(m => m.GetFailingTestsForBuildAsync(It.IsAny<Build>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(_testCaseResultsLatestRun.ToList);
                    buildDataServiceMock.Setup(m => m.GetAllFailingTestsForBuildAsync(It.IsAny<Build>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(_testCaseResultFailuresBuild);
                    buildDataServiceMock
                        .Setup(m => m.GetTestHistoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                        .Returns<string, string, string, DateTimeOffset, CancellationToken>((orgId, projectId, testName, maxCompleted, cancellationToken) =>
                              Task.FromResult(_testHistory.ContainsKey(testName) ? _testHistory[testName].ToList() : new List<TestHistoryByBranch>()));
                    buildDataServiceMock
                        .Setup(b => b.GetTestResultByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                            It.IsAny<CancellationToken>(), ResultDetails.None))
                        .Returns<string, string, int, int, CancellationToken, ResultDetails>((orgId, projectId, parse, id, cancellationToken, resultDetails) =>
                            Task.FromResult(_testResultsHistory.ContainsKey(id) ? _testResultsHistory[id] : MockTestCaseResult("", TestOutcomeValue.Failed, id: id)));
                    buildDataServiceMock
                        .Setup(b => b.GetTestResultByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                            It.IsAny<CancellationToken>(), ResultDetails.SubResults))
                        .Returns<string, string, int, int, CancellationToken, ResultDetails>((orgId, projectId, parse, id, cancellationToken, resultDetails) =>
                            Task.FromResult(_testCaseResultsLatestRun.SelectMany(t => t.Results).FirstOrDefault(t => t.Id == id) ?? MockTestCaseResult("", TestOutcomeValue.Failed, id: id)));
                    buildDataServiceMock.Setup(b =>
                        b.GetLatestBuildsForBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Branch>(), It.IsAny<DateTimeOffset>(),
                            It.IsAny<CancellationToken>()))
                        .ReturnsAsync(_latestBuilds);
                    buildDataServiceMock.Setup(
                            m => m.GetLatestBuildTimelineRecordsAsync(
                                It.IsAny<string>(),
                                It.IsAny<string>(),
                                It.IsAny<int>(),
                                It.IsAny<CancellationToken>()
                            )
                        )
                        .ReturnsAsync(_timeline);

                    buildDataServiceMock.Setup(
                            m => m.GetBuildTimelineRecordsAsync(
                                It.IsAny<string>(),
                                It.IsAny<string>(),
                                It.IsAny<int>(),
                                It.IsAny<Guid>(),
                                It.IsAny<CancellationToken>()
                            )
                        )
                        .Returns<string, string, int, Guid, CancellationToken>(
                            (orgId, projectId, buildId, timelineId, cancellationToken) =>
                                Task.FromResult<IReadOnlyList<TimelineRecord>>(_previousTimelines[timelineId])
                        );

                    var tableServiceMock = new Mock<IBuildAnalysisHistoryService>();
                    tableServiceMock.Setup(m => m.GetLastBuildAnalysisRecord(It.IsAny<int>(), It.IsAny<string>()));

                    var testResultsServiceMock = new Mock<ITestResultService>();
                    testResultsServiceMock.Setup(t =>
                        t.GetTestFailingWithKnownIssuesAnalysis(It.IsAny<IReadOnlyList<TestRunDetails>>(),
                            It.IsAny<List<KnownIssue>>(), It.IsAny<string>()))
                        .ReturnsAsync(new TestKnownIssuesAnalysis(false, new List<TestResult>()));

                    var githubIssuesServiceMock = new Mock<IGitHubIssuesService>();
                    githubIssuesServiceMock.Setup(t => t.GetRepositoryKnownIssues(It.IsAny<string>())).ReturnsAsync(_knownIssues.ToList());

                    var knownIssuesServiceMock = new Mock<IKnownIssuesService>();
                    var helixDataServiceMock = new Mock<IHelixDataService>();
                    var azDoToGitHubRepositoryMock = new Mock<IAzDoToGitHubRepositoryService>();

                    ServiceProvider services = new ServiceCollection()
                        .AddLogging(l => { l.AddProvider(new NUnitLogger()); })
                        .AddSingleton<BuildAnalysisProvider>()
                        .AddSingleton(knownIssuesServiceMock.Object)
                        .AddSingleton(buildDataServiceMock.Object)
                        .AddSingleton(buildRetryServiceMock.Object)
                        .AddSingleton(githubIssuesServiceMock.Object)
                        .AddSingleton(helixDataServiceMock.Object)
                        .AddSingleton(testResultsServiceMock.Object)
                        .AddSingleton(tableServiceMock.Object)
                        .AddSingleton(azDoToGitHubRepositoryMock.Object)
                        .AddSingleton<IKnownIssuesMatchService, KnownIssuesMatchProvider>()
                        .AddSingleton(new Mock<ICheckResultService>().Object)
                        .AddSingleton(new TelemetryClient(new TelemetryConfiguration()))
                        .Configure<KnownIssuesAnalysisLimits>(
                            l =>
                            {
                                l.RecordCountLimit = 1000;
                                l.LogLinesCountLimit = 100000;
                                l.FailingTestCountLimit = 1000;
                                l.HelixLogsFilesLimit = 100;
                            }
                        )
                        .BuildServiceProvider();

                    return new TestData(services, services.GetRequiredService<BuildAnalysisProvider>(), buildDataServiceMock);
                }
            }

            public static Builder Default { get; } = new Builder();
        }

        private class BuildBuilder
        {
            private readonly string _commitHash = "TEST-COMMIT-HASH";
            private readonly Branch _targetBranch = TargetBranch;
            private readonly BuildResult _result = BuildResult.Failed;
            private readonly ImmutableList<BuildValidationResult> _validationResults = ImmutableList<BuildValidationResult>.Empty;

            public BuildBuilder()
            {
            }

            private BuildBuilder(
                string commitHash,
                Branch targetBranch,
                BuildResult result,
                ImmutableList<BuildValidationResult> validationResults)
            {
                _commitHash = commitHash;
                _targetBranch = targetBranch;
                _result = result;
                _validationResults = validationResults;
            }

            private BuildBuilder With(
                string commitHash = null,
                Branch targetBranch = null,
                BuildResult? result = null,
                ImmutableList<BuildValidationResult> validationResults = null)
            {
                return new BuildBuilder(
                    commitHash ?? _commitHash,
                    targetBranch ?? _targetBranch,
                    result ?? _result,
                    validationResults ?? _validationResults);
            }

            public BuildBuilder AddValidationResult(BuildValidationResult validationResult) =>
                    With(validationResults: _validationResults.Add(validationResult));

            public BuildBuilder WithResult(BuildResult result) => With(result: result);

            public BuildBuilder WithTargetBranch(Branch targetBranch) => With(targetBranch: targetBranch);

            public Build Build()
            {
                return new Build(
                    id: 12345,
                    organizationName: "dnceng-public",
                    definitionName: "BuildDefinitionTestName",
                    repository: "BuildRepositoryTestId",
                    projectName: "public",
                    targetBranch: TargetBranch,
                    result: _result,
                    validationResults: ImmutableList.CreateRange(_validationResults)
                );
            }

            public static BuildBuilder Default { get; } = new BuildBuilder();
        }

        [Test]
        public async Task ValidateBuildAnalysisWithoutTimeline()
        {
            string expectedErrorMessage = "TEST Validation Error Message";

            var validationResult = new BuildValidationResult(BuildValidationStatus.Error, expectedErrorMessage);

            await using TestData testData = TestData.Default
                .AddBuild(BuildBuilder.Default.AddValidationResult(validationResult).Build())
                .Build();

            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            StepResult expectedStepResult = new StepResult
            {
                StepName = "Pipeline Definition Validation",
                Errors =
                {
                    new Error
                    {
                        ErrorMessage = expectedErrorMessage
                    }
                },
            };

            result.TestResults.Should().BeEmpty();
            result.BuildStepsResult.Should().NotBeEmpty()
                .And.HaveCount(1)
                .And.ContainEquivalentOf(expectedStepResult);
        }

        [Test]
        public async Task ValidateValidationErrorMessageContent()
        {
            var build = BuildBuilder.Default
                .AddValidationResult(new BuildValidationResult(BuildValidationStatus.Error, "Error Message"))
                .AddValidationResult(new BuildValidationResult(BuildValidationStatus.Ok, "OK Message"))
                .AddValidationResult(new BuildValidationResult(BuildValidationStatus.Warning, "Warning Message"))
                .Build();
            TestData.Builder builder = TestData.Default
                .AddBuild(build);

            await using TestData testData = builder.Build();

            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            var expectedStepResult = new StepResult
            {
                StepName = "Pipeline Definition Validation",
                Errors =
                {
                    new Error
                    {
                        ErrorMessage = "Error Message"
                    }
                },
            };

            result.TestResults.Should().BeEmpty();
            result.BuildStepsResult.Should().NotBeEmpty()
                .And.HaveCount(1)
                .And.ContainEquivalentOf(expectedStepResult);
            result.BuildStepsResult[0].Errors.Should().HaveCount(1)
                .And.BeEquivalentTo(expectedStepResult.Errors);
        }

        [Test]
        public async Task BuildAnalysisLatestPreviousAttemptTest()
        {

            //All the records are now successful as BuildResult.Succeeded and is the latest timeline
            var latestTimelineRecords = new List<TimelineRecord>
            {
                MockRecord(
                    "A",
                    3,
                    TaskResult.Succeeded,
                    new List<TimelineAttempt>
                    {
                        MockAttempt("1A", 1, "AA"),
                        MockAttempt("2A", 2, "AB"),
                        MockAttempt("3A", 3, "AC")
                    }
                ),
                MockRecord(
                    "BB",
                    3,
                    TaskResult.Succeeded,
                    new List<TimelineAttempt>
                    {
                        MockAttempt("1B", 1, "BA"),
                        MockAttempt("2B", 2, "BB"),
                        MockAttempt("3B", 3, "BC")
                    }
                ),
                MockRecord(
                    "C",
                    2,
                    TaskResult.Succeeded,
                    new List<TimelineAttempt>
                    {
                        MockAttempt("1C", 1, "CA"),
                        MockAttempt("2C", 2, "CB")
                    }
                )
            };

            //Previous timeline from different records
            var TimelineAC = new List<TimelineRecord>()
            {
                MockRecord(
                    "ACA",
                    3,
                    TaskResult.Failed,
                    new List<TimelineIssue> {new TimelineIssue("TimelineAC - Error Message")},
                    RecordType.Task
                )
            };
            var TimelineBC = new List<TimelineRecord>()
            {
                MockRecord(
                    "BCA",
                    3,
                    TaskResult.Failed,
                    new List<TimelineIssue> {new TimelineIssue("TimelineBC - Error Message")},
                    RecordType.Task
                )
            };
            var TimelineCB = new List<TimelineRecord>()
            {
                MockRecord(
                    "CBA",
                    2,
                    TaskResult.Failed,
                    new List<TimelineIssue> {new TimelineIssue("TimelineCB - Error Message")},
                    RecordType.Task
                )
            };
            var builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Succeeded).Build())
                .WithTimeline(latestTimelineRecords);

            builder = builder.WithPreviousTimeline("AC", TimelineAC);
            builder = builder.WithPreviousTimeline("BC", TimelineBC);
            builder = builder.WithPreviousTimeline("CB", TimelineCB);

            await using TestData testData = builder.Build();

            BuildResultAnalysis result =
                await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);
            result.LatestAttempt.AttemptId.Should().Be(3);
            result.LatestAttempt.HasBuildFailures.Should().BeTrue();
            result.LatestAttempt.HasTestFailures.Should().BeFalse();
            result.LatestAttempt.BuildStepsResult.Count.Should().Be(2);
            result.LatestAttempt.BuildStepsResult.SelectMany(h => h.Errors).Select(e => e.ErrorMessage).Any(e => e.Contains("TimelineBC - Error Message")).Should().BeTrue();
            result.LatestAttempt.BuildStepsResult.SelectMany(h => h.Errors).Select(e => e.ErrorMessage).Any(e => e.Contains("TimelineCB - Error Message")).Should().BeFalse();
        }

        [Test]
        public async Task BuildAnalysisPreviousAttemptTestResultsTest()
        {
            var latestTimelineRecords = new List<TimelineRecord>
            {
                MockRecord("A", 3, TaskResult.Succeeded, new List<TimelineAttempt>
                {
                    MockAttempt("1A", 1, "AA")
                })
            };

            var testResult = new List<TestRunDetails>
            {
                new TestRunDetails(new TestRunSummary(123, "TestRunNameTest", MockPipelineReference("","","")),
                    new List<TestCaseResult>
                    {
                        MockTestCaseResult("AutomatedTestNameTest", TestOutcomeValue.Failed)
                    },
                    new DateTimeOffset(2021, 5, 12, 0, 0, 0, TimeSpan.Zero))
            };

            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Succeeded).Build())
                .WithTimeline(latestTimelineRecords)
                .WithTestCaseResultFailuresBuild(testResult);

            builder = builder.WithPreviousTimeline("AA", new List<TimelineRecord>());

            await using TestData testData = builder.Build();

            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);
            result.LatestAttempt.HasBuildFailures.Should().Be(false);
            result.LatestAttempt.HasTestFailures.Should().Be(true);
            result.LatestAttempt.TestResults.Count.Should().Be(1);
        }

        [Test]
        public async Task BuildAnalysisAttemptWithoutPipeline()
        {
            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Failed).Build());
            await using TestData testData = builder.Build();

            BuildResultAnalysis result =
                await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            result.Attempt.Should().Be(0);
        }

        [Test]
        public async Task BuildAnalysisAttemptWithPipeline()
        {
            var timelineRecords = new List<TimelineRecord>
            {
                MockRecord("A", 3, TaskResult.Failed, new List<TimelineAttempt>()),
                MockRecord("B", 5, TaskResult.Failed, new List<TimelineAttempt>())
            };

            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Failed).Build())
                .WithTimeline(timelineRecords);
            await using TestData testData = builder.Build();

            BuildResultAnalysis result =
                await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            result.Attempt.Should().Be(5);
        }

        private static DateTimeOffset MockDateTimeOffset()
        {
            return new DateTimeOffset(2021, 5, 12, 0, 0, 0, TimeSpan.Zero);
        }
        
        [Test]
        public async Task BuildWithNullTargetBranch()
        {
            // Manually-triggered builds have an invalid branch name which is parsed to null.
            // Design allows null TargetBranch.
            // Analysis should not crash if presented with null TargetBranch.

            const string failingTestName = "TestA";
            Branch targetBranch = null;

            // Source branch test failure
            var testResult = new List<TestRunDetails>
            {
                new TestRunDetails(new TestRunSummary(123, "TestRunNameTest", MockPipelineReference("","","")),
                    new List<TestCaseResult>
                    {
                        MockTestCaseResult(failingTestName, TestOutcomeValue.Failed)
                    },
                    MockDateTimeOffset()
                    )
            };

            var latestBuilds = new List<Build>();

            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithTargetBranch(targetBranch).Build())
                .WithTestCaseResultsLatestRun(testResult)
                .WithLatestBuilds(latestBuilds);
            await using TestData testData = builder.Build();

            Func<Task> act = async () => await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);
            await act.Should().NotThrowAsync();
        }

        [Test]
        public async Task CreateConsoleLogLinkTest()
        {
            ImmutableDictionary<string, string> data = ImmutableDictionary.Create<string, string>().Add("logFileLineNumber", "1547");
            var latestTimelineRecords = new List<TimelineRecord>
            {
                MockRecord("A", 0, TaskResult.Failed, new List<TimelineIssue> {new TimelineIssue("", data:data)}, RecordType.Task, "url")
            };

            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.Build())
                .WithTimeline(latestTimelineRecords);

            await using TestData testData = builder.Build();
            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            string linkLogResult = result.BuildStepsResult.First().Errors.First().LinkLog;
            linkLogResult.Should().NotBeNullOrEmpty();
            linkLogResult.Should().Contain("buildId=12345").And.Contain("l=1547");
        }

        [Test]
        public async Task BuildWithTestFailureInContinueOnError()
        {
            var latestTimelineRecords = new List<TimelineRecord>
            {
                MockRecord("A", TaskResult.SucceededWithIssues, RecordType.Stage, "Stage_Name_A_Test"),
                MockRecord("AA", TaskResult.SucceededWithIssues, RecordType.Phase, "Phase_Name_A_Test", "A"),
                MockRecord("AAA", TaskResult.SucceededWithIssues, RecordType.Job, "Job_Name_A_Test", "AA"),
                MockRecord("AAAA", TaskResult.SucceededWithIssues, RecordType.Task, "Task_Name_A_Test", "AAA"),
                MockRecord("B", TaskResult.Failed, RecordType.Stage, "Stage_Name_B_Test"),
                MockRecord("BB", TaskResult.Failed, RecordType.Phase, "Phase_Name_B_Test", "B"),
                MockRecord("BBB", TaskResult.Failed, RecordType.Job, "Job_Name_B_Test", "BB"),
                MockRecord("BBBB", TaskResult.Failed, RecordType.Task, "Task_Name_B_Test", "BBB")
            };

            var testResult = new List<TestRunDetails>
            {
                MockTestCaseResultsByTestRun(id: 1,"AutomatedTestA",MockPipelineReference("Stage_Name_A_Test", "Phase_Name_A_Test", "Job_Name_A_Test")),
                MockTestCaseResultsByTestRun(id: 2, "AutomatedTestB",MockPipelineReference("Stage_Name_B_Test", "Phase_Name_B_Test","Job_Name_B_Test"))
            };

            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Failed).Build())
                .WithTimeline(latestTimelineRecords)
                .WithTestCaseResultsLatestRun(testResult);

            await using TestData testData = builder.Build();
            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);
            result.TestResults.Should().HaveCount(1);
            result.TestResults.First().TestCaseResult.Name.Should().Be("AutomatedTestB");
        }

        [Test]
        public async Task BuildWithTaskCanceled()
        {
            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Succeeded).Build())
                .WithTimeline(new List<TimelineRecord>()
                {
                    MockRecord(
                        name: "A",
                        attempt: 1,
                        result: TaskResult.Canceled,
                        issues: new List<TimelineIssue>()
                        {
                            new TimelineIssue("TaskCanceledTest")
                        },
                        RecordType.Task
                    ),
                });

            await using TestData testData = builder.Build();
            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            result.BuildStepsResult.Should().HaveCount(1);
            result.BuildStepsResult.First().Errors.Should().HaveCount(1);
            result.BuildStepsResult.First().Errors.First().ErrorMessage.Should().Be("TaskCanceledTest");
        }

        [Test]
        public async Task BuildMessageIsCleaned_ExitCode1_OneStep()
        {
            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Succeeded).Build())
                .WithTimeline(new List<TimelineRecord>()
                {
                    MockRecord(
                        name: "A",
                        attempt: 1,
                        result: TaskResult.Failed,
                        issues: new List<TimelineIssue>()
                        {
                            new TimelineIssue("Cmd.exe exited with code '1'.")
                        },
                        RecordType.Task
                    )
                });

            await using TestData testData = builder.Build();
            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            result.BuildStepsResult.Should().HaveCount(1);
            result.BuildStepsResult.First().Errors.Should().HaveCount(1);
            result.BuildStepsResult.First().Errors.First().ErrorMessage.Should().Contain("Cmd.exe exited with code '1'.");
        }

        [Test]
        public async Task BuildMessageIsCleaned_ExitCode1_TwoSteps()
        {
            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Succeeded).Build())
                .WithTimeline(new List<TimelineRecord>()
                {
                    MockRecord(
                        name: "A",
                        attempt: 1,
                        result: TaskResult.Failed,
                        issues: new List<TimelineIssue>()
                        {
                            new TimelineIssue("Cmd.exe exited with code '1'."),
                            new TimelineIssue("Failure running tests")
                        },
                        RecordType.Task
                    )
                });

            await using TestData testData = builder.Build();
            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            result.BuildStepsResult.Should().HaveCount(1);
            result.BuildStepsResult
                .First().Errors
                .Select(error => error.ErrorMessage).Should().NotContain("Cmd.exe exited with code '1'.");
        }

        [Test]
        public async Task BuildMessageIsCleaned_ExitCode1_MultipleUnique_NoOtherSteps()
        {
            string[] exitCodeMessages = {
                "Cmd.exe exited with code '1'.",
                "dotnet.exe exited with code '1'."
            };

            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Succeeded).Build())
                .WithTimeline(new List<TimelineRecord>()
                {
                    MockRecord(
                        name: "A",
                        attempt: 1,
                        result: TaskResult.Failed,
                        issues: new List<TimelineIssue>()
                        {
                            new TimelineIssue(exitCodeMessages[0]),
                            new TimelineIssue(exitCodeMessages[1]),
                        },
                        RecordType.Task
                    )
                });

            await using TestData testData = builder.Build();
            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            result.BuildStepsResult.Should().HaveCount(1);
            result.BuildStepsResult.First().Errors.Should().HaveCount(exitCodeMessages.Length);
            result.BuildStepsResult.First().Errors.Select(e => e.ErrorMessage).Should().Contain(exitCodeMessages);
        }

        [Test]
        public async Task BuildMessageIsCleaned_ExitCode1_Multiple_WithOtherSteps()
        {
            string[] exitCodeMessages = {
                "Cmd.exe exited with code '1'.",
                "dotnet.exe exited with code '1'."
            };

            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Succeeded).Build())
                .WithTimeline(new List<TimelineRecord>()
                {
                    MockRecord(
                        name: "A",
                        attempt: 1,
                        result: TaskResult.Failed,
                        issues: new List<TimelineIssue>()
                        {
                            new TimelineIssue(exitCodeMessages[0]),
                            new TimelineIssue(exitCodeMessages[1]),
                            new TimelineIssue("Something else is wrong")
                        },
                        RecordType.Task
                    )
                });

            await using TestData testData = builder.Build();
            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            result.BuildStepsResult.Should().HaveCount(1);
            result.BuildStepsResult.First().Errors.Should().HaveCount(1);
            result.BuildStepsResult.First().Errors.Select(e => e.ErrorMessage).Should().NotContain(exitCodeMessages);
        }

        [TestCase("pre (NETCORE_ENGINEERING_TELEMETRY=StringOfCharacters) post", "pre post")]
        [TestCase("(NETCORE_ENGINEERING_TELEMETRY=StringOfCharacters) post", "post")]
        [TestCase("pre (NETCORE_ENGINEERING_TELEMETRY=StringOfCharacters)", "pre ")]
        public async Task BuildMessageIsCleaned_NetcoreEngineeringTelemetry(string errorMessage, string expectedErrorMessage)
        {
            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Succeeded).Build())
                .WithTimeline(new List<TimelineRecord>()
                {
                    MockRecord(
                        name: "A",
                        attempt: 1,
                        result: TaskResult.Failed,
                        issues: new List<TimelineIssue>()
                        {
                            new TimelineIssue(errorMessage)
                        },
                        RecordType.Task
                    ),
                });

            await using TestData testData = builder.Build();
            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            result.BuildStepsResult.Should().HaveCount(1);
            result.BuildStepsResult.First().Errors.Should().HaveCount(1);
            result.BuildStepsResult.First().Errors.First().ErrorMessage.Should().Be(expectedErrorMessage);
        }

        [TestCase("net6.0-Linux-Debug-x64-CoreCLR_checked-(Alpine.312.Amd64.Open)ubuntu.1604.amd64.open@mcr.microsoft.com/dotnet-buildtools", "Alpine.312.Amd64.Open")]
        [TestCase("Alpine.312.Amd64.Open", "Alpine.312.Amd64.Open")]
        public async Task BuildAnalysis_TestConfigurations(string testRunName, string expectedConfigurationName)
        {
            // Source branch test failure
            var testResult = new List<TestRunDetails>
            {
                new TestRunDetails(new TestRunSummary(123, testRunName, MockPipelineReference("","","")),
                    new List<TestCaseResult>
                    {
                        MockTestCaseResult("TestA", TestOutcomeValue.Failed)
                    },
                    MockDateTimeOffset()
                )
            };

            var latestBuilds = new List<Build> { new Build(1) };

            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.Build())
                .WithTestCaseResultsLatestRun(testResult)
                .WithLatestBuilds(latestBuilds);
            await using TestData testData = builder.Build();

            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);
            result.TestResults.First().FailingConfigurations.Select(t => t.Configuration).First().Name.Should().Be(expectedConfigurationName);
        }

        [TestCase("Job_Name_A_Test",0)]
        [TestCase("Job_Name_B_Test", 1)]
        public async Task BuildTestMessageIsCleaned_WhenFailureIsListedAsTestFailure(string testFailingInJob, int expectedBuildFailures)
        {
            var testResult = new List<TestRunDetails>
            {
                MockTestCaseResultsByTestRun(id: 1,"AutomatedTestA",MockPipelineReference("Stage_Name_A_Test", "Phase_Name_A_Test", testFailingInJob)),
            };

            var latestTimelineRecords = new List<TimelineRecord>
            {
                MockRecord("A", TaskResult.Failed, RecordType.Stage, "Stage_Name_A_Test"),
                MockRecord("AA", TaskResult.Failed, RecordType.Phase, "Phase_Name_A_Test", "A"),
                MockRecord("AAA", TaskResult.Failed, RecordType.Job, "Job_Name_A_Test", "AA"),
                MockRecord(
                    name: "AAAA",
                    attempt: 1,
                    result: TaskResult.Failed,
                    issues: new List<TimelineIssue>()
                    {
                        new TimelineIssue("(NETCORE_ENGINEERING_TELEMETRY=Test) TestFailed")
                    },
                    RecordType.Task,
                    parent:"AAA"
                )
            };

            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Succeeded).Build())
                .WithTimeline(latestTimelineRecords)
                .WithTestCaseResultsLatestRun(testResult);

            await using TestData testData = builder.Build();
            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            result.BuildStepsResult.Should().HaveCount(expectedBuildFailures);
            result.TestResults.Should().HaveCount(1);
        }

        [Test]
        public async Task KnownIssueWithErrorMessageMatch()
        {
            string issueBody = "```\r\n{\r\n    \"errorMessage\" : \"Cmd.exe exited with code 1\"\r\n}\r\n```";
            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Failed).Build())
                .WithKnownIssues(ImmutableList.Create(KnownIssueHelper.ParseGithubIssue(MockIssue(issueBody), "testRepo", KnownIssueType.Infrastructure)))
                .WithTimeline(new List<TimelineRecord>()
                {
                    MockRecord(
                        name: "A",
                        attempt: 1,
                        result: TaskResult.Failed,
                        issues: new List<TimelineIssue>()
                        {
                            new TimelineIssue("The command 'Cmd.exe exited with code 1' failed")
                        },
                        RecordType.Task
                    )
                });


            await using TestData testData = builder.Build();
            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            result.BuildStepsResult.Should().HaveCount(1);
            result.BuildStepsResult.First().KnownIssues.Should().HaveCount(1);
        }

        [Test]
        public async Task KnownIssueWithRegexMatch()
        {
            string issueBody = "```\r\n{\r\n    \"ErrorPattern\" : \"The command .+ failed\"\r\n}\r\n```";
            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Failed).Build())
                .WithKnownIssues(ImmutableList.Create(KnownIssueHelper.ParseGithubIssue(MockIssue(issueBody), "testRepo", KnownIssueType.Infrastructure)))
                .WithTimeline(new List<TimelineRecord>()
                {
                    MockRecord(
                        name: "A",
                        attempt: 1,
                        result: TaskResult.Failed,
                        issues: new List<TimelineIssue>()
                        {
                            new TimelineIssue("The command 'open explorer' failed")
                        },
                        RecordType.Task
                    )
                });

            await using TestData testData = builder.Build();
            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            result.BuildStepsResult.Should().HaveCount(1);
            result.BuildStepsResult.First().KnownIssues.Should().HaveCount(1);
        }

        [Test]
        public async Task BuildMessageWithEmptyBody()
        {
            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Failed).Build())
                .WithKnownIssues(ImmutableList.Create(KnownIssueHelper.ParseGithubIssue(MockIssue(""), "testRepo", KnownIssueType.Infrastructure)))
                .WithTimeline(new List<TimelineRecord>()
                {
                    MockRecord(
                        name: "A",
                        attempt: 1,
                        result: TaskResult.Failed,
                        issues: new List<TimelineIssue>()
                        {
                            new TimelineIssue("The command 'open explorer' failed")
                        },
                        RecordType.Task
                    )
                });

            await using TestData testData = builder.Build();
            BuildResultAnalysis result = await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None);

            result.BuildStepsResult.Should().HaveCount(1);
            result.BuildStepsResult.First().KnownIssues.Should().HaveCount(0);
        }


        [Test]
        public async Task BuildAnalysisForValidationTest()
        {
            var timelineRecords = new List<TimelineRecord>
            {
                MockRecord("A", 3, TaskResult.Failed, new List<TimelineAttempt>()),
                MockRecord("B", 5, TaskResult.Failed, new List<TimelineAttempt>())
            };

            TestData.Builder builder = TestData.Default
                .AddBuild(BuildBuilder.Default.WithResult(BuildResult.Failed).Build())
                .WithTimeline(timelineRecords);
            await using TestData testData = builder.Build();
 
            await testData.BuildAnalysis.GetBuildResultAnalysisAsync(_buildReference, CancellationToken.None, true);
            testData.BuildDataService.Verify(b => b.GetTimelineRecordsFromAllAttempts(It.IsAny<IReadOnlyCollection<TimelineRecord>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
            testData.BuildDataService.Verify(b => b.GetAllFailingTestsForBuildAsync(It.IsAny<Build>(), It.IsAny<CancellationToken>()), Times.Once);
            testData.BuildDataService.Verify(b => b.GetFailingTestsForBuildAsync(It.IsAny<Build>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private static TimelineRecord MockRecord(string name, int attempt, TaskResult result, IEnumerable<TimelineIssue> issues, RecordType recordType, string logUrl = null, string parent = null)
        {
            var timelineRecord = new TimelineRecord(
                id: CreateGuid(name),
                name: name,
                attempt: attempt,
                result: result,
                recordType: recordType,
                issues: ImmutableList.CreateRange(issues),
                logUrl: logUrl,
                parentId: parent != null ? CreateGuid(parent) : (Guid?)null

            );
            return timelineRecord;
        }

        private static TimelineRecord MockRecord(string name, int attempt, TaskResult result, IEnumerable<TimelineAttempt> attempts)
        {
            var timelineRecord = new TimelineRecord(
                id: CreateGuid(name),
                name: name,
                attempt: attempt,
                result: result,
                previousAttempts: ImmutableList.CreateRange(attempts)
            );
            return timelineRecord;
        }

        private static TimelineRecord MockRecord(string name, TaskResult result, RecordType recordType, string identifier = "__default", string parent = null)
        {
            var timelineRecord = new TimelineRecord(
                id: CreateGuid(name),
                name: name,
                parentId: parent != null ? CreateGuid(parent) : (Guid?)null,
                identifier: identifier,
                recordType: recordType,
                result: result
            );
            return timelineRecord;
        }

        private static Guid CreateGuid(string name)
        {
            return Guid.Parse($"00000000-0000-0000-0000-{name.PadLeft(12, '0')}");
        }

        private static TimelineAttempt MockAttempt(string name, int attempt, string timeline)
        {
            return new TimelineAttempt(attempt, CreateGuid(timeline));
        }

        private static TestCaseResult MockTestCaseResult(string automatedTestName, DateTimeOffset completedDate, TestOutcomeValue value, int buildId = 0, int id = 0, string comment = null)
        {
            return new TestCaseResult(automatedTestName, completedDate, value, 0, id, buildId, new PreviousBuildRef(), "", "", "", comment, 55000);
        }

        private static TestCaseResult MockTestCaseResult(
            string automatedTestName,
            TestOutcomeValue value,
            int buildId = 0,
            int id = 0,
            int attempt = default)
        {
            return new TestCaseResult(
                automatedTestName,
                MockDateTimeOffset(),
                value,
                0,
                id,
                buildId,
                new PreviousBuildRef(),
                "",
                "",
                "",
                null,
                55000,
                attempt: attempt
            );
        }
        
        private static TestRunDetails MockTestCaseResultsByTestRun(int id, string name, PipelineReference pipelineReference)
        {
            return new TestRunDetails(new TestRunSummary(123, "TestRunNameTest", pipelineReference),
                new List<TestCaseResult>
                {
                    new TestCaseResult(name,
                        new DateTimeOffset(2021, 5, 12, 0, 0, 0, TimeSpan.Zero), TestOutcomeValue.Failed, 1, id, 1,
                        new PreviousBuildRef(), "", "", "", null, 55000)
                },
                MockDateTimeOffset());
        }

        private static PipelineReference MockPipelineReference(string stageName, string phaseName, string jobName)
        {
            return new PipelineReference(0, new StageReference(stageName, 0), new PhaseReference(phaseName, 0),
                new JobReference(jobName, 0));
        }

        private Octokit.Issue MockIssue(string body)
        {
            return new Octokit.Issue(default, default, default, default, default, Octokit.ItemState.Open, default, body, default,
                default, default, default, default, default, 1, default, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue,
                DateTimeOffset.MaxValue, 1, default, default, default, default, default, default);
        }
    }
}
