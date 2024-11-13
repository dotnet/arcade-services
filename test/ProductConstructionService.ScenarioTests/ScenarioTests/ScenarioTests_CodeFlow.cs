// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using NUnit.Framework;
using ProductConstructionService.Client.Models;

namespace ProductConstructionService.ScenarioTests.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Category("CodeFlow")]
internal class ScenarioTests_CodeFlow : ScenarioTestBase
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

    [Test]
    public async Task Darc_ForwardFlowTest()
    {
        using TestParameters parameters = await TestParameters.GetAsync();
        SetTestParameters(parameters);

        var channelName = GetTestChannelName();
        var branchName = GetTestBranchName();
        var productRepo = GetGitHubRepoUrl(TestRepository.TestRepo1Name);

        await using AsyncDisposableValue<string> testChannel = await CreateTestChannelAsync(channelName);

        await using AsyncDisposableValue<string> subscriptionId = await CreateSourceEnabledSubscriptionAsync(
            channelName,
            TestRepository.TestRepo1Name,
            TestRepository.VmrTestRepoName,
            "main",
            UpdateFrequency.None.ToString(),
            "maestro-auth-test",
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

                    // Now trigger the subscription
                    await TriggerSubscriptionAsync(subscriptionId.Value);

                    await CheckCodeFlowGitHubPullRequest(TestRepository.TestRepo1Name, TestRepository.VmrTestRepoName, "main", [TestFileName], TestFilePatches);
                }
            }
        }
    }
}
