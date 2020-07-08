using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DotNet.DarcLib.Tests
{
    public class RemoteTest
    {
        [Fact]
        public async Task MergeDependencyPullRequest()
        {
            Mock<IGitRepo> client = new Mock<IGitRepo>();
            Mock<IBarClient> barClient = new Mock<IBarClient>();
            Mock<ITestOutputHelper> output = new Mock<ITestOutputHelper>();
            Mock<XUnitLogger> logger = new Mock<XUnitLogger>(output.Object);
            MergePullRequestParameters mergePullRequest = new MergePullRequestParameters
            {
                DeleteSourceBranch = true,
                CommitToMerge = "",
                SquashMerge = true
            };

            PullRequest pr = new PullRequest();
            pr.Description = @"This pull request updates the following dependencies

[marker]: <> (Begin:390b1f10-7ba2-4d3a-142d-08d8149908a8)
## From https://github.com/maestro-auth-test/maestro-test1
- **Subscription**: 390b1f10-7ba2-4d3a-142d-08d8149908a8
- **Build**: 388602341
- **Date Produced**: 6/19/2020 9:45 PM
- **Commit**: 863063912

[DependencyUpdate]: <> (Begin) 

- **Updates**:
  - **Foo**: from  to 1.2.0
  - **Bar**: from  to 2.2.0 

[DependencyUpdate]: <> (End)

## Coherency Updates

The following updates ensure that dependencies with a *CoherentParentDependency*
attribute were produced in a build used as input to the parent dependency's build.
See [Dependency Description Format](https://github.com/dotnet/arcade/blob/master/Documentation/DependencyDescriptionFormat.md#dependency-description-overview)

[DependencyUpdate]: <> (Begin)

Coherency Update:
 - **Microsoft.NETCore.App.Internal**: from 3.1.4-servicing.20214.5 to 3.1.4-servicing.20221.3
 - **Microsoft.NETCore.App.Runtime.win-x64**: from 3.1.4 to 3.1.4

[DependencyUpdate]: <> (End)


[marker]: <> (End:390b1f10-7ba2-4d3a-142d-08d8149908a8)";
            pr.Title = "[352119842] Update dependencies from maestro-auth-test/maestro-test1";
            
            Commit firstCommit = new Commit("dotnet-maestro[bot]", "Sha", "TestCommit1");
            Commit secondCommit = new Commit("dotnet-maestro[bot]", "Sha", "TestCommit2");
            Commit thirdCommit = new Commit("User", "Sha", "Updated text");

            IList<Commit> commits = new List<Commit>();
            commits.Add(firstCommit);
            commits.Add(secondCommit);
            commits.Add(thirdCommit);

            client.Setup(x => x.GetPullRequestAsync(It.IsAny<string>())).ReturnsAsync(pr);
            client.Setup(x => x.GetPullRequestCommitsAsync(It.IsAny<string>())).ReturnsAsync(commits);

            List<string> commitToMerge = new List<string>();

            client.Setup(x => x.MergeDependencyPullRequestAsync(It.IsAny<string>(),
                It.IsAny<MergePullRequestParameters>(), Moq.Capture.In(commitToMerge))).Returns(Task.CompletedTask);

            Remote remote = new Remote(client.Object, barClient.Object, logger.Object);

            await remote.MergeDependencyPullRequestAsync(
                "https://github.com/test/test2",
                mergePullRequest);
            string expectedCommitMessage = @"[352119842] Update dependencies from maestro-auth-test/maestro-test1
- Updates:
  - Foo: from  to 1.2.0
  - Bar: from  to 2.2.0

Coherency Update:
 - Microsoft.NETCore.App.Internal: from 3.1.4-servicing.20214.5 to 3.1.4-servicing.20221.3
 - Microsoft.NETCore.App.Runtime.win-x64: from 3.1.4 to 3.1.4

- Manual commit - Updated text";
            Assert.Equal(expectedCommitMessage, commitToMerge[0]);
        }
    }
}
