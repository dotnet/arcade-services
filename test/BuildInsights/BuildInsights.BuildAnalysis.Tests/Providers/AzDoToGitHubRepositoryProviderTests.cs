// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.GitHub;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Octokit;

namespace BuildInsights.BuildAnalysis.Tests.Providers;

public partial class AzDoToGitHubRepositoryProviderTests
{
    [TestDependencyInjectionSetup]
    public static class TestSetup
    {
        public static void Defaults(IServiceCollection services)
        {
            Mock<IGitHubClient> gitHubClientMock = new Mock<IGitHubClient>();
            gitHubClientMock.Setup(t => t.Repository.Get(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new Octokit.Repository());

            Mock<IGitHubApplicationClientFactory> gitHubApplicationClientFactoryMock = new Mock<IGitHubApplicationClientFactory>();
            gitHubApplicationClientFactoryMock
                .Setup(g => g.CreateGitHubClientAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(gitHubClientMock.Object);

            services.AddSingleton(gitHubApplicationClientFactoryMock.Object);
            services.AddLogging(l => l.AddProvider(new NUnitLogger()));
        }

        public static Func<IServiceProvider, AzDoToGitHubRepositoryProvider> Processor(IServiceCollection services)
        {
            services.AddSingleton<AzDoToGitHubRepositoryProvider>();
            return s => s.GetRequiredService<AzDoToGitHubRepositoryProvider>();
        }

        public static Func<IServiceProvider, Mock<IGitHubChecksService>> GitHubChecksService(
            IServiceCollection collection, bool isRepositorySupported, bool hasIssues)
        {
            var githubCheckService = new Mock<IGitHubChecksService>();
            githubCheckService.Setup(g => g.IsRepositorySupported(It.IsAny<string>()))
                .ReturnsAsync(isRepositorySupported);
            githubCheckService.Setup(g => g.RepositoryHasIssues(It.IsAny<string>())).ReturnsAsync(hasIssues);

            collection.AddSingleton(githubCheckService.Object);
            return _ => githubCheckService;
        }
    }

    [TestCase("dotnet-arcade-services", true, "dotnet/arcade-services")]
    [TestCase("regular-test", true, "regular/test")]
    [TestCase("any_random-repo_test", true, "any_random/repo_test")]
    [TestCase("anyrandomtest", false, null)]
    public async Task GetGitHubRepositoryFromAzDoRepositoryNameTest(string name, bool isValid,
        string expectedGitHubRepository)
    {
        var buildRepository = new BuildRepository(name, "TfsGit");

        await using TestData testData = await TestData.Default.WithIsRepositorySupported(true).WithHasIssues(true).BuildAsync();
        AzDoToGitHubRepositoryResult result = await testData.Processor.TryGetGitHubRepositorySupportingKnownIssues(buildRepository, "12345");

        result.IsValidRepositoryAvailable.Should().Be(isValid);
        result.GitHubRepository.Should().Be(expectedGitHubRepository);
    }

    [TestCase("TfsGit", true)]
    [TestCase("TfsVersionControl", false)]
    [TestCase("Git", false)]
    [TestCase("GitHub", false)]
    public async Task GetGitHubRepositoryInvalidTypeTest(string type, bool expectedResult)
    {
        var buildRepository = new BuildRepository("dotnet-text=example-services", type);

        await using TestData testData = await TestData.Default.WithIsRepositorySupported(true).WithHasIssues(true).BuildAsync();
        AzDoToGitHubRepositoryResult result = await testData.Processor.TryGetGitHubRepositorySupportingKnownIssues(buildRepository, "12345");

        result.IsValidRepositoryAvailable.Should().Be(expectedResult);
    }

    [TestCase(true, true, true)]
    [TestCase(true, false, false)]
    public async Task GetGitHubRepositoryFromAzDoRepositoryIsValidButNoAvailable(bool isRepositorySupported,
        bool hasIssues, bool expectedResult)
    {
        await using TestData testData = await TestData.Default.WithIsRepositorySupported(isRepositorySupported).WithHasIssues(hasIssues).BuildAsync();
        var buildRepository = new BuildRepository("any_random-repo_test", "TfsGit");


        AzDoToGitHubRepositoryResult result = await testData.Processor.TryGetGitHubRepositorySupportingKnownIssues(buildRepository, "12345");
        result.IsValidRepositoryAvailable.Should().Be(expectedResult);
    }
}
