// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using NUnit.Framework;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Octokit;
using ProductConstructionService.ScenarioTests.Helpers;

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

        const string sourceRepoUri = $"https://github.com/{TestRepository.TestOrg}/{TestRepository.TestArcadeName}";
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

        await CreateTestChannelAsync(testChannelName);
        var sub = await CreateSubscriptionAsync(testChannelName, TestRepository.TestArcadeName, TestRepository.TestRepo2Name, targetBranch, "none", TestRepository.TestOrg, targetIsAzDo: targetAzDO);
        Build build =
            await CreateBuildAsync(GetRepoUrl(TestRepository.TestOrg, TestRepository.TestArcadeName), sourceBranch, TestRepository.ArcadeTestRepoCommit, sourceBuildNumber, sourceAssets);

        await using IAsyncDisposable _ = await AddBuildToChannelAsync(build.Id, testChannelName);

        using TemporaryDirectory repo = targetAzDO
            ? await CloneAzDoRepositoryAsync(TestRepository.TestRepo2Name)
            : await CloneRepositoryAsync(TestRepository.TestRepo2Name);

        using (ChangeDirectory(repo.Directory))
        {
            await RunGitAsync("checkout", "-b", targetBranch);
            await RunDarcAsync(includeConfigurationRepoParams: false, "add-dependency",
                "--name", DependencyFileManager.ArcadeSdkPackageName,
                "--type", "toolset",
                "--repo", sourceRepoUri);
            await RunGitAsync("commit", "-am", "Add dependencies.");
            await using IAsyncDisposable __ = await PushGitBranchAsync("origin", targetBranch);
            await TriggerSubscriptionAsync(sub);

            var expectedTitle = $"[{targetBranch}] Update dependencies from {TestRepository.TestOrg}/{TestRepository.TestArcadeName}";
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

                var dependencies = await RunDarcAsync(includeConfigurationRepoParams: false, "get-dependencies");
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

                using TemporaryDirectory arcadeRepo = await CloneRepositoryAsync(TestRepository.TestOrg, TestRepository.TestArcadeName);
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

    // This test verifies that we're able to flow eng/common and global.json during Arcade SDK updates from the VMR
    [Test]
    public async Task ArcadeSdkVmrUpdate_E2E()
    {
        var testChannelName = GetTestChannelName();
        var targetBranch = GetTestBranchName();
        var vmrBranch = GetTestBranchName();

        const string sourceRepo = "maestro-test-vmr";
        const string sourceRepoUri = $"https://github.com/{TestRepository.TestOrg}/{sourceRepo}";
        const string sourceBranch = "dependencyflow-tests";
        const string newArcadeSdkVersion = "2.1.0";
        const string arcadeEngCommonPath = "src/arcade/eng/common";
        const string engCommonFile = "file.txt";
        const string arcadeGlobalJsonPath = "src/arcade/global.json";

        const string globalJsonFile = """
            {
              "tools": {
                "dotnet": "2.2.203"
              },
              "msbuild-sdks": {
                "Microsoft.DotNet.Arcade.Sdk": "1.0.0-beta.19251.6",
                "Microsoft.DotNet.Helix.Sdk": "2.0.0-beta.19251.6"
              }
            }
            """;
        var sourceBuildNumber = _random.Next(int.MaxValue).ToString();

        List<AssetData> sourceAssets =
        [
            new AssetData(true)
            {
                Name = DependencyFileManager.ArcadeSdkPackageName,
                Version = newArcadeSdkVersion
            }
        ];

        await CreateTestChannelAsync(testChannelName);
        var sub = await CreateSubscriptionAsync(testChannelName, sourceRepo, TestRepository.TestRepo1Name, targetBranch, "none", TestRepository.TestOrg);

        TemporaryDirectory testRepoFolder = await CloneRepositoryAsync(TestRepository.TestRepo1Name);
        TemporaryDirectory vmrFolder = await CloneRepositoryAsync(TestRepository.VmrTestRepoName);

        await CreateTargetBranchAndExecuteTest(targetBranch, testRepoFolder.Directory, async () =>
        {
            using (ChangeDirectory(vmrFolder.Directory))
            {
                await using (await CheckoutBranchAsync(vmrBranch))
                {
                    // Create an arcade repo in the VMR
                    Directory.CreateDirectory(arcadeEngCommonPath);
                    await File.WriteAllTextAsync(Path.Combine(arcadeEngCommonPath, engCommonFile), "test");
                    await File.WriteAllTextAsync(arcadeGlobalJsonPath, globalJsonFile);

                    await GitAddAllAsync();
                    await GitCommitAsync("Add arcade files");

                    var repoSha = (await GitGetCurrentSha()).TrimEnd();
                    Build build = await CreateBuildAsync(GetRepoUrl(TestRepository.TestOrg, sourceRepo), sourceBranch, repoSha, sourceBuildNumber, sourceAssets);

                    await using IAsyncDisposable _ = await AddBuildToChannelAsync(build.Id, testChannelName);

                    // and push it to GH
                    await using (await PushGitBranchAsync("origin", vmrBranch))
                    {
                        await TriggerSubscriptionAsync(sub);

                        var expectedTitle = $"[{targetBranch}] Update dependencies from {TestRepository.TestOrg}/{sourceRepo}";

                        PullRequest pullRequest = await WaitForPullRequestAsync(TestRepository.TestRepo1Name, targetBranch);

                        await using (CleanUpPullRequestAfter(TestParameters.GitHubTestOrg, TestRepository.TestRepo1Name, pullRequest))
                        {
                            IReadOnlyList<PullRequestFile> files = await GitHubApi.PullRequest.Files(TestParameters.GitHubTestOrg, TestRepository.TestRepo1Name, pullRequest.Number);

                            files.Should().Contain(files => files.FileName == "global.json");
                            files.Should().Contain(files => files.FileName == "eng/common/file.txt");
                        }
                    }
                }
            }
        });
            
    }
}
