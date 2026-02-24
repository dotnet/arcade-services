// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.GitHub;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Octokit;
using CheckRun = BuildInsights.GitHub.Models.CheckRun;

namespace BuildInsights.BuildAnalysis.Tests.Providers;

[TestFixture]
public class RelatedBuildProviderTests
{
    private const int AzurePipelinesAppID = 9426;
    private static readonly Dictionary<string, string> AllowedTargetProjects = new() { { "TEST-TEST-NAME", "9ee6d478-d288-47f7-aacc-f6e6d082ae6d" } };

    public sealed class TestData : IDisposable, IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        public RelatedBuildProvider RelatedBuildProvider { get; }

        private TestData(ServiceProvider provider, RelatedBuildProvider relatedBuildProvider)
        {
            _provider = provider;
            RelatedBuildProvider = relatedBuildProvider;
        }

        public class Builder
        {
            private readonly List<CheckRun> _checkRuns = [];

            public Builder()
            {
            }

            private Builder With(List<CheckRun> checkRuns = null)
            {
                return new Builder(checkRuns ?? _checkRuns);
            }
            public Builder(List<CheckRun> checkRuns)
            {
                _checkRuns = checkRuns;
            }

            public Builder WithCheckRuns(List<CheckRun> checkRuns) => With(checkRuns: checkRuns);

            public TestData Build()
            {

                var gitHubChecksServiceMock = new Mock<IGitHubChecksService>();
                gitHubChecksServiceMock.Setup(g => g.GetBuildCheckRunsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(_checkRuns);

                var pipelineRequested = new Mock<IPipelineRequestedService>();
                pipelineRequested.Setup(p => p.IsBuildPipelineRequested(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                    .ReturnsAsync(true);

                var buildDataProvider = new Mock<IBuildDataService>();

                ServiceProvider services = new ServiceCollection()
                    .AddLogging(l => { l.AddProvider(new NUnitLogger()); })
                    .AddSingleton<RelatedBuildProvider>()
                    .AddSingleton(gitHubChecksServiceMock.Object)
                    .AddSingleton(pipelineRequested.Object)
                    .AddSingleton(buildDataProvider.Object)
                    .Configure<RelatedBuildProviderSettings>(o => { o.AllowedTargetProjects = AllowedTargetProjects; })
                    .BuildServiceProvider();

                return new TestData(services, services.GetRequiredService<RelatedBuildProvider>());
            }
        }

        public void Dispose()
        {
            _provider.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            return _provider.DisposeAsync();
        }

        public static Builder Default { get; } = new Builder();
    }

    [Test]
    public async Task GetRelatedBuildsTestForDefinedRelatedProject()
    {
        List<CheckRun> checkRuns =
        [
            new (CreateOctokitCheckRun(AzurePipelinesAppID, AllowedTargetProjects.Values.First()))
        ];

        TestData.Builder builder = TestData.Default
        .WithCheckRuns(checkRuns);
        await using TestData testData = builder.Build();

        RelatedBuilds result = await testData.RelatedBuildProvider.GetRelatedBuilds(new BuildReferenceIdentifier("", "", 0, "", 1, "", "", "", ""), CancellationToken.None);
        result.RelatedBuildsList.Should().HaveCount(1);
    }

    [Test]
    public async Task GetRelatedBuildsTestForDifferentRelatedProject()
    {
        TestData.Builder builder = TestData.Default;
        List<CheckRun> checkRuns =
        [
            new CheckRun(CreateOctokitCheckRun(0,"9ee6d477-d266-47f7-aacc-f6e6d082ae6d"))
        ];

        builder = builder.WithCheckRuns(checkRuns);
        await using TestData testData = builder.Build();

        RelatedBuilds result = await testData.RelatedBuildProvider.GetRelatedBuilds(new BuildReferenceIdentifier("", "", 0, "", 1, "", "", "", ""), CancellationToken.None);
        result.RelatedBuildsList.Should().HaveCount(0);
    }

    private static Octokit.CheckRun CreateOctokitCheckRun(int gitHubAppId, string targetProject, CheckStatus status = CheckStatus.Completed)
    {
        var dateTimeOffset = new DateTimeOffset(2021, 5, 27, 11, 0, 0, 0, TimeSpan.Zero);
        var githubApp = new GitHubApp(gitHubAppId, default, default, default, default, default, default, default,
            dateTimeOffset, dateTimeOffset, default, default);
        return new Octokit.CheckRun(0, "", "123|456|" + targetProject, "", "", "", status,
            CheckConclusion.Failure, dateTimeOffset, dateTimeOffset, new CheckRunOutputResponse(), "",
            new CheckSuite(), githubApp, new List<PullRequest>());
    }
}
