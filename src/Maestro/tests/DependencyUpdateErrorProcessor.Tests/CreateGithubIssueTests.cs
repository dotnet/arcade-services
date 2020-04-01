using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyUpdateErrorProcessor.Tests
{
    public class CreateGithubIssueTest :DependencyUpdateErrorProcessorTests
    {
        [Fact]
        public async Task UseInMemoryDatabaseTest()
        {
            var options = new DbContextOptionsBuilder<BuildAssetRegistryContext>()
                .UseInMemoryDatabase(databaseName: "testDb")
                .Options;
            var repositoryBranchUpdate = new RepositoryBranchUpdateHistoryEntry()
            {
                Repository = "https://github.com/maestro-auth-test/maestro-test2",
                Branch = "38",
                Method = "SynchronizePullRequestAsync",
                Success = false,
                Timestamp = new DateTime(2020, 04, 01)
            };
            using (var context = new BuildAssetRegistryContext(Context.HostingEnvironment, options))
            {
                context.RepoBranchUpdateInMemory = new List<RepositoryBranchUpdateHistoryEntry>
                    {repositoryBranchUpdate};
                DependencyUpdateErrorProcessor errorProcessor =
                    ActivatorUtilities.CreateInstance<DependencyUpdateErrorProcessor>(Scope.ServiceProvider, context);
                await errorProcessor.ProcessDependencyUpdateErrorsAsync();
            }
        }
    }
}
