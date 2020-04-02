using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    public class ScenarioTests_Builds : MaestroScenarioTestBase
    {
        private string repoUrl;
        private readonly string repoName = "maestro-test1";
        private readonly string sourceBuildNumber = "654321";
        private readonly string sourceCommit = "123456";
        private readonly string sourceBranch = "master";
        private readonly IImmutableList<AssetData> sourceAssets = ImmutableList<AssetData>.Empty;

        public ScenarioTests_Builds()
        {
            sourceAssets = GetAssetData("Foo", "1.1.0", "Bar", "2.1.0");
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
            Assert.IsFalse(retrievedBuild.Released);

            //Release the build
            Build updatedBuild = await MaestroApi.Builds.UpdateAsync(new BuildUpdate() { Released = true }, build.Id);
            Assert.IsTrue(updatedBuild.Released);

            // Gather a drop with release included
            string gatherWithReleasedDir = Path.Combine(base._parameters._dir + "gather-with-released");
            string dropOutput = "";
            try
            {
                dropOutput = await GatherDrop(build.Id, gatherWithReleasedDir, true);
            }
            catch (MaestroTestException)
            {
                // Expect an exception for the downloads because they don't exist
                StringAssert.Contains($"Gathering drop for build {sourceBuildNumber}", dropOutput);
                StringAssert.Contains("Downloading asset Bar@2.1.0", dropOutput);
                StringAssert.Contains("Downloading asset Foo@1.1.0", dropOutput);
            }

            TestContext.WriteLine(gatherWithReleasedDir);

            // Gather with release excluded (default behavior). Gather-drop should throw an error.
            string noReleaseDropOutput = "";

            try
            {
                string gatherWithNoReleasedDir = Path.Combine(base._parameters._dir + "gather-no-released");
                noReleaseDropOutput = await GatherDrop(build.Id, gatherWithNoReleasedDir, false);
                throw new MaestroTestException("Expected an execption from gather-drop with --released=false because all builds are released.");
            }
            catch (MaestroTestException)
            {
                StringAssert.Contains($"Skipping download of released build {sourceBuildNumber}", noReleaseDropOutput);
                StringAssert.DoesNotContain("Downloading asset Bar@2.1.0", noReleaseDropOutput);
                StringAssert.DoesNotContain("Downloading asset Foo@1.1.0", noReleaseDropOutput);
            }
            TestContext.WriteLine(noReleaseDropOutput);

            // Unrelease the build
            Build unreleaseBuild = await MaestroApi.Builds.UpdateAsync(new BuildUpdate() { Released = false }, build.Id);
            Assert.IsFalse(unreleaseBuild.Released);

            // Gather with release excluded again (default behavior)
            string gatherWithReleased2Dir = Path.Combine(base._parameters._dir + "gather-no-released-2");
            string gatherDropOutput = "";
            try
            {
                gatherDropOutput = await GatherDrop(build.Id, gatherWithReleased2Dir, false);
            }
            catch (MaestroTestException)
            {
                StringAssert.Contains($"Gathering drop for build {sourceBuildNumber}", gatherDropOutput);
                StringAssert.Contains("Downloading asset Bar@2.1.0", dropOutput);
                StringAssert.Contains("Downloading asset Foo@1.1.0", dropOutput);
            }
        }
    }
}
