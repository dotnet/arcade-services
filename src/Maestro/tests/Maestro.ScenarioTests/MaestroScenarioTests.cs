using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Octokit;
using Xunit;
using Xunit.Abstractions;

namespace Maestro.ScenarioTests
{
    public class MaestroScenarioTests : IAsyncLifetime
    {
        private TestParameters _parameters;
        private ScenarioTestHelpers _testHelpers;

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
            _testHelpers = new ScenarioTestHelpers(_parameters, _output);
        }

        public Task DisposeAsync()
        {
            _parameters.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task ArcadeSdkUpdate()
        {
            string testChannelName = "Test Channel " + _random.Next(int.MaxValue);
            var sourceRepo = "arcade";
            var sourceRepoUri = "https://github.com/dotnet/arcade";
            var sourceBranch = "dependencyflow-tests";
            var sourceCommit = "0b36b99e29b1751403e23cfad0a7dff585818051";
            var sourceBuildNumber = _random.Next(int.MaxValue).ToString();
            ImmutableList<AssetData> sourceAssets = ImmutableList.Create<AssetData>()
                .Add(new AssetData(true)
                {
                    Name = "Microsoft.DotNet.Arcade.Sdk",
                    Version = "2.1.0",
                });
            var targetRepo = "maestro-test2";
            var targetBranch = _random.Next(int.MaxValue).ToString();
            await using AsyncDisposableValue<string> channel = await _testHelpers.CreateTestChannelAsync(testChannelName).ConfigureAwait(false);
            await using AsyncDisposableValue<string> sub = await _testHelpers.CreateSubscriptionAsync(testChannelName, sourceRepo, targetRepo, targetBranch, "none");
            int buildId = await _testHelpers.CreateBuildAsync(_testHelpers.GetRepoUrl("dotnet", sourceRepo), sourceBranch, sourceCommit, sourceBuildNumber, sourceAssets);
            await using IAsyncDisposable _ = await _testHelpers.AddBuildToChannelAsync(buildId, testChannelName);

            using TemporaryDirectory repo = await _testHelpers.CloneRepositoryAsync(targetRepo);
            using (_testHelpers.ChangeDirectory(repo.Directory))
            {
                await _testHelpers.RunGitAsync("checkout", "-b", targetBranch).ConfigureAwait(false);
                await _testHelpers.RunDarcAsync("add-dependency",
                    "--name", "Microsoft.DotNet.Arcade.Sdk",
                    "--type", "toolset",
                    "--repo", sourceRepoUri);
                await _testHelpers.RunGitAsync("commit", "-am", "Add dependencies.");
                await using IAsyncDisposable ___ = await _testHelpers.PushGitBranchAsync("origin", targetBranch);
                await _testHelpers.TriggerSubscriptionAsync(sub.Value);

                PullRequest pr = await _testHelpers.WaitForPullRequestAsync(targetRepo, targetBranch);

                Assert.Equal($"[{targetBranch}] Update dependencies from dotnet/arcade", pr.Title);

                await _testHelpers.CheckoutRemoteRefAsync(pr.MergeCommitSha);

                string dependencies = await _testHelpers.RunDarcAsync("get-dependencies");
                string[] dependencyLines = dependencies.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(new[]
                {
                    "Name:             Microsoft.DotNet.Arcade.Sdk",
                    "Version:          2.1.0",
                    $"Repo:             {sourceRepoUri}",
                    $"Commit:           {sourceCommit}",
                    "Type:             Toolset",
                    "Pinned:           False",
                }, dependencyLines);

                using TemporaryDirectory arcadeRepo = await _testHelpers.CloneRepositoryAsync("dotnet", sourceRepo);
                using (_testHelpers.ChangeDirectory(arcadeRepo.Directory))
                {
                    await _testHelpers.CheckoutRemoteRefAsync(sourceCommit);
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
    }
}
