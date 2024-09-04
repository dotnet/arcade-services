// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Maestro.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Parallelizable]
internal class ScenarioTests_DefaultChannels : MaestroScenarioTestBase
{
    private readonly string _repoName = TestRepository.TestRepo1Name;
    private readonly string _branchName;
    private readonly string _branchNameWithRefsHeads;
    private TestParameters _parameters;

    public ScenarioTests_DefaultChannels()
    {
        _branchName = GetTestBranchName();
        _branchNameWithRefsHeads = $"refs/heads/{_branchName}";
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
        string repoUrl = GetGitHubRepoUrl(_repoName);

        string testChannelName1 = GetTestChannelName();
        string testChannelName2 = GetTestChannelName();

        await using (AsyncDisposableValue<string> channel1 = await CreateTestChannelAsync(testChannelName1))
        {
            await using (AsyncDisposableValue<string> channel2 = await CreateTestChannelAsync(testChannelName2))
            {
                await AddDefaultTestChannelAsync(testChannelName1, repoUrl, _branchNameWithRefsHeads);
                await AddDefaultTestChannelAsync(testChannelName2, repoUrl, _branchNameWithRefsHeads);

                string defaultChannels = await GetDefaultTestChannelsAsync(repoUrl, _branchName);
                defaultChannels.Should().Contain(testChannelName1, $"{testChannelName1} is not a default channel");
                defaultChannels.Should().Contain(testChannelName2, $"{testChannelName2} is not a default channel");

                await DeleteDefaultTestChannelAsync(testChannelName1, repoUrl, _branchName);
                await DeleteDefaultTestChannelAsync(testChannelName2, repoUrl, _branchName);

                defaultChannels = await GetDefaultTestChannelsAsync(repoUrl, _branchName);
                defaultChannels.Should().NotContain(testChannelName1, $"{testChannelName1} was not deleted from default channels");
                defaultChannels.Should().NotContain(testChannelName2, $"{testChannelName2} was not deleted from default channels");
            }
        }
    }
}
