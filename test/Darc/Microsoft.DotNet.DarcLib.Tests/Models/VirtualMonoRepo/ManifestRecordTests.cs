// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.Models.VirtualMonoRepo;

[TestFixture]
public class ManifestRecordTests
{
    [Test]
    public void GitHubCommitUrlIsConstructedTest()
    {
        ISourceComponent record = new RepositoryRecord(
            path: "arcade",
            remoteUri: "https://github.com/dotnet/arcade",
            commitSha: "4ee620cc1b57da45d93135e064d43a83e65bbb6e",
            barId: null);

        record.GetPublicUrl().Should().Be("https://github.com/dotnet/arcade/tree/4ee620cc1b57da45d93135e064d43a83e65bbb6e");

        record = new RepositoryRecord(
            path: "arcade",
            remoteUri: "https://github.com/dotnet/some.git.repo.git",
            commitSha: "4ee620cc1b57da45d93135e064d43a83e65bbb6e",
            barId: null);

        record.GetPublicUrl().Should().Be("https://github.com/dotnet/some.git.repo/tree/4ee620cc1b57da45d93135e064d43a83e65bbb6e");
    }

    [Test]
    public void AzDOCommitUrlIsConstructedTest()
    {
        ISourceComponent record = new RepositoryRecord(
            path: "command-line-api",
            remoteUri: "https://dev.azure.com/dnceng/internal/_git/dotnet-command-line-api",
            commitSha: "4ee620cc1b57da45d93135e064d43a83e65bbb6e",
            barId: null);

        record.GetPublicUrl().Should().Be("https://dev.azure.com/dnceng/internal/_git/dotnet-command-line-api/?version=GC4ee620cc1b57da45d93135e064d43a83e65bbb6e");
    }
}
