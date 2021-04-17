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
        private class TestData : IDisposable
        {
            public readonly AzureDevOpsTimeline Controller;
            private readonly ServiceProvider _services;

            public TestData(AzureDevOpsTimeline controller, ServiceProvider services)
            {
                Controller = controller;
                _services = services;
            }

            public void Dispose()
            {
                _services?.Dispose();
            }
        }

        private static TestData SetUp(
            ITimelineTelemetryRepository timelineTelemetryRepository, 
            IAzureDevOpsClient azureDevOpsClient,
            ISystemClock systemClock)
        {
            ServiceCollection collection = new ServiceCollection();
            collection.AddOptions();
            collection.AddLogging(l =>
            {
                l.AddProvider(new NUnitLogger());
            });

            collection.AddSingleton(systemClock);
            collection.AddSingleton(timelineTelemetryRepository);
            collection.AddSingleton(azureDevOpsClient);
            collection.AddScoped<AzureDevOpsTimeline>();

            ServiceProvider services = collection.BuildServiceProvider();
            return new TestData(services.GetRequiredService<AzureDevOpsTimeline>(), services);
        }

        /// <summary>
        /// Test a happy path. Database is empty, so all returned builds will be added.
        /// </summary>
        [Test]
        public async Task VanillaTest()
        {
            // Test input
            Dictionary<Build, List<Timeline>> builds = new Dictionary<Build, List<Timeline>>()
            {
                {
                    new Build()
                    {
                        Id = 1,
                        Project = new TeamProjectReference() { Name = "public" },
                        ValidationResults = Array.Empty<BuildRequestValidationResult>(),
                        Reason = "pullRequest",
                        Parameters = "{\"system.pullRequest.targetBranch\": \"theTargetBranch\"}"
                    },
                    new List<Timeline>() {
                        new Timeline()
                        {
                            Id = "1",
                            LastChangedOn = DateTimeOffset.Now,
                            Records = new[]
                            {
                                new TimelineRecord()
                                {
                                    Id = "1",
                                    Issues = Array.Empty<TimelineIssue>()
                                },
                                new TimelineRecord()
                                {
                                    Id = "2",
                                    Issues = Array.Empty<TimelineIssue>()
                                },
                            }
                        }
                    }
                }
            };

            // Test setup
            MockAzureClient mockAzureClient = new MockAzureClient(builds);
            InMemoryTimelineTelemetryRepository telemetryRepository = new InMemoryTimelineTelemetryRepository(new List<(string project, DateTimeOffset latestTime)>());

            using TestData testData = SetUp(telemetryRepository, mockAzureClient, new SystemClock());

            /// Test execution
            await testData.Controller.RunProject("public", 1000, CancellationToken.None);

            // Test results
            telemetryRepository.TimelineBuilds.Should().SatisfyRespectively(
                first =>
                {
                    first.Build.Should().BeSameAs(builds.Single().Key);
                    first.TargetBranch.Should().Be("theTargetBranch");
                });


            telemetryRepository.TimelineIssues.Should().BeEmpty();

            telemetryRepository.TimelineRecords.Should().HaveCount(2);
            telemetryRepository.TimelineRecords
                .Select(r => r.Raw)
                .Should().Contain(builds.Values
                    .SelectMany(list => list
                        .SelectMany(timeline => timeline.Records)));

            telemetryRepository.TimelineValidationMessages.Should().BeEmpty();
        }

        /// <summary>
        /// Build has multiple timelines. 
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task GetAdditionalTimelines()
        {
            // Test input
            Dictionary<Build, List<Timeline>> builds = new Dictionary<Build, List<Timeline>>()
            {
                {
                    new Build()
                    {
                        Id = 1,
                        Project = new TeamProjectReference() { Name = "public" },
                        ValidationResults = Array.Empty<BuildRequestValidationResult>(),
                        Reason = "pullRequest",
                        Parameters = "{\"system.pullRequest.targetBranch\": \"theTargetBranch\"}"
                    },
                    new List<Timeline>() {
                        new Timeline()
                        {
                            Id = "1",
                            LastChangedOn = DateTimeOffset.Now,
                            Records = new[]
                            {
                                new TimelineRecord()
                                {
                                    Id = "1",
                                    Issues = Array.Empty<TimelineIssue>()
                                },
                                new TimelineRecord()
                                {
                                    Id = "2",
                                    Issues = Array.Empty<TimelineIssue>(),
                                    PreviousAttempts = new[] {
                                        new TimelineAttempt() { TimelineId = "2" }
                                    }
                                },
                            }
                        },
                        new Timeline()
                        {
                            Id = "2",
                            LastChangedOn = DateTimeOffset.Now.AddHours(-1),
                            Records = new[]
                            {
                                new TimelineRecord()
                                {
                                    Id = "3",
                                    Issues = Array.Empty<TimelineIssue>()
                                },
                                new TimelineRecord()
                                {
                                    Id = "4",
                                    Issues = Array.Empty<TimelineIssue>()
                                },
                            }
                        }
                    }
                }
            };

            // Test setup
            MockAzureClient mockAzureClient = new MockAzureClient(builds);
            InMemoryTimelineTelemetryRepository telemetryRepository = new InMemoryTimelineTelemetryRepository(new List<(string project, DateTimeOffset latestTime)>());

            using TestData testData = SetUp(telemetryRepository, mockAzureClient, new SystemClock());

            /// Test execution
            await testData.Controller.RunProject("public", 1000, CancellationToken.None);

            // Test results
            telemetryRepository.TimelineRecords
                .Select(r => r.Raw)
                .Should()
                .BeEquivalentTo(builds.Values
                    .SelectMany(list => list
                        .SelectMany(timeline => timeline.Records)));

            telemetryRepository.TimelineValidationMessages.Should().BeEmpty();
        }

        /// <summary>
        /// Build has multiple timelines, but one timeline is older than the cutoff and should be ignored. 
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task AdditionalTimelines_IgnoreOldest()
        {
            // Test input
            Dictionary<Build, List<Timeline>> builds = new Dictionary<Build, List<Timeline>>()
            {
                {
                    new Build()
                    {
                        Id = 1,
                        Project = new TeamProjectReference() { Name = "public" },
                        ValidationResults = Array.Empty<BuildRequestValidationResult>(),
                        Reason = "pullRequest",
                        Parameters = "{\"system.pullRequest.targetBranch\": \"theTargetBranch\"}"
                    },
                    new List<Timeline>() {
                        new Timeline()
                        {
                            Id = "1",
                            LastChangedOn = DateTimeOffset.Parse("2020-01-01T12:00:00Z"),
                            Records = new[]
                            {
                                new TimelineRecord()
                                {
                                    Id = "1",
                                    Issues = Array.Empty<TimelineIssue>()
                                },
                                new TimelineRecord()
                                {
                                    Id = "2",
                                    Issues = Array.Empty<TimelineIssue>(),
                                    PreviousAttempts = new[] {
                                        new TimelineAttempt() { TimelineId = "2" }
                                    }
                                },
                            }
                        },
                        new Timeline()
                        {
                            Id = "2",
                            LastChangedOn = DateTimeOffset.Parse("2020-01-01T12:00:00Z").AddHours(-1),
                            Records = new[]
                            {
                                new TimelineRecord()
                                {
                                    Id = "3",
                                    Issues = Array.Empty<TimelineIssue>()
                                },
                                new TimelineRecord()
                                {
                                    Id = "4",
                                    Issues = Array.Empty<TimelineIssue>()
                                },
                            }
                        }
                    }
                }
            };

            // Test setup
            MockAzureClient mockAzureClient = new MockAzureClient(builds);

            InMemoryTimelineTelemetryRepository telemetryRepository = new InMemoryTimelineTelemetryRepository(
                new List<(string project, DateTimeOffset latestTime)>()
                {
                    ("public", DateTimeOffset.Parse("2020-01-01T12:00:00Z").AddHours(-1)) 
                });
            
            Mock<ISystemClock> mockSystemClock = new Mock<ISystemClock>();
            mockSystemClock.Setup(x => x.UtcNow).Returns(DateTimeOffset.Parse("2020-01-01T12:00:00Z"));

            using TestData testData = SetUp(telemetryRepository, mockAzureClient, mockSystemClock.Object);

            /// Test execution
            await testData.Controller.RunProject("public", 1000, CancellationToken.None);

            // Test results
            IEnumerable<TimelineRecord> expectedRecords = builds.Values
                .SelectMany(timelines => timelines
                    .Where(timeline => timeline.LastChangedOn > DateTimeOffset.Parse("2020-01-01T12:00:00Z").AddHours(-1))
                    .SelectMany(b => b.Records));

            telemetryRepository.TimelineRecords
                .Select(r => r.Raw)
                .Should().BeEquivalentTo(expectedRecords);
        }
    }
}
