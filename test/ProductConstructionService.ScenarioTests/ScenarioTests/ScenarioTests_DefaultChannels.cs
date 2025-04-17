// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Shouldly;
using NUnit.Framework;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Parallelizable]
internal class ScenarioTests_DefaultChannels : ScenarioTestBase
{
    private readonly string _repoName = TestRepository.TestRepo1Name;
    private readonly string _branchName;
    private readonly string _branchNameWithRefsHeads;

    public ScenarioTests_DefaultChannels()
    {
        _branchName = GetTestBranchName();
        _branchNameWithRefsHeads = $"refs/heads/{_branchName}";
    }

    [Test]
    public async Task ArcadeChannels_DefaultChannels()
    {
        var repoUrl = GetGitHubRepoUrl(_repoName);

        var testChannelName1 = GetTestChannelName();
        var testChannelName2 = GetTestChannelName();

        await using (AsyncDisposableValue<string> channel1 = await CreateTestChannelAsync(testChannelName1))
        {
            await using (AsyncDisposableValue<string> channel2 = await CreateTestChannelAsync(testChannelName2))
            {
                await AddDefaultTestChannelAsync(testChannelName1, repoUrl, _branchNameWithRefsHeads);
                await AddDefaultTestChannelAsync(testChannelName2, repoUrl, _branchNameWithRefsHeads);

                var defaultChannels = await GetDefaultTestChannelsAsync(repoUrl, _branchName);
                defaultChannels.ShouldContain(testChannelName1, customMessage: $"{testChannelName1} is not a default channel");
                defaultChannels.ShouldContain(testChannelName2, customMessage: $"{testChannelName2} is not a default channel");

                await DeleteDefaultTestChannelAsync(testChannelName1, repoUrl, _branchName);
                await DeleteDefaultTestChannelAsync(testChannelName2, repoUrl, _branchName);

                defaultChannels = await GetDefaultTestChannelsAsync(repoUrl, _branchName);
                defaultChannels.ShouldNotContain(testChannelName1, customMessage: $"{testChannelName1} was not deleted from default channels");
                defaultChannels.ShouldNotContain(testChannelName2, customMessage: $"{testChannelName2} was not deleted from default channels");
            }
        }
    }
}
