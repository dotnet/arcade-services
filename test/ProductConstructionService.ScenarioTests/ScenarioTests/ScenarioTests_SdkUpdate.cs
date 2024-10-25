// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using NUnit.Framework;
using ProductConstructionService.Client.Models;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[NonParallelizable]
internal class ScenarioTests_SdkUpdate : ScenarioTestBase
{
    private TestParameters _parameters;
    private readonly Random _random = new();

    [TearDown]
    public Task DisposeAsync()
    {
        _parameters.Dispose();
        return Task.CompletedTask;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ArcadeSdkUpdate_E2E(bool targetAzDO)
    {
        _parameters = await TestParameters.GetAsync();
        SetTestParameters(_parameters);

        var testChannelName = "Test Channel " + _random.Next(int.MaxValue);
        const string sourceOrg = "maestro-auth-test";
        const string sourceRepo = "arcade";
        const string sourceRepoUri = $"https://github.com/{sourceOrg}/{sourceRepo}";
        const string sourceBranch = "dependencyflow-tests";
        const string sourceCommit = "f3d51d2c9af2a3eb046fa54c5acdef9fb37db172";
        const string newArcadeSdkVersion = "2.1.0";
        var sourceBuildNumber = _random.Next(int.MaxValue).ToString();

        ImmutableList<AssetData> sourceAssets =
        [
            new AssetData(true)
            {
                Name = DependencyFileManager.ArcadeSdkPackageName,
                Version = newArcadeSdkVersion
            }
        ];
        var targetRepo = "maestro-test2";
        var targetBranch = "test/" + _random.Next(int.MaxValue).ToString();

        await using AsyncDisposableValue<string> channel =
            await CreateTestChannelAsync(testChannelName);
        await using AsyncDisposableValue<string> sub =
            await CreateSubscriptionAsync(testChannelName, sourceRepo, targetRepo, targetBranch, "none", sourceOrg: sourceOrg, targetIsAzDo: targetAzDO);
        Build build =
            await CreateBuildAsync(GetRepoUrl(sourceOrg, sourceRepo), sourceBranch, sourceCommit, sourceBuildNumber, sourceAssets);

        await using IAsyncDisposable _ = await AddBuildToChannelAsync(build.Id, testChannelName);

        using TemporaryDirectory repo = targetAzDO
            ? await CloneAzDoRepositoryAsync(targetRepo)
            : await CloneRepositoryAsync(targetRepo);

        using (ChangeDirectory(repo.Directory))
        {
            await RunGitAsync("checkout", "-b", targetBranch);
            await RunDarcAsync("add-dependency",
                "--name", DependencyFileManager.ArcadeSdkPackageName,
                "--type", "toolset",
                "--repo", sourceRepoUri);
            await RunGitAsync("commit", "-am", "Add dependencies.");
            await using IAsyncDisposable ___ = await PushGitBranchAsync("origin", targetBranch);
            await TriggerSubscriptionAsync(sub.Value);

            var expectedTitle = $"[{targetBranch}] Update dependencies from {sourceOrg}/{sourceRepo}";
            DependencyDetail expectedDependency = new()
            {
                Name = DependencyFileManager.ArcadeSdkPackageName,
                Version = newArcadeSdkVersion,
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Toolset,
                Pinned = false,
            };

            string prHead;
            if (targetAzDO)
            {
                prHead = await CheckAzDoPullRequest(
                    expectedTitle,
                    targetRepo,
                    targetBranch,
                    [expectedDependency],
                    repo.Directory,
                    isCompleted: false,
                    isUpdated: false,
                    expectedFeeds: null,
                    notExpectedFeeds: null);
            }
            else
            {
                Octokit.PullRequest pr = await WaitForPullRequestAsync(targetRepo, targetBranch);
                pr.Title.Should().BeEquivalentTo(expectedTitle);
                prHead = pr.Head.Ref;
            }

            await CheckoutRemoteRefAsync(prHead);

            var dependencies = await RunDarcAsync("get-dependencies");
            var dependencyLines = dependencies.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            dependencyLines.Should().BeEquivalentTo(
                [
                  $"Name:             {DependencyFileManager.ArcadeSdkPackageName}",
                  $"Version:          {newArcadeSdkVersion}",
                  $"Repo:             {sourceRepoUri}",
                  $"Commit:           {sourceCommit}",
                   "Type:             Toolset",
                   "Pinned:           False",
                ]);

            using TemporaryDirectory arcadeRepo = await CloneRepositoryAsync(sourceOrg, sourceRepo);
            using (ChangeDirectory(arcadeRepo.Directory))
            {
                await CheckoutRemoteRefAsync(sourceCommit);
            }

            var arcadeFiles = Directory.EnumerateFileSystemEntries(Path.Join(arcadeRepo.Directory, "eng", "common"),
                    "*", SearchOption.AllDirectories)
                .Select(s => s.Substring(arcadeRepo.Directory.Length))
                .ToHashSet();
            var repoFiles = Directory.EnumerateFileSystemEntries(Path.Join(repo.Directory, "eng", "common"), "*",
                    SearchOption.AllDirectories)
                .Select(s => s.Substring(repo.Directory.Length))
                .ToHashSet();

            arcadeFiles.Should().BeEquivalentTo(repoFiles);
        }
    }
}
