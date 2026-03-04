// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using NUnit.Framework;
using Octokit;
using Octokit.Helpers;
using static BuildInsights.ScenarioTests.TestParameters;

namespace BuildInsights.ScenarioTests;

public class GitHubTestHelper
{
    public const int StagingBuildAnalysisAppId = 216601;

    public static async Task<TestGitHubInformation> CreateScenarioTestEnvironment(
        string originalOwner,
        string botOwnerName,
        string testBranchName,
        string pullRequestTitle,
        string pullRequestHeadName,
        string repoName,
        long originalRepoId)
    {
        PullRequest? response = null;
        TestContext.WriteLine($"Create fork of {originalOwner}/{repoName}");
        Repository fork = await GitHubApi.Repository.Forks.Create(originalOwner, repoName, new NewRepositoryFork());
        long forkedRepoId = fork.Id;
        string forkedRepoName = fork.Name;

        try
        {
            TestContext.WriteLine($"Created fork {fork.Name} with id {fork.Id}");

            TestContext.WriteLine("Make branch");
            Reference targetBranch = await ExponentialRetry.RetryAsync(
                function: () => GitHubApi.Git.Reference.Get(forkedRepoId, "heads/main"),
                logRetry: ex => TestContext.WriteLine(ex.Message),
                isRetryable: _ => true
            );
            Reference testBranch = await ExponentialRetry.RetryAsync(
                function: () => client.Git.Reference.CreateBranch(botOwnerName, forkedRepoName, testBranchName, targetBranch),
                logRetry: ex => TestContext.WriteLine(ex.Message),
                isRetryable: _ => true
            );
            TestContext.WriteLine($"Made branch {botOwnerName}/{forkedRepoName}/{testBranchName} form {targetBranch}");

            TestContext.WriteLine("Get tree");
            TreeResponse currentTree = await GitHubApi.Git.Tree.Get(botOwnerName, forkedRepoName, targetBranch.Ref);

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
            TreeResponse treeResponse = await GitHubApi.Git.Tree.Create(botOwnerName, forkedRepoName, newTree);

            TestContext.WriteLine("Make commit");
            var newCommit = new NewCommit("commit to run a PR", treeResponse.Sha, testBranch.Object.Sha);
            Commit commit = await GitHubApi.Git.Commit.Create(botOwnerName, forkedRepoName, newCommit);

            var update = new ReferenceUpdate(commit.Sha);
            await GitHubApi.Git.Reference.Update(botOwnerName, forkedRepoName, testBranch.Ref, update);
            TestContext.WriteLine($"Created commit hash: {commit.Sha}");

            TestContext.WriteLine("Make PR");
            var pr = new NewPullRequest(pullRequestTitle, pullRequestHeadName, "main");
            response = await GitHubApi.PullRequest.Create(originalRepoId, pr);
            TestContext.WriteLine($"Created PR {response.Number}");

            return new TestGitHubInformation(response.Number, testBranch.Ref, forkedRepoId, commit);
        }
        catch
        {
            await CleanUpEnvironment(originalOwner, botOwnerName, forkedRepoName, response?.Number ?? default, forkedRepoId);
            throw;
        }
    }

    public static async Task CleanUpForks(string repoName, string botOwnerName)
    {
        IReadOnlyList<Repository> repos = await GitHubApi.Repository.GetAllForCurrent();
        foreach (Repository repo in repos.Where(r => r.Name.Contains(repoName)))
        {
            try
            {
                Repository oldFork = await GitHubApi.Repository.Get(botOwnerName, repo.Name);
                TestContext.WriteLine($"Deleting fork name: {oldFork.FullName} with id: {oldFork.Id}");
                await GitHubApi.Repository.Delete(oldFork.Id);
                await Task.Delay(TimeSpan.FromSeconds(15));
            }
            catch (NotFoundException)
            {
                TestContext.WriteLine("Ignore. We assume the fork was manually cleaned up or is in process of being deleted" +
                                      "and don't want to fail the test because of that.");
            }
        }
    }

    public static async Task CleanUpEnvironment(
        string originalOwner,
        string botOwnerName,
        string repoName,
        int pullRequestId = default,
        long forkRepoId = default)
    {
        try
        {
            if (pullRequestId != default)
            {
                TestContext.WriteLine($"Close the Pull Request: {pullRequestId}");
                var prUpdate = new PullRequestUpdate
                {
                    State = ItemState.Closed
                };
                await GitHubApi.PullRequest.Update(originalOwner, repoName, pullRequestId, prUpdate);
            }

            if (forkRepoId != default)
            {
                TestContext.WriteLine($"Delete fork: {forkRepoId}");
                await GitHubApi.Repository.Delete(forkRepoId);
            }
        }
        catch (NotFoundException)
        {
            TestContext.WriteLine("Ignore. We assume the PR was manually cleaned up and don't want to fail the test because of that.");
        }
    }
}

public record TestGitHubInformation(
    int PullRequestId,
    string BranchReference,
    long ForkedRepoId,
    Commit Commit);
