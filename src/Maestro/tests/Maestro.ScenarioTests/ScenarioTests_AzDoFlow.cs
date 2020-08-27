using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace Maestro.ScenarioTests
{
    [TestFixture]
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
    }
}
