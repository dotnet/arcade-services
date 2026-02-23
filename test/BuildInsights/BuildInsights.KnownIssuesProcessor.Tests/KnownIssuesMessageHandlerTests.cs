using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis;
using BuildInsights.GitHub.Models;
using BuildInsights.GitHub;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssuesProcessor;
using BuildInsights.AzureStorage.Cache;
using Moq;
using NUnit.Framework;

namespace BuildInsights.KnownIssuesProcessor.Tests
{
    public partial class KnownIssuesMessageHandlerTests
    {
        [TestDependencyInjectionSetup]
        public static class TestConfig
        {
            public static void Defaults(IServiceCollection collection)
            {
                Mock<IBuildProcessingStatusService> buildProcessingStatusServiceMock = new Mock<IBuildProcessingStatusService>();
                buildProcessingStatusServiceMock.Setup(m => m.GetBuildsWithOverrideConclusion(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<BuildProcessingStatusEvent>());

                collection.AddLogging(l => l.AddProvider(new NUnitLogger()));
                collection.AddSingleton<ISystemClock>(new TestClock());
                collection.AddSingleton(buildProcessingStatusServiceMock.Object);
            }

            public static Func<IServiceProvider, KnownIssuesMessageHandler> Processor(IServiceCollection collection)
            {
                collection.AddSingleton<KnownIssuesMessageHandler>();

                return s => s.GetRequiredService<KnownIssuesMessageHandler>();
            }

            public static Func<IServiceProvider, List<string>> BuildDataService(IServiceCollection collection, List<Build> builds)
            {
                var buildDataServiceMock = new Mock<IBuildDataService>();
                var repositories = new List<string>();

                buildDataServiceMock.Setup(m => m.GetFailedBuildsAsync("dnceng-public", It.IsAny<string>(), Capture.In(repositories), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(builds ?? new List<Build>());
                buildDataServiceMock.Setup(m => m.GetFailedBuildsAsync("dnceng", It.IsAny<string>(), Capture.In(repositories), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<Build>());
                collection.AddSingleton(buildDataServiceMock.Object);
                return _ => repositories;
            }

            public static Func<IServiceProvider, List<Build>> RequestAnalysisService(IServiceCollection collection)
            {
                var requestAnalysisServiceMock = new Mock<IRequestAnalysisService>();
                List<Build> builds = new List<Build>();
                requestAnalysisServiceMock.Setup(m => m.RequestAnalysisAsync(It.IsAny<IReadOnlyList<Build>>()))
                    .Callback<IReadOnlyList<Build>>((buildsRequested) => builds.AddRange(buildsRequested))
                    .Returns(Task.CompletedTask);
                collection.AddSingleton(requestAnalysisServiceMock.Object);
                return _ => builds;
            }

            public static void GitHubChecksService(IServiceCollection collection, string body, string repositoryWithOwner)
            {
                GitHubIssue issue = new GitHubIssue(id: 1234, body: body, repositoryWithOwner: repositoryWithOwner ?? "dotnet/test");
                var gitHubChecksServiceMock = new Mock<IGitHubChecksService>();
                gitHubChecksServiceMock.Setup(m => m.GetIssueAsync(It.IsAny<string>(),  It.IsAny<long>()))
                    .ReturnsAsync(issue);
               
                collection.AddSingleton(gitHubChecksServiceMock.Object);
            }

            public static Func<IServiceProvider, List<string>> GitHubIssueService(IServiceCollection collection)
            {
                List<string> issueBody = new List<string>();

                var gitHubIssueServiceMock = new Mock<IGitHubIssuesService>();
                gitHubIssueServiceMock.Setup(t =>
                    t.UpdateIssueBodyAsync(It.IsAny<string>(), It.IsAny<int>(), Capture.In(issueBody))).Returns(Task.CompletedTask)
                    .Verifiable();
                collection.AddSingleton(gitHubIssueServiceMock.Object);

                return _ => issueBody;
            }

            public static Func<IServiceProvider, List<KnownIssueAnalysis>> KnownIssuesHistoryService(IServiceCollection collection, List<KnownIssueAnalysis> knownIssueAnalysis)
            {
                var historyServiceMock = new Mock<IKnownIssuesHistoryService>();
                historyServiceMock.Setup(m => m.GetKnownIssuesHistory(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(),It.IsAny<CancellationToken>()))
                    .ReturnsAsync(knownIssueAnalysis ?? new List<KnownIssueAnalysis>());
                collection.AddSingleton(historyServiceMock.Object);
                return _ => knownIssueAnalysis;
            }

            public static Func<IServiceProvider, List<BuildAnalysisEvent>> BuildAnalysisHistoryService(IServiceCollection collection, List<BuildAnalysisEvent> buildAnalysisEvents)
            {
                var mockBuildAnalysisHistoryService = new Mock<IBuildAnalysisHistoryService>();
                mockBuildAnalysisHistoryService.Setup(m => m.GetBuildsWithRepositoryNotSupported(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(buildAnalysisEvents ?? new List<BuildAnalysisEvent>());

                collection.AddSingleton(mockBuildAnalysisHistoryService.Object);

                return _ => buildAnalysisEvents;
            }

            public static void ProcessorOptions(IServiceCollection collection, bool repoOnly)
            {
                collection.Configure<KnownIssuesProcessorOptions>(o =>
                {
                    o.RepositoryIssuesOnly = repoOnly;
                    o.KnownIssuesRepo = "dotnet/test";
                    o.BuildAnalysisQueueEndpoint = "Endpoint";
                    o.BuildAnalysisQueueName = "Test-Queue";
                    o.AzureDevOpsProjects = new List<AzureDevOpsProjects>()
                    {
                        new AzureDevOpsProjects
                        {
                            OrgId = "dnceng",
                            ProjectId = "public"
                        },
                        new AzureDevOpsProjects
                        {
                            OrgId = "dnceng-public",
                            ProjectId = "public"
                        },
                        new AzureDevOpsProjects
                        {
                            OrgId = "dnceng",
                            ProjectId = "internal",
                            IsInternal = true
                        }
                    };
                });
            }
        }

        public static readonly DateTimeOffset TestNow = DateTimeOffset.Parse("2020-01-02T15-04-04.07Z");
        public class MockWork : IQueuedWork
        {
            private readonly string _value;

            public MockWork(int issueId)
            {
                _value = JsonSerializer.Serialize(
                    new
                    {
                        issueId = issueId,
                        repository = "dotnet/test"
                    }
                );
            }

            public void Dispose()
            {
            }

            public string Id => "TEST-QUEUE-MESSAGE";
            public int DequeueCount => 1;
            public DateTimeOffset? NextVisibleTime => TestNow + TimeSpan.FromMinutes(1);
            public DateTimeOffset CreatedTime => TestNow - TimeSpan.FromMinutes(10);

            public Task<string> GetStringAsync()
            {
                return Task.FromResult(_value);
            }
        }

        [Test]
        public async Task NoProcessedBuilds()
        {
            string body = "```\r\n{\r\n    \"errorMessage\" : \"test error message\"\r\n}\r\n```";
            using var testData = TestData.Default.WithBody(body)
                .WithBuilds(new List<Build>{new (12334) })
                .Build();
            await testData.Processor.HandleMessageAsync(new MockWork(1234), false, CancellationToken.None);
            testData.RequestAnalysisService.Should().HaveCount(1);
        }

        [Test]
        public async Task NoFailedBuilds()
        {
            string body = "```\r\n{\r\n    \"errorMessage\" : \"test error message\"\r\n}\r\n```";
            using var testData = TestData.Default.WithBody(body).Build();
            await testData.Processor.HandleMessageAsync(new MockWork(1234), false, CancellationToken.None);
        }

        [Test]
        public async Task BuildWithRepositoryFilter()
        {
            string body = "```\r\n{\r\n    \"errorMessage\" : \"test error message\"\r\n}\r\n```";
            using var testData = TestData.Default.WithBody(body).WithRepositoryWithOwner("dotnet/just-for-this-test").Build();
            await testData.Processor.HandleMessageAsync(new MockWork(123456), false, CancellationToken.None);
            testData.BuildDataService.Any(t => t.Equals("dotnet-just-for-this-test")).Should().BeTrue();
            testData.BuildDataService.Any(t => t.Equals("dotnet/just-for-this-test")).Should().BeTrue();
        }

        [Test]
        public async Task ReprocessBuildsByErrorPattern()
        {
            string body = "```\r\n{\r\n    \"errorPattern\" : \"test error message.*\"\r\n}\r\n```";
            var knownIssueAnalyzes = new List<KnownIssueAnalysis> { new("test error message.*" ,12345, "1234")};
            var buildList = new List<Build> {new(12345)};

            using TestData testData = TestData.Default.WithBody(body)
                .WithBuilds(buildList)
                .WithKnownIssueAnalysis(knownIssueAnalyzes)
                .Build();

            await testData.Processor.HandleMessageAsync(new MockWork(1234), false, CancellationToken.None);
            testData.RequestAnalysisService.Should().HaveCount(0);
        }

        [Test]
        public async Task ReprocessBuildsByErrorMessage()
        {
            string body = "```\r\n{\r\n    \"errorMessage\" : \"test error message\"\r\n}\r\n```";
            var knownIssueAnalyzes = new List<KnownIssueAnalysis> {new( "test error message", 12345, "1234")};
            var buildList = new List<Build> {new(12345)};

            using TestData testData = TestData.Default.WithBody(body)
                .WithBuilds(buildList)
                .WithKnownIssueAnalysis(knownIssueAnalyzes)
                .Build();

            await testData.Processor.HandleMessageAsync(new MockWork(1234), false, CancellationToken.None);
            testData.RequestAnalysisService.Should().HaveCount(0);
        }

        [Test]
        public async Task ReprocessBuildsWithNoSupportedBuilds()
        {
            string body = "```\r\n{\r\n    \"errorMessage\" : \"test error message\"\r\n}\r\n```";

            var buildList = new List<Build> {new(12345), new(56789), new(54321)};
            var buildAnalysisEvents = new List<BuildAnalysisEvent> {MockBuildAnalysisEvent(54321)};

            using TestData testData = TestData.Default.WithBody(body)
                .WithBuilds(buildList)
                .WithBuildAnalysisEvents(buildAnalysisEvents)
                .Build();

            await testData.Processor.HandleMessageAsync(new MockWork(1234), false, CancellationToken.None);
            testData.RequestAnalysisService.Should().HaveCount(2);
        }

        [Test]
        public async Task NoJson()
        {
            string body = "no error message";
            using var testData = TestData.Default.WithBody(body).Build();
            await testData.Processor.HandleMessageAsync(new MockWork(1234), false, CancellationToken.None);
            testData.GitHubIssueService.Should().HaveCount(1);
            string issueBody = testData.GitHubIssueService[0];
            issueBody.Should().Contain(KnownIssueHelper.ErrorMessageTemplateIdentifier);

        }

        [Test]
        public async Task RepoOnly()
        {
            string body = "no error message";
            using var testData = TestData.Default.WithBody(body).WithRepoOnly(true).Build();
            await testData.Processor.HandleMessageAsync(new MockWork(1234), false, CancellationToken.None);
            testData.GitHubIssueService.Should().HaveCount(1);
            string issueBody = testData.GitHubIssueService[0];
            issueBody.Should().Contain(KnownIssueHelper.ErrorMessageTemplateIdentifier);
        }

        [Test]
        public async Task JsonButNoErrorMessage()
        {
            string body = "```\r\n{\r\n    \"errorMessage\" : \"\"\r\n}\r\n```";
            using var testData = TestData.Default.WithBody(body).Build();
            await testData.Processor.HandleMessageAsync(new MockWork(1234), false, CancellationToken.None);
            testData.GitHubIssueService.Should().HaveCount(0);
        }


        [Test]
        public async Task JsonWithErrorPattern()
        {
            string body = "```\r\n{\r\n    \"errorPattern\" : \"\"\r\n}\r\n```";
            using var testData = TestData.Default.WithBody(body).WithRepoOnly(true).Build();
            await testData.Processor.HandleMessageAsync(new MockWork(1234), false, CancellationToken.None);
            testData.GitHubIssueService.Should().HaveCount(0);
        }

        private BuildAnalysisEvent MockBuildAnalysisEvent(int buildId)
        {
            return new BuildAnalysisEvent("ANY_PIPELINE_NAME_TEST", buildId, "ANY_REPO_TEST", "ANY_PROJECT",
                MockDateTimeOffset(), false);
        }

        private DateTimeOffset MockDateTimeOffset()
        {
            return new DateTimeOffset(2021, 5, 12, 0, 0, 0, TimeSpan.Zero);
        }
    }
}
