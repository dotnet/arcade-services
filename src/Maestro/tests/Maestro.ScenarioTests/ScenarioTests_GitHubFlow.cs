using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [Category("PostDeployment")]
    [NonParallelizable]
    public class ScenarioTests_GitHubFlow : MaestroScenarioTestBase
    {
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
        public async Task Darc_GitHubFlow_Batched()
        {
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(_parameters);

            TestContext.WriteLine("Github Dependency Flow, batched");
            await testLogic.DarcBatchedFlowTestBase($"GitHub_BatchedTestBranch_{Environment.MachineName}", 
                $"GitHub Batched Channel {Environment.MachineName}", 
                false).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched_AllChecksSuccessful()
        {
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(_parameters);

            TestContext.WriteLine("GitHub Dependency Flow, non-batched, all checks successful");
            await testLogic.NonBatchedFlowTestBase($"GitHub_NonBatchedTestBranch_AllChecks_{Environment.MachineName}", 
                $"GitHub Non-Batched All Checks Channel {Environment.MachineName}", false, true).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched()
        {
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(_parameters);

            TestContext.WriteLine("GitHub Dependency Flow, non-batched");
            await testLogic.NonBatchedFlowTestBase($"GitHub_NonBatchedTestBranch_{Environment.MachineName}", 
                $"GitHub Non-Batched Channel {Environment.MachineName}", 
                false).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched_WithCoherency()
        {
            EndToEndFlowLogic testLogic = new EndToEndFlowLogic(_parameters);

            TestContext.WriteLine("GitHub Dependency Flow, non-batched");
            await testLogic.NonBatchedFlowTestBase(
                $"GitHub_NonBatchedTestCoherencyBranch_{Environment.MachineName}", 
                $"GitHub Non-Batched Coherency Channel {Environment.MachineName}", 
                false, isCoherencyTest: true).ConfigureAwait(false);
        }
    }
}
