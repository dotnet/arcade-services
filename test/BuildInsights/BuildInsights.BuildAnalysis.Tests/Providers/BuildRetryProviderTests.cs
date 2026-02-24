// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests.Providers;

[TestFixture]
public class BuildRetryProviderTests
{
    public sealed class TestData : IDisposable, IAsyncDisposable
    {
        private readonly ServiceProvider _services;

        private TestData(ServiceProvider services, BuildRetryProvider buildRetry)
        {
            _services = services;
            BuildRetry = buildRetry;
        }

        public BuildRetryProvider BuildRetry { get; }

        public static Builder Default { get; } = new Builder();

        public ValueTask DisposeAsync()
        {
            return _services.DisposeAsync();
        }

        public void Dispose()
        {
            _services.Dispose();
        }

        public class Builder
        {
            private readonly Build _build = new(result: BuildResult.Failed);
            private readonly BuildConfiguration _buildConfiguration;
            private readonly ImmutableList<TimelineRecord> _timeline = [new TimelineRecord()];

            public Builder()
            {
            }

            private Builder(
                ImmutableList<TimelineRecord> timeline,
                BuildConfiguration buildConfiguration,
                Build build)
            {
                _timeline = timeline;
                _buildConfiguration = buildConfiguration;
                _build = build;
            }

            private Builder With(
                ImmutableList<TimelineRecord> timeline = null,
                BuildConfiguration buildConfiguration = null,
                Build build = null)
            {
                return new Builder(
                    timeline ?? _timeline,
                    buildConfiguration ?? _buildConfiguration,
                    build ?? _build
                );
            }

            public Builder WithBuildConfiguration(BuildConfiguration buildConfiguration) => With(buildConfiguration: buildConfiguration);
            public Builder WithBuild(Build build) => With(build: build);
            public Builder WithTimeline(List<TimelineRecord> timeline) => With(timeline: timeline.ToImmutableList());

            public TestData Build()
            {
                IOptions<BuildConfigurationFileSettings> buildConfigurationFileSettingsMock = Options.Create(
                    new BuildConfigurationFileSettings
                    {
                        FileName = ""
                    });

                var buildOperationsServiceMock = new Mock<IBuildOperationsService>();
                buildOperationsServiceMock.Setup(o =>
                        o.RetryBuild(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
                var buildDataServiceMock = new Mock<IBuildDataService>();
                buildDataServiceMock.Setup(b => b.GetBuildConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(_buildConfiguration);
                buildDataServiceMock.Setup(b =>
                        b.GetBuildAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(_build);
                buildDataServiceMock.Setup(b =>
                        b.GetLatestBuildTimelineRecordsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                            It.IsAny<CancellationToken>()))
                    .ReturnsAsync(_timeline);

                ServiceProvider services = new ServiceCollection()
                    .AddLogging(l => { l.AddProvider(new NUnitLogger()); })
                    .AddSingleton(buildDataServiceMock.Object)
                    .AddSingleton(buildOperationsServiceMock.Object)
                    .AddSingleton(buildConfigurationFileSettingsMock)
                    .AddSingleton<BuildRetryProvider>()
                    .BuildServiceProvider();

                return new TestData(services, services.GetRequiredService<BuildRetryProvider>());
            }
        }
    }

    [TestCase(BuildResult.Failed, true)]
    [TestCase(BuildResult.Succeeded, false)]
    public async Task BuildSuitableNotSuitableToRetryByStatus(BuildResult buildResult, bool expectedOutput)
    {
        var buildConfiguration = new BuildConfiguration { RetryByAnyError = true };
        Build build = new Build(result: buildResult);

        await using TestData testData = TestData.Default
            .WithBuildConfiguration(buildConfiguration)
            .WithBuild(build)
            .Build();

        bool result = await testData.BuildRetry.RetryIfSuitable("", "", 0, CancellationToken.None);
        result.Should().Be(expectedOutput);
    }

    [TestCase(4, true)]
    [TestCase(2, false)]
    public async Task BuildSuitableNotSuitableToRetryByAttempts(int retryCountLimit, bool expectedOutput)
    {
        var buildConfiguration = new BuildConfiguration { RetryCountLimit = retryCountLimit, RetryByAnyError = true };

        var records = new List<TimelineRecord>
        {
            new(result: TaskResult.Failed, attempt: 3)
        };

        await using TestData testData = TestData.Default
            .WithBuildConfiguration(buildConfiguration)
            .WithTimeline(records)
            .Build();

        bool result = await testData.BuildRetry.RetryIfSuitable("", "", 0, CancellationToken.None);
        result.Should().Be(expectedOutput);
    }

    [Test]
    public async Task BuildSuitableNotSuitableToRetryEmptyRecords()
    {
        await using TestData testData = TestData.Default
            .WithTimeline(new List<TimelineRecord>())
            .WithBuildConfiguration(new BuildConfiguration())
            .Build();

        bool result = await testData.BuildRetry.RetryIfSuitable("", "", 0, CancellationToken.None);
        result.Should().BeFalse();
    }

    [Test]
    public async Task BuildSuitableNotSuitableToRetryNullConfiguration()
    {
        await using TestData testData = TestData.Default
            .WithBuildConfiguration(null)
            .Build();

        bool result = await testData.BuildRetry.RetryIfSuitable("", "", 0, CancellationToken.None);
        result.Should().BeFalse();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task BuildSuitableToRetryByAny(bool retryByAnyError)
    {
        var buildConfiguration = new BuildConfiguration { RetryByAnyError = retryByAnyError };
        await using TestData testData = TestData.Default
            .WithBuildConfiguration(buildConfiguration)
            .Build();

        bool result = await testData.BuildRetry.RetryIfSuitable("", "", 0, CancellationToken.None);
        result.Should().Be(retryByAnyError);
    }

    [TestCase(".*Vstest failed with error.*", true)]
    [TestCase("Test Run Failed", false)]
    public async Task BuildSuitableToRetryByError(string errorRegex, bool expectedResult)
    {
        ImmutableList<TimelineIssue> timelineIssues = [new TimelineIssue("Vstest failed with error. Check logs for failures. There might be failed tests.")];
        var buildConfiguration = new BuildConfiguration
        {
            RetryByErrors =
            [
                new Errors {ErrorRegex = errorRegex}
            ]
        };
        var records = new List<TimelineRecord>
        {
            new(result: TaskResult.Failed, issues: timelineIssues)
        };

        await using TestData testData = TestData.Default
            .WithBuildConfiguration(buildConfiguration)
            .WithTimeline(records)
            .Build();

        bool result = await testData.BuildRetry.RetryIfSuitable("", "", 0, CancellationToken.None);
        result.Should().Be(expectedResult);
    }

    [TestCase("Job_Name_Restarting", true)]
    [TestCase("JobNameRestarting", true)]
    [TestCase("Job_Name_Not_Matching", false)]
    public async Task BuildSuitableToRetryMatchPipelineJob(string jobName, bool expectedResult)
    {
        var buildConfiguration = new BuildConfiguration
        {
            RetryByPipeline = new Pipeline
            {
                RetryJobs =
                [
                    new Job
                    {
                        JobName = jobName
                    }
                ]
            }
        };

        var records = new List<TimelineRecord>
        {
            new(id: CreateGuid("A"), result: TaskResult.Failed, name:"ExtraJobName", identifier: "Extra_Job_Identifier", recordType:RecordType.Job),
            new(id: CreateGuid("B"), result: TaskResult.Failed, name:"JobNameRestarting", identifier: "Job_Name_Restarting", recordType:RecordType.Job)
        };

        await using TestData testData = TestData.Default
            .WithBuildConfiguration(buildConfiguration)
            .WithTimeline(records)
            .Build();

        bool result = await testData.BuildRetry.RetryIfSuitable("", "", 0, CancellationToken.None);
        result.Should().Be(expectedResult);
    }

    [TestCase("Phase_Name_Restarting", true)]
    [TestCase("PhaseNameRestarting", true)]
    [TestCase("Phase_Name_Not_Matching", false)]
    public async Task BuildSuitableToRetryMatchPipelinePhase(string phaseName, bool expectedResult)
    {
        var buildConfiguration = new BuildConfiguration
        {
            RetryByPipeline = new Pipeline
            {
                RetryPhases =
                [
                    new Phase()
                    {
                        PhaseName = phaseName
                    }
                ]
            }
        };

        var records = new List<TimelineRecord>
        {
            new(id: CreateGuid("A"), result: TaskResult.Failed, name:"ExtraPhaseName", identifier: "Extra_Phase_Identifier", recordType:RecordType.Job),
            new(id: CreateGuid("B"), result: TaskResult.Failed, name:"PhaseNameRestarting", identifier: "Phase_Name_Restarting", recordType:RecordType.Phase)
        };

        await using TestData testData = TestData.Default
            .WithBuildConfiguration(buildConfiguration)
            .WithTimeline(records)
            .Build();

        bool result = await testData.BuildRetry.RetryIfSuitable("", "", 0, CancellationToken.None);
        result.Should().Be(expectedResult);
    }

    [TestCase("Stage_Name_Restarting", true)]
    [TestCase("StageNameRestarting", true)]
    [TestCase("Stage_Name_Not_Matching", false)]
    public async Task BuildSuitableToRetryMatchPipelineStage(string stageName, bool expectedResult)
    {
        var buildConfiguration = new BuildConfiguration
        {
            RetryByPipeline = new Pipeline
            {
                RetryStages =
                [
                    new Stage()
                    {
                        StageName = stageName
                    }
                ]
            }
        };

        var records = new List<TimelineRecord>
        {
            new(id: CreateGuid("A"), result: TaskResult.Failed, name:"ExtraStageName", identifier: "Extra_Stage_Identifier", recordType:RecordType.Job),
            new(id: CreateGuid("B"), result: TaskResult.Failed, name:"StageNameRestarting", identifier: "Stage_Name_Restarting", recordType:RecordType.Stage)
        };

        await using TestData testData = TestData.Default
            .WithBuildConfiguration(buildConfiguration)
            .WithTimeline(records)
            .Build();

        bool result = await testData.BuildRetry.RetryIfSuitable("", "", 0, CancellationToken.None);
        result.Should().Be(expectedResult);
    }

    [TestCase("JobNameTestRetry", true)]
    [TestCase("JobNameNotMacthing", false)]
    public async Task RetryByErrorsInPipelineByJob(string jobName, bool expectedResult)
    {
        var buildConfiguration = new BuildConfiguration
        {
            RetryByErrorsInPipeline = new RetryByErrorsInPipeline
            {
                ErrorInPipelineByJobs =
                [
                    new ErrorInPipelineByJobs
                    {
                        JobsNames = [jobName],
                        ErrorRegex = ".*Vstest failed with error.*"
                    }
                ]
            }
        };

        ImmutableList<TimelineIssue> timelineIssues = [new TimelineIssue("Vstest failed with error. Check logs for failures. There might be failed tests.")];

        var records = new List<TimelineRecord>
        {
            new(id: CreateGuid("A"), result: TaskResult.Failed, recordType: RecordType.Job, name: "JobNameTestAny"),
            new(id: CreateGuid("B"), result: TaskResult.Failed, issues: timelineIssues, recordType: RecordType.Job, name: "JobNameTestRetry")
        };

        await using TestData testData = TestData.Default
           .WithBuildConfiguration(buildConfiguration)
           .WithTimeline(records)
           .Build();

        bool result = await testData.BuildRetry.RetryIfSuitable("", "", 0, CancellationToken.None);
        result.Should().Be(expectedResult);
    }


    [TestCase("StageNameTestRetry", true)]
    [TestCase("StageNameNotMatching", false)]
    public async Task RetryByErrorsInPipelineByStage(string stageName, bool expectedResult)
    {
        var buildConfiguration = new BuildConfiguration
        {
            RetryByErrorsInPipeline = new RetryByErrorsInPipeline
            {
                ErrorInPipelineByStage =
                [
                    new ErrorInPipelineByStage()
                    {
                        StageName =  stageName ,
                        ErrorRegex = ".*Vstest failed with error.*"
                     }
                ]
            }
        };

        ImmutableList<TimelineIssue> timelineIssues = [new TimelineIssue("Vstest failed with error. Check logs for failures. There might be failed tests.")];

        var records = new List<TimelineRecord>
        {
            new(id: CreateGuid("A"), result: TaskResult.Failed, recordType: RecordType.Stage, name: "StageNameTestRetry"),
            new(id: CreateGuid("B"), parentId: CreateGuid("A"), result: TaskResult.Failed, issues: timelineIssues, recordType: RecordType.Task, name: "TaskNameAny")
        };

        await using TestData testData = TestData.Default
           .WithBuildConfiguration(buildConfiguration)
           .WithTimeline(records)
           .Build();

        bool result = await testData.BuildRetry.RetryIfSuitable("", "", 0, CancellationToken.None);
        result.Should().Be(expectedResult);
    }

    [TestCase("StageNameTestRetry", "JobNameTestRetry", true)]
    [TestCase("StageNameTestRetry", "JobNameTestRetryNotMatching", false)]
    [TestCase("StageNameNotMatching", "JobNameTestAny", false)]
    public async Task RetryByErrorsInPipelineByJobsInStage(string stageName, string jobName, bool expectedResult)
    {
        var buildConfiguration = new BuildConfiguration
        {
            RetryByErrorsInPipeline = new RetryByErrorsInPipeline
            {
                ErrorInPipelineByJobsInStage =
                [
                    new ErrorInPipelineByJobsInStage
                    {
                        StageName = stageName ,
                        JobsNames = [jobName],
                        ErrorRegex = ".*Vstest failed with error.*"
                    }
                ]

            }
        };

        ImmutableList<TimelineIssue> timelineIssues = [new TimelineIssue("Vstest failed with error. Check logs for failures. There might be failed tests.")];

        //STARTING FOR THE HIERARCHY THING
        var records = new List<TimelineRecord>
        {
            new(id: CreateGuid("A"), result: TaskResult.Failed, recordType: RecordType.Stage, name: "StageNameTestAny"),
            new(id: CreateGuid("B"), result: TaskResult.Failed, recordType: RecordType.Stage, name: "StageNameTestRetry"),
            new(id: CreateGuid("C"), parentId: CreateGuid("B"), result: TaskResult.Failed, issues: timelineIssues, recordType: RecordType.Job, name: "JobNameTestRetry")
        };

        await using TestData testData = TestData.Default
           .WithBuildConfiguration(buildConfiguration)
           .WithTimeline(records)
           .Build();

        bool result = await testData.BuildRetry.RetryIfSuitable("", "", 0, CancellationToken.None);
        result.Should().Be(expectedResult);
    }


    [Test]
    public async Task BuildSuitableToRetryJobsInStage()
    {
        var buildConfiguration = new BuildConfiguration
        {
            RetryByPipeline = new Pipeline
            {
                RetryJobsInStage =
                [
                    new JobsInStage()
                    {
                        StageName = "StageNameRetryTest",
                        JobsNames = ["JobNameRetryTest"]
                    }
                ]
            }
        };

        var records = new List<TimelineRecord>
        {
            new(id: CreateGuid("A"), result: TaskResult.Failed, name:"StageNameRetryTest", identifier: "Stage_Name_Restarting", recordType:RecordType.Stage),
            new(id: CreateGuid("B"), parentId: CreateGuid("A"), result: TaskResult.Failed, name:"JobNameRetryTest", identifier: "Job_Name_Retry_Test", recordType:RecordType.Job),
        };

        await using TestData testData = TestData.Default
            .WithBuildConfiguration(buildConfiguration)
            .WithTimeline(records)
            .Build();

        bool result = await testData.BuildRetry.RetryIfSuitable("", "", 0, CancellationToken.None);
        result.Should().BeTrue();
    }

    [TestCase(true, 1, true)]
    [TestCase(true, 2, false)]
    public async Task BuildSuitableToRetryForKnownIssue(bool knownIssueBuildRetryRequest, int currentBuildAttempts, bool expectedRetry)
    {
        var records = new List<TimelineRecord>
        {
            new(result: TaskResult.Failed, attempt: currentBuildAttempts)
        };

        await using TestData testData = TestData.Default
            .WithTimeline(records)
            .Build();

        bool result = await testData.BuildRetry.RetryIfKnownIssueSuitable("", "", 0, CancellationToken.None);
        result.Should().Be(expectedRetry);
    }

    private static Guid CreateGuid(string name)
    {
        return Guid.Parse($"00000000-0000-0000-0000-{name.PadLeft(12, '0')}");
    }
}
