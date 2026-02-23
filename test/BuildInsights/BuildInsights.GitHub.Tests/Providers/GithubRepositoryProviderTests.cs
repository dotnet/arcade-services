using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AwesomeAssertions;
using BuildInsights.GitHub;
using Moq;
using NUnit.Framework;
using Octokit;

namespace BuildInsights.GitHub.Tests.Providers;

[TestFixture]
public partial class GithubRepositoryProviderTests
{
    [TestDependencyInjectionSetup]
    public static class TestSetup
    {
        public const string TestTargetBranch = "test";

        public static void Defaults(IServiceCollection services)
        {
            services.AddLogging(l => l.AddProvider(new NUnitLogger()));
        }

        public static Func<IServiceProvider, Mock<IGitHubApplicationClientFactory>> GitHubClient(IServiceCollection services, List<RepositoryContent> repositoryContents)
        {
            var responseMock = new Mock<IResponse>();
            responseMock.SetupGet(r => r.ContentType).Returns("ignored");

            var mockGitHubApplicationClientFactory = new Mock<IGitHubApplicationClientFactory>();
            var mockGitHubClient = new Mock<IGitHubClient>();
            mockGitHubClient
                .Setup(m => m.Repository.Content.GetAllContentsByRef(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), TestTargetBranch))
                .ReturnsAsync(repositoryContents ?? new List<RepositoryContent>());
            mockGitHubClient
                .Setup(m => m.Repository.Content.GetAllContentsByRef(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsNotIn(TestTargetBranch)))
                .Throws(new NotFoundException(responseMock.Object));
            mockGitHubApplicationClientFactory
                .Setup(m => m.CreateGitHubClientAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(mockGitHubClient.Object);

            services.AddSingleton(_ => mockGitHubApplicationClientFactory.Object);

            return _ => mockGitHubApplicationClientFactory;
        }

        public static Func<IServiceProvider, GithubRepositoryProvider> Processor(IServiceCollection services)
        {
            services.AddSingleton<GithubRepositoryProvider>();

            return s => s.GetRequiredService<GithubRepositoryProvider>();
        }
    }

    [Test]
    public async Task GetFileAsync_WhenTargetBranchIsEmpty_ReturnsEmptyString()
    {
        await using TestData testData = await TestData.Default.BuildAsync();

        string branch = string.Empty;
        string result = await testData.Processor.GetFileAsync("any/repository", "anypath", branch);

        result.Should().Be(string.Empty);
    }

    [Test]
    public async Task GetFileAsync_WhenBuildAnalysisSettingsFileNotFound_ReturnsEmptyString()
    {
        await using TestData testData = await TestData.Default.BuildAsync();

        string branch = "random.notest.branch";
        string result = await testData.Processor.GetFileAsync("any/repository", "anypath", branch);

        result.Should().Be(string.Empty);
    }

    [Test]
    public async Task GetFileAsync_WhenFound_ReturnsContentString()
    {
        string expectedContent = "Expected content test";
        await using TestData testData = await TestData.Default
            .WithRepositoryContents(new List<RepositoryContent> {MockRepositoryContent(expectedContent)})
            .BuildAsync();

        string result = await testData.Processor.GetFileAsync("any/repository", "anypath", TestSetup.TestTargetBranch);

        result.Should().Be(expectedContent);
    }

    private RepositoryContent MockRepositoryContent(string content)
    {
        return new RepositoryContent("", "", "", 1, ContentType.File, "", "", "", "", "",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(content)), "", "");
    }
}
