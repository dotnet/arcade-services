using System.Collections.Immutable;
using System.Threading.Tasks;
using Maestro.ScenarioTests.ObjectHelpers;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    public class ScenarioTests_Dependencies : MaestroScenarioTestBase
    {
        private readonly IImmutableList<AssetData> source1Assets = ImmutableList<AssetData>.Empty;
        private readonly IImmutableList<AssetData> source2Assets = ImmutableList<AssetData>.Empty;
        private readonly IImmutableList<AssetData> targetAssets = ImmutableList<AssetData>.Empty;
        private string source1RepoUri;
        private string source2RepoUri;
        private string targetRepoUri;
        private readonly string source1RepoName = "maestro-test1";
        private readonly string source2RepoName = "maestro-test3";
        private readonly string targetRepoName = "maestro-test2";
        private readonly string target1BuildNumber = "098765";
        private readonly string target2BuildNumber = "987654";
        private readonly string sourceBuildNumber = "654321";
        private readonly string sourceCommit = "SourceCommitVar";
        private readonly string targetCommit = "TargetCommitVar";
        private readonly string sourceBranch = "DependenciesSourceBranch";
        private readonly string targetBranch = "DependenciesTargetBranch";
        private readonly string testChannelName = "Test Channel Dependencies";

        public ScenarioTests_Dependencies()
        {
            source1Assets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
            source2Assets = GetAssetData("Pizza", "3.1.0", "Hamburger", "4.1.0");
            targetAssets = GetAssetData("Source1", "3.1.0", "Source2", "4.1.0");
        }

        [Test]
        public async Task ArcadeDependencies_EndToEnd()
        {
            source1RepoUri = GetRepoUrl(source1RepoName);
            source2RepoUri = GetRepoUrl(source2RepoName);
            targetRepoUri = GetRepoUrl(targetRepoName);

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
                targetBuild1.PublishUsingPipelines, targetBuild1.Commit, targetBuild1.Channels, targetBuild1.Assets, dependencies);

            // Add the target build a second time, should populate the BuildDependencies table and use the previous TimeToInclusion
            Build targetBuild2 = await CreateBuildAsync(targetRepoUri, targetBranch, targetCommit, target2BuildNumber, targetAssets, dependencies);
            await AddBuildToChannelAsync(targetBuild2.Id, testChannelName);
            Build newTargetBuild2 = new Build(targetBuild2.Id, targetBuild2.DateProduced, targetBuild2.Staleness, targetBuild2.Released,
                targetBuild2.PublishUsingPipelines, targetBuild2.Commit, targetBuild2.Channels, targetBuild2.Assets, dependencies);

            Build retrievedBuild1 = await MaestroApi.Builds.GetBuildAsync(targetBuild1.Id);
            Build retrievedBuild2 = await MaestroApi.Builds.GetBuildAsync(targetBuild2.Id);

            Assert.AreEqual(newTargetBuild1.Dependencies.Count, retrievedBuild1.Dependencies.Count);
            Assert.AreEqual(newTargetBuild2.Dependencies.Count, retrievedBuild2.Dependencies.Count);

            Assert.IsTrue(
                (retrievedBuild1.Dependencies[0].BuildId == build1.Id && retrievedBuild1.Dependencies[1].BuildId == build2.Id)
                || (retrievedBuild1.Dependencies[0].BuildId == build2.Id && retrievedBuild1.Dependencies[1].BuildId == build1.Id));

            BuildRefComprarer buildRefComprarer = new BuildRefComprarer();

            Assert.IsTrue(buildRefComprarer.Equals(retrievedBuild1.Dependencies[0], retrievedBuild2.Dependencies[0]));
            Assert.IsTrue(buildRefComprarer.Equals(retrievedBuild1.Dependencies[1], retrievedBuild2.Dependencies[1]));
        }
    }
}
