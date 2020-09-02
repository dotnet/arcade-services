using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [NonParallelizable]
    [Category("PostDeployment")]
    class ScenarioTests_AzDoFlow : MaestroScenarioTestBase
    {
        [Test]
        public async Task Darc_AzDoFlow_Batched()
        {
            TestParameters parameters = await TestParameters.GetAsync();
            SetTestParameters(parameters);

            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);

            TestContext.WriteLine("Azure DevOps Dependency Flow, batched");
            await testLogic.DarcBatchedFlowTestBase($"AzDo_BatchedTestBranch_{Environment.MachineName}", $"AzDo Batched Channel {Environment.MachineName}", true);
        }

        [Test]
        public async Task Darc_AzDoFlow_NonBatched_AllChecksSuccessful()
        {
            TestParameters parameters = await TestParameters.GetAsync();
            SetTestParameters(parameters);

            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);
            TestContext.WriteLine("AzDo Dependency Flow, non-batched, all checks successful");
            await testLogic.NonBatchedFlowTestBase($"AzDo_NonBatchedTestBranch_AllChecks_{Environment.MachineName}", $"AzDo Non-Batched All Checks Channel {Environment.MachineName}", true, true).ConfigureAwait(true);
        }

        [Test]
        public async Task Darc_AzDoFlow_NonBatched()
        {
            TestParameters parameters = await TestParameters.GetAsync();
            SetTestParameters(parameters);

            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);
            TestContext.WriteLine("AzDo Dependency Flow, non-batched");
            await testLogic.NonBatchedFlowTestBase($"AzDo_NonBatchedTestBranch_{Environment.MachineName}", $"AzDo Non-Batched Channel {Environment.MachineName}", true).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_AzDoFlow_FeedFlow()
        {
            TestContext.WriteLine("Azure DevOps Internal feed flow");
            TestParameters parameters = await TestParameters.GetAsync();
            SetTestParameters(parameters);

            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(parameters);
            await testLogic.NonBatchedFlowTestBase($"AzDo_FeedFlowBranch_{Environment.MachineName}", $"AzDo_FeedFlowChannel_{Environment.MachineName}", true, isFeedTest: true).ConfigureAwait(false);
        }
    }
}
