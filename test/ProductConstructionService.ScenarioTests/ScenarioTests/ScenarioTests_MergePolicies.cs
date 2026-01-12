// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using NUnit.Framework;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using ProductConstructionService.ScenarioTests.Helpers;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Parallelizable]
internal class ScenarioTests_MergePolicies : ScenarioTestBase
{
    private readonly Random _random = new();
    private const string SourceRepo = "maestro-test1";
    private const string TargetRepo = "maestro-test2";

    private string GetTestChannelName()
    {
        return "Test Channel " + _random.Next(int.MaxValue);
    }

    private static string GetTargetBranch()
    {
        return Guid.NewGuid().ToString();
    }

    [Test]
    public async Task Darc_GitHubFlow_AutoMerge_GithubChecks_AllChecksSuccessful()
    {
        var testChannelName = GetTestChannelName();
        var targetBranch = GetTargetBranch();

        await AutoMergeFlowTestBase(TargetRepo, SourceRepo, targetBranch, testChannelName, ["--all-checks-passed"]);
    }

    [Test]
    public async Task Darc_GitHubFlow_AutoMerge_GithubChecks_ValidateCoherencyCheck()
    {
        var testChannelName = GetTestChannelName();
        var targetBranch = GetTargetBranch();

        await AutoMergeFlowTestBase(TargetRepo, SourceRepo, targetBranch, testChannelName, ["--validate-coherency"]);
    }

    [Test]
    public async Task Darc_GitHubFlow_AutoMerge_GithubChecks_Standard()
    {
        var testChannelName = GetTestChannelName();
        var targetBranch = GetTargetBranch();

        await AutoMergeFlowTestBase(TargetRepo, SourceRepo, targetBranch, testChannelName, ["--standard-automerge"]);
    }

    [Test]
    public async Task Darc_GitHubFlow_AutoMerge_GithubChecks_NoRequestedChanges()
    {
        var testChannelName = GetTestChannelName();
        var targetBranch = GetTargetBranch();

        await AutoMergeFlowTestBase(TargetRepo, SourceRepo, targetBranch, testChannelName, ["--no-requested-changes"]);
    }

    public async Task AutoMergeFlowTestBase(string targetRepo, string sourceRepo, string targetBranch, string testChannelName, List<string> args)
    {
        var targetRepoUri = GetGitHubRepoUrl(targetRepo);
        var sourceRepoUri = GetGitHubRepoUrl(sourceRepo);
        var sourceBranch = "dependencyflow-tests";
        var sourceCommit = "0b36b99e29b1751403e23cfad0a7dff585818051";
        var sourceBuildNumber = _random.Next(int.MaxValue).ToString();
        List<AssetData> sourceAssets =
        [
            new AssetData(true)
            {
                Name = GetUniqueAssetName("Foo"),
                Version = "1.1.0",
            },
            new AssetData(true)
            {
                Name = GetUniqueAssetName("Bar"),
                Version = "2.1.0",
            },
        ];

        TestContext.WriteLine($"Creating test channel {testChannelName}");
        await CreateTestChannelAsync(testChannelName);

        TestContext.WriteLine($"Adding a subscription from ${sourceRepo} to ${targetRepo}");
        var sub = await CreateSubscriptionAsync(testChannelName, sourceRepo, targetRepo, targetBranch, "none", "maestro-auth-test", additionalOptions: args);

        TestContext.WriteLine("Set up build for intake into target repository");
        var build = await CreateBuildAsync(sourceRepoUri, sourceBranch, sourceCommit, sourceBuildNumber, sourceAssets);
        await using IAsyncDisposable _ = await AddBuildToChannelAsync(build.Id, testChannelName);

        TestContext.WriteLine("Cloning target repo to prepare the target branch");
        using TemporaryDirectory repo = await CloneRepositoryAsync(targetRepo);
        using (ChangeDirectory(repo.Directory))
        {
            await RunGitAsync("checkout", "-b", targetBranch);
            TestContext.WriteLine("Adding dependencies to target repo");
            await AddDependenciesToLocalRepo(repo.Directory, sourceAssets.First().Name, sourceRepoUri);
            await AddDependenciesToLocalRepo(repo.Directory, sourceAssets.Last().Name, sourceRepoUri);

            TestContext.WriteLine("Pushing branch to remote");
            await RunGitAsync("commit", "-am", "Add dependencies.");
            await using IAsyncDisposable ___ = await PushGitBranchAsync("origin", targetBranch);

            await TriggerSubscriptionAsync(sub);

            TestContext.WriteLine($"Waiting on PR to be opened in ${targetRepoUri}");
            var testResult = await CheckGithubPullRequestChecks(targetRepo, targetBranch);
            testResult.Should().BeTrue();
        }
    }
}
