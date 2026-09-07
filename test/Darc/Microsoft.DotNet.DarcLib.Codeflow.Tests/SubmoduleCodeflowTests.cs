// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

[TestFixture]
internal class SubmoduleCodeflowTests : CodeFlowTests
{
    // A submodule pointer bumped only on the VMR side backflows cleanly into the repo. When the same submodule
    // is also bumped differently on the repo side, both pointers reference commits in the submodule's own linear
    // history, so git's submodule-aware merge fast-forwards the gitlink to the newer commit instead of raising a
    // conflict. That is the expected behavior here: the backflow produces no conflicts and the repo ends up with
    // the newer submodule commit.
    [Test]
    public async Task BackflowSubmoduleConflictResolvesToNewerCommitTest()
    {
        await EnsureTestRepoIsInitialized();

        var submodulePath = new UnixPath("externals/external-repo");
        var sourceManifestPath = VmrPath / VmrInfo.DefaultRelativeSourceManifestPath;

        // Add a submodule to the product repo and forward flow it so the VMR's source manifest tracks it
        await GitOperations.InitializeSubmodule(ProductRepoPath, "second-repo", SecondRepoPath, submodulePath);
        await GitOperations.CommitAll(ProductRepoPath, "Added a submodule");

        var forwardBranchName = GetTestBranchName(forwardFlow: true);
        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(forwardBranchName);

        // Create additional submodule commits we can point at from either side
        await File.WriteAllTextAsync(SecondRepoPath / "clean.txt", "Clean submodule bump");
        await GitOperations.CommitAll(SecondRepoPath, "Clean submodule commit");
        var cleanSubmoduleSha = await GitOperations.GetRepoLastCommit(SecondRepoPath);

        await File.WriteAllTextAsync(SecondRepoPath / "repo-side.txt", "Repo side submodule bump");
        await GitOperations.CommitAll(SecondRepoPath, "Repo-side submodule commit");
        var repoSideSubmoduleSha = await GitOperations.GetRepoLastCommit(SecondRepoPath);

        await File.WriteAllTextAsync(SecondRepoPath / "vmr-side.txt", "VMR side submodule bump");
        await GitOperations.CommitAll(SecondRepoPath, "VMR-side submodule commit");
        var vmrSideSubmoduleSha = await GitOperations.GetRepoLastCommit(SecondRepoPath);

        // Points the source manifest submodule at a given SHA and commits it in the VMR,
        // simulating an independent submodule bump on the VMR side (e.g. `darc vmr reset-submodule`).
        // The pointer lives in source-manifest.json under src/, outside the mapping's diff, so the backflow
        // carries it as a submodule-only change (there are no other source changes to bundle it with).
        async Task BumpVmrSubmodulePointer(string sha, string message)
        {
            await GitOperations.Checkout(VmrPath, "main");
            var sourceManifest = SourceManifest.FromFile(sourceManifestPath);
            var submoduleRecord = sourceManifest.Submodules.Single();
            sourceManifest.UpdateSubmodule(new SubmoduleRecord(submoduleRecord.Path, submoduleRecord.RemoteUri, sha));
            await File.WriteAllTextAsync(sourceManifestPath, sourceManifest.ToJson());
            await GitOperations.CommitAll(VmrPath, message);
        }

        // First backflow: bump the submodule pointer on the VMR side only. It flows cleanly, no conflict.
        var cleanBackBranchName = GetTestBranchName();
        await BumpVmrSubmodulePointer(cleanSubmoduleSha, "Bump submodule pointer in the VMR (clean)");
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, cleanBackBranchName);
        codeFlowResult.ShouldHaveUpdates();
        codeFlowResult.ConflictedFiles.Should().BeEmpty();
        await FinalizeBackFlow(cleanBackBranchName);

        // Second backflow: bump the submodule again on the VMR side AND bump it differently on the repo side.
        await BumpVmrSubmodulePointer(vmrSideSubmoduleSha, "Bump submodule pointer in the VMR (conflicting)");

        // We stage the gitlink directly so that a plain `git commit` (no `add -A`) records the pointer change.
        await GitOperations.Checkout(ProductRepoPath, "main");
        var updateIndex = await GitOperations.ExecuteGitCommand(
            ProductRepoPath, "update-index", "--cacheinfo", $"160000,{repoSideSubmoduleSha},{submodulePath}");
        updateIndex.ThrowIfFailed("Failed to bump submodule pointer in the repo");
        // Sync the submodule working tree to the staged pointer (as the backflow does) so the repo tree stays clean.
        var syncSubmodule = await GitOperations.ExecuteGitCommand(
            ProductRepoPath, "-c", "protocol.file.allow=always", "submodule", "update", "--checkout", "--", submodulePath);
        syncSubmodule.ThrowIfFailed("Failed to sync submodule working tree in the repo");
        await GitOperations.Commit(ProductRepoPath, "Bump submodule pointer in the repo");

        // The submodule pointer in the repo (repoSideSubmoduleSha) is a descendant of the one coming from the VMR
        // (vmrSideSubmoduleSha) in the submodule's own history, so git's submodule-aware merge fast-forwards the
        // gitlink to the newer commit instead of conflicting. The backflow completes without conflicts.
        var conflictBackBranchName = GetTestBranchName();
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, conflictBackBranchName);

        codeFlowResult.ConflictedFiles.Count().Should().Be(0);
        await FinalizeBackFlow(conflictBackBranchName);

        // The repo keeps the newer submodule commit.
        var finalSubmoduleSha = (await GitOperations.ExecuteGitCommand(
            ProductRepoPath, "rev-parse", $"HEAD:{submodulePath}")).StandardOutput.Trim();
        finalSubmoduleSha.Should().Be(vmrSideSubmoduleSha);
    }

    // Backflows a submodule pointer bump into a product repo where the submodule is NOT checked out - it is a
    // gitlink only, its commit objects are not present locally, which is how the service normally operates on
    // repos without populating submodules. The repo and VMR point the gitlink at two different commits from the
    // submodule's linear history. Because the submodule's history is not available locally, git cannot inspect it
    // during the backflow merge and therefore cannot fast-forward the differing gitlink to the newer commit; it
    // reports the pointers as an unmerged conflict ("commits not present"). Contrast this with the sibling
    // BackflowSubmoduleConflictResolvesToNewerCommitTest, which performs the same kind of linear pointer bump but
    // with the submodule checked out (its objects present), so git fast-forwards to the newer commit with no
    // conflict. The checkout state is the sole variable that changes the outcome.
    [Test]
    public async Task BackflowSubmoduleConflictNotCheckedOutTest()
    {
        await EnsureTestRepoIsInitialized();

        var submodulePath = new UnixPath("externals/external-repo");
        var submoduleFileName = Constants.GetRepoFileName(Constants.SecondRepoName);
        var sourceManifestPath = VmrPath / VmrInfo.DefaultRelativeSourceManifestPath;

        // Add a submodule to the product repo and forward flow it so the VMR's source manifest tracks it
        await GitOperations.InitializeSubmodule(ProductRepoPath, "second-repo", SecondRepoPath, submodulePath);
        await GitOperations.CommitAll(ProductRepoPath, "Added a submodule");

        var forwardBranchName = GetTestBranchName(forwardFlow: true);
        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(forwardBranchName);

        // Create two submodule commits (a straight linear history) to point the gitlink at from either side.
        // They are created only after the product repo cloned the submodule, so the product repo's submodule
        // object store never receives them.
        await File.WriteAllTextAsync(SecondRepoPath / "repo-side.txt", "Repo side submodule bump");
        await GitOperations.CommitAll(SecondRepoPath, "Repo-side submodule commit");
        var repoSideSubmoduleSha = await GitOperations.GetRepoLastCommit(SecondRepoPath);

        await File.WriteAllTextAsync(SecondRepoPath / "vmr-side.txt", "VMR side submodule bump");
        await GitOperations.CommitAll(SecondRepoPath, "VMR-side submodule commit");
        var vmrSideSubmoduleSha = await GitOperations.GetRepoLastCommit(SecondRepoPath);

        // Points the source manifest submodule at a given SHA and commits it in the VMR,
        // simulating an independent submodule bump on the VMR side (e.g. `darc vmr reset-submodule`).
        // The pointer lives in source-manifest.json under src/, outside the mapping's diff, so the backflow
        // carries it as a submodule-only change (there are no other source changes to bundle it with).
        async Task BumpVmrSubmodulePointer(string sha, string message)
        {
            await GitOperations.Checkout(VmrPath, "main");
            var sourceManifest = SourceManifest.FromFile(sourceManifestPath);
            var submoduleRecord = sourceManifest.Submodules.Single();
            sourceManifest.UpdateSubmodule(new SubmoduleRecord(submoduleRecord.Path, submoduleRecord.RemoteUri, sha));
            await File.WriteAllTextAsync(sourceManifestPath, sourceManifest.ToJson());
            await GitOperations.CommitAll(VmrPath, message);
        }

        // Make the submodule "not checked out": deinitialize it so its working tree is emptied while the gitlink
        // stays tracked in the repo's tree/index. This is the sole difference from the sibling test.
        await GitOperations.Checkout(ProductRepoPath, "main");
        var deinit = await GitOperations.ExecuteGitCommand(
            ProductRepoPath, "submodule", "deinit", "-f", "--", submodulePath);
        deinit.ThrowIfFailed("Failed to deinitialize the submodule in the repo");
        // Document the not-checked-out precondition: the submodule working tree is empty.
        File.Exists(ProductRepoPath / submodulePath / submoduleFileName).Should().BeFalse();

        // Bump the submodule on the VMR side (to the newer commit) AND bump it on the repo side (to the older
        // commit). Unlike the sibling, we do NOT run `submodule update --checkout` afterwards - the submodule
        // intentionally stays not checked out.
        await BumpVmrSubmodulePointer(vmrSideSubmoduleSha, "Bump submodule pointer in the VMR (conflicting)");

        // We stage the gitlink directly so that a plain `git commit` (no `add -A`) records the pointer change.
        await GitOperations.Checkout(ProductRepoPath, "main");
        var updateIndex = await GitOperations.ExecuteGitCommand(
            ProductRepoPath, "update-index", "--cacheinfo", $"160000,{repoSideSubmoduleSha},{submodulePath}");
        updateIndex.ThrowIfFailed("Failed to bump submodule pointer in the repo");
        await GitOperations.Commit(ProductRepoPath, "Bump submodule pointer in the repo");

        // The submodule's commit objects are not present locally (it was never checked out), so git cannot
        // fast-forward the differing gitlink during the merge and surfaces it as an unmerged conflict.
        var branchName = GetTestBranchName();
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);

        codeFlowResult.ConflictedFiles.Should().Contain(f => f.Path == submodulePath);
    }

    // Scenario for https://github.com/dotnet/arcade-services/issues/6444 (same-direction forward flow):
    // The submodule is bumped independently on BOTH sides - on the VMR side via the real `darc vmr reset-submodule`
    // operation and on the repo side by bumping the gitlink - and neither bump is flown to the other side first.
    // A forward flow then computes the submodule diff purely from the repo's own history (last FF -> current, i.e.
    // original -> repoSideSubmoduleSha), even though the VMR is actually at vmrSideSubmoduleSha. Because the previous
    // flow was also a forward flow, this exercises the SameDirectionFlowAsync path.
    [Test]
    public async Task ForwardFlowSubmoduleBumpedOnBothSidesSameDirectionTest()
    {
        await EnsureTestRepoIsInitialized();

        var submodulePath = new UnixPath("externals/external-repo");
        var sourceManifestPath = VmrPath / VmrInfo.DefaultRelativeSourceManifestPath;

        // Add a submodule to the product repo and forward flow it so the VMR's source manifest tracks it
        await GitOperations.InitializeSubmodule(ProductRepoPath, "second-repo", SecondRepoPath, submodulePath);
        await GitOperations.CommitAll(ProductRepoPath, "Added a submodule");

        var initialForwardBranch = GetTestBranchName(forwardFlow: true);
        var initialFlow = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, initialForwardBranch);
        initialFlow.ShouldHaveUpdates();
        await FinalizeForwardFlow(initialForwardBranch);

        // Bump the submodule divergently on both sides without flowing either bump first. The last flow before the
        // upcoming forward flow is the initial forward flow, so this stays a same-direction flow.
        _ = await BumpSubmoduleOnBothSidesDivergentlyAsync(submodulePath, sourceManifestPath);

        // Forward flow. The repo's submodule diff is original -> repoSideSubmoduleSha, but the VMR is at
        // vmrSideSubmoduleSha, so the two sides diverged and the flow must surface a conflict rather than clobber one.
        var forwardBranch = GetTestBranchName(forwardFlow: true);
        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranch);
        codeFlowResult.ShouldHaveUpdates();

        AssertSubmoduleConflictSurfaced(codeFlowResult);
    }

    // Scenario for https://github.com/dotnet/arcade-services/issues/6444 (opposite-direction forward flow):
    // Same divergent submodule bump as the sibling test, but an unrelated backflow is merged between the initial
    // forward flow and the final forward flow. That makes the last flow a backflow, so the final forward flow runs
    // through OppositeDirectionFlowAsync (which recreates the previous flow before applying the repo diff).
    [Test]
    public async Task ForwardFlowSubmoduleBumpedOnBothSidesOppositeDirectionTest()
    {
        await EnsureTestRepoIsInitialized();

        var submodulePath = new UnixPath("externals/external-repo");
        var sourceManifestPath = VmrPath / VmrInfo.DefaultRelativeSourceManifestPath;

        // Add a submodule to the product repo and forward flow it so the VMR's source manifest tracks it
        await GitOperations.InitializeSubmodule(ProductRepoPath, "second-repo", SecondRepoPath, submodulePath);
        await GitOperations.CommitAll(ProductRepoPath, "Added a submodule");

        var initialForwardBranch = GetTestBranchName(forwardFlow: true);
        var initialFlow = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, initialForwardBranch);
        initialFlow.ShouldHaveUpdates();
        await FinalizeForwardFlow(initialForwardBranch);

        // Flow an unrelated change from the VMR into the repo so the last flow becomes a backflow. This backflow
        // does not touch the submodule (still at the original commit on both sides), so it flows cleanly.
        var backBranch = GetTestBranchName();
        var backFlow = await ChangeVmrFileAndFlowIt("Unrelated VMR change", backBranch);
        backFlow.ShouldHaveUpdates();
        await FinalizeBackFlow(backBranch);

        // Bump the submodule divergently on both sides without flowing either bump first.
        _ = await BumpSubmoduleOnBothSidesDivergentlyAsync(submodulePath, sourceManifestPath);

        // Forward flow. Because the last flow was a backflow, this goes through OppositeDirectionFlowAsync.
        var forwardBranch = GetTestBranchName(forwardFlow: true);
        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranch);
        codeFlowResult.ShouldHaveUpdates();

        AssertSubmoduleConflictSurfaced(codeFlowResult);
    }

    // Creates two new submodule commits (linear history), resets the VMR's submodule to the newer one via the real
    // `darc vmr reset-submodule` operation (re-inlining its content and updating the source manifest) and points the
    // repo's gitlink at the older one, committing each change on its own side without flowing it.
    // Returns (repoSideSubmoduleSha, vmrSideSubmoduleSha).
    private async Task<(string RepoSideSha, string VmrSideSha)> BumpSubmoduleOnBothSidesDivergentlyAsync(
        UnixPath submodulePath,
        NativePath sourceManifestPath)
    {
        // Create two additional submodule commits (linear history) to point the two sides at
        await File.WriteAllTextAsync(SecondRepoPath / "repo-side.txt", "Repo side submodule bump");
        await GitOperations.CommitAll(SecondRepoPath, "Repo-side submodule commit");
        var repoSideSubmoduleSha = await GitOperations.GetRepoLastCommit(SecondRepoPath);

        await File.WriteAllTextAsync(SecondRepoPath / "vmr-side.txt", "VMR side submodule bump");
        await GitOperations.CommitAll(SecondRepoPath, "VMR-side submodule commit");
        var vmrSideSubmoduleSha = await GitOperations.GetRepoLastCommit(SecondRepoPath);

        // Reset the VMR submodule to vmrSideSubmoduleSha with the real `darc vmr reset-submodule` operation and DO
        // NOT backflow it. SecondRepoPath's HEAD is vmrSideSubmoduleSha, so the operation re-inlines that content
        // into the VMR AND points the source manifest at it - the same full on-disk effect a real reset produces.
        // The operation only stages its changes, so we commit them afterwards.
        await GitOperations.Checkout(VmrPath, "main");
        var submoduleManifestPath = SourceManifest.FromFile(sourceManifestPath).Submodules.Single().Path;
        await CallResetSubmoduleOperation(submoduleManifestPath, SecondRepoPath);
        await GitOperations.CommitAll(VmrPath, "Reset submodule to VMR-side commit (reset-submodule)");

        // Bump the submodule pointer in the repo to a DIFFERENT commit than the VMR did.
        await GitOperations.Checkout(ProductRepoPath, "main");
        var updateIndex = await GitOperations.ExecuteGitCommand(
            ProductRepoPath, "update-index", "--cacheinfo", $"160000,{repoSideSubmoduleSha},{submodulePath}");
        updateIndex.ThrowIfFailed("Failed to bump submodule pointer in the repo");
        // Sync the submodule working tree to the staged pointer so the repo tree stays clean.
        var syncSubmodule = await GitOperations.ExecuteGitCommand(
            ProductRepoPath, "-c", "protocol.file.allow=always", "submodule", "update", "--checkout", "--", submodulePath);
        syncSubmodule.ThrowIfFailed("Failed to sync submodule working tree in the repo");
        await GitOperations.Commit(ProductRepoPath, "Bump submodule pointer in the repo");

        return (repoSideSubmoduleSha, vmrSideSubmoduleSha);
    }

    // Runs the real `darc vmr reset-submodule` operation, pointing it at the submodule source repo (whose HEAD is
    // used as the target commit/content). The operation stages its changes only, so callers commit afterwards.
    private async Task CallResetSubmoduleOperation(string submoduleManifestPath, NativePath submoduleSourceRepo)
    {
        var options = new ResetSubmoduleCommandLineOptions
        {
            VmrPath = VmrPath,
            Path = submoduleManifestPath,
        };

        var operation = ActivatorUtilities.CreateInstance<ResetSubmoduleOperation>(ServiceProvider, options);
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(submoduleSourceRepo);
        try
        {
            var result = await operation.ExecuteAsync();
            result.Should().Be(0);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }
    }

    // Expected behavior for https://github.com/dotnet/arcade-services/issues/6444:
    // The submodule changed on both sides (the repo bumped its gitlink while the VMR reset it to a different commit)
    // without either change being flown first. The forward flow must NOT silently pick a winner - it should detect
    // the divergence, leave the source-manifest.json conflict unresolved so it surfaces as a conflict (service) /
    // error (darc), and add a comment telling the user to pick a commit and run `darc vmr reset-submodule`.
    private void AssertSubmoduleConflictSurfaced(CodeFlowResult codeFlowResult)
    {
        codeFlowResult.HadConflicts.Should().BeTrue(
            "the divergent submodule change must not be auto-resolved");
        codeFlowResult.ConflictedFiles.Should().Contain(
            f => f.Path == VmrInfo.DefaultRelativeSourceManifestPath,
            "the source-manifest.json conflict caused by the divergent submodule change should be left unresolved");
        GetLastFlowCollectedComments().Should().Contain(
            c => c.Contains("reset-submodule"),
            "the user should be told to resolve the submodule conflict manually via `darc vmr reset-submodule`");
    }
}
