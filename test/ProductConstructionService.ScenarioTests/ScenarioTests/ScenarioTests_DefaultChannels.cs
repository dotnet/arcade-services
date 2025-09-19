// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
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

        await CreateTestChannelAsync(testChannelName1);
        await CreateTestChannelAsync(testChannelName2);

        await AddDefaultTestChannelAsync(testChannelName1, repoUrl, _branchNameWithRefsHeads);
        await AddDefaultTestChannelAsync(testChannelName2, repoUrl, _branchNameWithRefsHeads);

        var defaultChannels = await GetDefaultTestChannelsAsync(repoUrl, _branchName);
        defaultChannels.Should().Contain(testChannelName1, $"{testChannelName1} is not a default channel");
        defaultChannels.Should().Contain(testChannelName2, $"{testChannelName2} is not a default channel");

        await DeleteDefaultTestChannelAsync(testChannelName1, repoUrl, _branchName);
        await DeleteDefaultTestChannelAsync(testChannelName2, repoUrl, _branchName);

        defaultChannels = await GetDefaultTestChannelsAsync(repoUrl, _branchName);
        defaultChannels.Should().NotContain(testChannelName1, $"{testChannelName1} was not deleted from default channels");
        defaultChannels.Should().NotContain(testChannelName2, $"{testChannelName2} was not deleted from default channels");
    }

    private async Task AddDefaultTestChannelAsync(string testChannelName, string repoUri, string branchName)
    {
        await RunDarcAsync(
        [
            "add-default-channel",
            "--channel", testChannelName,
            "--repo", repoUri,
            "--branch", branchName,
            ..GetConfigurationManagementDarcArgs(),
        ]);
        await RefreshConfiguration();
    }

    private static async Task<string> GetDefaultTestChannelsAsync(string repoUri, string branch)
    {
        return await RunDarcAsync("get-default-channels", "--source-repo", repoUri, "--branch", branch);
    }

    private async Task DeleteDefaultTestChannelAsync(string testChannelName, string repoUri, string branch)
    {
        await RunDarcAsync(
        [
            "delete-default-channel",
            "--channel", testChannelName,
            "--repo", repoUri,
            "--branch", branch,
            ..GetConfigurationManagementDarcArgs(),
        ]);
        await RefreshConfiguration();
    }
}
