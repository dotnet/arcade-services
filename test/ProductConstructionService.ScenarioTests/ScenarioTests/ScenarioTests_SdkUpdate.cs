// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using NUnit.Framework;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[NonParallelizable]
internal class ScenarioTests_SdkUpdate : ScenarioTestBase
{
    private readonly Random _random = new();

    [TestCase(false)]
    [TestCase(true)]
    public async Task ArcadeSdkUpdate_E2E(bool targetAzDO)
    {
        var testChannelName = GetTestChannelName();
        var targetBranch = GetTestBranchName();

        const string sourceRepo = "arcade";
        const string sourceRepoUri = $"https://github.com/{TestRepository.TestOrg}/{sourceRepo}";
        const string sourceBranch = "dependencyflow-tests";
        const string newArcadeSdkVersion = "2.1.0";
        var sourceBuildNumber = _random.Next(int.MaxValue).ToString();

        List<AssetData> sourceAssets =
        [
            new AssetData(true)
            {
                Name = DependencyFileManager.ArcadeSdkPackageName,
                Version = newArcadeSdkVersion
            }
        ];

        await using AsyncDisposableValue<string> channel =
            await CreateTestChannelAsync(testChannelName);
        await using AsyncDisposableValue<string> sub =
            await CreateSubscriptionAsync(testChannelName, sourceRepo, TestRepository.TestRepo2Name, targetBranch, "none", TestRepository.TestOrg, targetIsAzDo: targetAzDO);
        Build build =
            await CreateBuildAsync(GetRepoUrl(TestRepository.TestOrg, sourceRepo), sourceBranch, TestRepository.ArcadeTestRepoCommit, sourceBuildNumber, sourceAssets);

        await using IAsyncDisposable _ = await AddBuildToChannelAsync(build.Id, testChannelName);

        using TemporaryDirectory repo = targetAzDO
            ? await CloneAzDoRepositoryAsync(TestRepository.TestRepo2Name)
            : await CloneRepositoryAsync(TestRepository.TestRepo2Name);

        using (ChangeDirectory(repo.Directory))
        {
            await RunGitAsync("checkout", "-b", targetBranch);
            await RunDarcAsync("add-dependency",
                "--name", DependencyFileManager.ArcadeSdkPackageName,
                "--type", "toolset",
                "--repo", sourceRepoUri);
            await RunGitAsync("commit", "-am", "Add dependencies.");
            await using IAsyncDisposable __ = await PushGitBranchAsync("origin", targetBranch);
            await TriggerSubscriptionAsync(sub.Value);

            var expectedTitle = $"[{targetBranch}] Update dependencies from {TestRepository.TestOrg}/{sourceRepo}";
            DependencyDetail expectedDependency = new()
            {
                Name = DependencyFileManager.ArcadeSdkPackageName,
                Version = newArcadeSdkVersion,
                RepoUri = sourceRepoUri,
                Commit = TestRepository.ArcadeTestRepoCommit,
                Type = DependencyType.Toolset,
                Pinned = false,
            };

            string prHead;
            IAsyncDisposable cleanUp;
            if (targetAzDO)
            {
                prHead = await CheckAzDoPullRequest(
                    expectedTitle,
                    TestRepository.TestRepo2Name,
                    targetBranch,
                    [expectedDependency],
                    repo.Directory,
                    isCompleted: false,
                    isUpdated: false,
                    cleanUp: false,
                    expectedFeeds: null,
                    notExpectedFeeds: null);

                cleanUp = AsyncDisposable.Create(async () =>
                {
                    await TestParameters.AzDoClient.DeleteBranchAsync(GetAzDoRepoUrl(TestRepository.TestRepo2Name), prHead);
                });
            }
            else
            {
                Octokit.PullRequest pr = await WaitForPullRequestAsync(TestRepository.TestRepo2Name, targetBranch);
                pr.Title.Should().BeEquivalentTo(expectedTitle);
                prHead = pr.Head.Ref;

                cleanUp = CleanUpPullRequestAfter(TestRepository.TestOrg, TestRepository.TestRepo2Name, pr);
            }

            await using (cleanUp)
            {
                await CheckoutRemoteRefAsync(prHead);

                var dependencies = await RunDarcAsync("get-dependencies");
                var dependencyLines = dependencies.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
                dependencyLines.Should().BeEquivalentTo(
                    [
                      $"Name:             {DependencyFileManager.ArcadeSdkPackageName}",
                      $"Version:          {newArcadeSdkVersion}",
                      $"Repo:             {sourceRepoUri}",
                      $"Commit:           {TestRepository.ArcadeTestRepoCommit}",
                       "Type:             Toolset",
                       "Pinned:           False",
                    ]);

                using TemporaryDirectory arcadeRepo = await CloneRepositoryAsync(TestRepository.TestOrg, sourceRepo);
                using (ChangeDirectory(arcadeRepo.Directory))
                {
                    await CheckoutRemoteRefAsync(TestRepository.ArcadeTestRepoCommit);
                }

                var arcadeFiles = Directory.EnumerateFileSystemEntries(Path.Join(arcadeRepo.Directory, "eng", "common"),
                        "*", SearchOption.AllDirectories)
                    .Select(s => s.Substring(arcadeRepo.Directory.Length))
                    .ToHashSet();
                var repoFiles = Directory.EnumerateFileSystemEntries(Path.Join(repo.Directory, "eng", "common"),
                        "*", SearchOption.AllDirectories)
                    .Select(s => s.Substring(repo.Directory.Length))
                    .ToHashSet();

                arcadeFiles.Should().BeEquivalentTo(repoFiles);
            }
        }
    }

    [Test]
    public async Task ArcadeSdkVmrUpdate_E2E()
    {
        var testChannelName = GetTestChannelName();
        var targetBranch = GetTestBranchName();

        const string sourceRepo = "maestro-test-vmr";
        const string sourceRepoUri = $"https://github.com/{TestRepository.TestOrg}/{sourceRepo}";
        const string sourceBranch = "dependencyflow-tests";
        const string fakeArcadeCommit = "77eb350818ed386bd88ca8725ec5b5e0d17bab74";
        const string newArcadeSdkVersion = "2.1.0";
        var sourceBuildNumber = _random.Next(int.MaxValue).ToString();

        List<AssetData> sourceAssets =
        [
            new AssetData(true)
            {
                Name = DependencyFileManager.ArcadeSdkPackageName,
                Version = newArcadeSdkVersion
            }
        ];

        await using AsyncDisposableValue<string> channel = await CreateTestChannelAsync(testChannelName);
        await using AsyncDisposableValue<string> sub =
            await CreateSubscriptionAsync(testChannelName, sourceRepo, TestRepository.TestRepo1Name, targetBranch, "none", TestRepository.TestOrg);
        Build build =
            await CreateBuildAsync(GetRepoUrl(TestRepository.TestOrg, sourceRepo), sourceBranch, fakeArcadeCommit, sourceBuildNumber, sourceAssets);

        await using IAsyncDisposable _ = await AddBuildToChannelAsync(build.Id, testChannelName);

        TemporaryDirectory testRepoFolder = await CloneRepositoryAsync(TestRepository.TestRepo1Name);

        using (ChangeDirectory(testRepoFolder.Directory))
        {
            await using (await CheckoutBranchAsync(targetBranch))
            {
                // and push it to GH
                await using (await PushGitBranchAsync("origin", targetBranch))
                {
                    await TriggerSubscriptionAsync(sub.Value);
                }
            }
        }
    }
}
