using FluentAssertions;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    public class AzureDevOpsTimelineTests
    {
        private sealed class TestData : IDisposable
        {
            public readonly AzureDevOpsTimeline Controller;
            private readonly ServiceProvider _services;
            public InMemoryTimelineTelemetryRepository Repository { get; }

            public TestData(AzureDevOpsTimeline controller, InMemoryTimelineTelemetryRepository repository, ServiceProvider services)
            {
                Controller = controller;
                Repository = repository;
                _services = services;
            }

            public void Dispose()
            {
                _services?.Dispose();
            }
        }

        private class TestDataBuilder
        {
            private readonly ServiceCollection collection = new ServiceCollection();
            private InMemoryTimelineTelemetryRepository _repository = new InMemoryTimelineTelemetryRepository();

            /// <summary>
            /// Create a TestData builder with default configuration
            /// </summary>
            public TestDataBuilder()
            {
                collection.AddOptions();
                collection.AddLogging(logging =>
                {
                    logging.AddProvider(new NUnitLogger());
                });
                collection.AddScoped<AzureDevOpsTimeline>();
            }

            public TestData Build()
            {
                collection.AddScoped<AzureDevOpsTimeline>();
                collection.AddSingleton<ITimelineTelemetryRepository>(_repository);

                ServiceProvider services = collection.BuildServiceProvider();
                return new TestData(services.GetRequiredService<AzureDevOpsTimeline>(), _repository, services);
            }

            public TestDataBuilder AddBuildsWithTimelines(IEnumerable<BuildAndTimeline> builds)
            {
                var b = builds.ToDictionary(key => key.Build, key => key.Timelines.ToList());

                collection.AddSingleton<IAzureDevOpsClient>(client => new MockAzureClient(b));

                return this;
            }

            public TestDataBuilder AddTelemetryRepository()
            {
                return this;
            }

            public TestDataBuilder AddTelemetryRepository(string project, DateTimeOffset latestTime)
            {
                _repository = new InMemoryTimelineTelemetryRepository(
                    new List<(string project, DateTimeOffset latestTime)> { (project, latestTime) });

                return this;
            }

            public TestDataBuilder AddStaticClock(DateTimeOffset dateTime)
            {
                Mock<ISystemClock> mockSystemClock = new Mock<ISystemClock>();
                mockSystemClock.Setup(x => x.UtcNow).Returns(dateTime);

                collection.AddSingleton(mockSystemClock.Object);

                return this;
            }
        }

        public class BuildAndTimeline
        {
            public Build Build { get; }
            public IList<Timeline> Timelines { get; } = new List<Timeline>();

            public BuildAndTimeline(Build build)
            {
                Build = build;
            }

            public BuildAndTimeline(Build build, IList<Timeline> timelines)
            {
                Build = build;
                Timelines = timelines;
            }
        }
            
        public class BuildAndTimelineBuilder
        {
            private BuildAndTimeline _build;            

            public TimelineBuilder AddTimeline(string id, DateTimeOffset lastChangedOn)
            {
                return new TimelineBuilder(id, lastChangedOn);
            }

            public BuildAndTimelineBuilder AddTimeline(TimelineBuilder timelineBuilder)
            {
                _build.Timelines.Add(timelineBuilder.Build());

                return this;
            }

            public BuildAndTimelineBuilder(Build build)
            {
                _build = new BuildAndTimeline(build);
            }

            public static BuildAndTimelineBuilder NewPullRequestBuild(int id, string projectName, string branchName)
            {
                Build build = new Build()
                {
                    Id = id,
                    Project = new TeamProjectReference() { Name = projectName },
                    ValidationResults = Array.Empty<BuildRequestValidationResult>(),
                    Reason = "pullRequest",
                    Parameters = $"{{\"system.pullRequest.targetBranch\": \"{branchName}\"}}"
                };

                return new BuildAndTimelineBuilder(build);
            }

            public BuildAndTimeline Build()
            {
                return _build;
            }
        }

        public class TimelineBuilder
        {
            private Timeline timeline;
            private List<TimelineRecord> records = new List<TimelineRecord>();

            public Timeline Build()
            {
                timeline.Records = records.ToArray();
                return timeline;
            }

            public TimelineBuilder(string id, DateTimeOffset lastChangedOn)
            {
                timeline = new Timeline()
                {
                    Id = id,
                    LastChangedOn = lastChangedOn,
                    Records = Array.Empty<TimelineRecord>()
                };
            }

            public static TimelineBuilder EmptyTimeline(string id, DateTimeOffset lastChangedOn)
            {
                return new TimelineBuilder(id, lastChangedOn);
            }

            public TimelineBuilder AddRecord()
            {
                return AddRecord(null);
            }

            public TimelineBuilder AddRecord(string previousAttemptTimelineId)
            {
                int nextId = 1;
                if (records.Any())
                {
                    nextId += records.Max(r => int.Parse(r.Id));
                }

                TimelineRecord record = new TimelineRecord()
                {
                    Id = nextId.ToString(),
                    Issues = Array.Empty<TimelineIssue>()
                };

                if (string.IsNullOrEmpty(previousAttemptTimelineId))
                {
                    record.PreviousAttempts = Array.Empty<TimelineAttempt>();
                }
                else
                {
                    record.PreviousAttempts = new[] {
                        new TimelineAttempt() { TimelineId = previousAttemptTimelineId }
                    };
                }

                records.Add(record);

                return this;
            }
        }

        /// <summary>
        /// Test a happy path. Database is empty, so all returned builds will be added.
        /// </summary>
        [Test]
        public async Task VanillaTest()
        {
            DateTimeOffset timeDatum = DateTimeOffset.Parse("2021-01-01T01:00:00Z");
            string azdoProjectName = "public";
            string targetBranchName = "theTargetBranch";

            BuildAndTimeline build = BuildAndTimelineBuilder.NewPullRequestBuild(1, azdoProjectName, targetBranchName)
                .AddTimeline(TimelineBuilder.EmptyTimeline("1", timeDatum)
                    .AddRecord()
                    .AddRecord()
                ).Build();

            // Test setup
            using TestData testData = new TestDataBuilder()
                .AddTelemetryRepository()
                .AddStaticClock(timeDatum)
                .AddBuildsWithTimelines(new List<BuildAndTimeline>() {
                    build
                }).Build();

            /// Test execution
            await testData.Controller.RunProject(azdoProjectName, 1000, CancellationToken.None);

            // Test results
            testData.Repository.TimelineBuilds.Should().SatisfyRespectively(
                first =>
                {
                    first.Build.Should().BeSameAs(build.Build);
                    first.TargetBranch.Should().Be(targetBranchName);
                });

            testData.Repository.TimelineIssues.Should().BeEmpty();

            testData.Repository.TimelineRecords.Should().HaveCount(2);
            testData.Repository.TimelineRecords
                .Select(r => r.Raw)
                .Should().Contain(build.Timelines
                    .SelectMany(timeline => timeline.Records));

            testData.Repository.TimelineValidationMessages.Should().BeEmpty();
        }

        /// <summary>
        /// Build has multiple timelines. 
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task GetAdditionalTimelines()
        {
            DateTimeOffset timeDatum = DateTimeOffset.Parse("2021-01-01T01:00:00Z");
            string azdoProjectName = "public";
            string targetBranchName = "theTargetBranch";

            // Test input
            BuildAndTimeline build = BuildAndTimelineBuilder.NewPullRequestBuild(1, azdoProjectName, targetBranchName)
                .AddTimeline(TimelineBuilder.EmptyTimeline("1", timeDatum)
                    .AddRecord()
                    .AddRecord("2")) // Represents a retry
                .AddTimeline(TimelineBuilder.EmptyTimeline("2", timeDatum.AddHours(-1))
                    .AddRecord()
                    .AddRecord())
                .Build();

            // Test setup
            using TestData testData = new TestDataBuilder()
                .AddTelemetryRepository()
                .AddStaticClock(timeDatum)
                .AddBuildsWithTimelines(new List<BuildAndTimeline>() {
                    build
                }).Build();

            /// Test execution
            await testData.Controller.RunProject("public", 1000, CancellationToken.None);

            // Test results
            testData.Repository.TimelineRecords
                .Select(r => r.Raw)
                .Should()
                .BeEquivalentTo(build.Timelines
                    .SelectMany(timeline => timeline.Records));

            testData.Repository.TimelineValidationMessages.Should().BeEmpty();
        }

        /// <summary>
        /// Build has multiple timelines, but one timeline is older than the cutoff and should be ignored. 
        /// </summary>
        [Test]
        public async Task AdditionalTimelines_IgnoreOldest()
        {
            // Test input
            DateTimeOffset timeDatum = DateTimeOffset.Parse("2021-01-01T01:00:00Z");
            string azdoProjectName = "public";
            string targetBranchName = "theTargetBranch";

            BuildAndTimeline build = BuildAndTimelineBuilder.NewPullRequestBuild(1, azdoProjectName, targetBranchName)
                .AddTimeline(TimelineBuilder.EmptyTimeline("1", timeDatum)
                    .AddRecord()
                    .AddRecord("2")) // Represents a retry
                .AddTimeline(TimelineBuilder.EmptyTimeline("2", timeDatum.AddHours(-1))
                    .AddRecord()
                    .AddRecord())
                .Build();

            // Test setup
            using TestData testData = new TestDataBuilder()
                .AddTelemetryRepository(azdoProjectName, timeDatum.AddHours(-1))
                .AddStaticClock(timeDatum)
                .AddBuildsWithTimelines(new List<BuildAndTimeline>() {
                    build
                }).Build();

            /// Test execution
            await testData.Controller.RunProject(azdoProjectName, 1000, CancellationToken.None);

            // Test results
            IEnumerable<TimelineRecord> expectedRecords = build.Timelines
                .Where(timeline => timeline.LastChangedOn > timeDatum.AddHours(-1))
                .SelectMany(b => b.Records);

            testData.Repository.TimelineRecords
                .Select(r => r.Raw)
                .Should().BeEquivalentTo(expectedRecords);
        }
    }
}
