using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Services;
using Microsoft.Internal.Helix.GitHub.Models;
using Microsoft.Internal.Helix.GitHub.Services;
using Microsoft.Internal.Helix.KnownIssues.Models;
using Microsoft.Internal.Helix.KnownIssues.Services;
using Microsoft.Internal.Helix.QueueInsights.Services;
using Microsoft.Internal.Helix.Utility.Azure;
using Moq;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildResultAnalysisProcessor.Tests
{
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public partial class AnalysisProcessorsTests
    {
        [TestDependencyInjectionSetup]
        public static class TestSetup
        {
            public static void Defaults(IServiceCollection collection)
            {
                collection.AddLogging(l => l.AddProvider(new NUnitLogger()));
                collection.AddSingleton(new TelemetryClient(new TelemetryConfiguration()));
                collection.AddSingleton<ISystemClock>(new TestClock());
                collection.AddOperationTracking(o => o.ShouldStartActivity = false);

                var relatedBuildServiceMock = new Mock<IRelatedBuildService>();
                relatedBuildServiceMock
                    .Setup(x => x.GetRelatedBuilds(It.IsAny<BuildReferenceIdentifier>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new RelatedBuilds(Enumerable.Empty<BuildReferenceIdentifier>()));
                collection.AddSingleton(relatedBuildServiceMock.Object);

                var pipelineRequestedProvider = new Mock<IPipelineRequestedService>();
                pipelineRequestedProvider.Setup(p => p.IsBuildPipelineRequested(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(true);
                pipelineRequestedProvider.Setup(p => p.GetBuildsByPipelineConfiguration(It.IsAny<ImmutableList<BuildReferenceIdentifier>>(), It.IsAny<NamedBuildReference>()))
                    .ReturnsAsync((ImmutableList<BuildReferenceIdentifier> relatedBuildsParameter, NamedBuildReference namedBuildReference) =>
                        new BuildsByPipelineConfiguration(relatedBuildsParameter, ImmutableList<BuildReferenceIdentifier>.Empty));
                collection.AddSingleton(pipelineRequestedProvider.Object);

                var queueInsightsMock = new Mock<IQueueInsightsService>();
                queueInsightsMock.Setup(x => x.CreateQueueInsightsAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<IImmutableSet<int>>(),
                        It.IsAny<string>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(0);
                collection.AddSingleton(queueInsightsMock.Object);
                collection.AddSingleton(new Mock<IPullRequestService>().Object);
                collection.AddSingleton(new Mock<IAzDoToGitHubRepositoryService>().Object);
            }

            public static Func<IServiceProvider, AnalysisProcessor> Processor(IServiceCollection collection)
            {
                collection.AddSingleton<AnalysisProcessor>();

                return s => s.GetRequiredService<AnalysisProcessor>();
            }

            public static Func<IServiceProvider, Mock<IKnownIssueValidationService>> KnownIssueValidationService(IServiceCollection collection)
            {
                Mock<IKnownIssueValidationService> knownIssueValidationService = new Mock<IKnownIssueValidationService>();
                collection.AddSingleton(knownIssueValidationService.Object);

                return _ => knownIssueValidationService;
            }

            public static Func<IServiceProvider, Mock<IBuildAnalysisRepositoryConfigurationService>> IBuildAnalysisRepoConfigurationService(IServiceCollection collection)
            {
                Mock<IBuildAnalysisRepositoryConfigurationService> buildAnalysisConfigurationService = new Mock<IBuildAnalysisRepositoryConfigurationService>();
                collection.AddSingleton(buildAnalysisConfigurationService.Object);

                return _ => buildAnalysisConfigurationService;
            }

            public static Func<IServiceProvider, IBuildProcessingStatusService> BuildProcessingStatus(IServiceCollection collection, bool isUnderProcessing)
            {
                Mock<IBuildProcessingStatusService> mockBuildProcessingStatusService = new Mock<IBuildProcessingStatusService>();

                mockBuildProcessingStatusService
                    .Setup(m => m.IsBuildBeingProcessed(It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(isUnderProcessing);

                collection.AddSingleton(mockBuildProcessingStatusService.Object);

                return s => s.GetRequiredService<IBuildProcessingStatusService>();
            }

            public static Func<IServiceProvider, List<MergeBuildAnalysisAction>> MergedBuildAnalysisAction(IServiceCollection collection)
            {
                var myAnalysis = new MergedBuildResultAnalysis("COMMIT-HASH", ImmutableList.Create(new BuildResultAnalysis()), CheckResult.Passed, null, null, null);
                var analysisProvider = new Mock<IMergedBuildAnalysisService>();
                List<MergeBuildAnalysisAction> actions = new List<MergeBuildAnalysisAction>();
                analysisProvider.Setup(a => a.GetMergedAnalysisAsync(It.IsAny<NamedBuildReference>(), Capture.In(actions), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(myAnalysis);
                collection.AddSingleton(analysisProvider.Object);

                return _ => actions;
            }

            public static Func<IServiceProvider, Mock<IGitHubIssuesService>> GitHubIssuesService(IServiceCollection collection)
            {
                var issueService = new Mock<IGitHubIssuesService>();
                issueService.Setup(g => g.GetCriticalInfrastructureIssuesAsync())
                    .ReturnsAsync(ImmutableList<KnownIssue>.Empty);
                collection.AddSingleton(issueService.Object);

                return _ => issueService;
            }

            public static Func<IServiceProvider, List<MarkdownParameters>> SentAnalyses(IServiceCollection collection)
            {
                var sentAnalyses = new List<MarkdownParameters>();
                var markdownGenerator = new Mock<IMarkdownGenerator>();
                markdownGenerator.Setup(m => m.GenerateMarkdown(Capture.In(sentAnalyses)))
                    .Returns("FAKE-MARKDOWN-STRING");
                collection.AddSingleton(markdownGenerator.Object);
                return _ => sentAnalyses;
            }

            public static Func<IServiceProvider, (List<CheckResult>, Mock<IGitHubChecksService>)> SentCheckResults(IServiceCollection collection, bool? repositorySupported)
            {
                var sentCheckResults = new List<CheckResult>();
                var checksService = new Mock<IGitHubChecksService>();
                checksService.Setup(c => c.IsRepositorySupported(It.IsAny<string>()))
                    .ReturnsAsync(repositorySupported ?? true);
                checksService.Setup(
                        c => c.PostChecksResultAsync(
                            It.IsAny<string>(),
                            It.IsAny<string>(),
                            It.IsAny<string>(),
                            It.IsAny<string>(),
                            It.IsAny<string>(),
                            Capture.In(sentCheckResults),
                            It.IsAny<CancellationToken>()
                        )
                    )
                    .ReturnsAsync(0);

                checksService
                    .Setup(c => c.GetCheckRunAsyncForApp(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
                    .ReturnsAsync(new CheckRun());

                collection.AddSingleton(checksService.Object);

                return _ => (sentCheckResults, checksService);
            }

            public static void BuildService(IServiceCollection collection, bool? buildIsCompleted)
            {
                var buildAnalysisService = new Mock<IBuildAnalysisService>();
                collection.AddSingleton(buildAnalysisService.Object);

                var buildService = new Mock<IBuildDataService>();
                buildService.Setup(b => b.GetBuildAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new Build(repository: "TEST-REPOSITORY", isComplete: buildIsCompleted ?? true));
                collection.AddSingleton(buildService.Object);



                var tableServiceMock = new Mock<IBuildAnalysisHistoryService>();
                tableServiceMock.Setup(m => m.GetLastBuildAnalysisRecord(It.IsAny<int>(), It.IsAny<string>()))
                    .Returns(() => null);
                collection.AddSingleton(tableServiceMock.Object);
            }

            public static Func<IServiceProvider, MockContextualStorage> ContextualStorage(IServiceCollection collection)
            {
                collection.AddSingleton<IContextualStorage, MockContextualStorage>();
                return s => (MockContextualStorage)s.GetRequiredService<IContextualStorage>();
            }

            public static Func<IServiceProvider, Mock<IDistributedLock>> DistributedLock(IServiceCollection collection, bool shouldLockThrowTimeout, Task lockWait)
            {
                var service = new Mock<IDistributedLockService>();
                var @lock = new Mock<IDistributedLock>();
                @lock.Setup(m => m.Dispose()).Verifiable();
                @lock.Setup(m => m.DisposeAsync()).Verifiable();

                var setup = service.Setup(
                    m => m.AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())
                );

                if (shouldLockThrowTimeout)
                {
                    setup.Throws<TimeoutException>();
                }
                else
                {
                    setup.Returns(
                        async () =>
                        {
                            if (lockWait != null)
                                await lockWait;
                            return @lock.Object;
                        }
                    );
                }

                collection.AddSingleton(service.Object);

                return _ => @lock;
            }
        }

        public static readonly DateTimeOffset TestNow = DateTimeOffset.Parse("2020-01-02T15-04-04.07Z");
        public const string CommitHash = "TEST-OPAQUE-COMMIT-HASH";
        public const int BuildId = 111222333;

        public class MockWork : IQueuedWork
        {
            private readonly string _value;

            public MockWork(int buildId, bool completed)
            {
                if (completed)
                {
                    _value = JsonSerializer.Serialize(
                        new
                        {
                            eventType = "build.complete",
                            resource = new {
                                id = buildId,
                                url = "https://dev.azure.com/TESTCOLLECTION/00000000-0000-0000-0000-111111111111/_apis/build/Builds/2"
                            },
                            resourceContainers = new {project = new {id = "TEST-PROJECT-ID"}}
                        }
                    );
                }
                else
                {
                    _value = JsonSerializer.Serialize(
                        new
                        {
                            eventType = "ms.vss-pipelines.run-state-changed-event",
                            resource = new
                            {
                                runId = buildId,
                                runUrl =
                                    "https://dev.azure.test/TESTCOLLECTION/00000000-0000-0000-0000-111111111111/_apis/pipelines/666666/runs/111222333"
                            },
                        }
                    );
                }
            }

            public MockWork(string organization, string repository, int issueId)
            {
                _value = JsonSerializer.Serialize(
                    new
                    {
                        eventType = "knownissue.validate",
                        organization,
                        repository,
                        repositoryWithOwner = $"{organization}/{repository}"
                    }
                );
            }

           public MockWork(string repository, string headSha, string checkResult, string justification)
           {
                _value = JsonSerializer.Serialize(
                    new
                    {
                        eventType = "checkrun.conclusion-update",
                        repository,
                        headSha,
                        checkResult,
                        justification
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
        public async Task BuildCompletedSendsAnalysisWithOnlyCompletedPipelines()
        {
            await using var test = await TestData.Default
                .BuildAsync();
            await test.Processor.HandleMessageAsync(new MockWork(11122333, completed: true), false, CancellationToken.None);
            test.MergedBuildAnalysisAction.Should().BeEquivalentTo(new[] { MergeBuildAnalysisAction.Include });
        }

        [Test]
        public async Task BuildStaredSendsAnalysisWithOnlyPendingPipelines()
        {
            await using var test = await TestData.Default
                .WithBuildIsCompleted(false)
                .BuildAsync();
            await test.Processor.HandleMessageAsync(new MockWork(11122333, completed: false), false, CancellationToken.None);
            test.MergedBuildAnalysisAction.Should().BeEquivalentTo(new[] { MergeBuildAnalysisAction.Exclude });
        }

        [Test]
        public async Task UnsupportedRepositoryAborts()
        {
            await using var test = await TestData.Default
                .WithRepositorySupported(false)
                .BuildAsync();
            await test.Processor.HandleMessageAsync(new MockWork(11122333, completed: true), false, CancellationToken.None);

            test.SentAnalyses.Should().BeEmpty();
            test.SentCheckResults.Item1.Should().BeEmpty();
            test.ContextualStorage.PathContext.Should().BeNull();
        }

        [Test]
        public async Task CanAcquireBlobLeaseAsync()
        {
            await using var test = await TestData.Default.BuildAsync();
            await test.Processor.HandleMessageAsync(new MockWork(11122333, completed: true), false, CancellationToken.None);

            test.DistributedLock.Verify(m => m.DisposeAsync(), Times.Once);
        }

        [Test]
        public async Task CanAcquireBlobLeaseAfterWait()
        {
            TaskCompletionSource<bool> waitTaskSource = new TaskCompletionSource<bool>();

            await using var test = await TestData.Default
                .WithLockWait(waitTaskSource.Task)
                .BuildAsync();
            Task testResultTask = test.Processor.HandleMessageAsync(new MockWork(11122333, completed: true), false, CancellationToken.None);

            Task waitTask = await Task.WhenAny(testResultTask, Task.Delay(TimeSpan.FromSeconds(2)));
            waitTask.Should().NotBeSameAs(testResultTask);

            waitTaskSource.SetResult(true);
            await testResultTask;

            test.DistributedLock.Verify(m => m.DisposeAsync(), Times.Once);
        }

        [Test]
        public async Task CannotAcquireBlobLease()
        {
            await using var test = await TestData.Default
                .WithShouldLockThrowTimeout(true)
                .BuildAsync();
            await test.Processor.Invoking(o => o.HandleMessageAsync(new MockWork(11122333, completed: true), false, CancellationToken.None)).Should().ThrowAsync<TimeoutException>();
        }

        [Test]
        public async Task KnownIssueValidation()
        {
            await using var test = await TestData.Default.BuildAsync();

            await test.Processor.HandleMessageAsync(new MockWork("TEST_ORGANIZATION", "TEST_REPOSITORY", 1), false, CancellationToken.None);
            test.KnownIssueValidationService.Verify(k => k.ValidateKnownIssue(It.IsAny<KnownIssueValidationMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        }


       [Test]
        public async Task CheckRerunOverrideTest()
        {
            await using var test = await TestData.Default.BuildAsync();
            await test.Processor.HandleMessageAsync(new MockWork("test/repositor", "0123headSha", "Success", "Testing justification"), false, CancellationToken.None);

            test.SentCheckResults.Item2.Verify(k => k.UpdateCheckRunConclusion(It.IsAny<CheckRun>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Octokit.CheckConclusion>()), Times.Once);
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("             ")]
        public async Task CheckRerunOverrideNoJustificationProvidedTest(string justification)
        {
            await using var test = await TestData.Default.BuildAsync();
            await test.Processor.HandleMessageAsync(new MockWork("test/repository", "0123headSha", "Success", justification), false, CancellationToken.None);

            test.GitHubIssuesService.Verify(k => k.AddCommentToIssueAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Once);
            test.SentCheckResults.Item2.Verify(k => k.UpdateCheckRunConclusion(It.IsAny<CheckRun>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Octokit.CheckConclusion>()), Times.Never);
        }

        [TestCase(true, 0)]
        [TestCase(false, 1)]
        public async Task UnderProcessingsTests(bool isUnderProcessing, int expectedActions)
        {
            await using var test = await TestData.Default
                .WithIsUnderProcessing(isUnderProcessing)
                .BuildAsync();

            await test.Processor.HandleMessageAsync(new MockWork(111222333, completed: true), false, CancellationToken.None);
            test.MergedBuildAnalysisAction.Count.Should().Be(expectedActions);
        }
    }

    public class MockContextualStorage : BaseContextualStorage
    {
        readonly Dictionary<string, byte[]> _data = new Dictionary<string, byte[]>();
        protected override async Task PutAsync(string root, string name, Stream data, CancellationToken cancellationToken)
        {
            using MemoryStream stream = new MemoryStream();
            await data.CopyToAsync(stream, cancellationToken);
            _data[name] = stream.ToArray();
        }

        protected override Task<Stream> TryGetAsync(string root, string name, CancellationToken cancellationToken)
        {
            if (_data.TryGetValue(name, out byte[] mem))
            {
                return Task.FromResult<Stream>(new MemoryStream(mem, false));
            }

            return Task.FromResult<Stream>(null);
        }

        public new string PathContext => base.PathContext;
    }
}
