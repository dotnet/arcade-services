using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Internal.Helix.GitHub.Models;
using Microsoft.Internal.Helix.GitHub.Providers;
using Moq;
using NUnit.Framework;
using Octokit;
using CheckRun = Octokit.CheckRun;
using CheckStatus = Octokit.CheckStatus;
using Repository = Octokit.Repository;

namespace Microsoft.Internal.Helix.GitHub.Tests
{
    [TestFixture]
    public class GitHubCheckServiceTests
    {
        private const int AzurePipelinesAppID = 9426;

        public GitHubChecksProvider SetUp(
            Mock<ICheckRunsClient> checkRunsClientMockOverride = null,
            Mock<IGitHubApplicationClientFactory> gitHubApplicationClientFactoryMockOverride = null
        )
        {
            Mock<ICheckRunsClient> checkRunsClientMock = checkRunsClientMockOverride ?? new Mock<ICheckRunsClient>();
            var gitHubClientMock = new Mock<IGitHubClient>();
            gitHubClientMock.SetupGet(m => m.Check.Run).Returns(checkRunsClientMock.Object);

            Mock<IGitHubApplicationClientFactory> gitHubApplicationClientFactoryMock =
                gitHubApplicationClientFactoryMockOverride;
            if (gitHubApplicationClientFactoryMock == null)
            {
                gitHubApplicationClientFactoryMock = new Mock<IGitHubApplicationClientFactory>();
                gitHubApplicationClientFactoryMock
                    .Setup(g => g.CreateGitHubClientAsync(It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(gitHubClientMock.Object);
            }

            var collection = new ServiceCollection();
            collection.AddSingleton<GitHubChecksProvider>();
            collection.AddSingleton(gitHubClientMock.Object);
            collection.AddSingleton(gitHubApplicationClientFactoryMock.Object);
            collection.AddLogging(l => { l.AddProvider(new NUnitLogger()); });
            ServiceProvider services = collection.BuildServiceProvider();
            return services.GetRequiredService<GitHubChecksProvider>();
        }

        private Repository MockRepository()
        {
            string name = "ExistingRepo";
            string fullName = "ExistingOwner/ExistingRepo";

            return new Repository("", "", "", "", "", "", "", "", 1, "", new User(), name, fullName, true, "", "", "",
                default, default, 1, 1, default, 1, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue,
                DateTimeOffset.MaxValue, new RepositoryPermissions(), new Repository(), new Repository(), new LicenseMetadata(), true, true,
                default, true, true, 1,
                1, true, true, true, true, 1, true, default, new List<string>(), true, true, true, new SecurityAndAnalysis());
        }

        [TestCase("ExistingOwner/ExistingRepo", true)]
        [TestCase("ExistingOwner/NotExistingRepo", false)]
        [TestCase("AnyOwnerNotExisting/AnyRepoNotExisting", false)]
        public async Task IsRepositorySupportedTest(string repository, bool expectedResult)
        {
            var repo = MockRepository();
            var responseMock = new Mock<IResponse>();
            responseMock.SetupGet(r => r.ContentType).Returns("ignored");
            var gitHubClientMock = new Mock<IGitHubClient>();
            gitHubClientMock.Setup(g => g.GitHubApps.Installation.GetAllRepositoriesForCurrent()).ReturnsAsync(
                new RepositoriesResponse(1, new List<Repository> {repo}));

            var gitHubApplicationClientFactoryMock = new Mock<IGitHubApplicationClientFactory>();
            gitHubApplicationClientFactoryMock
                .Setup(g => g.CreateGitHubClientAsync(It.IsIn("ExistingOwner"), It.IsAny<string>()))
                .ReturnsAsync(gitHubClientMock.Object);
            gitHubApplicationClientFactoryMock
                .Setup(g => g.CreateGitHubClientAsync(It.IsNotIn("ExistingOwner"), It.IsAny<string>()))
                .Throws(new NotFoundException(responseMock.Object));

            GitHubChecksProvider gitHubCheckService = SetUp(gitHubApplicationClientFactoryMockOverride: gitHubApplicationClientFactoryMock);

            bool result = await gitHubCheckService.IsRepositorySupported(repository);
            result.Should().Be(expectedResult);
        }

        [Test]
        public async Task PostChecksResultAsyncTest()
        {
            IList<string> orgInput = new List<string>();
            IList<string> repoInput = new List<string>();
            IList<NewCheckRun> newCheckRunInput = new List<NewCheckRun>();

            var checkRunsClientMock = new Mock<ICheckRunsClient>();
            var checkRun = new CheckRun(12345, "COMMIT", default, default, default, default, CheckStatus.Completed,
                default, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue, default, default, default, default, default);
            checkRunsClientMock.Setup(m =>
                    m.Create(Capture.In(orgInput), Capture.In(repoInput), Capture.In(newCheckRunInput)))
                .Returns(Task.FromResult(checkRun));

            GitHubChecksProvider gitHubCheckService = SetUp(checkRunsClientMockOverride: checkRunsClientMock);
            long id = await gitHubCheckService.PostChecksResultAsync("Build Analysis", ".NET Result Analysis", "markdownTestText", "TestOrg/ThisTestRepo",
                "abcdefghijklmnopqrstuvwxyz", CheckResult.Passed, CancellationToken.None);

            orgInput[0].Should().Be("TestOrg");
            repoInput[0].Should().Be("ThisTestRepo");
            newCheckRunInput[0].HeadSha.Should().Be("abcdefghijklmnopqrstuvwxyz");
            newCheckRunInput[0].Output.Text.Should().Be("markdownTestText");
            id.Should().Be(12345);
        }


        [Test]
        public async Task PostChecksResultAsyncTestWhenReturningInProgress()
        {
            IList<string> orgInput = new List<string>();
            IList<string> repoInput = new List<string>();
            IList<NewCheckRun> newCheckRunInput = new List<NewCheckRun>();

            var checkRun = CreateOctokitCheckRun(1, "12345");
            var checkRunsClientMock = new Mock<ICheckRunsClient>();
            checkRunsClientMock.Setup(m =>
                    m.Create(Capture.In(orgInput), Capture.In(repoInput), Capture.In(newCheckRunInput)))
                .Returns(Task.FromResult(checkRun));

            GitHubChecksProvider gitHubCheckService = SetUp(checkRunsClientMockOverride: checkRunsClientMock);
            await gitHubCheckService.PostChecksResultAsync("", "", "","a/b",
                "", CheckResult.InProgress, CancellationToken.None);

            newCheckRunInput[0].Status.Should().Be(new StringEnum<CheckStatus>(CheckStatus.InProgress));

            newCheckRunInput[0].Conclusion.Should().BeNull();
        }


        [TestCase(AzurePipelinesAppID, "",0)]
        [TestCase(987, "123|456|00000000-0000-0000-0000-0000000000ab", 0)]
        [TestCase(AzurePipelinesAppID, "123|456|00000000-0000-0000-0000-0000000000ab", 1)]
        public async Task GetBuildCheckRunsAsyncTest(int id, string externalId, int expectedCount)
        {
            List<CheckRun> checkRuns = new List<CheckRun>()
            {
                CreateOctokitCheckRun(id, externalId)
            };

            Mock<IGitHubApplicationClientFactory> mockGitHubApplicationClientFactory = GetMockGitHubApplicationClientFactory(checkRuns);

            GitHubChecksProvider gitHubCheckService = SetUp(gitHubApplicationClientFactoryMockOverride: mockGitHubApplicationClientFactory);
            var result = await gitHubCheckService.GetBuildCheckRunsAsync("anyOwner/anyName", "123456789");
            result.Count().Should().Be(expectedCount);
        }

        [TestCase(1, 1, true)]
        [TestCase(1, 2, false)]
        public async Task GeCheckRunsForAppAsyncTest(int checkRunAppId, int appId, bool shouldMatch)
        {
            List<CheckRun> checkRuns = new List<CheckRun>()
            {
                CreateOctokitCheckRun(checkRunAppId, "TestExternalId")
            };

            Mock<IGitHubApplicationClientFactory> mockGitHubApplicationClientFactory = GetMockGitHubApplicationClientFactory(checkRuns);

            GitHubChecksProvider gitHubCheckService = SetUp(gitHubApplicationClientFactoryMockOverride: mockGitHubApplicationClientFactory);
            var result = await gitHubCheckService.GetCheckRunAsyncForApp("anyOwner/anyName", "123456789", appId, "");

            if (shouldMatch)
            {
                result.Should().NotBeNull();
            }
            else
            {
                result.Should().BeNull();
            }
        }

        [TestCase("TestCheckRunName",  true)]
        [TestCase("AyOtherCheckRunName", false)]
        public async Task GeCheckRunsForAppFilterByCheckRunNameTest(string checkRunAppNameFiltered, bool shouldMatch)
        {
            string checkRunName = "TestCheckRunName";
            List<CheckRun> checkRuns = new List<CheckRun>()
            {
                CreateOctokitCheckRun(1, "TestExternalId", checkRunName)
            };

            Mock<IGitHubApplicationClientFactory> mockGitHubApplicationClientFactory = GetMockGitHubApplicationClientFactory(checkRuns);

            GitHubChecksProvider gitHubCheckService = SetUp(gitHubApplicationClientFactoryMockOverride: mockGitHubApplicationClientFactory);
            var result = await gitHubCheckService.GetCheckRunAsyncForApp("anyOwner/anyName", "123456789", 1, checkRunAppNameFiltered);

            if (shouldMatch)
            {
                result.Should().NotBeNull();
            }
            else
            {
                result.Should().BeNull();
            }
        }

        private static Mock<IGitHubApplicationClientFactory> GetMockGitHubApplicationClientFactory(List<CheckRun> checkRuns)
        {

            CheckRunsResponse checkRunsResponse = new CheckRunsResponse(checkRuns.Count, checkRuns);
            Mock<IGitHubClient> client = new Mock<IGitHubClient>();
            client.Setup(c => c.Check.Run.GetAllForReference(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(checkRunsResponse);
            Mock<IGitHubApplicationClientFactory> mockGitHubApplicationClientFactory =
                new Mock<IGitHubApplicationClientFactory>();
            mockGitHubApplicationClientFactory
                .Setup(g => g.CreateGitHubClientAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(client.Object);
            return mockGitHubApplicationClientFactory;
        }

        private static CheckRun CreateOctokitCheckRun(int gitHubAppId, string externalId, string checkRunName = "")
        {
            var dateTimeOffset = new DateTimeOffset(2021, 5, 27, 12, 0, 0, TimeSpan.Zero);
            var gitHubApp = new GitHubApp(gitHubAppId, "", "", "", new User(), "", "", "",
                dateTimeOffset, dateTimeOffset, new InstallationPermissions(), new List<string>());

            return new Octokit.CheckRun(gitHubAppId, "", externalId, "", "", "", CheckStatus.Completed,
                Octokit.CheckConclusion.Failure, dateTimeOffset, dateTimeOffset, new CheckRunOutputResponse(), checkRunName, new CheckSuite(), gitHubApp,
                new List<PullRequest>());
        }
    }
}
