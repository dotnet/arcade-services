using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [Category("PostDeployment")]
    public class ScenarioTests_Channels : MaestroScenarioTestBase
    {
        private TestParameters _parameters;

        [TearDown]
        public Task DisposeAsync()
        {
            _parameters.Dispose();
            return Task.CompletedTask;
        }

        [Test]
        public async Task ArcadeChannels_EndToEnd()
        {
            _parameters = await TestParameters.GetAsync();
            SetTestParameters(_parameters);

            // Create a new channel
            string testChannelName = $"Test Channel End to End {Environment.MachineName}";

            await using (AsyncDisposableValue<string> channel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
            {
                // Get the channel and make sure it's there
                string returnedChannel = await GetTestChannelsAsync().ConfigureAwait(false);
                StringAssert.Contains(testChannelName, returnedChannel, "Channel was not created or could not be retrieved");

                // Delete the channel
                await DeleteTestChannelAsync(testChannelName).ConfigureAwait(false);

                // Get the channel and make sure it was deleted
                string returnedChannel2 = await GetTestChannelsAsync().ConfigureAwait(false);
                StringAssert.DoesNotContain(testChannelName, returnedChannel2, "Channel was not deleted");
            }
        }
    }
}
