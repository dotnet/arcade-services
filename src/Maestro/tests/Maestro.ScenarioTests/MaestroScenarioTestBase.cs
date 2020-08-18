using Maestro.ScenarioTests.ObjectHelpers;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;
using Octokit;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Maestro.ScenarioTests
{
    public class MaestroScenarioTestBase
    {
        private TestParameters _parameters;

        public IMaestroApi MaestroApi => _parameters.MaestroApi;

        public GitHubClient GitHubApi => _parameters.GitHubApi;

        public Microsoft.DotNet.DarcLib.AzureDevOpsClient AzDoClient => _parameters.AzDoClient;

        public MaestroScenarioTestBase()
        {
        }

        public void SetTestParameters(TestParameters parameters)
        {
            _parameters = parameters;
        }

        public async Task<PullRequest> WaitForPullRequestAsync(string targetRepo, string targetBranch)
        {
            Repository repo = await GitHubApi.Repository.Get(_parameters.GitHubTestOrg, targetRepo).ConfigureAwait(false);

            var attempts = 10;
            while (attempts-- > 0)
            {
                IReadOnlyList<PullRequest> prs = await GitHubApi.PullRequest.GetAllForRepository(repo.Id, new PullRequestRequest
                {
                    State = ItemStateFilter.Open,
                    Base = targetBranch,
                }).ConfigureAwait(false);
                if (prs.Count == 1)
                {
                    return prs[0];
                }
                if (prs.Count > 1)
                {
                    throw new MaestroTestException($"More than one pull request found in {targetRepo} targeting {targetBranch}");
                }

                await Task.Delay(60 * 1000).ConfigureAwait(false);
            }

            throw new MaestroTestException($"No pull request was created in {targetRepo} targeting {targetBranch}");
        }

        public async Task<PullRequest> WaitForUpdatedPullRequestAsync(string targetRepo, string targetBranch, int attempts = 7)
        {
            Repository repo = await GitHubApi.Repository.Get(_parameters.GitHubTestOrg, targetRepo).ConfigureAwait(false);
            PullRequest pr = await WaitForPullRequestAsync(targetRepo, targetBranch);

            while (attempts-- > 0)
            {
                pr = await GitHubApi.PullRequest.Get(repo.Id, pr.Number);

                if (pr.CreatedAt != pr.UpdatedAt)
                {
                    return pr;
                }

                await Task.Delay(60 * 1000).ConfigureAwait(false);
            }

            throw new MaestroTestException($"The created pull request for {targetRepo} targeting {targetBranch} was not updated with subsequent subscriptions after creation");
        }

        public async Task<bool> WaitForMergedPullRequestAsync(string targetRepo, string targetBranch, int attempts = 7)
        {
            Repository repo = await GitHubApi.Repository.Get(_parameters.GitHubTestOrg, targetRepo).ConfigureAwait(false);
            PullRequest pr = await WaitForPullRequestAsync(targetRepo, targetBranch);

            while (attempts-- > 0)
            {
                TestContext.WriteLine($"Starting check for merge, attempts remaining {attempts}");
                pr = await GitHubApi.PullRequest.Get(repo.Id, pr.Number);

                if (pr.State == ItemState.Closed)
                {
                    return true;
                }

                await Task.Delay(60 * 1000).ConfigureAwait(false);
            }

            throw new MaestroTestException($"The created pull request for {targetRepo} targeting {targetBranch} was not merged within {attempts} minutes");
        }

        internal async Task<int> GetAzDoPullRequestIdAsync(string targetRepoName, string targetBranch, bool isUpdated)
        {
            string searchBaseUrl = GetAzDoRepoUrl(targetRepoName);
            string apiBaseUrl = GetAzDoApiRepoUrl(targetRepoName);

            IEnumerable<int> prs = new List<int>();
            int attempts = 10;

            while (attempts-- > 0)
            {
                try
                {
                    prs = await AzDoClient.SearchPullRequestsAsync(searchBaseUrl, Microsoft.DotNet.DarcLib.PrStatus.Open, targetPullRequestBranch: targetBranch).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    if (ex.Message == "Response status code does not indicate success: 404 (Not Found).")
                    {
                        // This is expected before the PR has been created
                    }
                    else
                    {
                        throw;
                    }
                }

                if (prs.Count() == 1)
                {
                    return prs.FirstOrDefault();
                }

                if (prs.Count() > 1)
                {
                    throw new MaestroTestException($"More than one pull request found in {searchBaseUrl} targeting {targetBranch}");
                }

                await Task.Delay(60 * 1000).ConfigureAwait(false);
            }

            throw new MaestroTestException($"No pull request was created in {searchBaseUrl} targeting {targetBranch}");
        }

        internal async Task<Microsoft.DotNet.DarcLib.PullRequest> GetAzDoPullRequestAsync(int pullRequestId, string targetRepoName, string targetBranch, bool isUpdated, string expectedPRTitle = null)
        {
            string apiBaseUrl = GetAzDoApiRepoUrl(targetRepoName);

            if (!isUpdated)
            {
                return await AzDoClient.GetPullRequestAsync($"{apiBaseUrl}/pullRequests/{pullRequestId}");
            }

            if (string.IsNullOrEmpty(expectedPRTitle))
            {
                throw new ArgumentNullException(expectedPRTitle, "ExpectedPRTitle must be defined for AzDo PRs that require an update.");
            }

            Microsoft.DotNet.DarcLib.PullRequest pr = await AzDoClient.GetPullRequestAsync($"{apiBaseUrl}/pullRequests/{pullRequestId}");

            for (int tries = 7; tries > 0; tries--)
            {
                pr = await AzDoClient.GetPullRequestAsync($"{apiBaseUrl}/pullRequests/{pullRequestId}");
                string trimmedTitle = Regex.Replace(pr.Title, @"\s+", " ");

                if (trimmedTitle == expectedPRTitle)
                {
                    return pr;
                }

                await Task.Delay(60 * 1000).ConfigureAwait(false);
            }

            throw new MaestroTestException($"The created pull request for {targetRepoName} targeting {targetBranch} was not updated with subsequent subscriptions after creation");
        }

        public async Task CheckBatchedGitHubPullRequest(string targetBranch, string source1RepoName, string source2RepoName,
            string targetRepoName, List<Microsoft.DotNet.DarcLib.DependencyDetail> expectedDependencies, string repoDirectory)
        {
            string expectedPRTitle = $"[{targetBranch}] Update dependencies from {_parameters.GitHubTestOrg}/{source1RepoName} {_parameters.GitHubTestOrg}/{source2RepoName}";
            await CheckGitHubPullRequest(expectedPRTitle, targetRepoName, targetBranch, expectedDependencies, repoDirectory, false, true);
        }

        public async Task CheckNonBatchedGitHubPullRequest(string sourceRepoName, string targetRepoName, string targetBranch,
            List<Microsoft.DotNet.DarcLib.DependencyDetail> expectedDependencies, string repoDirectory, bool isCompleted = false, bool isUpdated = false)
        {
            string expectedPRTitle = $"[{targetBranch}] Update dependencies from {_parameters.GitHubTestOrg}/{sourceRepoName}";
            await CheckGitHubPullRequest(expectedPRTitle, targetRepoName, targetBranch, expectedDependencies, repoDirectory, isCompleted, isUpdated);
        }

        public async Task CheckGitHubPullRequest(string expectedPRTitle, string targetRepoName, string targetBranch,
            List<Microsoft.DotNet.DarcLib.DependencyDetail> expectedDependencies, string repoDirectory, bool isCompleted, bool isUpdated)
        {
            TestContext.WriteLine($"Checking opened PR in {targetBranch} {targetRepoName}");
            PullRequest pullRequest = isUpdated ?
                await WaitForUpdatedPullRequestAsync(targetRepoName, targetBranch)
                : await WaitForPullRequestAsync(targetRepoName, targetBranch);

            StringAssert.AreEqualIgnoringCase(expectedPRTitle, pullRequest.Title);

            using (ChangeDirectory(repoDirectory))
            {
                await ValidatePullRequestDependencies(targetRepoName, pullRequest.Head.Ref, expectedDependencies);

                if (isCompleted)
                {
                    TestContext.WriteLine($"Checking for automatic merging of PR in {targetBranch} {targetRepoName}");

                    await WaitForMergedPullRequestAsync(targetRepoName, targetBranch);
                }
            }
        }

        public async Task CheckBatchedAzDoPullRequest(string source1RepoName, string source2RepoName, string targetRepoName, string targetBranch,
            List<Microsoft.DotNet.DarcLib.DependencyDetail> expectedDependencies, string repoDirectory, bool complete = false)
        {
            string expectedPRTitle = $"[{targetBranch}] Update dependencies from {_parameters.AzureDevOpsAccount}/{_parameters.AzureDevOpsProject}/{source1RepoName} {_parameters.AzureDevOpsAccount}/{_parameters.AzureDevOpsProject}/{source2RepoName}";
            await CheckAzDoPullRequest(expectedPRTitle, targetRepoName, targetBranch, expectedDependencies, repoDirectory, complete, true, null, null);
        }

        public async Task CheckNonBatchedAzDoPullRequest(string sourceRepoName, string targetRepoName, string targetBranch,
            List<Microsoft.DotNet.DarcLib.DependencyDetail> expectedDependencies, string repoDirectory, bool isCompleted = false, bool isUpdated = false,
            string[] expectedFeeds = null, string[] notExpectedFeeds = null)
        {
            string expectedPRTitle = $"[{targetBranch}] Update dependencies from {_parameters.AzureDevOpsAccount}/{_parameters.AzureDevOpsProject}/{sourceRepoName}";
            await CheckAzDoPullRequest(expectedPRTitle, targetRepoName, targetBranch, expectedDependencies, repoDirectory, false, isUpdated, expectedFeeds, notExpectedFeeds);
        }

        public async Task CheckAzDoPullRequest(string expectedPRTitle, string targetRepoName, string targetBranch,
            List<Microsoft.DotNet.DarcLib.DependencyDetail> expectedDependencies, string repoDirectory, bool isCompleted, bool isUpdated,
            string[] expectedFeeds, string[] notExpectedFeeds)
        {
            string targetRepoUri = GetAzDoApiRepoUrl(targetRepoName);
            TestContext.WriteLine($"Checking Opened PR in {targetBranch} {targetRepoUri} ...");
            int pullRequestId = await GetAzDoPullRequestIdAsync(targetRepoName, targetBranch, isUpdated);
            Microsoft.DotNet.DarcLib.PullRequest pullRequest = await GetAzDoPullRequestAsync(pullRequestId, targetRepoName, targetBranch, isUpdated, expectedPRTitle);

            string trimmedTitle = Regex.Replace(pullRequest.Title, @"\s+", " ");
            StringAssert.AreEqualIgnoringCase(expectedPRTitle, trimmedTitle);

            Microsoft.DotNet.DarcLib.PrStatus expectedPRState = isCompleted ? Microsoft.DotNet.DarcLib.PrStatus.Closed : Microsoft.DotNet.DarcLib.PrStatus.Open;
            var prStatus = await AzDoClient.GetPullRequestStatusAsync(GetAzDoApiRepoUrl(targetRepoName) + $"/pullRequests/{pullRequestId}");
            Assert.AreEqual(expectedPRState, prStatus);

            using (ChangeDirectory(repoDirectory))
            {
                await ValidatePullRequestDependencies(targetRepoName, pullRequest.BaseBranch, expectedDependencies);
            }
        }

        public async Task ValidatePullRequestDependencies(string targetRepoName, string pullRequestBaseBranch, List<Microsoft.DotNet.DarcLib.DependencyDetail> expectedDependencies, int tries = 1)
        {
            int triesRemaining = tries;
            while (triesRemaining-- > 0)
            {
                await CheckoutRemoteBranchAsync(pullRequestBaseBranch).ConfigureAwait(false);
                await RunGitAsync("pull").ConfigureAwait(false);

                string actualDependencies = await RunDarcAsync("get-dependencies").ConfigureAwait(false);
                string expectedDependenciesString = DependencyCollectionStringBuilder.GetString(expectedDependencies);

                Assert.AreEqual(expectedDependenciesString, actualDependencies, $"Expected: {expectedDependenciesString} \r\n Actual: {actualDependencies}");

            }
        }

        public async Task GitCommitAsync(string message)
        {
            await RunGitAsync("commit", "-am", message);
        }

        public async Task<IAsyncDisposable> PushGitBranchAsync(string remote, string branch)
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

        public string GetRepoUrl(string org, string repository)
        {
            return $"https://github.com/{org}/{repository}";
        }

        public string GetRepoUrl(string repository)
        {
            return GetRepoUrl(_parameters.GitHubTestOrg, repository);
        }

        public string GetRepoFetchUrl(string repository)
        {
            return GetRepoFetchUrl(_parameters.GitHubTestOrg, repository);
        }

        public string GetRepoFetchUrl(string org, string repository)
        {
            return $"https://{_parameters.GitHubUser}:{_parameters.GitHubToken}@github.com/{org}/{repository}";
        }

        public string GetAzDoRepoUrl(string repoName, string azdoAccount = "dnceng", string azdoProject = "internal")
        {
            return $"https://dev.azure.com/{azdoAccount}/{azdoProject}/_git/{repoName}";
        }

        public string GetAzDoApiRepoUrl(string repoName, string azdoAccount = "dnceng", string azdoProject = "internal")
        {
            return $"https://dev.azure.com/{azdoAccount}/{azdoProject}/_apis/git/repositories/{repoName}";
        }

        public Task<string> RunDarcAsyncWithInput(string input, params string[] args)
        {
            return TestHelpers.RunExecutableAsyncWithInput(_parameters.DarcExePath, input, args.Concat(new[]
            {
                "-p", _parameters.MaestroToken,
                "--bar-uri", _parameters.MaestroBaseUri,
                "--github-pat", _parameters.GitHubToken,
                "--azdev-pat", _parameters.AzDoToken,
            }).ToArray());
        }

        public Task<string> RunDarcAsync(params string[] args)
        {
            return TestHelpers.RunExecutableAsync(_parameters.DarcExePath, args.Concat(new[]
            {
                "-p", _parameters.MaestroToken,
                "--bar-uri", _parameters.MaestroBaseUri,
                "--github-pat", _parameters.GitHubToken,
                "--azdev-pat", _parameters.AzDoToken,
            }).ToArray());
        }

        public Task<string> RunGitAsync(params string[] args)
        {
            return TestHelpers.RunExecutableAsync(_parameters.GitExePath, args);
        }

        public async Task<AsyncDisposableValue<string>> CreateTestChannelAsync(string testChannelName)
        {
            string message = "";

            try
            {
                message = await RunDarcAsync("delete-channel", "--name", testChannelName).ConfigureAwait(false);
            }
            catch (MaestroTestException)
            {
                // If there are subscriptions associated the the channel then a previous test clean up failed
                // Run a subscription clean up and try again
                try
                {
                    await DeleteSubscriptionsForChannel(testChannelName).ConfigureAwait(false);
                    await RunDarcAsync("delete-channel", "--name", testChannelName).ConfigureAwait(false);
                }
                catch (MaestroTestException)
                {
                    // Otherwise ignore failures from delete-channel, its just a pre-cleanup that isn't really part of the test
                    // And if the test previously succeeded then it'll fail because the channel doesn't exist
                }
            }

            await RunDarcAsync("add-channel", "--name", testChannelName, "--classification", "test").ConfigureAwait(false);

            return AsyncDisposableValue.Create(testChannelName, async () =>
            {
                TestContext.WriteLine($"Cleaning up Test Channel {testChannelName}");
                try
                {
                    string doubleDelete = await RunDarcAsync("delete-channel", "--name", testChannelName).ConfigureAwait(false);
                }
                catch (MaestroTestException)
                {
                // Ignore failures from delete-channel on cleanup, this delete is here to ensure that the channel is deleted
                // even if the test does not do an explicit delete as part of the test. Other failures are typical that the channel has already been deleted.
            }
            });
        }

        public async Task<string> GetTestChannelsAsync()
        {
            return await RunDarcAsync("get-channels").ConfigureAwait(false);
        }

        public async Task DeleteTestChannelAsync(string testChannelName)
        {
            await RunDarcAsync("delete-channel", "--name", testChannelName).ConfigureAwait(false);
        }

        public async Task<string> AddDefaultTestChannelAsync(string testChannelName, string repoUri, string branchName)
        {
            return await RunDarcAsync("add-default-channel", "--channel", testChannelName, "--repo", repoUri, "--branch", branchName, "-q").ConfigureAwait(false);
        }

        public async Task<string> GetDefaultTestChannelsAsync(string repoUri, string branch)
        {
            return await RunDarcAsync("get-default-channels", "--source-repo", repoUri, "--branch", branch).ConfigureAwait(false);
        }

        public async Task DeleteDefaultTestChannelAsync(string testChannelName, string repoUri, string branch)
        {
            await RunDarcAsync("delete-default-channel", "--channel", testChannelName, "--repo", repoUri, "--branch", branch).ConfigureAwait(false);
        }

        public async Task<AsyncDisposableValue<string>> CreateSubscriptionAsync(
            string sourceChannelName,
            string sourceRepo,
            string targetRepo,
            string targetBranch,
            string updateFrequency,
            string sourceOrg = "dotnet",
            List<string> additionalOptions = null,
            bool sourceIsAzDo = false,
            bool targetIsAzDo = false,
            bool trigger = false)
        {
            string sourceUrl = sourceIsAzDo ? GetAzDoRepoUrl(sourceRepo) : GetRepoUrl(sourceOrg, sourceRepo);
            string targetUrl = targetIsAzDo ? GetAzDoRepoUrl(targetRepo) : GetRepoUrl(targetRepo);

            string[] command = {"add-subscription", "-q",
                "--channel", sourceChannelName,
                "--source-repo", sourceUrl,
                "--target-repo", targetUrl,
                "--target-branch", targetBranch,
                "--update-frequency", updateFrequency,
                trigger?"--trigger":"--no-trigger"};

            if (additionalOptions != null)
            {
                command = command.Concat(additionalOptions).ToArray();
            }

            string output = await RunDarcAsync(command).ConfigureAwait(false);

            Match match = Regex.Match(output, "Successfully created new subscription with id '([a-f0-9-]+)'");
            if (!match.Success)
            {
                throw new MaestroTestException("Unable to create subscription.");
            }

            string subscriptionId = match.Groups[1].Value;
            return AsyncDisposableValue.Create(subscriptionId, async () =>
            {
                TestContext.WriteLine($"Cleaning up Test Subscription {subscriptionId}");
                try
                {
                    await RunDarcAsync("delete-subscriptions", "--id", subscriptionId, "--quiet").ConfigureAwait(false);
                }
                catch (MaestroTestException)
                {
                // If this throws an exception the most likely cause is that the subscription was deleted as part of the test case
            }
            });
        }

        public async Task<AsyncDisposableValue<string>> CreateSubscriptionAsync(string yamlDefinition)
        {
            string output = await RunDarcAsyncWithInput(yamlDefinition, "add-subscription", "-q", "--read-stdin", "--no-trigger").ConfigureAwait(false);

            Match match = Regex.Match(output, "Successfully created new subscription with id '([a-f0-9-]+)'");
            if (match.Success)
            {
                string subscriptionId = match.Groups[1].Value;
                return AsyncDisposableValue.Create(subscriptionId, async () =>
                {
                    TestContext.WriteLine($"Cleaning up Test Subscription {subscriptionId}");
                    try
                    {
                        await RunDarcAsync("delete-subscriptions", "--id", subscriptionId, "--quiet").ConfigureAwait(false);
                    }
                    catch (MaestroTestException)
                    {
                    // If this throws an exception the most likely cause is that the subscription was deleted as part of the test case
                }
                });
            }

            throw new MaestroTestException("Unable to create subscription.");
        }

        public async Task<string> GetSubscriptionInfo(string subscriptionId)
        {
            return await RunDarcAsync("get-subscriptions", "--ids", subscriptionId).ConfigureAwait(false);
        }

        public async Task<string> GetSubscriptions(string channelName)
        {
            return await RunDarcAsync("get-subscriptions", "--channel", channelName);
        }

        public async Task ValidateSubscriptionInfo(string subscriptionId, string expectedSubscriptionInfo)
        {
            string subscriptionInfo = await GetSubscriptionInfo(subscriptionId);
            StringAssert.AreEqualIgnoringCase(expectedSubscriptionInfo, subscriptionInfo);
        }

        public async Task SetSubscriptionStatus(bool enableSub, string subscriptionId = null, string channelName = null)
        {
            string actionToTake = enableSub ? "--enable" : "-d";

            if (channelName != null)
            {
                await RunDarcAsync("subscription-status", actionToTake, "--channel", channelName, "--quiet").ConfigureAwait(false);
            }
            else
            {
                await RunDarcAsync("subscription-status", "--id", subscriptionId, actionToTake, "--quiet").ConfigureAwait(false);
            }
        }

        public async Task<string> DeleteSubscriptionsForChannel(string channelName)
        {
            return await RunDarcAsync("delete-subscriptions", "--channel", channelName, "--quiet");
        }

        public async Task<string> DeleteSubscriptionById(string subscriptionId)
        {
            return await RunDarcAsync("delete-subscriptions", "--id", subscriptionId, "--quiet").ConfigureAwait(false);
        }

        public Task<Build> CreateBuildAsync(string repositoryUrl, string branch, string commit, string buildNumber, IImmutableList<AssetData> assets)
        {
            return CreateBuildAsync(repositoryUrl, branch, commit, buildNumber, assets, ImmutableList<BuildRef>.Empty);
        }

        public async Task<Build> CreateBuildAsync(string repositoryUrl, string branch, string commit, string buildNumber, IImmutableList<AssetData> assets, IImmutableList<BuildRef> dependencies)
        {
            Build build = await MaestroApi.Builds.CreateAsync(new BuildData(
                commit: commit,
                azureDevOpsAccount: _parameters.AzureDevOpsAccount,
                azureDevOpsProject: _parameters.AzureDevOpsProject,
                azureDevOpsBuildNumber: buildNumber,
                azureDevOpsRepository: repositoryUrl,
                azureDevOpsBranch: branch,
                released: false,
                stable: false)
            {
                AzureDevOpsBuildId = _parameters.AzureDevOpsBuildId,
                AzureDevOpsBuildDefinitionId = _parameters.AzureDevOpsBuildDefinitionId,
                GitHubRepository = repositoryUrl,
                GitHubBranch = branch,
                Assets = assets,
                Dependencies = dependencies,
            });

            return build;
        }

        public async Task<string> GetDarcBuildAsync(int buildId)
        {
            string buildString = await RunDarcAsync("get-build", "--id", buildId.ToString());
            return buildString;
        }

        public async Task<string> UpdateBuildAsync(int buildId, string updateParams)
        {
            string buildString = await RunDarcAsync("update-build", "--id", buildId.ToString(), updateParams);
            return buildString;
        }

        public async Task AddDependenciesToLocalRepo(string repoPath, List<AssetData> dependencies, string repoUri, string coherentParent = "")
        {
            using (ChangeDirectory(repoPath))
            {
                foreach (AssetData asset in dependencies)
                {
                    List<string> parameters = new List<string>() { "add-dependency", "--name", asset.Name, "--type", "product", "--repo", repoUri };

                    if (!String.IsNullOrEmpty(coherentParent))
                    {
                        parameters.Add("--coherent-parent");
                        parameters.Add(coherentParent);
                    }
                    string[] parameterArr = parameters.ToArray();

                    await RunDarcAsync(parameterArr);
                }
            }
        }

        public async Task<string> GatherDrop(int buildId, string outputDir, bool includeReleased)
        {
            string[] args = new[] { "gather-drop", "--id", buildId.ToString(), "--dry-run", "--output-dir", outputDir };

            if (includeReleased)
            {
                args = args.Append("--include-released").ToArray();
            }

            return await RunDarcAsync(args);
        }

        public async Task TriggerSubscriptionAsync(string subscriptionId)
        {
            await MaestroApi.Subscriptions.TriggerSubscriptionAsync(Guid.Parse(subscriptionId));
        }

        public async Task<IAsyncDisposable> AddBuildToChannelAsync(int buildId, string channelName)
        {
            await RunDarcAsync("add-build-to-channel", "--id", buildId.ToString(), "--channel", channelName, "--skip-assets-publishing");
            return AsyncDisposable.Create(async () =>
            {
                TestContext.WriteLine($"Removing build {buildId} from channel {channelName}");
                await RunDarcAsync("delete-build-from-channel", "--id", buildId.ToString(), "--channel", channelName);
            });
        }

        public async Task DeleteBuildFromChannelAsync(string buildId, string channelName)
        {
            await RunDarcAsync("delete-build-from-channel", "--id", buildId, "--channel", channelName);
        }

        public IDisposable ChangeDirectory(string directory)
        {
            string old = Directory.GetCurrentDirectory();
            TestContext.WriteLine($"Switching to directory {directory}");
            Directory.SetCurrentDirectory(directory);
            return Disposable.Create(() =>
            {
                TestContext.WriteLine($"Switching back to directory {old}");
                Directory.SetCurrentDirectory(old);
            });
        }

        public Task<TemporaryDirectory> CloneRepositoryAsync(string repository)
        {
            return CloneRepositoryAsync(_parameters.GitHubTestOrg, repository);
        }

        public async Task<TemporaryDirectory> CloneRepositoryAsync(string org, string repository)
        {
            using var shareable = Shareable.Create(TemporaryDirectory.Get());
            string directory = shareable.Peek()!.Directory;

            string fetchUrl = GetRepoFetchUrl(org, repository);
            await RunGitAsync("clone", "--quiet", fetchUrl, directory).ConfigureAwait(false);

            using (ChangeDirectory(directory))
            {
                await RunGitAsync("config", "user.email", $"{_parameters.GitHubUser}@test.com").ConfigureAwait(false);
                await RunGitAsync("config", "user.name", _parameters.GitHubUser).ConfigureAwait(false);
                await RunGitAsync("config", "gc.auto", "0").ConfigureAwait(false);
                await RunGitAsync("config", "advice.detachedHead", "false").ConfigureAwait(false);
                await RunGitAsync("config", "color.ui", "false").ConfigureAwait(false);
            }

            return shareable.TryTake()!;
        }

        public string GetAzDoRepoAuthUrl(string repoName)
        {
            return $"https://{_parameters.GitHubUser}:{_parameters.AzDoToken}@dev.azure.com/{_parameters.AzureDevOpsAccount}/{_parameters.AzureDevOpsProject}/_git/{repoName}";
        }

        public async Task<TemporaryDirectory> CloneAzDoRepositoryAsync(string repoName, string targetBranch)
        {
            using var shareable = Shareable.Create(TemporaryDirectory.Get());
            string directory = shareable.Peek()!.Directory;

            string authUrl = GetAzDoRepoAuthUrl(repoName);
            await RunGitAsync("clone", "--quiet", authUrl, directory).ConfigureAwait(false);

            using (ChangeDirectory(directory))
            {
                // The GitHubUser and AzDoUser have the same user name so this uses the existing parameter
                await RunGitAsync("config", "user.email", $"{_parameters.GitHubUser}@test.com").ConfigureAwait(false);
                await RunGitAsync("config", "user.name", _parameters.GitHubUser).ConfigureAwait(false);
            }

            //  string fetchUrl = GetRepoFetchUrl(org, repository);
            //string repoUrl = GetAzDoRepoUrl(repoName);
            //_parameters.AzDoClient.Clone(repoUrl, targetBranch, directory);


            return shareable.TryTake()!;
        }

        public async Task CheckoutRemoteRefAsync(string commit)
        {
            await RunGitAsync("fetch", "origin", commit);
            await RunGitAsync("checkout", commit);
        }

        public async Task CheckoutRemoteBranchAsync(string branchName)
        {
            await RunGitAsync("fetch", "origin", branchName);
            await RunGitAsync("checkout", branchName);
        }

        public async Task<IAsyncDisposable> CheckoutBranchAsync(string branchName)
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

        public async Task DeleteBranchAsync(string branchName)
        {
            await RunGitAsync("push", "origin", "--delete", branchName);

        }

        internal IImmutableList<AssetData> GetAssetData(string asset1Name, string asset1Version, string asset2Name, string asset2Version)
        {
            string location = @"https://pkgs.dev.azure.com/dnceng/public/_packaging/NotARealFeed/nuget/v3/index.json";
            LocationType locationType = LocationType.NugetFeed;

            AssetData asset1 = GetAssetDataWithLocations(asset1Name, asset1Version, location, locationType);

            AssetData asset2 = GetAssetDataWithLocations(asset2Name, asset2Version, location, locationType);

            return ImmutableList.Create(asset1, asset2);
        }

        internal AssetData GetAssetDataWithLocations(string assetName, string assetVersion,
            string assetLocation1, LocationType assetLocationType1,
            string assetLocation2 = null, LocationType assetLocationType2 = LocationType.None)
        {
            var locationsListBuilder = ImmutableList.CreateBuilder<AssetLocationData>();

            AssetLocationData location1 = new AssetLocationData(assetLocationType1)
            { Location = assetLocation1 };
            locationsListBuilder.Add(location1);

            if (assetLocation2 != null && assetLocationType2 != LocationType.None)
            {
                AssetLocationData location2 = new AssetLocationData(assetLocationType2)
                { Location = assetLocation2 };
                locationsListBuilder.Add(location2);
            }

            AssetData asset = new AssetData(false)
            {
                Name = assetName,
                Version = assetVersion,
                Locations = locationsListBuilder.ToImmutable()
            };

            return asset;
        }

        internal IImmutableList<AssetData> GetSingleAssetData(string assetName, string assetVersion)
        {
            AssetData asset = new AssetData(false)
            {
                Name = assetName,
                Version = assetVersion,
                Locations = ImmutableList.Create(new AssetLocationData(LocationType.NugetFeed)
                { Location = @"https://pkgs.dev.azure.com/dnceng/public/_packaging/NotARealFeed/nuget/v3/index.json" })
            };

            return ImmutableList.Create(asset);
        }

        public async Task SetRepositoryPolicies(string repoUri, string branchName, string[] policyParams = null)
        {
            string[] commandParams = { "set-repository-policies", "-q", "--repo", repoUri, "--branch", branchName };

            if (policyParams != null)
            {
                commandParams = commandParams.Concat(policyParams).ToArray();
            }

            await RunDarcAsync(commandParams);
        }

        public async Task<string> GetRepositoryPolicies(string repoUri, string branchName)
        {
            return await RunDarcAsync("get-repository-policies", "--all", "--repo", repoUri, "--branch", branchName);
        }
    }
}
