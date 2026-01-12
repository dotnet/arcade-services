// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using NUnit.Framework;
using ProductConstructionService.ScenarioTests.Helpers;

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

        await CreateTestChannelAsync(testChannelName);

        // Get the channel and make sure it's there
        var returnedChannel = await GetTestChannelsAsync();
        returnedChannel.Should().Contain(testChannelName, "Channel was not created or could not be retrieved");

        // Delete the channel
        await DeleteTestChannelAsync(testChannelName);

        // Get the channel and make sure it was deleted
        var returnedChannel2 = await GetTestChannelsAsync();
        returnedChannel2.Should().NotContain(testChannelName, "Channel was not deleted");
    }
}
