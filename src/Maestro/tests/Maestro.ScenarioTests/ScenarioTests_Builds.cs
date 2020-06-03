using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [Category("ScenarioTest")]
    public class ScenarioTests_Builds : MaestroScenarioTestBase
    {
        private string repoUrl;
        private readonly string repoName = "maestro-test1";
        private readonly string sourceBuildNumber = "654321";
        private readonly string sourceCommit = "123456";
        private readonly string sourceBranch = "master";
        private readonly IImmutableList<AssetData> sourceAssets;
        private TestParameters _parameters;

        public ScenarioTests_Builds()
        {
            sourceAssets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
        }

        [SetUp]
        public async Task InitializeAsync()
        {
            _parameters = await TestParameters.GetAsync();
            SetTestParameters(_parameters);
        }

        [TearDown]
        public Task DisposeAsync()
        {
            _parameters.Dispose();
            return Task.CompletedTask;
        }

        // Create a new build and check some of the metadata. Then mark as released and check again
        [Test]
        public async Task ArcadeBuilds_EndToEnd()
        {
            TestContext.WriteLine("Darc/Maestro build handing tests");

            repoUrl = GetRepoUrl(repoName);

            // Create a build for the source repo
            Build build = await CreateBuildAsync(repoUrl, sourceBranch, sourceCommit, sourceBuildNumber, sourceAssets);
            Build retrievedBuild = await MaestroApi.Builds.GetBuildAsync(build.Id);
            Assert.IsFalse(retrievedBuild.Released, "Retrieved build has Released set to true when it should be false");

            //Release the build
            Build updatedBuild = await MaestroApi.Builds.UpdateAsync(new BuildUpdate() { Released = true }, build.Id);
            Assert.IsTrue(updatedBuild.Released, "Retrieved build has Released set to false when it should be true");

            // Gather a drop with release included
            string gatherWithReleasedDir = Path.Combine(_parameters._dir + "gather-with-released");
            string dropOutput = "";

            TestContext.WriteLine("Starting 'Gather with released 1' using folder " + gatherWithReleasedDir);

            dropOutput = await GatherDrop(build.Id, gatherWithReleasedDir, true);

            StringAssert.Contains($"Gathering drop for build {sourceBuildNumber}", dropOutput, "Gather with released 1");
            StringAssert.Contains("Downloading asset Bar@2.1.0", dropOutput, "Gather with released 1");
            StringAssert.Contains("Downloading asset Foo@1.1.0", dropOutput, "Gather with released 1");

            // Gather with release excluded (default behavior). Gather-drop should throw an error.
            TestContext.WriteLine("Starting 'Gather with release excluded' - gather-drop should throw an error.");

            string gatherWithNoReleasedDir = Path.Combine(_parameters._dir + "gather-no-released");
            Assert.ThrowsAsync<MaestroTestException>(async () => await GatherDrop(build.Id, gatherWithNoReleasedDir, false), "Gather with release excluded");

            // Unrelease the build
            Build unreleaseBuild = await MaestroApi.Builds.UpdateAsync(new BuildUpdate() { Released = false }, build.Id);
            Assert.IsFalse(unreleaseBuild.Released);

            // Gather with release excluded again (default behavior)
            string gatherWithNoReleased2Dir = Path.Combine(_parameters._dir + "gather-no-released-2");
            string gatherDropOutput = "";

            TestContext.WriteLine("Starting 'Gather unreleased with release excluded' using folder " + gatherWithNoReleased2Dir);

            gatherDropOutput = await GatherDrop(build.Id, gatherWithNoReleased2Dir, false);

            StringAssert.Contains($"Gathering drop for build {sourceBuildNumber}", gatherDropOutput, "Gather unreleased with release excluded");
            StringAssert.Contains("Downloading asset Bar@2.1.0", dropOutput, "Gather unreleased with release excluded");
            StringAssert.Contains("Downloading asset Foo@1.1.0", dropOutput, "Gather unreleased with release excluded");
        }
    }
}
