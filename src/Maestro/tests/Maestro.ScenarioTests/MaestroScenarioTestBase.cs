using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;
using Octokit;

namespace Maestro.ScenarioTests
{
    public class MaestroScenarioTestBase
    {
        private TestParameters _parameters;

        public IMaestroApi MaestroApi => _parameters.MaestroApi;

        public GitHubClient GitHubApi => _parameters.GitHubApi;

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

        public async Task CheckBatchedGitHubPullRequest(string targetBranch, string source1RepoName, string source2RepoName,
            string targetRepoName, List<Microsoft.DotNet.DarcLib.DependencyDetail> expectedDependencies, string repoDirectory)
        {
            string expectedPRTitle = $"[{targetBranch}] Update dependencies from {_parameters.GitHubTestOrg}/{source1RepoName} {_parameters.GitHubTestOrg}/{source2RepoName}";
            await CheckGitHubPullRequest(expectedPRTitle, targetRepoName, targetBranch, expectedDependencies, repoDirectory, false);
        }

        public async Task CheckNonBatchedGitHubPullRequest(string targetbranch, string sourceRepoName, string targetRepoName, string targetBranch,
            List<Microsoft.DotNet.DarcLib.DependencyDetail> expectedDependencies, string repoDirectory, bool complete = false)
        {
            string expectedPRTitle = $"[{targetBranch}] Update dependencies from {_parameters.GitHubTestOrg}/{sourceRepoName}";
            await CheckGitHubPullRequest(expectedPRTitle, targetRepoName, targetBranch, expectedDependencies, repoDirectory, complete);
        }

        public async Task CheckGitHubPullRequest(string expectedPRTitle, string targetRepoName, string targetBranch,
            List<Microsoft.DotNet.DarcLib.DependencyDetail> expectedDependencies, string repoDirectory, bool complete)
        {
            TestContext.WriteLine($"Checking opened PR in {targetBranch} {targetRepoName}");
            PullRequest pullRequest = await WaitForPullRequestAsync(targetRepoName, targetBranch);

            StringAssert.AreEqualIgnoringCase(expectedPRTitle, pullRequest.Title);
            ItemState expectedPRState = complete ? ItemState.Closed : ItemState.Open;
            Assert.AreEqual(expectedPRState, pullRequest.State);

            using (ChangeDirectory(repoDirectory))
            {

            }
        }

        public async Task GitCommitAsync(string message)
        {
            await RunGitAsync("commit", "-am", message);
        }

        public async Task<IAsyncDisposable> PushGitBranchAsync(string remote, string branch)
        {
            await RunGitAsync("push", remote, branch);
            return AsyncDisposable.Create(async () =>
            {
                TestContext.WriteLine($"Cleaning up Remote branch {branch}");
                await RunGitAsync("push", remote, "--delete", branch);
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
            try
            {
                await RunDarcAsync("delete-channel", "--name", testChannelName).ConfigureAwait(false);
            }
            catch (MaestroTestException)
            {
                // Ignore failures from delete-channel, its just a pre-cleanup that isn't really part of the test
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
                    // Ignore failures from delete-channel, this delete is here to ensure that the channel is deleted
                    // even if the test does not do an explicit delete as part of the test
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
            bool targetIsAzDo = false)
        {
            string sourceUrl = sourceIsAzDo ? GetAzDoRepoUrl(sourceRepo, azdoProject: sourceOrg) : GetRepoUrl(sourceOrg, sourceRepo);
            string targetUrl = targetIsAzDo ? GetAzDoRepoUrl(targetRepo) : GetRepoUrl(targetRepo);

            string[] command = {"add-subscription", "-q", "--no-trigger",
                "--channel", sourceChannelName,
                "--source-repo", sourceUrl,
                "--target-repo", targetUrl,
                "--target-branch", targetBranch,
                "--update-frequency", updateFrequency};

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

        public async Task AddDependenciesToLocalRepo(string repoPath, List<AssetData> dependencies, string repoUri)
        {
            using (ChangeDirectory(repoPath))
            {
                foreach (AssetData asset in dependencies)
                {
                    await RunDarcAsync("add-dependency", "--name", asset.Name, "--type", "product", "--repo", repoUri);
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

        public async Task CheckoutRemoteRefAsync(string commit)
        {
            await RunGitAsync("fetch", "origin", commit);
            await RunGitAsync("checkout", commit);
        }

        public async Task CheckoutBranchAsync(string branchName)
        {
            await RunGitAsync("checkout", "-B", branchName);
        }

        internal IImmutableList<AssetData> GetAssetData(string asset1Name, string asset1Version, string asset2Name, string asset2Version)
        {
            AssetData asset1 = new AssetData(false)
            {
                Name = asset1Name,
                Version = asset1Version,
                Locations = ImmutableList.Create(new AssetLocationData(LocationType.NugetFeed)
                { Location = @"https://pkgs.dev.azure.com/dnceng/public/_packaging/NotARealFeed/nuget/v3/index.json" })
            };

            AssetData asset2 = new AssetData(false)
            {
                Name = asset2Name,
                Version = asset2Version,
                Locations = ImmutableList.Create(new AssetLocationData(LocationType.NugetFeed)
                { Location = @"https://pkgs.dev.azure.com/dnceng/public/_packaging/NotARealFeed/nuget/v3/index.json" })
            };

            return ImmutableList.Create(asset1, asset2);
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
