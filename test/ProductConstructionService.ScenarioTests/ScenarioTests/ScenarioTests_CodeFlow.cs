// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using NUnit.Framework;
using ProductConstructionService.Client.Models;

#nullable enable

namespace ProductConstructionService.ScenarioTests.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Category("CodeFlow")]
internal class ScenarioTests_CodeFlow : CodeFlowScenarioTestBase
{
    private const string TestFileName = "newFile.txt";

    private static Dictionary<string, string> TestFilesContent = new()
    {
        { TestFileName, "test" }
    };

    private static Dictionary<string, string> TestFilePatches = new()
    {
        { TestFileName, "@@ -0,0 +1 @@\n+test\n\\ No newline at end of file" }
    };

    [TearDown]
    public void Dispose()
    {
        _parameters.Dispose();
    }

    [SetUp]
    public async Task SetUp()
    {
        _parameters = await TestParameters.GetAsync();
        ConfigureDarcArgs();
    }

    [Test]
    public async Task Darc_ForwardFlowTest()
    {
        var channelName = GetTestChannelName();
        var branchName = GetTestBranchName();
        var productRepo = GetGitHubRepoUrl(TestRepository.TestRepo1Name);
        var targetBranchName = "main";

        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(channelName);

        await using AsyncDisposableValue<string> subscriptionId = await CreateSourceEnabledSubscriptionAsync(
            channelName,
            TestRepository.TestRepo1Name,
            TestRepository.VmrTestRepoName,
            targetBranchName,
            UpdateFrequency.None.ToString(),
            _parameters.GitHubTestOrg,
            [],
            targetDirectory: TestRepository.TestRepo1Name);

        TemporaryDirectory reposFolder = await CloneRepositoryAsync(TestRepository.TestRepo1Name);
        var newFilePath = Path.Combine(reposFolder.Directory, TestFileName);

        using (ChangeDirectory(reposFolder.Directory))
        {
            await using (await CheckoutBranchAsync(branchName))
            {
                // Make a change in a product repo
                TestContext.WriteLine("Making code changes to the repo");
                using FileStream newFileStream = File.Create(newFilePath);
                {
                    using StreamWriter newFileWriter = new(newFileStream);
                    newFileWriter.Write(TestFilesContent[TestFileName]);
                }

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
    }

    [Test]
    public async Task Darc_BackwardFlowTest()
    {
        var channelName = GetTestChannelName();
        var branchName = GetTestBranchName();
        var productRepo = GetGitHubRepoUrl(TestRepository.TestRepo1Name);
        var targetBranchName = "master";

        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(channelName);

        await using AsyncDisposableValue<string> subscriptionId = await CreateSourceEnabledSubscriptionAsync(
            channelName,
            TestRepository.VmrTestRepoName,
            TestRepository.TestRepo1Name,
            targetBranchName,
            UpdateFrequency.None.ToString(),
            _parameters.GitHubTestOrg,
            [],
            sourceDirectory: TestRepository.TestRepo1Name);

        // Clone the VMR
        TemporaryDirectory reposFolder = await CloneRepositoryAsync(TestRepository.VmrTestRepoName);
        var newFilePath = Path.Combine(reposFolder.Directory, "src", TestRepository.TestRepo1Name, TestFileName);

        using (ChangeDirectory(reposFolder.Directory))
        {
            await using (await CheckoutBranchAsync(branchName))
            {
                // Make a change in the VMR
                TestContext.WriteLine("Making code changes in the VMR");
                using FileStream newFileStream = File.Create(newFilePath);
                {
                    using StreamWriter newFileWriter = new(newFileStream);
                    newFileWriter.Write(TestFilesContent[TestFileName]);
                }

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
                    await CheckBackwardFlowGitHubPullRequest(TestRepository.VmrTestRepoName, TestRepository.TestRepo1Name, targetBranchName, [TestFileName], TestFilePatches, repoSha);
                }
            }
        }
    }
}
