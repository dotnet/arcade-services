// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;

namespace Maestro.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
internal class ScenarioTests_SdkUpdate : MaestroScenarioTestBase
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

        string testChannelName = "Test Channel " + _random.Next(int.MaxValue);
        const string sourceRepo = "arcade";
        const string sourceRepoUri = "https://github.com/dotnet/arcade";
        const string sourceBranch = "dependencyflow-tests";
        const string sourceCommit = "0b36b99e29b1751403e23cfad0a7dff585818051";
        const string newArcadeSdkVersion = "2.1.0";
        var sourceBuildNumber = _random.Next(int.MaxValue).ToString();

        ImmutableList<AssetData> sourceAssets = ImmutableList.Create<AssetData>()
            .Add(new AssetData(true)
            {
                Name = DependencyFileManager.ArcadeSdkPackageName,
                Version = newArcadeSdkVersion,
            });
        var targetRepo = "maestro-test2";
        var targetBranch = "test/" + _random.Next(int.MaxValue).ToString();

        await using AsyncDisposableValue<string> channel =
            await CreateTestChannelAsync(testChannelName).ConfigureAwait(false);
        await using AsyncDisposableValue<string> sub =
            await CreateSubscriptionAsync(testChannelName, sourceRepo, targetRepo, targetBranch, "none", targetIsAzDo: targetAzDO);
        Build build =
            await CreateBuildAsync(GetRepoUrl("dotnet", sourceRepo), sourceBranch, sourceCommit, sourceBuildNumber, sourceAssets);

        await using IAsyncDisposable _ = await AddBuildToChannelAsync(build.Id, testChannelName);

        using TemporaryDirectory repo = targetAzDO
            ? await CloneAzDoRepositoryAsync(targetRepo)
            : await CloneRepositoryAsync(targetRepo);

        using (ChangeDirectory(repo.Directory))
        {
            await RunGitAsync("checkout", "-b", targetBranch).ConfigureAwait(false);
            await RunDarcAsync("add-dependency",
                "--name", DependencyFileManager.ArcadeSdkPackageName,
                "--type", "toolset",
                "--repo", sourceRepoUri);
            await RunGitAsync("commit", "-am", "Add dependencies.");
            await using IAsyncDisposable ___ = await PushGitBranchAsync("origin", targetBranch);
            await TriggerSubscriptionAsync(sub.Value);

            string expectedTitle = $"[{targetBranch}] Update dependencies from dotnet/arcade";
            DependencyDetail expectedDependency = new()
            {
                Name = DependencyFileManager.ArcadeSdkPackageName,
                Version = newArcadeSdkVersion,
                RepoUri = sourceRepoUri,
                Commit = sourceCommit,
                Type = DependencyType.Toolset,
                Pinned = false,
            };

            if (targetAzDO)
            {
                await CheckAzDoPullRequest(
                    expectedTitle,
                    targetRepo,
                    targetBranch,
                    [expectedDependency],
                    repo.Directory,
                    isCompleted: false,
                    isUpdated: false,
                    expectedFeeds: null,
                    notExpectedFeeds: null);
                return;
            }

            Octokit.PullRequest pr = await WaitForPullRequestAsync(targetRepo, targetBranch);
            pr.Title.Should().BeEquivalentTo(expectedTitle);

            await CheckoutRemoteRefAsync(pr.MergeCommitSha);

            string dependencies = await RunDarcAsync("get-dependencies");
            string[] dependencyLines = dependencies.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            dependencyLines.Should().BeEquivalentTo(
                [
                  $"Name:             {DependencyFileManager.ArcadeSdkPackageName}",
                  $"Version:          {newArcadeSdkVersion}",
                  $"Repo:             {sourceRepoUri}",
                  $"Commit:           {sourceCommit}",
                   "Type:             Toolset",
                   "Pinned:           False",
                ]);

            using TemporaryDirectory arcadeRepo = await CloneRepositoryAsync("dotnet", sourceRepo);
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
