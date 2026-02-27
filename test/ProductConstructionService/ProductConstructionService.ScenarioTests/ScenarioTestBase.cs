// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NuGet.Configuration;
using NUnit.Framework;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using ProductConstructionService.ScenarioTests.Helpers;

[assembly: Parallelizable(ParallelScope.Fixtures)]

#nullable enable
namespace ProductConstructionService.ScenarioTests;

internal abstract partial class ScenarioTestBase
{
    private static readonly TimeSpan WAIT_DELAY = TimeSpan.FromSeconds(25);

    private string _packageNameSalt = null!;
    private string _testNamespace = null!;
    private string[] _configRepoDarcParams = [];
    private const string ScenarioTestBaseBranch = "origin/scenario-test";
    private bool _namespaceIngested = false;
    protected TemporaryDirectory _temporaryDirectory = null!;

    // We need this for tests where we have multiple updates
    private readonly Dictionary<long, DateTimeOffset> _lastUpdatedPrTimes = [];

    protected static IProductConstructionServiceApi PcsApi => TestParameters.PcsApi;

    protected static Octokit.GitHubClient GitHubApi => TestParameters.GitHubApi;

    protected static AzureDevOpsClient AzDoClient => TestParameters.AzDoClient;

    [SetUp]
    public async Task BaseSetup()
    {
        _packageNameSalt = Guid.NewGuid().ToString().Substring(0, 8);
        _testNamespace = $"pcs-scenario-{Guid.NewGuid().ToString().Substring(0, 8)}";
        _temporaryDirectory = await CloneAzDoRepositoryAsync(TestRepository.MaestroConfigurationRepoName);
        _configRepoDarcParams = [
            "--configuration-repository", _temporaryDirectory.Directory,
            "--configuration-base-branch", ScenarioTestBaseBranch,
            "--configuration-branch", _testNamespace,
            "--no-pr"
        ];
        _namespaceIngested = false;
        await RunGitAsync("-C", _temporaryDirectory.Directory, "config", "user.email", $"{TestParameters.GitHubUser}@test.com");
        await RunGitAsync("-C", _temporaryDirectory.Directory, "config", "user.name", TestParameters.GitHubUser);
    }

    [TearDown]
    public async Task BaseTearDown()
    {
        if (_namespaceIngested)
        {
            await PcsApi.Ingestion.DeleteNamespaceAsync(_testNamespace, saveChanges: true);
            await RunGitAsync("-C", _temporaryDirectory.Directory, "checkout", ScenarioTestBaseBranch);
            await RunGitAsync("-C", _temporaryDirectory.Directory, "branch", "-D", _testNamespace); 
        }

        _temporaryDirectory.Dispose();
    }

    protected async Task<Octokit.PullRequest> WaitForPullRequestAsync(string targetRepo, string targetBranch)
    {
        Octokit.Repository repo = await GitHubApi.Repository.Get(TestParameters.GitHubTestOrg, targetRepo);

        var attempts = 20;
        while (attempts-- > 0)
        {
            IReadOnlyList<Octokit.PullRequest> prs = await GitHubApi.PullRequest.GetAllForRepository(repo.Id, new Octokit.PullRequestRequest
            {
                Base = targetBranch,
            });

            if (prs.Count == 1)
            {
                // We use this method when we're creating the PR, and when we're fetching the updated PR
                // We only want to set the Creation time when we're creating it
                if (!_lastUpdatedPrTimes.ContainsKey(prs[0].Id))
                {
                    _lastUpdatedPrTimes[prs[0].Id] = prs[0].CreatedAt;
                }
                return prs[0];
            }

            if (prs.Count > 1)
            {
                throw new ScenarioTestException($"More than one pull request found in {targetRepo} targeting {targetBranch}");
            }

            await Task.Delay(WAIT_DELAY);
        }

        throw new ScenarioTestException($"No pull request was created in {targetRepo} targeting {targetBranch}");
    }

    protected async Task<Octokit.PullRequest> WaitForUpdatedPullRequestAsync(string targetRepo, string targetBranch, int attempts = 30)
    {
        Octokit.Repository repo = await GitHubApi.Repository.Get(TestParameters.GitHubTestOrg, targetRepo);
        Octokit.PullRequest pr = await WaitForPullRequestAsync(targetRepo, targetBranch);

        while (attempts-- > 0)
        {
            pr = await GitHubApi.PullRequest.Get(repo.Id, pr.Number);

            if (_lastUpdatedPrTimes[pr.Id] != pr.UpdatedAt)
            {
                _lastUpdatedPrTimes[pr.Id] = pr.UpdatedAt;
                return pr;
            }

            await Task.Delay(WAIT_DELAY);
        }

        var waitTime = WAIT_DELAY * attempts;
        throw new ScenarioTestException(
            $"The created pull request for {targetRepo} targeting {targetBranch} was not updated with subsequent subscriptions after {waitTime.Minutes} minutes");
    }

    protected static async Task MergePullRequestAsync(string targetRepo, Octokit.PullRequest pr)
    {
        Octokit.MergePullRequest mergePullRequest = new()
        {
            MergeMethod = Octokit.PullRequestMergeMethod.Squash
        };

        await GitHubApi.PullRequest.Merge(TestParameters.GitHubTestOrg, targetRepo, pr.Number, mergePullRequest);
    }

    private async Task<bool> WaitForMergedPullRequestAsync(string targetRepo, string targetBranch, int attempts = 30)
    {
        Octokit.Repository repo = await GitHubApi.Repository.Get(TestParameters.GitHubTestOrg, targetRepo);
        Octokit.PullRequest pr = await WaitForPullRequestAsync(targetRepo, targetBranch);

        while (attempts-- > 0)
        {
            TestContext.WriteLine($"Starting check for merge, attempts remaining {attempts}");
            pr = await GitHubApi.PullRequest.Get(repo.Id, pr.Number);

            if (pr.Merged == true)
            {
                return true;
            }

            await Task.Delay(WAIT_DELAY);
        }

        var waitTime = WAIT_DELAY * attempts;
        throw new ScenarioTestException(
            $"The created pull request for {targetRepo} targeting {targetBranch} was not merged within {waitTime.Minutes} minutes");
    }

    private static async Task<int> GetAzDoPullRequestIdAsync(string targetRepoName, string targetBranch)
    {
        var searchBaseUrl = GetAzDoRepoUrl(targetRepoName);
        IEnumerable<int> prs = new List<int>();

        var attempts = 30;
        while (attempts-- > 0)
        {
            try
            {
                prs = await SearchPullRequestsAsync(searchBaseUrl, targetBranch);
            }
            catch (HttpRequestException ex)
            {
                // Returning a 404 is expected before the PR has been created
                var logger = new NUnitLogger();
                logger.LogInformation("Searching for AzDo pull requests returned an error: {errorMessage}", ex.Message);
            }

            if (prs.Count() == 1)
            {
                return prs.FirstOrDefault();
            }

            if (prs.Count() > 1)
            {
                throw new ScenarioTestException($"More than one pull request found in {targetRepoName} targeting {targetBranch}");
            }

            await Task.Delay(WAIT_DELAY);
        }

        var waitTime = WAIT_DELAY * attempts;
        throw new ScenarioTestException(
            $"No pull request was created in {searchBaseUrl} targeting {targetBranch} within {waitTime.Minutes} minutes");
    }

    private static async Task<IEnumerable<int>> SearchPullRequestsAsync(string repoUri, string targetPullRequestBranch)
    {
        (var accountName, var projectName, var repoName) = AzureDevOpsClient.ParseRepoUri(repoUri);
        var query = new StringBuilder();

        AzureDevOpsPrStatus prStatus = AzureDevOpsPrStatus.Active;
        query.Append($"searchCriteria.status={prStatus.ToString().ToLower()}");
        query.Append($"&searchCriteria.targetRefName=refs/heads/{targetPullRequestBranch}");

        JObject content = await TestParameters.AzDoClient.ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get,
            accountName,
            projectName,
            $"_apis/git/repositories/{repoName}/pullrequests?{query}",
            new NUnitLogger()
            );

        IEnumerable<int> prs = content.Value<JArray>("value")!.Select(r => r.Value<int>("pullRequestId"));

        return prs;
    }

    private static async Task<AsyncDisposableValue<PullRequest>> GetAzDoPullRequestAsync(int pullRequestId, string targetRepoName, string targetBranch, bool isUpdated, bool cleanUp, string? expectedPRTitle = null)
    {
        var repoUri = GetAzDoRepoUrl(targetRepoName);
        (var accountName, var projectName, var repoName) = AzureDevOpsClient.ParseRepoUri(repoUri);
        var apiBaseUrl = GetAzDoApiRepoUrl(targetRepoName);

        if (string.IsNullOrEmpty(expectedPRTitle))
        {
            throw new Exception($"{nameof(expectedPRTitle)} must be defined for AzDo PRs that require an update");
        }

        for (var tries = 30; tries > 0; tries--)
        {
            PullRequest pr = await AzDoClient.GetPullRequestAsync($"{apiBaseUrl}/pullRequests/{pullRequestId}");
            var trimmedTitle = Regex.Replace(pr.Title, @"\s+", " ");

            if (!isUpdated || trimmedTitle == expectedPRTitle)
            {
                return AsyncDisposableValue.Create(pr, async () =>
                {
                    TestContext.WriteLine($"Cleaning up pull request {pr.Title}");

                    try
                    {
                        await TestParameters.AzDoClient.ExecuteAzureDevOpsAPIRequestAsync(
                                HttpMethod.Patch,
                                accountName,
                                projectName,
                                $"_apis/git/repositories/{targetRepoName}/pullrequests/{pullRequestId}",
                                new NUnitLogger(),
                                "{ \"status\" : \"abandoned\"}",
                                logFailure: false);

                        if (cleanUp)
                        {
                            await TestParameters.AzDoClient.DeleteBranchAsync(repoUri, pr.HeadBranch);
                        }
                    }
                    catch
                    {
                        // If this throws it means that it was cleaned up by a different clean up method first
                    }
                });
            }

            await Task.Delay(WAIT_DELAY);
        }

        var waitTime = WAIT_DELAY * 30;
        throw new ScenarioTestException(
            $"The created pull request for {targetRepoName} targeting {targetBranch} was not updated with subsequent subscriptions within {waitTime.Minutes} minutes");
    }

    protected async Task CheckBatchedGitHubPullRequest(
        string targetBranch,
        string[] sourceRepoNames,
        string targetRepoName,
        List<DependencyDetail> expectedDependencies,
        string repoDirectory,
        bool cleanUp)
    {
        var repoNames = sourceRepoNames
            .Select(name => $"{TestParameters.GitHubTestOrg}/{name}")
            .OrderBy(s => s);

        var expectedPRTitle = $"[{targetBranch}] Update dependencies from {string.Join(", ", repoNames)}";
        await CheckGitHubPullRequest(expectedPRTitle, targetRepoName, targetBranch, expectedDependencies, repoDirectory, isCompleted: false, isUpdated: true, cleanUp);
    }

    protected async Task CheckNonBatchedGitHubPullRequest(
        string sourceRepoName,
        string targetRepoName,
        string targetBranch,
        List<DependencyDetail> expectedDependencies,
        string repoDirectory,
        bool isCompleted,
        bool isUpdated,
        bool cleanUp)
    {
        var expectedPRTitle = $"[{targetBranch}] Update dependencies from {TestParameters.GitHubTestOrg}/{sourceRepoName}";
        await CheckGitHubPullRequest(expectedPRTitle, targetRepoName, targetBranch, expectedDependencies, repoDirectory, isCompleted, isUpdated, cleanUp);
    }

    protected static string GetExpectedCodeFlowDependencyVersionEntry(string sourceRepo, string targetRepoName, string sha, int buildId) =>
        $"<Source Uri=\"{GetGitHubRepoUrl(sourceRepo)}\" Mapping=\"{targetRepoName}\" Sha=\"{sha}\" BarId=\"{buildId}\" />";

    protected async Task CheckGitHubPullRequestWithTargetDirectories(
        string expectedPRTitle,
        string targetRepoName,
        string targetBranch,
        Dictionary<string, List<DependencyDetail>> expectedDependenciesByDirectory,
        string repoDirectory,
        bool isCompleted,
        bool isUpdated,
        bool cleanUp)
    {
        TestContext.WriteLine($"Checking opened PR in {targetBranch} {targetRepoName}");
        Octokit.PullRequest pullRequest = isUpdated
            ? await WaitForUpdatedPullRequestAsync(targetRepoName, targetBranch)
            : await WaitForPullRequestAsync(targetRepoName, targetBranch);

        pullRequest.Title.Should().Be(expectedPRTitle);

        using (ChangeDirectory(repoDirectory))
        {
            var cleanUpTask = cleanUp
                ? CleanUpPullRequestAfter(TestParameters.GitHubTestOrg, targetRepoName, pullRequest)
                : AsyncDisposable.Create(async () => await Task.CompletedTask);

            await using (cleanUpTask)
            {
                await ValidatePullRequestDependenciesInDirectories(pullRequest.Head.Ref, expectedDependenciesByDirectory);

                if (isCompleted)
                {
                    TestContext.WriteLine($"Checking for automatic merging of PR in {targetBranch} {targetRepoName}");

                    await WaitForMergedPullRequestAsync(targetRepoName, targetBranch);
                }
            }
        }
    }

    private async Task ValidatePullRequestDependenciesInDirectories(string pullRequestBaseBranch, Dictionary<string, List<DependencyDetail>> expectedDependenciesByDirectory, int tries = 1)
    {
        var triesRemaining = tries;
        while (triesRemaining-- > 0)
        {
            await CheckoutRemoteBranchAsync(pullRequestBaseBranch);
            await RunGitAsync("pull");

            foreach (var (directory, expectedDependencies) in expectedDependenciesByDirectory)
            {
                TestContext.WriteLine($"Validating dependencies in directory: {directory}");
                
                var actualDependencies = await RunDarcAsync(includeConfigurationRepoParams: false, "get-dependencies", "--relative-base-path", directory);
                var expectedDependenciesString = DependencyCollectionStringBuilder.GetString(expectedDependencies);

                actualDependencies.Should().Be(expectedDependenciesString);
            }
        }
    }

    protected async Task CheckGitHubPullRequest(
        string expectedPRTitle,
        string targetRepoName,
        string targetBranch,
        List<DependencyDetail> expectedDependencies,
        string repoDirectory,
        bool isCompleted,
        bool isUpdated,
        bool cleanUp)
    {
        TestContext.WriteLine($"Checking opened PR in {targetBranch} {targetRepoName}");
        Octokit.PullRequest pullRequest = isUpdated
            ? await WaitForUpdatedPullRequestAsync(targetRepoName, targetBranch)
            : await WaitForPullRequestAsync(targetRepoName, targetBranch);

        pullRequest.Title.Should().Be(expectedPRTitle);

        using (ChangeDirectory(repoDirectory))
        {
            var cleanUpTask = cleanUp
                ? CleanUpPullRequestAfter(TestParameters.GitHubTestOrg, targetRepoName, pullRequest)
                : AsyncDisposable.Create(async () => await Task.CompletedTask);

            await using (cleanUpTask)
            {
                await ValidatePullRequestDependencies(pullRequest.Head.Ref, expectedDependencies);

                if (isCompleted)
                {
                    TestContext.WriteLine($"Checking for automatic merging of PR in {targetBranch} {targetRepoName}");

                    await WaitForMergedPullRequestAsync(targetRepoName, targetBranch);
                }
            }
        }
    }

    protected async Task CheckBatchedAzDoPullRequest(
        string[] sourceRepoNames,
        string targetRepoName,
        string targetBranch,
        List<DependencyDetail> expectedDependencies,
        string repoDirectory,
        bool complete = false)
    {
        var repoNames = sourceRepoNames
            .Select(n => $"{TestParameters.AzureDevOpsAccount}/{TestParameters.AzureDevOpsProject}/{n}")
            .OrderBy(s => s);

        var expectedPRTitle = $"[{targetBranch}] Update dependencies from {string.Join(", ", repoNames)}";
        await CheckAzDoPullRequest(
            expectedPRTitle,
            targetRepoName,
            targetBranch,
            expectedDependencies,
            repoDirectory,
            complete,
            isUpdated: true,
            cleanUp: true,
            expectedFeeds: null,
            notExpectedFeeds: null);
    }

    protected async Task CheckNonBatchedAzDoPullRequest(
        string sourceRepoName,
        string targetRepoName,
        string targetBranch,
        List<DependencyDetail> expectedDependencies,
        string repoDirectory,
        bool isCompleted,
        bool isUpdated,
        bool cleanUp,
        string[]? expectedFeeds = null,
        string[]? notExpectedFeeds = null)
    {
        var expectedPRTitle = $"[{targetBranch}] Update dependencies from {TestParameters.AzureDevOpsAccount}/{TestParameters.AzureDevOpsProject}/{sourceRepoName}";
        // TODO (https://github.com/dotnet/arcade-services/issues/3149): I noticed we are not passing isCompleted further down - when I put it there the tests started failing - but we should fix this
        await CheckAzDoPullRequest(
            expectedPRTitle,
            targetRepoName,
            targetBranch,
            expectedDependencies,
            repoDirectory,
            false,
            isUpdated,
            cleanUp,
            expectedFeeds,
            notExpectedFeeds);
    }

    protected async Task<string> CheckAzDoPullRequest(
        string expectedPRTitle,
        string targetRepoName,
        string targetBranch,
        List<DependencyDetail> expectedDependencies,
        string repoDirectory,
        bool isCompleted,
        bool isUpdated,
        bool cleanUp,
        string[]? expectedFeeds,
        string[]? notExpectedFeeds)
    {
        var targetRepoUri = GetAzDoApiRepoUrl(targetRepoName);
        TestContext.WriteLine($"Checking Opened PR in {targetBranch} {targetRepoUri} ...");
        var pullRequestId = await GetAzDoPullRequestIdAsync(targetRepoName, targetBranch);
        await using AsyncDisposableValue<PullRequest> pullRequest = await GetAzDoPullRequestAsync(pullRequestId, targetRepoName, targetBranch, isUpdated, cleanUp, expectedPRTitle);

        var trimmedTitle = Regex.Replace(pullRequest.Value.Title, @"\s+", " ");
        trimmedTitle.Should().Be(expectedPRTitle);

        PrStatus expectedPRState = isCompleted ? PrStatus.Closed : PrStatus.Open;
        var prInfo = await AzDoClient.GetPullRequestAsync(GetAzDoApiRepoUrl(targetRepoName) + $"/pullRequests/{pullRequestId}");
        prInfo.Status.Should().Be(expectedPRState);

        using (ChangeDirectory(repoDirectory))
        {
            await ValidatePullRequestDependencies(pullRequest.Value.HeadBranch, expectedDependencies);

            if (expectedFeeds != null && notExpectedFeeds != null)
            {
                TestContext.WriteLine("Validating Nuget feeds in PR branch");

                ISettings settings = Settings.LoadSpecificSettings(@"./", "nuget.config");
                var packageSourceProvider = new PackageSourceProvider(settings);
                IEnumerable<string> sources = packageSourceProvider.LoadPackageSources().Select(p => p.Source);

                sources.Should().Contain(expectedFeeds);
                sources.Should().NotContain(notExpectedFeeds);
            }
        }

        return pullRequest.Value.HeadBranch;
    }

    private async Task ValidatePullRequestDependencies(string pullRequestBaseBranch, List<DependencyDetail> expectedDependencies, int tries = 1)
    {
        var triesRemaining = tries;
        while (triesRemaining-- > 0)
        {
            await CheckoutRemoteBranchAsync(pullRequestBaseBranch);
            await RunGitAsync("pull");

            var actualDependencies = await RunDarcAsync(includeConfigurationRepoParams: false, "get-dependencies");
            var expectedDependenciesString = DependencyCollectionStringBuilder.GetString(expectedDependencies);

            actualDependencies.Should().Be(expectedDependenciesString);
        }
    }

    protected static async Task GitCommitAsync(string message)
    {
        await RunGitAsync("commit", "-am", message);
    }

    protected static async Task GitAddAllAsync() => await RunGitAsync("add", ".");

    protected static async Task<string> GitGetCurrentSha() => await RunGitAsync("rev-parse", "HEAD");

    protected static async Task<IAsyncDisposable> PushGitBranchAsync(string remote, string branch)
    {
        await RunGitAsync("push", "-u", remote, branch);
        return AsyncDisposable.Create(async () =>
        {
            TestContext.WriteLine($"Cleaning up Remote branch {branch}");

            try
            {
                await RunGitAsync("push", remote, "--delete", branch);
            }
            catch
            {
                // If this throws it means that it was cleaned up by a different clean up method first
            }
        });
    }

    protected static string GetRepoUrl(string org, string repository)
    {
        return $"https://github.com/{org}/{repository}";
    }

    protected static string GetGitHubRepoUrl(string repository)
    {
        return GetRepoUrl(TestParameters.GitHubTestOrg, repository);
    }

    protected static string GetRepoFetchUrl(string repository)
    {
        return GetRepoFetchUrl(TestParameters.GitHubTestOrg, repository);
    }

    protected static string GetRepoFetchUrl(string org, string repository)
    {
        return $"https://{TestParameters.GitHubUser}:{TestParameters.GitHubToken}@github.com/{org}/{repository}";
    }

    protected static string GetAzDoRepoUrl(string repoName, string azdoAccount = "dnceng", string azdoProject = "internal")
    {
        return $"https://dev.azure.com/{azdoAccount}/{azdoProject}/_git/{repoName}";
    }

    protected static string GetAzDoApiRepoUrl(string repoName, string azdoAccount = "dnceng", string azdoProject = "internal")
    {
        return $"https://dev.azure.com/{azdoAccount}/{azdoProject}/_apis/git/repositories/{repoName}";
    }

    protected async Task<string> RunDarcAsyncWithInput(string input, params string[] args)
    {
        var ret = await TestHelpers.RunExecutableAsyncWithInput(TestParameters.DarcExePath, input,
        [
            .. args,
            .. TestParameters.BaseDarcRunArgs,
            .. _configRepoDarcParams
        ]);

        await IngestNamespace();

        return ret;
    }

    protected async Task<string> RunDarcAsync(bool includeConfigurationRepoParams = false, params string[] args)
    {
        var configRepoArgs = includeConfigurationRepoParams ? _configRepoDarcParams : [];
        var ret = await TestHelpers.RunExecutableAsync(TestParameters.DarcExePath,
        [
            .. args,
            .. configRepoArgs,
            .. TestParameters.BaseDarcRunArgs,
        ]);

        if (includeConfigurationRepoParams)
        {
            await IngestNamespace();
        }

        return ret;
    }

    protected static Task<string> RunGitAsync(params string[] args)
    {
        return TestHelpers.RunExecutableAsync(TestParameters.GitExePath, args);
    }

    protected async Task CreateTestChannelAsync(string testChannelName)
        => await RunDarcAsync(includeConfigurationRepoParams: true, "add-channel", "--name", testChannelName, "--classification", "test");

    protected async Task AddDependenciesToLocalRepo(string repoPath, string name, string repoUri, bool isToolset = false)
    {
        using (ChangeDirectory(repoPath))
        {
            await RunDarcAsync(includeConfigurationRepoParams: false, ["add-dependency", "--name", name, "--type", isToolset ? "toolset" : "product", "--repo", repoUri, "--version", "0.0.1"]);
        }
    }

    protected async Task AddDependenciesToLocalRepoWithDirectory(
        string repoPath,
        List<AssetData> dependencies,
        string repoUri,
        string? targetDirectory = null,
        string coherentParent = "",
        bool pinned = false)
    {
        using (ChangeDirectory(repoPath))
        {
            foreach (AssetData asset in dependencies)
            {
                List<string> parameters =
                [
                    "add-dependency",
                    "--name", asset.Name,
                    "--version", asset.Version,
                    "--type", "product",
                    "--repo", repoUri
                ];

                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    parameters.Add("--relative-base-path");
                    parameters.Add(targetDirectory);
                }

                if (!string.IsNullOrEmpty(coherentParent))
                {
                    parameters.Add("--coherent-parent");
                    parameters.Add(coherentParent);
                }
                if (pinned)
                {
                    parameters.Add("--pinned");
                }

                var parameterArr = parameters.ToArray();

                await RunDarcAsync(includeConfigurationRepoParams: false, parameterArr);
            }
        }
    }
    protected async Task<string> GetTestChannelsAsync()
    {
        return await RunDarcAsync(includeConfigurationRepoParams: false, "get-channels");
    }

    protected async Task<string?> DeleteTestChannelAsync(string testChannelName)
    {
        return await RunDarcAsync(includeConfigurationRepoParams: true, "delete-channel", "--name", testChannelName);
    }

    protected async Task<string> AddDefaultTestChannelAsync(string testChannelName, string repoUri, string branchName)
    {
        return await RunDarcAsync(includeConfigurationRepoParams: true, "add-default-channel", "--channel", testChannelName, "--repo", repoUri, "--branch", branchName, "-q");
    }

    protected async Task<string> GetDefaultTestChannelsAsync(string repoUri, string branch)
    {
        return await RunDarcAsync(includeConfigurationRepoParams: false, "get-default-channels", "--source-repo", repoUri, "--branch", branch);
    }

    protected async Task DeleteDefaultTestChannelAsync(string testChannelName, string repoUri, string branch)
    {
        await RunDarcAsync(includeConfigurationRepoParams: true, "delete-default-channel", "--channel", testChannelName, "--repo", repoUri, "--branch", branch);
    }

    protected async Task<string> CreateSubscriptionAsync(
        string sourceChannelName,
        string sourceRepo,
        string targetRepo,
        string targetBranch,
        string updateFrequency,
        string sourceOrg = "dotnet",
        List<string>? additionalOptions = null,
        bool sourceIsAzDo = false,
        bool targetIsAzDo = false)
    {
        var sourceUrl = sourceIsAzDo ? GetAzDoRepoUrl(sourceRepo) : GetRepoUrl(sourceOrg, sourceRepo);
        var targetUrl = targetIsAzDo ? GetAzDoRepoUrl(targetRepo) : GetGitHubRepoUrl(targetRepo);

        string[] command =
         [
            "add-subscription", "-q",
            "--channel", sourceChannelName,
            "--source-repo", sourceUrl,
            "--target-repo", targetUrl,
            "--target-branch", targetBranch,
            "--update-frequency", updateFrequency,
            .. additionalOptions ?? []
        ];

        var output = await RunDarcAsync(includeConfigurationRepoParams: true, command);

        Match match = Regex.Match(output, "Successfully added subscription with id '([a-f0-9-]+)' on branch");
        if (!match.Success)
        {
            throw new ScenarioTestException("Unable to create subscription.");
        }

        return match.Groups[1].Value;
    }

    protected async Task<string> CreateSubscriptionAsync(string yamlDefinition)
    {
        var output = await RunDarcAsyncWithInput(yamlDefinition, ["add-subscription", "-q", "--read-stdin"]);

        Match match = Regex.Match(output, "Successfully added subscription with id '([a-f0-9-]+)' on branch");
        if (!match.Success)
        {
            throw new ScenarioTestException("Unable to create subscription.");
        }

        return match.Groups[1].Value;
    }

    protected async Task<string> GetSubscriptionInfo(string subscriptionId)
    {
        return await RunDarcAsync(includeConfigurationRepoParams: false, "get-subscriptions", "--ids", subscriptionId);
    }

    protected async Task<string> GetSubscriptions(string channelName)
    {
        return await RunDarcAsync(includeConfigurationRepoParams: false, "get-subscriptions", "--channel", channelName);
    }

    protected async Task SetSubscriptionStatusByChannel(bool enableSub, string channelName)
    {
        await RunDarcAsync(includeConfigurationRepoParams: true, "subscription-status", enableSub ? "--enable" : "-d", "--channel", channelName, "--quiet");
    }

    protected async Task SetSubscriptionStatusById(bool enableSub, string subscriptionId)
    {
        await RunDarcAsync(includeConfigurationRepoParams: true, "subscription-status", "--id", subscriptionId, enableSub ? "--enable" : "-d", "--quiet");
    }

    protected async Task<string> DeleteSubscriptionsForChannel(string channelName)
    {
        return await RunDarcAsync(includeConfigurationRepoParams: true, "delete-subscriptions", "--channel", channelName, "--quiet");
    }

    protected async Task<string> DeleteSubscriptionById(string subscriptionId)
    {
        return await RunDarcAsync(includeConfigurationRepoParams: true, "delete-subscriptions", "--id", subscriptionId, "--quiet");
    }

    protected static Task<Build> CreateBuildAsync(string repositoryUrl, string branch, string commit, string buildNumber, List<AssetData> assets)
    {
        return CreateBuildAsync(repositoryUrl, branch, commit, buildNumber, assets, []);
    }

    protected static async Task<Build> CreateBuildAsync(string repositoryUrl, string branch, string commit, string buildNumber, List<AssetData> assets, List<BuildRef> dependencies)
    {
        Build build = await PcsApi.Builds.CreateAsync(new BuildData(
            commit: commit,
            azureDevOpsAccount: TestParameters.AzureDevOpsAccount,
            azureDevOpsProject: TestParameters.AzureDevOpsProject,
            azureDevOpsBuildNumber: buildNumber,
            azureDevOpsRepository: repositoryUrl,
            azureDevOpsBranch: branch,
            released: false,
            stable: false)
        {
            AzureDevOpsBuildId = TestParameters.AzureDevOpsBuildId,
            AzureDevOpsBuildDefinitionId = TestParameters.AzureDevOpsBuildDefinitionId,
            GitHubRepository = repositoryUrl,
            GitHubBranch = branch,
            Assets = assets,
            Dependencies = dependencies,
        });

        return build;
    }

    protected async Task<string> GetDarcBuildAsync(int buildId)
    {
        var buildString = await RunDarcAsync(includeConfigurationRepoParams: false, "get-build", "--id", buildId.ToString());
        return buildString;
    }

    protected async Task<string> UpdateBuildAsync(int buildId, string updateParams)
    {
        var buildString = await RunDarcAsync(includeConfigurationRepoParams: false, "update-build", "--id", buildId.ToString(), updateParams);
        return buildString;
    }

    protected async Task AddDependenciesToLocalRepo(
        string repoPath,
        List<AssetData> dependencies,
        string repoUri,
        string coherentParent = "",
        bool pinned = false)
    {
        using (ChangeDirectory(repoPath))
        {
            foreach (AssetData asset in dependencies)
            {
                List<string> parameters =
                [
                    "add-dependency",
                    "--name", asset.Name,
                    "--version", asset.Version,
                    "--type", "product",
                    "--repo", repoUri,
                ];

                if (!string.IsNullOrEmpty(coherentParent))
                {
                    parameters.Add("--coherent-parent");
                    parameters.Add(coherentParent);
                }
                if (pinned)
                {
                    parameters.Add("--pinned");
                }

                var parameterArr = parameters.ToArray();

                await RunDarcAsync(includeConfigurationRepoParams: false, parameterArr);
            }
        }
    }

    protected async Task<string> GatherDrop(int buildId, string outputDir, bool includeReleased, string extraAssetsRegex)
    {
        string[] args = ["gather-drop", "--id", buildId.ToString(), "--dry-run", "--output-dir", outputDir];

        if (includeReleased)
        {
            args = [.. args, "--include-released"];
        }

        if (!string.IsNullOrEmpty(extraAssetsRegex))
        {
            args = [.. args, "--always-download-asset-filters", extraAssetsRegex];
        }

        return await RunDarcAsync(includeConfigurationRepoParams: false, args);
    }

    protected static async Task TriggerSubscriptionAsync(string subscriptionId)
    {
        TestContext.WriteLine("Triggering the subscription " + subscriptionId);
        await PcsApi.Subscriptions.TriggerSubscriptionAsync(0, force: false, Guid.Parse(subscriptionId));
    }

    protected async Task<IAsyncDisposable> AddBuildToChannelAsync(int buildId, string channelName)
    {
        TestContext.WriteLine($"Adding build {buildId} to channel");
        await RunDarcAsync(includeConfigurationRepoParams: false, "add-build-to-channel", "--id", buildId.ToString(), "--channel", channelName, "--skip-assets-publishing");
        return AsyncDisposable.Create(async () =>
        {
            TestContext.WriteLine($"Removing build {buildId} from channel {channelName}");
            await RunDarcAsync(includeConfigurationRepoParams: false, "delete-build-from-channel", "--id", buildId.ToString(), "--channel", channelName);
        });
    }

    protected async Task DeleteBuildFromChannelAsync(string buildId, string channelName)
    {
        await RunDarcAsync(includeConfigurationRepoParams: false, "delete-build-from-channel", "--id", buildId, "--channel", channelName);
    }

    protected static IDisposable ChangeDirectory(string directory)
    {
        var old = Directory.GetCurrentDirectory();
        TestContext.WriteLine($"Switching to directory {directory}");
        Directory.SetCurrentDirectory(directory);
        return Disposable.Create(() =>
        {
            TestContext.WriteLine($"Switching back to directory {old}");
            Directory.SetCurrentDirectory(old);
        });
    }

    protected static Task<TemporaryDirectory> CloneRepositoryAsync(string repository)
    {
        return CloneRepositoryAsync(TestParameters.GitHubTestOrg, repository);
    }

    protected static async Task<TemporaryDirectory> CloneRepositoryAsync(string org, string repository)
    {
        using var shareable = Shareable.Create(TemporaryDirectory.Get());
        var directory = shareable.Peek()!.Directory;

        var fetchUrl = GetRepoFetchUrl(org, repository);
        await RunGitAsync("clone", "--quiet", fetchUrl, directory);

        await RunGitAsync("-C", directory, "config", "user.email", $"{TestParameters.GitHubUser}@test.com");
        await RunGitAsync("-C", directory, "config", "user.name", TestParameters.GitHubUser);
        await RunGitAsync("-C", directory, "config", "gc.auto", "0");
        await RunGitAsync("-C", directory, "config", "advice.detachedHead", "false");
        await RunGitAsync("-C", directory, "config", "color.ui", "false");

        return shareable.TryTake()!;
    }

    protected static string GetAzDoRepoAuthUrl(string repoName)
    {
        return $"https://{TestParameters.AzDoToken}@dev.azure.com/{TestParameters.AzureDevOpsAccount}/{TestParameters.AzureDevOpsProject}/_git/{repoName}";
    }

    protected static async Task<TemporaryDirectory> CloneAzDoRepositoryAsync(string repoName)
    {
        using var shareable = Shareable.Create(TemporaryDirectory.Get());
        var directory = shareable.Peek()!.Directory;

        var authUrl = GetAzDoRepoAuthUrl(repoName);
        await RunGitAsync("clone", "--quiet", authUrl, directory);

        // The GitHubUser and AzDoUser have the same user name so this uses the existing parameter
        await RunGitAsync("-C", directory, "config", "user.email", $"{TestParameters.GitHubUser}@test.com");
        await RunGitAsync("-C", directory, "config", "user.name", TestParameters.GitHubUser);

        return shareable.TryTake()!;
    }

    protected async Task<TemporaryDirectory> CloneRepositoryWithDarc(string repoName, string version, string reposToIgnore, bool includeToolset, int depth)
    {
        var sourceRepoUri = GetRepoUrl("dotnet", repoName);

        using var shareable = Shareable.Create(TemporaryDirectory.Get());
        var directory = shareable.Peek().Directory;

        var reposFolder = Path.Join(directory, "cloned-repos");
        var gitDirFolder = Path.Join(directory, "git-dirs");

        // Clone repo
        await RunDarcAsync(includeConfigurationRepoParams: false, "clone", "--repo", sourceRepoUri, "--version", version, "--git-dir-folder", gitDirFolder, "--ignore-repos", reposToIgnore, "--repos-folder", reposFolder, "--depth", depth.ToString(), includeToolset ? "--include-toolset" : "");

        return shareable.TryTake()!;
    }

    protected static async Task CheckoutRemoteRefAsync(string commit)
    {
        await RunGitAsync("pull", "origin");
        await RunGitAsync("checkout", commit);
    }

    protected static async Task CheckoutRemoteBranchAsync(string branchName)
    {
        await RunGitAsync("fetch", "origin", branchName);
        await RunGitAsync("checkout", branchName);
    }

    protected static async Task<IAsyncDisposable> CheckoutBranchAsync(string branchName)
    {
        await RunGitAsync("fetch", "origin");
        await RunGitAsync("checkout", "-b", branchName);

        return AsyncDisposable.Create(async () =>
        {
            TestContext.WriteLine($"Deleting remote branch {branchName}");
            try
            {
                await DeleteBranchAsync(branchName);
            }
            catch
            {
                // If this throws it means that it was cleaned up by a different clean up method first
            }
        });
    }

    protected static async Task DeleteBranchAsync(string branchName)
    {
        await RunGitAsync("push", "origin", "--delete", branchName);
    }

    protected static List<AssetData> GetAssetData(string asset1Name, string asset1Version, string asset2Name, string asset2Version)
    {
        var location = @"https://pkgs.dev.azure.com/dnceng/public/_packaging/NotARealFeed/nuget/v3/index.json";
        LocationType locationType = LocationType.NugetFeed;

        AssetData asset1 = GetAssetDataWithLocations(asset1Name, asset1Version, location, locationType);

        AssetData asset2 = GetAssetDataWithLocations(asset2Name, asset2Version, location, locationType);

        return [asset1, asset2];
    }

    protected static List<AssetData> GetAssetData(string asset1Name, string asset1Version, string asset2Name, string asset2Version, string asset3Name, string asset3Version)
    {
        var location = @"https://pkgs.dev.azure.com/dnceng/public/_packaging/NotARealFeed/nuget/v3/index.json";
        LocationType locationType = LocationType.NugetFeed;

        AssetData asset1 = GetAssetDataWithLocations(asset1Name, asset1Version, location, locationType);
        AssetData asset2 = GetAssetDataWithLocations(asset2Name, asset2Version, location, locationType);
        AssetData asset3 = GetAssetDataWithLocations(asset3Name, asset3Version, location, locationType);

        return [asset1, asset2, asset3];
    }

    protected static AssetData GetAssetDataWithLocations(
        string assetName,
        string assetVersion,
        string assetLocation1,
        LocationType assetLocationType1,
        string? assetLocation2 = null,
        LocationType assetLocationType2 = LocationType.None)
    {
        var asset = new AssetData(false)
        {
            Name = assetName,
            Version = assetVersion,
            Locations =
            [
                new AssetLocationData(assetLocationType1)
                {
                    Location = assetLocation1
                }
            ]
        };

        if (assetLocation2 != null && assetLocationType2 != LocationType.None)
        {
            asset.Locations =
            [
                ..asset.Locations,
                new AssetLocationData(assetLocationType2)
                {
                    Location = assetLocation2
                }
            ];
        }

        return asset;
    }

    protected async Task SetRepositoryPolicies(string repoUri, string branchName, string[]? policyParams = null)
    {
        string[] commandParams = ["set-repository-policies", "-q", "--repo", repoUri, "--branch", branchName, .. policyParams ?? []];

        await RunDarcAsync(includeConfigurationRepoParams: true, commandParams);
    }

    protected async Task<string> GetRepositoryPolicies(string repoUri, string branchName)
    {
        return await RunDarcAsync(includeConfigurationRepoParams: false, "get-repository-policies", "--all", "--repo", repoUri, "--branch", branchName);
    }

    protected static async Task WaitForMergedPullRequestAsync(string targetRepo, string targetBranch, Octokit.PullRequest pr, Octokit.Repository repo, int attempts = 40)
    {
        while (attempts-- > 0)
        {
            TestContext.WriteLine($"Starting check for merge, attempts remaining {attempts}");
            pr = await GitHubApi.PullRequest.Get(repo.Id, pr.Number);

            if (pr.State == Octokit.ItemState.Closed)
            {
                return;
            }

            await Task.Delay(WAIT_DELAY);
        }

        var waitTime = WAIT_DELAY * attempts;
        throw new ScenarioTestException(
            $"The created pull request for {targetRepo} targeting {targetBranch} was not merged within {waitTime.Minutes} minutes");
    }

    protected async Task<bool> CheckGithubPullRequestChecks(string targetRepoName, string targetBranch, TimeSpan? waitTime = null)
    {
        TestContext.WriteLine($"Checking opened PR in {targetBranch} {targetRepoName}");
        Octokit.PullRequest pullRequest = await WaitForPullRequestAsync(targetRepoName, targetBranch);
        Octokit.Repository repo = await GitHubApi.Repository.Get(TestParameters.GitHubTestOrg, targetRepoName);

        await Task.Delay(waitTime ?? TimeSpan.FromSeconds(5 * 60 + 30));

        List<Octokit.CheckRun> maestroChecks = await WaitForPullRequestMaestroChecksAsync(pullRequest.Url, pullRequest.Head.Sha, repo.Id);

        foreach (var checkRun in maestroChecks)
        {
            if (checkRun.Status != "completed" && !checkRun.Output.Title.Contains(AllChecksSuccessfulMergePolicy.WaitingForChecksMsg))
            {
                TestContext.WriteLine($"Check '{checkRun.Output.Title}' with id {checkRun.Id} on PR {pullRequest.Url} has not completed in time. Check's status: {checkRun.Status}");
                return false;
            }
        }
        TestContext.WriteLine($"All maestro merge policy checks found in the PR ({pullRequest.Url}) have completed.");
        return true;
    }

    protected static async Task<List<Octokit.CheckRun>> WaitForPullRequestMaestroChecksAsync(string prUrl, string commitSha, long repoId, int attempts = 2)
    {
        while (attempts-- > 0)
        {
            TestContext.WriteLine($"Waiting for maestro checks to be added to the PR, attempts remaining {attempts}");
            Octokit.CheckRunsResponse prChecks = await GitHubApi.Check.Run.GetAllForReference(repoId, commitSha);

            var maestroChecks = prChecks.CheckRuns
                .Where(cr => cr.ExternalId.StartsWith(MergePolicyConstants.MaestroMergePolicyCheckRunPrefix))
                .ToList();

            if (maestroChecks.Count > 0)
            {
                TestContext.WriteLine($"Found {maestroChecks.Count} Maestro Merge Policy checks for PR {prUrl}");
                return maestroChecks;
            }
            await Task.Delay(WAIT_DELAY);
        }
        throw new ScenarioTestException($"No Maestro Merge Policy checks were found in the PR ({prUrl}) during the allotted time.");
    }

    protected async Task<Octokit.PullRequest> WaitForFileContentInPullRequest(
        string repoDir,
        string targetRepoName,
        string targetBranch,
        string filePath,
        string expectedContent,
        int maxAttempts = 5)
    {
        using var _ = ChangeDirectory(repoDir);
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var pr = await WaitForUpdatedPullRequestAsync(targetRepoName, targetBranch);
            await CheckoutRemoteRefAsync(pr.Head.Ref);
            var content = await File.ReadAllTextAsync(filePath);
            if (content == expectedContent)
            {
                return pr;
            }
        }

        throw new ScenarioTestException($"File {filePath} in branch {targetBranch} did not have expected content in PR.");
    }

    protected static string GetTestChannelName([CallerMemberName] string testName = "")
    {
        return $"Test {testName} {Guid.NewGuid().ToString().Substring(0, 16)}";
    }

    protected static string GetTestBranchName([CallerMemberName] string testName = "")
    {
        return $"test/{testName}/{Guid.NewGuid().ToString().Substring(0, 16)}";
    }

    protected string GetUniqueAssetName(string packageName)
    {
        return $"{packageName}.{_packageNameSalt}";
    }

    protected static IAsyncDisposable CleanUpPullRequestAfter(string owner, string repo, Octokit.PullRequest pullRequest)
        => AsyncDisposable.Create(async () =>
        {
            await ClosePullRequest(owner, repo, pullRequest);
        });

    protected static async Task ClosePullRequest(string owner, string repo, Octokit.PullRequest pullRequest)
    {
        try
        {
            var pullRequestUpdate = new Octokit.PullRequestUpdate
            {
                State = Octokit.ItemState.Closed
            };

            await GitHubApi.Repository.PullRequest.Update(owner, repo, pullRequest.Number, pullRequestUpdate);
        }
        catch
        {
            // Closed already
        }
        try
        {
            await GitHubApi.Git.Reference.Delete(owner, repo, $"heads/{pullRequest.Head.Ref}");
        }
        catch
        {
            // branch already deleted
        }
    }

    protected static async Task CreateTargetBranchAndExecuteTest(string targetBranchName, string targetDirectory, Func<Task> test)
    {
        // first create a new target branch
        using var _ = ChangeDirectory(targetDirectory);
        await using var __ = await CheckoutBranchAsync(targetBranchName);
        await using var ___ = await PushGitBranchAsync("origin", targetBranchName);
        await test();
    }

    protected static async Task WaitForNewCommitInPullRequest(string repo, Octokit.PullRequest pr, int numberOfCommits = 2)
    {
        var attempts = 30;
        while (attempts-- > 0)
        {
            pr = await GitHubApi.PullRequest.Get(TestParameters.GitHubTestOrg, repo, pr.Number);
            if (pr.Commits >= numberOfCommits)
            {
                return;
            }
            await Task.Delay(WAIT_DELAY);
        }

        var waitTime = WAIT_DELAY * attempts;
        throw new ScenarioTestException(
            $"The created pull request for repo targeting {pr.Base.Ref} did not have a new commit within {waitTime.Minutes} minutes");
    }

    protected async Task<Octokit.PullRequest> WaitForPullRequestWithConflict(string repo, string targetBranch)
    {
        TestContext.WriteLine("Waiting for the new PR to show up");
        var pr = await WaitForPullRequestAsync(repo, targetBranch);

        // WaitForPullRequestAsync fetches prs in bulk, which doesn't fetch fields like Mergeable and MergeableState which we need
        // Additionally, the Mergeable and MergeableState computed asynchronously so they might not be ready right after PR creation
        var attempt = 0;
        while (attempt < 5)
        {
            TestContext.WriteLine("Waiting for the mergeable field to be computed by GitHub for the PR");
            pr = await GitHubApi.PullRequest.Get(TestParameters.GitHubTestOrg, repo, pr.Number);
            if (pr.Mergeable.HasValue && pr.MergeableState.HasValue)
            {
                break;
            }

            await Task.Delay(WAIT_DELAY);
            attempt++;
        }

        if (!pr.Mergeable.HasValue || !pr.MergeableState.HasValue)
        {
            throw new ScenarioTestException($"Failed to get mergeable state for PR " + pr.HtmlUrl + " in alloted time");
        }

        pr.Mergeable.Should().BeFalse("PR " + pr.HtmlUrl + " should have conflicts");
        pr.MergeableState.ToString().Should().Be("dirty", "PR " + pr.HtmlUrl + " should be dirty");
        return pr;
    }

    protected async Task IngestNamespace()
    {
        _namespaceIngested = true;
        var configuration = await TestParameters.ConfigRepoParser.ParseAsync(_temporaryDirectory.Directory, _testNamespace);
        
        await PcsApi.Ingestion.IngestNamespaceAsync(
            _testNamespace,
            true,
            configuration.ToPcsClient());
    }
}
