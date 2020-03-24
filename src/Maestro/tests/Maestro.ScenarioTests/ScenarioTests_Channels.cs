using System.Threading.Tasks;
using NUnit.Framework;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    public class ScenarioTests_Channels : MaestroScenarioTestBase
    {
        private readonly string repoName = "maestro-test1";
        private readonly string branchName;
        private readonly string branchNameWithRefsHeads;

        public ScenarioTests_Channels()
        {
            branchName = "ChannelTestBranch";
            branchNameWithRefsHeads = $"refs/heads/{branchName}";
        }

        [Test]
        public async Task ArcadeChannels_EndToEnd()
        {
            // Create a new channel
            string testChannelName = "Test Channel End to End";

            await using (AsyncDisposableValue<string> channel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
            {
                // Get the channel and make sure it's there
                string returnedChannel = await GetTestChannelsAsync().ConfigureAwait(false);
                StringAssert.Contains(testChannelName, returnedChannel);

                // Delete the channel
                await DeleteTestChannelAsync(testChannelName).ConfigureAwait(false);

                // Get the channel and make sure it was deleted
                string returnedChannel2 = await GetTestChannelsAsync().ConfigureAwait(false);
                StringAssert.DoesNotContain(testChannelName, returnedChannel2);
            }
        }

        [Test]
        public async Task ArcadeChannels_DefaultChannels()
        {
            string repoUrl = GetRepoUrl(repoName);

            string testChannelName1 = "Test Channel Default 1";
            string testChannelName2 = "Test Channel Default 2";

            await using (AsyncDisposableValue<string> channel1 = await CreateTestChannelAsync(testChannelName1).ConfigureAwait(false))
            {
                await using (AsyncDisposableValue<string> channel2 = await CreateTestChannelAsync(testChannelName2).ConfigureAwait(false))
                {
                    await AddDefaultTestChannelAsync(testChannelName1, repoUrl, branchNameWithRefsHeads).ConfigureAwait(false);
                    await AddDefaultTestChannelAsync(testChannelName2, repoUrl, branchNameWithRefsHeads).ConfigureAwait(false);

                    string defaultChannels = await GetDefaultTestChannelsAsync(repoUrl, branchName).ConfigureAwait(false);
                    StringAssert.Contains(testChannelName1, defaultChannels);
                    StringAssert.Contains(testChannelName2, defaultChannels);

                    await DeleteDefaultTestChannelAsync(testChannelName1, repoUrl, branchName);
                    await DeleteDefaultTestChannelAsync(testChannelName2, repoUrl, branchName);

                    defaultChannels = await GetDefaultTestChannelsAsync(repoUrl, branchName).ConfigureAwait(false);
                    StringAssert.DoesNotContain(testChannelName1, defaultChannels);
                    StringAssert.DoesNotContain(testChannelName2, defaultChannels);
                }
            }
        }
    }
}
