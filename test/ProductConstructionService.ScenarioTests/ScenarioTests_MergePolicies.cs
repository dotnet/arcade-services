// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;
using Build = Microsoft.DotNet.Maestro.Client.Models.Build;

namespace ProductConstructionService.ScenarioTests;

[TestFixture]
[Category("PostDeployment")]
[Parallelizable]
internal class ScenarioTests_MergePolicies : MaestroScenarioTestBase
{
    private TestParameters _parameters;
    private readonly Random _random = new();
    private const string SourceRepo = "maestro-test1";
    private const string TargetRepo = "maestro-test2";

    [SetUp]
    public async Task InitializeAsync()
    {
        _parameters = await TestParameters.GetAsync(useNonPrimaryEndpoint: true);
        SetTestParameters(_parameters);
    }

    [TearDown]
    public Task DisposeAsync()
    {
        _parameters.Dispose();
        return Task.CompletedTask;
    }

    private string GetTestChannelName()
    {
        return "Test Channel " + _random.Next(int.MaxValue);
    }

    private string GetTargetBranch()
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
        ImmutableList<AssetData> sourceAssets = ImmutableList.Create<AssetData>()
            .Add(new AssetData(true)
            {
                Name = "Foo",
                Version = "1.1.0",
            })
            .Add(new AssetData(true)
            {
                Name = "Bar",
                Version = "2.1.0",
            });

        TestContext.WriteLine($"Creating test channel {testChannelName}");
        await using AsyncDisposableValue<string> channel = await CreateTestChannelAsync(testChannelName);

        TestContext.WriteLine($"Adding a subscription from ${sourceRepo} to ${targetRepo}");
        await using AsyncDisposableValue<string> sub = await CreateSubscriptionAsync(testChannelName, sourceRepo, targetRepo, targetBranch, "none", "maestro-auth-test", additionalOptions: args);

        TestContext.WriteLine("Set up build for intake into target repository");
        Build build = await CreateBuildAsync(sourceRepoUri, sourceBranch, sourceCommit, sourceBuildNumber, sourceAssets);
        await using IAsyncDisposable _ = await AddBuildToChannelAsync(build.Id, testChannelName);

        TestContext.WriteLine("Cloning target repo to prepare the target branch");
        using TemporaryDirectory repo = await CloneRepositoryAsync(targetRepo);
        using (ChangeDirectory(repo.Directory))
        {
            await RunGitAsync("checkout", "-b", targetBranch);
            TestContext.WriteLine("Adding dependencies to target repo");
            await AddDependenciesToLocalRepo(repo.Directory, "Foo", sourceRepoUri);
            await AddDependenciesToLocalRepo(repo.Directory, "Bar", sourceRepoUri);

            TestContext.WriteLine("Pushing branch to remote");
            await RunGitAsync("commit", "-am", "Add dependencies.");
            await using IAsyncDisposable ___ = await PushGitBranchAsync("origin", targetBranch);

            await TriggerSubscriptionAsync(sub.Value);

            TestContext.WriteLine($"Waiting on PR to be opened in ${targetRepoUri}");
            var testResult = await CheckGithubPullRequestChecks(targetRepo, targetBranch);
            testResult.Should().BeTrue();
        }
    }
}
