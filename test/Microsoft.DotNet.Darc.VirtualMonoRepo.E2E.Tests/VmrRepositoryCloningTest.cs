// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.VirtualMonoRepo.E2E.Tests;
internal class VmrRepositoryCloningTest : VmrTestsBase
{
    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public async Task RepositoryBranchShouldResetToRemote(bool resetToRemote)
    {
        var branchName = "main";

        ILocalGitRepo oldClone = await CloneProductRepoAsync(branchName, resetToRemote: false);

        var filePath = Path.Combine(oldClone.Path, Constants.GetRepoFileName(Constants.ProductRepoName));
        var remoteFileContent = File.ReadAllText(filePath);
        var remoteCommit = await oldClone.GetShaForRefAsync(branchName);

        // We advance the clone one commit ahead to see if it rewinds back with the reset
        File.WriteAllText(filePath, "new content");
        await oldClone.StageAsync(["."]);
        await oldClone.CommitAsync("Change test file", false);

        var localCommit = await GitOperations.GetRepoLastCommit(oldClone.Path);
        var localFileContent = File.ReadAllText(filePath);
        localCommit.Should().NotBe(remoteCommit);
        localFileContent.Should().NotBe(remoteFileContent);

        var newClone = await CloneProductRepoAsync(branchName, resetToRemote: resetToRemote);

        newClone.Path.Should().Be(oldClone.Path);
        (await newClone.GetShaForRefAsync(branchName)).Should().Be(resetToRemote ? remoteCommit : localCommit);
        File.ReadAllText(filePath).Should().Be(resetToRemote ? remoteFileContent : localFileContent);
    }

    private async Task<ILocalGitRepo> CloneProductRepoAsync(string branchName, bool resetToRemote)
    {
        using var scope = ServiceProvider.CreateScope();
        var repoCloneManager = scope.ServiceProvider.GetRequiredService<IRepositoryCloneManager>();

        return await repoCloneManager.PrepareCloneAsync(VmrTestsOneTimeSetUp.CommonProductRepoPath, branchName, resetToRemote);
    }

    protected override Task CopyReposForCurrentTest() => Task.CompletedTask;
    protected override Task CopyVmrForCurrentTest() => Task.CompletedTask;
}
