// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Shouldly;
using NUnit.Framework;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Parallelizable]
internal class ScenarioTests_Channels : ScenarioTestBase
{
    [Test]
    public async Task ArcadeChannels_EndToEnd()
    {
        // Create a new channel
        var testChannelName = GetTestChannelName();

        await using (AsyncDisposableValue<string> channel = await CreateTestChannelAsync(testChannelName))
        {
            // Get the channel and make sure it's there
            var returnedChannel = await GetTestChannelsAsync();
            returnedChannel.ShouldContain(testChannelName, "Channel was not created or could not be retrieved");

            // Delete the channel
            await DeleteTestChannelAsync(testChannelName);

            // Get the channel and make sure it was deleted
            var returnedChannel2 = await GetTestChannelsAsync();
            returnedChannel2.ShouldNotContain(testChannelName, "Channel was not deleted");
        }
    }
}
