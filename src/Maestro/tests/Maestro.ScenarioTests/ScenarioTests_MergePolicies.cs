using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;


namespace Maestro.ScenarioTests
{
    [TestFixture]
    [Category("PostDeployment")]
    public class ScenarioTests_MergePolicies : MaestroScenarioTestBase
    {
        private TestParameters _parameters;
        private Random _random = new Random();

        [SetUp]
        public async Task InitializeAsync()
        {
            Environment.SetEnvironmentVariable("MAESTRO_BASEURI", "https://1072664df082.ngrok.io");
            Environment.SetEnvironmentVariable("DARC_PACKAGE_SOURCE", @"C:\Users\t-lorisw\arcade-services\artifacts\packages\Debug\NonShipping\");
            _parameters = await TestParameters.GetAsync();
            SetTestParameters(_parameters);
        }

        [TearDown]
        public Task DisposeAsync()
        {
            _parameters.Dispose();
            return Task.CompletedTask;
        }

        [Test]
        public async Task Darc_GitHubFlow_AutoMerge_GithubChecks_AllChecksSuccessful()
        {
            string testChannelName = "Test Channel " + _random.Next(int.MaxValue);
            var sourceRepo = "maestro-test1";
            var targetRepo = "maestro-test2";
            var targetBranch = _random.Next(int.MaxValue).ToString();

            TestContext.WriteLine("GitHub Dependency Flow, non-batched, all checks successful");
            await AutoMergeFlowTestBase(targetRepo, sourceRepo, targetBranch, testChannelName, new string[] {"--all-checks-passed" });
        }

        [Test]
        public async Task Darc_GitHubFlow_AutoMerge_GithubChecks_Standard()
        {
            string testChannelName = "Test Channel " + _random.Next(int.MaxValue);
            var sourceRepo = "maestro-test1";
            var targetRepo = "maestro-test2";
            var targetBranch = _random.Next(int.MaxValue).ToString();

            TestContext.WriteLine("GitHub Dependency Flow, non-batched, standard");
            await AutoMergeFlowTestBase(targetRepo, sourceRepo, targetBranch, testChannelName, new string[] {"--standard-automerge","--trigger" });
        }

        public async Task AutoMergeFlowTestBase(string targetRepo, string sourceRepo, string targetBranch, string testChannelName, string[] args)
        {
            string targetRepoUri = GetRepoUrl(targetRepo);
            var sourceRepoUri = GetRepoUrl(sourceRepo);
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
            await using AsyncDisposableValue<string> channel = await CreateTestChannelAsync(testChannelName).ConfigureAwait(false);

            TestContext.WriteLine("Set up build for intake into target repository");
            Build build = await CreateBuildAsync(sourceRepoUri, sourceBranch, sourceCommit, sourceBuildNumber, sourceAssets);
            await using IAsyncDisposable _ = await AddBuildToChannelAsync(build.Id, testChannelName);

            TestContext.WriteLine("Cloning target repo to prepare the target branch");
            using TemporaryDirectory repo = await CloneRepositoryAsync(targetRepo);
            using (ChangeDirectory(repo.Directory))
            {
                await RunGitAsync("checkout", "-b", targetBranch).ConfigureAwait(false);
                TestContext.WriteLine("Adding dependencies to target repo");
                await RunDarcAsync("add-dependency",
                    "--name", "Microsoft.DotNet.Arcade.Sdk",
                    "--type", "toolset",
                    "--repo", sourceRepoUri);

                TestContext.WriteLine("Pushing branch to remote");
                await RunGitAsync("commit", "-am", "Add dependencies.");
                await using IAsyncDisposable ___ = await PushGitBranchAsync("origin", targetBranch);

                TestContext.WriteLine($"Adding a subscription from ${sourceRepo} to ${targetRepo}");
                await using AsyncDisposableValue<string> sub = await CreateSubscriptionAsync(testChannelName, sourceRepo, targetRepo, targetBranch, "none", sourceRepoUri, targetRepoUri, args);

                await TriggerSubscriptionAsync(sub.Value);

                TestContext.WriteLine($"Waiting on PR to be opened in ${targetRepoUri}");
                bool testResult = await CheckGithubPullRequestChecks(targetRepo, targetBranch);
                Assert.IsTrue(testResult);
            }
        }
    }
}
