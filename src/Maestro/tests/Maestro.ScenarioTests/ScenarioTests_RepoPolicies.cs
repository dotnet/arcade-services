using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [Category("ScenarioTest")]
    public class ScenarioTests_RepoPolicies : MaestroScenarioTestBase
    {
        private readonly string repoName = "maestro-test1";
        private readonly string branchName = "RepoPoliciesTestBranch";
        private TestParameters _parameters;

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

        [Test]
        public async Task ArcadeRepoPolicies_EndToEnd()
        {
            TestContext.WriteLine("Repository merge policy handling");
            TestContext.WriteLine("Running tests...");

            string repoUrl = GetRepoUrl(repoName);

            TestContext.WriteLine("Setting repository merge policy to empty");
            await SetRepositoryPolicies(repoUrl, branchName);
            string emptyPolicies = await GetRepositoryPolicies(repoUrl, branchName);
            string expectedEmpty = $"{repoUrl} @ {branchName}\r\n- Merge Policies: []\r\n";
            StringAssert.AreEqualIgnoringCase(expectedEmpty, emptyPolicies, "Repository merge policy is not empty");

            TestContext.WriteLine("Setting repository merge policy to standard");
            await SetRepositoryPolicies(repoUrl, branchName, new string[] { "--standard-automerge" });
            string standardPolicies = await GetRepositoryPolicies(repoUrl, branchName);
            string expectedStandard = $"{repoUrl} @ {branchName}\r\n- Merge Policies:\r\n  Standard\r\n";
            StringAssert.AreEqualIgnoringCase(expectedStandard, standardPolicies, "Repository policy not set to standard");

            TestContext.WriteLine("Setting repository merge policy to all checks successful");
            await SetRepositoryPolicies(repoUrl, branchName, new string[] { "--all-checks-passed", "--ignore-checks", "A,B" });
            string allChecksPolicies = await GetRepositoryPolicies(repoUrl, branchName);
            string expectedAllChecksPolicies = $"{repoUrl} @ {branchName}\r\n- Merge Policies:\r\n  AllChecksSuccessful\r\n    ignoreChecks = \r\n" +
                "                   [\r\n" +
                "                     \"A\",\r\n" +
                "                     \"B\"\r\n" +
                "                   ]\r\n";
            StringAssert.AreEqualIgnoringCase(expectedAllChecksPolicies, allChecksPolicies, "Repository policy is incorrect for all checks successful case");
        }
    }
}
