using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [Category("PostDeployment")]
    public class ScenarioTests_GitHubFlow : MaestroScenarioTestBase
    {
        private TestParameters _parameters;
        private EndToEndFlowLogic testLogic;

        public ScenarioTests_GitHubFlow()
        {
        }

        [SetUp]
        public async Task InitializeAsync()
        {
            _parameters = await TestParameters.GetAsync();
            SetTestParameters(_parameters);

            testLogic = new EndToEndFlowLogic(_parameters);
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
            TestContext.WriteLine("Github Dependency Flow, batched");
            await testLogic.DarcBatchedFlowTestBase($"GitHub_BatchedTestBranch_{Environment.MachineName}", 
                $"GitHub Batched Channel {Environment.MachineName}", 
                false).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched_AllChecksSuccessful()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched, all checks successful");
            await testLogic.NonBatchedFlowTestBase($"GitHub_NonBatchedTestBranch_AllChecks_{Environment.MachineName}", 
                $"GitHub Non-Batched All Checks Channel {Environment.MachineName}", false, true).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched");
            await testLogic.NonBatchedFlowTestBase($"GitHub_NonBatchedTestBranch_{Environment.MachineName}", 
                $"GitHub Non-Batched Channel {Environment.MachineName}", 
                false).ConfigureAwait(false);
        }

        [Test]
        public async Task Darc_GitHubFlow_NonBatched_WithCoherency_SharedCode()
        {
            TestContext.WriteLine("GitHub Dependency Flow, non-batched");
            await testLogic.NonBatchedFlowTestBase(
                $"GitHub_NonBatchedTestCoherencyBranch_{Environment.MachineName}", 
                $"GitHub Non-Batched Coherency Channel {Environment.MachineName}", 
                false, isCoherencyTest: true).ConfigureAwait(false);
        }
    }
}
