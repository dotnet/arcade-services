using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [Category("PostDeployment")]
    public class ScenarioTests_DefaultChannels : MaestroScenarioTestBase
    {
        private readonly string repoName = TestRepository.TestRepo1Name;
        private readonly string branchName;
        private readonly string branchNameWithRefsHeads;
        private TestParameters _parameters;

        public ScenarioTests_DefaultChannels()
        {
            branchName = $"ChannelTestBranch_{Environment.MachineName}";
            branchNameWithRefsHeads = $"refs/heads/{branchName}";
        }

        [SetUp]
        public async Task InitializeAsync()
        {
            _parameters = await TestParameters.GetAsync();
            SetTestParameters(_parameters);
        }

        [TearDown]
        public Task DisposeAsync()
        {
            _parameters.Dispose();
            return Task.CompletedTask;
        }

        [Test]
        public async Task ArcadeChannels_DefaultChannels()
        {
            string repoUrl = GetRepoUrl(repoName);

            string testChannelName1 = $"TestChannelDefault1_{Environment.MachineName}";
            string testChannelName2 = $"TestChannelDefault2_{Environment.MachineName}";

            await using (AsyncDisposableValue<string> channel1 = await CreateTestChannelAsync(testChannelName1).ConfigureAwait(false))
            {
                await using (AsyncDisposableValue<string> channel2 = await CreateTestChannelAsync(testChannelName2).ConfigureAwait(false))
                {
                    await AddDefaultTestChannelAsync(testChannelName1, repoUrl, branchNameWithRefsHeads).ConfigureAwait(false);
                    await AddDefaultTestChannelAsync(testChannelName2, repoUrl, branchNameWithRefsHeads).ConfigureAwait(false);

                    string defaultChannels = await GetDefaultTestChannelsAsync(repoUrl, branchName).ConfigureAwait(false);
                    StringAssert.Contains(testChannelName1, defaultChannels, $"{testChannelName1} is not a default channel");
                    StringAssert.Contains(testChannelName2, defaultChannels, $"{testChannelName2} is not a default channel");

                    await DeleteDefaultTestChannelAsync(testChannelName1, repoUrl, branchName);
                    await DeleteDefaultTestChannelAsync(testChannelName2, repoUrl, branchName);

                    defaultChannels = await GetDefaultTestChannelsAsync(repoUrl, branchName).ConfigureAwait(false);
                    StringAssert.DoesNotContain(testChannelName1, defaultChannels, $"{testChannelName1} was not deleted from default channels");
                    StringAssert.DoesNotContain(testChannelName2, defaultChannels, $"{testChannelName2} was not deleted from default channels");
                }
            }
        }
    }
}
