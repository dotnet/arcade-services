// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;

#nullable enable

namespace ProductConstructionService.ScenarioTests.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Category("CodeFlow")]
internal class ScenarioTests_CodeFlow : CodeFlowScenarioTestBase
{
    private const string TestFileName = "newFile.txt";

    private static readonly Dictionary<string, string> TestFilesContent = new()
    {
        { TestFileName, "test" }
    };

    private static readonly Dictionary<string, string> TestFilePatches = new()
    {
        { TestFileName, "@@ -0,0 +1 @@\n+test\n\\ No newline at end of file" }
    };

    [Test]
    public async Task Vmr_ForwardFlowTest()
    {
        var channelName = GetTestChannelName();
        var branchName = GetTestBranchName();
        var productRepo = GetGitHubRepoUrl(TestRepository.TestRepo1Name);
        var targetBranchName = GetTestBranchName();

        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(channelName);

        await using AsyncDisposableValue<string> subscriptionId = await CreateForwardFlowSubscriptionAsync(
            channelName,
            TestRepository.TestRepo1Name,
            TestRepository.VmrTestRepoName,
            targetBranchName,
            UpdateFrequency.None.ToString(),
            TestParameters.GitHubTestOrg,
            targetDirectory: TestRepository.TestRepo1Name);

        TemporaryDirectory vmrDirectory = await CloneRepositoryAsync(TestRepository.VmrTestRepoName);
        TemporaryDirectory reposFolder = await CloneRepositoryAsync(TestRepository.TestRepo1Name);
        var newFilePath = Path.Combine(reposFolder.Directory, TestFileName);

        await CreateTargetBranchAndExecuteTest(targetBranchName, vmrDirectory, async () =>
        {
            using (ChangeDirectory(reposFolder.Directory))
            {
                await using (await CheckoutBranchAsync(branchName))
                {
                    // Make a change in a product repo
                    TestContext.WriteLine("Making code changes to the repo");
                    await File.WriteAllTextAsync(newFilePath, TestFilesContent[TestFileName]);

                    await GitAddAllAsync();
                    await GitCommitAsync("Add new file");

                    // Push it to github
                    await using (await PushGitBranchAsync("origin", branchName))
                    {
                        var repoSha = (await GitGetCurrentSha()).TrimEnd();

                        // Create a new build from the commit and add it to a channel
                        Build build = await CreateBuildAsync(
                            GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                            branchName,
                            repoSha,
                            "1",
                            []);

                        TestContext.WriteLine("Adding build to channel");
                        await AddBuildToChannelAsync(build.Id, channelName);

                        TestContext.WriteLine("Triggering the subscription");
                        // Now trigger the subscription
                        await TriggerSubscriptionAsync(subscriptionId.Value);

                        TestContext.WriteLine("Verifying subscription PR");
                        await CheckForwardFlowGitHubPullRequest(TestRepository.TestRepo1Name, TestRepository.VmrTestRepoName, targetBranchName, [TestFileName], TestFilePatches);
                    }
                }
            }
        });
    }

    [Test]
    public async Task Vmr_BackwardFlowTest()
    {
        var channelName = GetTestChannelName();
        var branchName = GetTestBranchName();
        var productRepo = GetGitHubRepoUrl(TestRepository.TestRepo1Name);
        var targetBranchName = GetTestBranchName();

        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(channelName);

        await using AsyncDisposableValue<string> subscriptionId = await CreateBackwardFlowSubscriptionAsync(
            channelName,
            TestRepository.VmrTestRepoName,
            TestRepository.TestRepo1Name,
            targetBranchName,
            UpdateFrequency.None.ToString(),
            TestParameters.GitHubTestOrg,
            sourceDirectory: TestRepository.TestRepo1Name);

        TemporaryDirectory testRepoFolder = await CloneRepositoryAsync(TestRepository.TestRepo1Name);
        TemporaryDirectory reposFolder = await CloneRepositoryAsync(TestRepository.VmrTestRepoName);
        var newFilePath = Path.Combine(reposFolder.Directory, "src", TestRepository.TestRepo1Name, TestFileName);

        await CreateTargetBranchAndExecuteTest(targetBranchName, testRepoFolder, async () =>
        {
            using (ChangeDirectory(reposFolder.Directory))
            {
                await using (await CheckoutBranchAsync(branchName))
                {
                    // Make a change in the VMR
                    TestContext.WriteLine("Making code changes in the VMR");
                    File.WriteAllText(newFilePath, TestFilesContent[TestFileName]);

                    await GitAddAllAsync();
                    await GitCommitAsync("Add new file");

                    // Push it to github
                    await using (await PushGitBranchAsync("origin", branchName))
                    {
                        var repoSha = (await GitGetCurrentSha()).TrimEnd();

                        // Create a new build from the commit and add it to a channel
                        Build build = await CreateBuildAsync(
                            GetGitHubRepoUrl(TestRepository.VmrTestRepoName),
                            branchName,
                            repoSha,
                            "1",
                            // We might want to add some assets here to mimic what happens in the VMR
                            []);

                        TestContext.WriteLine("Adding build to channel");
                        await AddBuildToChannelAsync(build.Id, channelName);

                        TestContext.WriteLine("Triggering the subscription");
                        // Now trigger the subscription
                        await TriggerSubscriptionAsync(subscriptionId.Value);

                        TestContext.WriteLine("Verifying subscription PR");
                        await CheckBackwardFlowGitHubPullRequest(TestRepository.VmrTestRepoName, TestRepository.TestRepo1Name, targetBranchName, [TestFileName], TestFilePatches, repoSha, build.Id);
                    }
                }
            }
        });
    }

    [Test]
    public async Task Vmr_BatchedForwardFlowTest()
    {
        var channelName = GetTestChannelName();
        var branch1Name = GetTestBranchName();
        var branch2Name = GetTestBranchName();
        var productRepo = GetGitHubRepoUrl(TestRepository.TestRepo1Name);
        var targetBranchName = GetTestBranchName();

        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(channelName);

        await using AsyncDisposableValue<string> subscription1Id = await CreateForwardFlowSubscriptionAsync(
            channelName,
            TestRepository.TestRepo1Name,
            TestRepository.VmrTestRepoName,
            targetBranchName,
            UpdateFrequency.None.ToString(),
            TestParameters.GitHubTestOrg,
            targetDirectory: TestRepository.TestRepo1Name,
            batchable: true);

        await using AsyncDisposableValue<string> subscription2Id = await CreateForwardFlowSubscriptionAsync(
            channelName,
            TestRepository.TestRepo2Name,
            TestRepository.VmrTestRepoName,
            targetBranchName,
            UpdateFrequency.None.ToString(),
            TestParameters.GitHubTestOrg,
            targetDirectory: TestRepository.TestRepo2Name,
            batchable: true);

        TemporaryDirectory vmrDirectory = await CloneRepositoryAsync(TestRepository.VmrTestRepoName);
        TemporaryDirectory repo1 = await CloneRepositoryAsync(TestRepository.TestRepo1Name);
        TemporaryDirectory repo2 = await CloneRepositoryAsync(TestRepository.TestRepo2Name);
        var newFile1Path = Path.Combine(repo1.Directory, TestFileName);
        var newFile2Path = Path.Combine(repo2.Directory, TestFileName);

        await CreateTargetBranchAndExecuteTest(targetBranchName, vmrDirectory, async () =>
        {
            using (ChangeDirectory(repo1.Directory))
            await using (await CheckoutBranchAsync(branch1Name))
            {
                // Make a change in a product repo
                TestContext.WriteLine("Making code changes to the repo");
                await File.WriteAllTextAsync(newFile1Path, TestFilesContent[TestFileName]);

                await GitAddAllAsync();
                await GitCommitAsync("Add new file");

                // Push it to github
                await using (await PushGitBranchAsync("origin", branch1Name))
                {
                    using (ChangeDirectory(repo2.Directory))
                    await using (await CheckoutBranchAsync(branch2Name))
                    {
                        // Make a change in a product repo
                        TestContext.WriteLine("Making code changes to the repo");
                        await File.WriteAllTextAsync(newFile2Path, TestFilesContent[TestFileName]);

                        await GitAddAllAsync();
                        await GitCommitAsync("Add new file");

                        // Push it to github
                        await using (await PushGitBranchAsync("origin", branch2Name))
                        {
                            var repoSha = (await GitGetCurrentSha()).TrimEnd();

                            // Create a new build from the commit and add it to a channel
                            Build build1 = await CreateBuildAsync(
                                GetGitHubRepoUrl(TestRepository.TestRepo1Name),
                                branch1Name,
                                repoSha,
                                "B1",
                                []);

                            Build build2 = await CreateBuildAsync(
                                GetGitHubRepoUrl(TestRepository.TestRepo2Name),
                                branch2Name,
                                repoSha,
                                "B2",
                                []);

                            TestContext.WriteLine("Adding builds to channel");
                            await AddBuildToChannelAsync(build1.Id, channelName);
                            await AddBuildToChannelAsync(build2.Id, channelName);

                            TestContext.WriteLine("Triggering the subscriptions");
                            // Now trigger the subscriptions
                            await TriggerSubscriptionAsync(subscription1Id.Value);
                            await TriggerSubscriptionAsync(subscription2Id.Value);

                            TestContext.WriteLine("Verifying subscription PR");
                            await CheckForwardFlowGitHubPullRequest(TestRepository.TestRepo1Name, TestRepository.VmrTestRepoName, targetBranchName, [TestFileName], TestFilePatches);
                        }
                    }
                }
            }
        });
    }
}
