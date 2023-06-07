using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Maestro.ScenarioTests.ObjectHelpers;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [Category("PostDeployment")]
    public class ScenarioTests_Dependencies : MaestroScenarioTestBase
    {

        private TestParameters _parameters;

        [TearDown]
        public Task DisposeAsync()
        {
            _parameters.Dispose();
            return Task.CompletedTask;
        }

        [Test]
        public async Task ArcadeDependencies_EndToEnd()
        {
            _parameters = await TestParameters.GetAsync();
            SetTestParameters(_parameters);

            string source1RepoName = TestRepository.TestRepo1Name;
            string source2RepoName = TestRepository.TestRepo3Name;
            string targetRepoName = TestRepository.TestRepo2Name;
            string target1BuildNumber = "098765";
            string target2BuildNumber = "987654";
            string sourceBuildNumber = "654321";
            string sourceCommit = "SourceCommitVar";
            string targetCommit = "TargetCommitVar";
            string sourceBranch = $"DependenciesSourceBranch_{Environment.MachineName}";
            string targetBranch = $"DependenciesTargetBranch_{Environment.MachineName}";
            string testChannelName = $"TestChannel_Dependencies_{Environment.MachineName}";

            IImmutableList<AssetData> source1Assets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
            IImmutableList<AssetData> source2Assets = GetAssetData("Pizza", "3.1.0", "Hamburger", "4.1.0");
            IImmutableList<AssetData> targetAssets = GetAssetData("Source1", "3.1.0", "Source2", "4.1.0");
            string source1RepoUri = GetRepoUrl(source1RepoName);
            string source2RepoUri = GetRepoUrl(source2RepoName);
            string targetRepoUri = GetRepoUrl(targetRepoName);

            TestContext.WriteLine($"Creating test channel {testChannelName}");
            await CreateTestChannelAsync(testChannelName);

            TestContext.WriteLine("Set up build1 for intake into target repository");
            Build build1 = await CreateBuildAsync(source1RepoUri, sourceBranch, sourceCommit, sourceBuildNumber, source1Assets);
            await AddBuildToChannelAsync(build1.Id, testChannelName);

            TestContext.WriteLine("Set up build2 for intake into target repository");
            Build build2 = await CreateBuildAsync(source2RepoUri, sourceBranch, sourceCommit, sourceBuildNumber, source2Assets);
            await AddBuildToChannelAsync(build2.Id, testChannelName);

            ImmutableList<BuildRef> dependencies = ImmutableList<BuildRef>.Empty;
            BuildRef buildRef1 = new BuildRef(build1.Id, true, 1);
            dependencies = dependencies.Add(buildRef1);
            BuildRef buildRef2 = new BuildRef(build2.Id, true, 2);
            dependencies = dependencies.Add(buildRef2);

            // Add the target build once, should populate the BuildDependencies table and calculate TimeToInclusion
            TestContext.WriteLine("Set up targetBuild in target repository");
            Build targetBuild1 = await CreateBuildAsync(targetRepoUri, targetBranch, targetCommit, target1BuildNumber, targetAssets, dependencies);
            await AddBuildToChannelAsync(targetBuild1.Id, testChannelName);
            Build newTargetBuild1 = new Build(targetBuild1.Id, targetBuild1.DateProduced, targetBuild1.Staleness, targetBuild1.Released,
                targetBuild1.Stable, targetBuild1.Commit, targetBuild1.Channels, targetBuild1.Assets, dependencies, incoherencies: null);

            // Add the target build a second time, should populate the BuildDependencies table and use the previous TimeToInclusion
            Build targetBuild2 = await CreateBuildAsync(targetRepoUri, targetBranch, targetCommit, target2BuildNumber, targetAssets, dependencies);
            await AddBuildToChannelAsync(targetBuild2.Id, testChannelName);
            Build newTargetBuild2 = new Build(targetBuild2.Id, targetBuild2.DateProduced, targetBuild2.Staleness, targetBuild2.Released,
                targetBuild2.Stable, targetBuild2.Commit, targetBuild2.Channels, targetBuild2.Assets, dependencies, incoherencies: null);

            Build retrievedBuild1 = await MaestroApi.Builds.GetBuildAsync(targetBuild1.Id);
            Build retrievedBuild2 = await MaestroApi.Builds.GetBuildAsync(targetBuild2.Id);

            Assert.AreEqual(newTargetBuild1.Dependencies.Count, retrievedBuild1.Dependencies.Count);
            Assert.AreEqual(newTargetBuild2.Dependencies.Count, retrievedBuild2.Dependencies.Count);

            BuildRefComparer buildRefComparer = new BuildRefComparer();

            CollectionAssert.AreEqual(retrievedBuild1.Dependencies, newTargetBuild1.Dependencies, buildRefComparer);
            CollectionAssert.AreEqual(retrievedBuild2.Dependencies, newTargetBuild2.Dependencies, buildRefComparer);
        }
    }
}
