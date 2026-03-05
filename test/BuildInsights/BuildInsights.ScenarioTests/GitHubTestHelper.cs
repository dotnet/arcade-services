// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.GitHub.Authentication;
using NUnit.Framework;
using Octokit;
using Octokit.Helpers;
using static BuildInsights.ScenarioTests.ScenarioTestConfiguration;

namespace BuildInsights.ScenarioTests;

public class GitHubTestHelper
{
    public static async Task<TestGitHubInformation> CreateTestPr(
        string owner,
        string repoName,
        string testBranchName,
        string pullRequestTitle,
        string pullRequestHeadName)
    {
        TestContext.WriteLine("Make branch");
        Reference targetBranch = await ExponentialRetry.RetryAsync(
            function: () => GitHubApi.Git.Reference.Get(owner, repoName, "heads/main"),
            logRetry: ex => TestContext.WriteLine(ex.Message),
            isRetryable: _ => true);
        Reference testBranch = await ExponentialRetry.RetryAsync(
            function: () => GitHubApi.Git.Reference.CreateBranch(owner, repoName, testBranchName, targetBranch),
            logRetry: ex => TestContext.WriteLine(ex.Message),
            isRetryable: _ => true);
        TestContext.WriteLine($"Made branch {owner}/{repoName}/{testBranchName} form {targetBranch}");

        try
        {
            (PullRequest response, Commit commit) = await PreparePullRequest(owner, repoName, pullRequestTitle, pullRequestHeadName, targetBranch, testBranch);
            TestContext.WriteLine($"Created PR {response.Number}");

            return new TestGitHubInformation(owner, repoName, response.Number, testBranch.Ref, commit);
        }
        catch
        {
            await CleanUpBranch(owner, repoName, testBranch);
            throw;
        }
    }

    private static async Task CleanUpBranch(string owner, string repoName, Reference testBranch)
    {
        try
        {
            TestContext.WriteLine($"Delete branch {testBranch.Ref} in {owner}/{repoName}");
            await GitHubApi.Git.Reference.Delete(owner, repoName, testBranch.Ref);
        }
        catch
        {
        }
    }

    private static async Task<(PullRequest response, Commit commit)> PreparePullRequest(
        string owner,
        string repoName,
        string pullRequestTitle,
        string pullRequestHeadName,
        Reference targetBranch,
        Reference testBranch)
    {
        TreeResponse currentTree = await GitHubApi.Git.Tree.Get(owner, repoName, targetBranch.Ref);

        var newTree = new NewTree
        {
            BaseTree = currentTree.Sha
        };

        var fakeItemBlob = new NewTreeItem
        {
            Path = "fake_item.txt",
            Mode = Octokit.FileMode.File,
            Type = TreeType.Blob,
            Content = "Lorem ipsum dolor sit amet"
        };
        newTree.Tree.Add(fakeItemBlob);

        TestContext.WriteLine("Create the new tree in the repo");
        TreeResponse treeResponse = await GitHubApi.Git.Tree.Create(owner, repoName, newTree);

        TestContext.WriteLine("Make commit");
        var newCommit = new NewCommit("commit to run a PR", treeResponse.Sha, testBranch.Object.Sha);
        Commit commit = await GitHubApi.Git.Commit.Create(owner, repoName, newCommit);

        var update = new ReferenceUpdate(commit.Sha);
        await GitHubApi.Git.Reference.Update(owner, repoName, testBranch.Ref, update);
        TestContext.WriteLine($"Created commit hash: {commit.Sha}");

        TestContext.WriteLine("Make PR");
        NewPullRequest request = new(pullRequestTitle, pullRequestHeadName, "main");
        PullRequest pr = await GitHubApi.PullRequest.Create(owner, repoName, request);
        return (pr, commit);
    }
}

public record TestGitHubInformation(
    string Owner,
    string RepoName,
    int PullRequestId,
    string BranchReference,
    Commit Commit) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        try
        {
            TestContext.WriteLine($"Close the Pull Request: {PullRequestId}");
            var prUpdate = new PullRequestUpdate
            {
                State = ItemState.Closed
            };
            await GitHubApi.PullRequest.Update(Owner, RepoName, PullRequestId, prUpdate);
        }
        catch (NotFoundException)
        {
            TestContext.WriteLine("Ignore. We assume the PR was manually cleaned up and don't want to fail the test because of that.");
        }
    }
}

public class TestGitHubClientFactory : IGitHubApplicationClientFactory
{
    public IGitHubClient CreateGitHubAppClient() => GitHubApi;
    public IGitHubClient CreateGitHubAppClient(string name) => CreateGitHubAppClient();
    public Task<IGitHubClient> CreateGitHubClientAsync(string owner, string repo) => Task.FromResult(CreateGitHubAppClient());
}
