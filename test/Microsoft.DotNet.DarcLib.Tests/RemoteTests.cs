// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Internal.Testing.Utility;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;

[TestFixture]
public class RemoteTests
{
    [Test]
    public async Task ValidateCommitMessageTest()
    {
        var client = new Mock<IRemoteGitRepo>();
        var barClient = new Mock<IBarApiClient>();
        var localGitClient = new Mock<ILocalLibGit2Client>();
        var sourceMappingParser = new Mock<ISourceMappingParser>();
        var mergePullRequest = new MergePullRequestParameters
        {
            DeleteSourceBranch = true,
            CommitToMerge = string.Empty,
            SquashMerge = true
        };

        var pr = new PullRequest
        {
            Title = "[352119842] Update dependencies from maestro-auth-test/maestro-test1",
            Description =
                """
                This pull request updates the following dependencies

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
                See [Dependency Description Format](https://github.com/dotnet/arcade/blob/main/Documentation/DependencyDescriptionFormat.md#dependency-description-overview)

                [DependencyUpdate]: <> (Begin)

                Coherency Update:
                 - **Microsoft.NETCore.App.Internal**: from 3.1.4-servicing.20214.5 to 3.1.4-servicing.20221.3
                 - **Microsoft.NETCore.App.Runtime.win-x64**: from 3.1.4 to 3.1.4

                [DependencyUpdate]: <> (End)


                [marker]: <> (End:390b1f10-7ba2-4d3a-142d-08d8149908a8)
                """
        };

        var firstCommit = new Commit(Constants.DarcBotName, "Sha", "TestCommit1");
        var secondCommit = new Commit(Constants.DarcBotName, "Sha", "TestCommit2");
        var thirdCommit = new Commit("User", "Sha", "Updated text");

        IList<Commit> commits =
        [
            firstCommit,
            secondCommit,
            thirdCommit
        ];

        client.Setup(x => x.GetPullRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(pr);
        client.Setup(x => x.GetPullRequestCommitsAsync(It.IsAny<string>()))
            .ReturnsAsync(commits);

        var commitToMerge = new List<string>();

        client
            .Setup(x => x.MergeDependencyPullRequestAsync(It.IsAny<string>(),
                It.IsAny<MergePullRequestParameters>(), Capture.In(commitToMerge)))
            .Returns(Task.CompletedTask);

        var logger = new NUnitLogger();

        var remote = new Remote(
            client.Object,
            new VersionDetailsParser(),
            sourceMappingParser.Object,
            Mock.Of<IRemoteFactory>(),
            new AssetLocationResolver(barClient.Object),
            new NoOpRedisClient(),
            logger);

        await remote.MergeDependencyPullRequestAsync("https://github.com/test/test2", mergePullRequest);
        var expectedCommitMessage =
            """
            [352119842] Update dependencies from maestro-auth-test/maestro-test1
            - Updates:
              - Foo: from  to 1.2.0
              - Bar: from  to 2.2.0

            Coherency Update:
             - Microsoft.NETCore.App.Internal: from 3.1.4-servicing.20214.5 to 3.1.4-servicing.20221.3
             - Microsoft.NETCore.App.Runtime.win-x64: from 3.1.4 to 3.1.4

             - Updated text
            """;
        commitToMerge[0].Should().Be(expectedCommitMessage);
    }
}
