using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BuildInsights.KnownIssues.Models;
using BuildInsights.KnownIssues;
using Moq;
using NUnit.Framework;
using Octokit;

namespace BuildInsights.KnownIssues.Tests
{
    [TestFixture]
    public class GitHubIssuesProviderTests
    {
        private sealed class TestData : IDisposable, IAsyncDisposable
        {
            private readonly ServiceProvider _provider;
            public GitHubIssuesProvider IssuesProvider { get; }
            private TestData(ServiceProvider provider, GitHubIssuesProvider issuesProvider)
            {
                _provider = provider;
                IssuesProvider = issuesProvider;
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
                private readonly Mock<IGitHubClient> _gitHubClientMock = new Mock<IGitHubClient>();
                private readonly Mock<IGitHubApplicationClientFactory> _gitHubApplicationClientFactoryMock = new Mock<IGitHubApplicationClientFactory>();

                public Builder()
                {
                }

                private Builder(Mock<IGitHubClient> gitHubClientMock)
                {
                    _gitHubClientMock = gitHubClientMock;
                }

                public Builder With(Mock<IGitHubClient> gitHubClientMock = null)
                {
                    return new Builder(gitHubClientMock ?? _gitHubClientMock);
                }
                public TestData Build()
                {
                    ServiceCollection collection = new ServiceCollection();
                    collection.Configure<GitHubIssuesSettings>(
                        o =>
                        {
                            o.CriticalIssuesRepositories = new List<string>() { "testing/test-known-issues" };
                            o.CriticalIssuesLabels = new List<string>() { "Critical", "FC - Infrastructure" };
                        }
                    );
                    collection.AddSingleton<IGitHubIssuesService, GitHubIssuesProvider>();

                    _gitHubApplicationClientFactoryMock
                        .Setup(g => g.CreateGitHubClientAsync(It.IsAny<string>(), It.IsAny<string>()))
                        .ReturnsAsync(_gitHubClientMock.Object);
                    collection.AddSingleton(_gitHubApplicationClientFactoryMock.Object);
                    collection.AddLogging(l => l.AddProvider(new NUnitLogger()));

                    ServiceProvider services = collection.BuildServiceProvider();
                    return new TestData(services, (GitHubIssuesProvider)services.GetRequiredService<IGitHubIssuesService>());
                }
            }

            public static TestData Default()
            {
                return new Builder().Build();
            }

            public static Builder Create() => new Builder();
        }

        [Test]
        public async Task GetCriticalIssues()
        {
            Mock<IGitHubClient> mockIssueService = new Mock<IGitHubClient>();
            mockIssueService.Setup(m => m.Issue.GetAllForRepository(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RepositoryIssueRequest>()))
                .ReturnsAsync(GenerateIssueList(2));
            using var testData = TestData.Create().With(gitHubClientMock: mockIssueService).Build();
            IEnumerable<KnownIssue> result = await testData.IssuesProvider.GetCriticalInfrastructureIssuesAsync();
            result.Should().HaveCount(2);
        }

        [Test]
        public async Task GetIssuesNullLabels()
        {
            Mock<IGitHubClient> mockIssueService = new Mock<IGitHubClient>();
            mockIssueService.Setup(m => m.Issue.GetAllForRepository(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RepositoryIssueRequest>()))
                .ReturnsAsync(GenerateIssueList(2));
            using var testData = TestData.Create().With(gitHubClientMock: mockIssueService).Build();
            ImmutableList<KnownIssue> result = await testData.IssuesProvider.GetIssues("testing/test-known-issues", KnownIssueType.Infrastructure, null);
            result.Should().HaveCount(2);
        }

        [Test]
        public async Task GetIssuesNoResults()
        {
            Mock<IGitHubClient> mockIssueService = new Mock<IGitHubClient>();
            mockIssueService.Setup(m => m.Issue.GetAllForRepository(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RepositoryIssueRequest>()))
                .ReturnsAsync(new List<Issue>());
            using var testData = TestData.Create().With(gitHubClientMock: mockIssueService).Build();
            ImmutableList<KnownIssue> result = await testData.IssuesProvider.GetIssues("testing/test-known-issues", KnownIssueType.Infrastructure, null);
            result.Should().BeEmpty();
        }

        private List<Issue> GenerateIssueList(int elements)
        {
            List<Issue> issues = new List<Issue>();
            for(int x = 0; x < elements; x++)
            {
                Issue issue =  new Issue(default, default, default, default, default, ItemState.Open, "Test Issue " + x, string.Empty, default,
                    default, default, default, default, default, 1, default, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue,
                    DateTimeOffset.MaxValue, 1, default, default, default, default, default, default);
                issues.Add(issue);
            }
            return issues;
        }
    }
}
