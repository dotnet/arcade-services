// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using NUnit.Framework;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Parallelizable]
internal class ScenarioTests_Channels : MaestroScenarioTestBase
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
        var testChannelName = $"Test Channel End to End {Environment.MachineName}";

        await using (AsyncDisposableValue<string> channel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false))
        {
            // Get the channel and make sure it's there
            var returnedChannel = await GetTestChannelsAsync().ConfigureAwait(false);
            returnedChannel.Should().Contain(testChannelName, "Channel was not created or could not be retrieved");

            // Delete the channel
            await DeleteTestChannelAsync(testChannelName).ConfigureAwait(false);

            // Get the channel and make sure it was deleted
            var returnedChannel2 = await GetTestChannelsAsync().ConfigureAwait(false);
            returnedChannel2.Should().NotContain(testChannelName, "Channel was not deleted");
        }
    }
}
