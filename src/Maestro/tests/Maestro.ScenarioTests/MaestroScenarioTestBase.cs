using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Octokit;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Maestro.ScenarioTests
{
    public class MaestroScenarioTestBase : IAsyncLifetime
    {
        private TestParameters _parameters;
        private readonly ITestOutputHelper _output;

        internal readonly Random _random = new Random();

        public IMaestroApi MaestroApi => _parameters.MaestroApi;

        public GitHubClient GitHubApi => _parameters.GitHubApi;

        public MaestroScenarioTestBase(ITestOutputHelper output)
        {
            _parameters = null!;
            _output = output;
        }

        public async Task InitializeAsync()
        {
            _parameters = await TestParameters.GetAsync(_output);
        }

        public Task DisposeAsync()
        {
            _parameters.Dispose();
            return Task.CompletedTask;
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
                    throw new XunitException($"More than one pull request found in {targetRepo} targeting {targetBranch}");
                }

                await Task.Delay(60 * 1000).ConfigureAwait(false);
            }

            throw new XunitException($"No pull request was created in {targetRepo} targeting {targetBranch}");
        }

        public async Task<IAsyncDisposable> PushGitBranchAsync(string remote, string branch)
        {
            await RunGitAsync("push", remote, branch);
            return AsyncDisposable.Create(async () =>
            {
                _output.WriteLine($"Cleaning up Remote branch {branch}");
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

        public Task<string> RunDarcAsync(params string[] args)
        {
            return TestHelpers.RunExecutableAsync(_output, _parameters.DarcExePath, args.Concat(new[]
            {
                "-p", _parameters.MaestroToken,
                "--bar-uri", _parameters.MaestroBaseUri,
                "--github-pat", _parameters.GitHubToken,
            }).ToArray());
        }

        public Task<string> RunGitAsync(params string[] args)
        {
            return TestHelpers.RunExecutableAsync(_output, _parameters.GitExePath, args);
        }

        public async Task<AsyncDisposableValue<string>> CreateTestChannelAsync(string testChannelName)
        {
            try
            {
                await RunDarcAsync("delete-channel", "--name", testChannelName).ConfigureAwait(false);
            }
            catch (XunitException)
            {
                // Ignore failures from delete-channel, its just a pre-cleanup that isn't really part of the test
            }

            await RunDarcAsync("add-channel", "--name", testChannelName, "--classification", "test").ConfigureAwait(false);

            return AsyncDisposableValue.Create(testChannelName, async () =>
            {
                _output.WriteLine($"Cleaning up Test Channel {testChannelName}");
                await RunDarcAsync("delete-channel", "--name", testChannelName).ConfigureAwait(false);
            });
        }

        public async Task<AsyncDisposableValue<string>> CreateSubscriptionAsync(string sourceChannelName, string sourceRepo, string targetRepo, string targetBranch, string updateFrequency)
        {
            string output = await RunDarcAsync("add-subscription", "-q", "--no-trigger",
                "--channel", sourceChannelName,
                "--source-repo", GetRepoUrl("dotnet", sourceRepo),
                "--target-repo", GetRepoUrl(targetRepo),
                "--target-branch", targetBranch,
                "--update-frequency", updateFrequency).ConfigureAwait(false);

            Match match = Regex.Match(output, "Successfully created new subscription with id '([a-f0-9-]+)'");
            if (match.Success)
            {
                string subscriptionId = match.Groups[1].Value;
                return AsyncDisposableValue.Create(subscriptionId, async () =>
                {
                    _output.WriteLine($"Cleaning up Test Subscription {subscriptionId}");
                    await RunDarcAsync("delete-subscriptions", "--id", subscriptionId, "--quiet").ConfigureAwait(false);
                });
            }

            throw new XunitException("Unable to create subscription.");
        }

        public Task<int> CreateBuildAsync(string repositoryUrl, string branch, string commit, string buildNumber, IImmutableList<AssetData> assets)
        {
            return CreateBuildAsync(repositoryUrl, branch, commit, buildNumber, assets, ImmutableList<BuildRef>.Empty);
        }

        public async Task<int> CreateBuildAsync(string repositoryUrl, string branch, string commit, string buildNumber, IImmutableList<AssetData> assets, IImmutableList<BuildRef> dependencies)
        {
            Build build = await MaestroApi.Builds.CreateAsync(new BuildData(
                commit: commit,
                azureDevOpsAccount: _parameters.AzureDevOpsAccount,
                azureDevOpsProject: _parameters.AzureDevOpsProject,
                azureDevOpsBuildNumber: buildNumber,
                azureDevOpsRepository: repositoryUrl,
                azureDevOpsBranch: branch,
                publishUsingPipelines: false,
                released: false)
            {
                AzureDevOpsBuildId = _parameters.AzureDevOpsBuildId,
                AzureDevOpsBuildDefinitionId = _parameters.AzureDevOpsBuildDefinitionId,
                GitHubRepository = repositoryUrl,
                GitHubBranch = branch,
                Assets = assets,
                Dependencies = dependencies,
            });

            return build.Id;
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
                _output.WriteLine($"Removing build {buildId} from channel {channelName}");
                await RunDarcAsync("delete-build-from-channel", "--id", buildId.ToString(), "--channel", channelName);
            });
        }

        public IDisposable ChangeDirectory(string directory)
        {
            string old = Directory.GetCurrentDirectory();
            _output.WriteLine($"Switching to directory {directory}");
            Directory.SetCurrentDirectory(directory);
            return Disposable.Create(() =>
            {
                _output.WriteLine($"Switching back to directory {old}");
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
    }
}
