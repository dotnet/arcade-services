using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.DotNet.DarcLib.Tests
{
    public class RemoteTest
    {
        [Fact]
        public async Task MergeDependencyPullRequest()
        {
            Mock<IGitRepo> client = new Mock<IGitRepo>();
            Mock<IBarClient> barClient = new Mock<IBarClient>();
            Mock<ILogger> logger = new Mock<ILogger>();
            MergePullRequestParameters mergePullRequest = new MergePullRequestParameters
            {
                DeleteSourceBranch = true,
                CommitToMerge = "",
                SquashMerge = true
            };

            PullRequest pr = new PullRequest();
            pr.Description = @"[marker]: <> (End:b2a4bbef-8dc3-4cb3-5a13-08d818a46851)
## Coherency Updates

The following updates ensure that dependencies with a *CoherentParentDependency*
attribute were produced in a build used as input to the parent dependency's build.
See [Dependency Description Format](https://github.com/dotnet/arcade/blob/master/Documentation/DependencyDescriptionFormat.md#dependency-description-overview)

-  **Coherency Updates**:
  -  **Microsoft.NETCore.App.Internal**: from 3.1.4-servicing.20214.5 to 3.1.4-servicing.20221.3
  -  **Microsoft.NETCore.App.Runtime.win-x64**: from 3.1.4 to 3.1.4

# From https://dev.azure.com/dnceng/internal/_git/maestro-test1
- **Subscription**: 95a525c4-1bd7-46db-8ac2-08d818b0d8de
- **Build**: 629895977
- **Date Produced**: 6/25/2020 2:40 AM
- **Commit**: 2003274066
- **Branch**: master
- **Updates**:
  - **Foo**: from 67 to 1.2.0
  - **Bar**: from 75 to 2.2.0

[marker]: <> (End:95a525c4-1bd7-46db-8ac2-08d818b0d8de)";
            pr.Title = "[352119842] Update dependencies from maestro-auth-test/maestro-test1";
            
            Commit firstCommit = new Commit("dotnet-maestro[bot]", "Sha", "TestCommit1");
            Commit secondCommit = new Commit("dotnet-maestro[bot]", "Sha", "TestCommit2");
            Commit thirdCommit = new Commit("User", "Sha", "Manual Commit");

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

            client.Verify(x=>x.GetPullRequestAsync(It.IsAny<string>()), Times.Once);
            client.Verify(x => x.GetPullRequestCommitsAsync(It.IsAny<string>()), Times.Once);
            client.Verify(x => x.MergeDependencyPullRequestAsync(It.IsAny<string>(), It.IsAny<MergePullRequestParameters>(),It.IsAny<string>()), Times.Once);
            string expectedCommitMessage = @"[352119842] Update dependencies from maestro-auth-test/maestro-test1

- Foo: from 67 to 1.2.0
- Bar: from 75 to 2.2.0

- Manual Commit

Coherency Update:

-  Microsoft.NETCore.App.Internal: from 3.1.4-servicing.20214.5 to 3.1.4-servicing.20221.3
-  Microsoft.NETCore.App.Runtime.win-x64: from 3.1.4 to 3.1.4";
            Assert.Equal(expectedCommitMessage, commitToMerge[0]);
        }
    }
}
