using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [NonParallelizable]
    [Category("PostDeployment")]
    public class ScenarioTests_GitHubFlow
    {
        [Test]
        public async Task Darc_GitHubFlow_Batched()
        {
            using TestParameters parameters = await TestParameters.GetAsync();
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            TestContext.WriteLine("Github Dependency Flow, batched");
            await testLogic.DarcBatchedFlowTestBase($"GitHub_BatchedTestBranch_{Environment.MachineName}", 
                $"GitHub Batched Channel {Environment.MachineName}", 
                false).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched_AllChecksSuccessful()
        {
            using TestParameters parameters = await TestParameters.GetAsync();
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            TestContext.WriteLine("GitHub Dependency Flow, non-batched, all checks successful");
            await testLogic.NonBatchedFlowTestBase($"GitHub_NonBatchedTestBranch_AllChecks_{Environment.MachineName}", 
                $"GitHub Non-Batched All Checks Channel {Environment.MachineName}", false, true).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched()
        {
            using TestParameters parameters = await TestParameters.GetAsync();
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            TestContext.WriteLine("GitHub Dependency Flow, non-batched");
            await testLogic.NonBatchedFlowTestBase($"GitHub_NonBatchedTestBranch_{Environment.MachineName}", 
                $"GitHub Non-Batched Channel {Environment.MachineName}", 
                false).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched_WithCoherency()
        {
            using TestParameters parameters = await TestParameters.GetAsync();
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            TestContext.WriteLine("GitHub Dependency Flow, non-batched");
            await testLogic.NonBatchedFlowTestBase(
                $"GitHub_NonBatchedTestCoherencyBranch_{Environment.MachineName}", 
                $"GitHub Non-Batched Coherency Channel {Environment.MachineName}", 
                false, isCoherencyTest: true).ConfigureAwait(false);
        }
    }
}
