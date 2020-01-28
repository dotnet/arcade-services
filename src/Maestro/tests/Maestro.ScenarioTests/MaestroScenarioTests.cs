using System;
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
    public class MaestroScenarioTests : IAsyncLifetime
    {
        private TestParameters _parameters;

        private readonly ITestOutputHelper _output;
        private readonly Random _random = new Random();

        public IMaestroApi MaestroApi => _parameters.MaestroApi;

        public GitHubClient GitHubApi => _parameters.GitHubApi;

        public MaestroScenarioTests(ITestOutputHelper output)
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
            return Task.CompletedTask;
        }

        [Fact]
        public async Task ArcadeSdkUpdate()
        {
            var testChannelName = "Test Channel " + _random.Next(int.MaxValue);
            var sourceRepo = "arcade";
            var sourceRepoUri = "https://github.com/dotnet/arcade";
            var sourceBranch = "dependencyflow-tests";
            var sourceCommit = "0b36b99e29b1751403e23cfad0a7dff585818051";
            var sourceBuildNumber = _random.Next(int.MaxValue).ToString();
            var sourceAssets = ImmutableList.Create<AssetData>()
                .Add(new AssetData(true)
                {
                    Name = "Microsoft.DotNet.Arcade.Sdk",
                    Version = "2.1.0",
                });
            var targetRepo = "maestro-test2";
            var targetBranch = _random.Next(int.MaxValue).ToString();
            await using var channel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false);
            await using var sub = await CreateSubscriptionAsync(testChannelName, sourceRepo, targetRepo, targetBranch, "none");
            var buildId = await CreateBuildAsync(GetRepoUrl("dotnet", sourceRepo), sourceBranch, sourceCommit, sourceBuildNumber, sourceAssets);
            await using var _ = await AddBuildToChannelAsync(buildId, testChannelName);

            using var repo = await CloneRepositoryAsync(targetRepo);
            using (ChangeDirectory(repo.Directory))
            {
                await RunGitAsync("checkout", "-b", targetBranch).ConfigureAwait(false);
                await RunDarcAsync("add-dependency",
                    "--name", "Microsoft.DotNet.Arcade.Sdk",
                    "--type", "toolset",
                    "--repo", sourceRepoUri);
                await RunGitAsync("commit", "-am", "Add dependencies.");
                await using var ___ = await PushGitBranchAsync("origin", targetBranch);
                await TriggerSubscriptionAsync(sub.Value);

                var pr = await WaitForPullRequestAsync(targetRepo, targetBranch);

                Assert.Equal($"[{targetBranch}] Update dependencies from dotnet/arcade", pr.Title);

                await CheckoutRemoteRefAsync(pr.MergeCommitSha);

                var dependencies = await RunDarcAsync("get-dependencies");
                var dependencyLines = dependencies.Split(new[] {'\n', '\r'}, StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(new[]
                {
                    "Name:             Microsoft.DotNet.Arcade.Sdk",
                    "Version:          2.1.0",
                    $"Repo:             {sourceRepoUri}",
                    $"Commit:           {sourceCommit}",
                    "Type:             Toolset",
                    "Pinned:           False",
                }, dependencyLines);

                using var arcadeRepo = await CloneRepositoryAsync("dotnet", sourceRepo);
                using (ChangeDirectory(arcadeRepo.Directory))
                {
                    await CheckoutRemoteRefAsync(sourceCommit);
                }

                var arcadeFiles = Directory.EnumerateFileSystemEntries(Path.Join(arcadeRepo.Directory, "eng", "common"),
                        "*", SearchOption.AllDirectories)
                    .Select(s => s.Substring(arcadeRepo.Directory.Length))
                    .ToHashSet();
                var repoFiles = Directory.EnumerateFileSystemEntries(Path.Join(repo.Directory, "eng", "common"), "*",
                        SearchOption.AllDirectories)
                    .Select(s => s.Substring(repo.Directory.Length))
                    .ToHashSet();

                Assert.Empty(arcadeFiles.Except(repoFiles));
                Assert.Empty(repoFiles.Except(arcadeFiles));
            }
        }

        private async Task<PullRequest> WaitForPullRequestAsync(string targetRepo, string targetBranch)
        {
            var repo = await GitHubApi.Repository.Get(_parameters.GitHubTestOrg, targetRepo).ConfigureAwait(false);
            var attempts = 10;
            while (attempts-- > 0)
            {
                var prs = await GitHubApi.PullRequest.GetAllForRepository(repo.Id, new PullRequestRequest
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

        private async Task<IAsyncDisposable> PushGitBranchAsync(string remote, string branch)
        {
            await RunGitAsync("push", remote, branch);
            return AsyncDisposable.Create(async () =>
            {
                _output.WriteLine($"Cleaning up Remote branch {branch}");
                await RunGitAsync("push", remote, "--delete", branch);
            });
        }

        private string GetRepoUrl(string org, string repository)
        {
            return $"https://github.com/{org}/{repository}";
        }

        private string GetRepoUrl(string repository)
        {
            return GetRepoUrl(_parameters.GitHubTestOrg, repository);
        }

        private string GetRepoFetchUrl(string repository)
        {
            return GetRepoFetchUrl(_parameters.GitHubTestOrg, repository);
        }

        private string GetRepoFetchUrl(string org, string repository)
        {
            return $"https://{_parameters.GitHubUser}:{_parameters.GitHubToken}@github.com/{org}/{repository}";
        }

        private Task<string> RunDarcAsync(params string[] args)
        {
            return TestHelpers.RunExecutableAsync(_output, _parameters.DarcExePath, args.Concat(new[]
            {
                "-p", _parameters.MaestroToken,
                "--bar-uri", _parameters.MaestroBaseUri,
                "--github-pat", _parameters.GitHubToken,
            }).ToArray());
        }

        private Task<string> RunGitAsync(params string[] args)
        {
            return TestHelpers.RunExecutableAsync(_output, _parameters.GitExePath, args);
        }

        private async Task<AsyncDisposableValue<string>> CreateTestChannelAsync(string testChannelName)
        {
            try
            {
                await RunDarcAsync("delete-channel", "--name", testChannelName).ConfigureAwait(false);
            }
            catch (XunitException) { }

            await RunDarcAsync("add-channel", "--name", testChannelName, "--classification", "test").ConfigureAwait(false);

            return AsyncDisposableValue.Create(testChannelName, async () =>
            {
                _output.WriteLine($"Cleaning up Test Channel {testChannelName}");
                await RunDarcAsync("delete-channel", "--name", testChannelName).ConfigureAwait(false);
            });
        }

        private async Task<AsyncDisposableValue<string>> CreateSubscriptionAsync(string sourceChannelName, string sourceRepo, string targetRepo, string targetBranch, string updateFrequency)
        {
            var output = await RunDarcAsync("add-subscription", "-q", "--no-trigger",
                "--channel", sourceChannelName,
                "--source-repo", GetRepoUrl("dotnet", sourceRepo),
                "--target-repo", GetRepoUrl(targetRepo),
                "--target-branch", targetBranch,
                "--update-frequency", updateFrequency).ConfigureAwait(false);

            var match = Regex.Match(output, "Successfully created new subscription with id '([a-f0-9-]+)'");
            if (match.Success)
            {
                var subscriptionId = match.Groups[1].Value;
                return AsyncDisposableValue.Create(subscriptionId, async () =>
                {
                    _output.WriteLine($"Cleaning up Test Subscription {subscriptionId}");
                    await RunDarcAsync("delete-subscriptions", "--id", subscriptionId, "--quiet").ConfigureAwait(false);
                });
            }

            throw new XunitException("Unable to create subscription.");
        }

        private Task<int> CreateBuildAsync(string repositoryUrl, string branch, string commit, string buildNumber, IImmutableList<AssetData> assets)
        {
            return CreateBuildAsync(repositoryUrl, branch, commit, buildNumber, assets, ImmutableList<BuildRef>.Empty);
        }

        private async Task<int> CreateBuildAsync(string repositoryUrl, string branch, string commit, string buildNumber, IImmutableList<AssetData> assets, IImmutableList<BuildRef> dependencies)
        {
            var build = await MaestroApi.Builds.CreateAsync(new BuildData(
                commit: commit,
                azureDevOpsAccount: "dnceng",
                azureDevOpsProject: "internal",
                azureDevOpsBuildNumber: buildNumber,
                azureDevOpsRepository: repositoryUrl,
                azureDevOpsBranch: branch,
                publishUsingPipelines: false,
                released: false)
            {
                AzureDevOpsBuildId = 144618,
                AzureDevOpsBuildDefinitionId = 6,
                GitHubRepository = repositoryUrl,
                GitHubBranch = branch,
                Assets = assets,
                Dependencies = dependencies,
            });

            return build.Id;
        }

        private async Task TriggerSubscriptionAsync(string subscriptionId)
        {
            await MaestroApi.Subscriptions.TriggerSubscriptionAsync(Guid.Parse(subscriptionId));
        }

        private async Task<IAsyncDisposable> AddBuildToChannelAsync(int buildId, string channelName)
        {
            await RunDarcAsync("add-build-to-channel", "--id", buildId.ToString(), "--channel", channelName);
            return AsyncDisposable.Create(async () =>
            {
                _output.WriteLine($"Removing build {buildId} from channel {channelName}");
                await RunDarcAsync("delete-build-from-channel", "--id", buildId.ToString(), "--channel", channelName);
            });
        }

        private IDisposable ChangeDirectory(string directory)
        {
            var old = Directory.GetCurrentDirectory();
            _output.WriteLine($"Switching to directory {directory}");
            Directory.SetCurrentDirectory(directory);
            return Disposable.Create(() =>
            {
                _output.WriteLine($"Switching back to directory {old}");
                Directory.SetCurrentDirectory(old);
            });
        }

        private Task<TemporaryDirectory> CloneRepositoryAsync(string repository)
        {
            return CloneRepositoryAsync(_parameters.GitHubTestOrg, repository);
        }

        private async Task<TemporaryDirectory> CloneRepositoryAsync(string org, string repository)
        {
            using var shareable = Shareable.Create(TemporaryDirectory.Get());
            var directory = shareable.Peek()!.Directory;

            var fetchUrl = GetRepoFetchUrl(org, repository);
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

        private async Task CheckoutRemoteRefAsync(string commit)
        {
            await RunGitAsync("fetch", "origin", commit);
            await RunGitAsync("checkout", commit);
        }
    }
}
