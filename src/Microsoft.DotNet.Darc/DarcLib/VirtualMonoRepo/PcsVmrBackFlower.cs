// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// Class for flowing code from the VMR to product repos.
/// This class is used in the context of darc CLI as some behaviours around repo preparation differ.
/// </summary>
public interface IPcsVmrBackFlower : IVmrBackFlower
{
    /// <summary>
    /// Flows backward the code from the VMR to the target branch of a product repo.
    /// This overload is used in the context of the PCS.
    /// </summary>
    /// <param name="mappingName">Mapping to flow</param>
    /// <param name="build">Build to flow</param>
    /// <param name="baseBranch">If target branch does not exist, it is created off of this branch</param>
    /// <param name="targetBranch">Target branch to make the changes on</param>
    /// <returns>
    ///     Boolean whether there were any changes to be flown
    ///     and a path to the local repo where the new branch is created
    ///  </returns>
    Task<(bool HadUpdates, NativePath RepoPath)> FlowBackAsync(
        string mappingName,
        Build build,
        string baseBranch,
        string targetBranch,
        CancellationToken cancellationToken = default);
}

internal class PcsVmrBackFlower : VmrBackFlower, IPcsVmrBackFlower
{
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrCloneManager _vmrCloneManager;
    private readonly IRepositoryCloneManager _repositoryCloneManager;

    public PcsVmrBackFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            IVmrDependencyTracker dependencyTracker,
            IDependencyFileManager dependencyFileManager,
            IVmrCloneManager vmrCloneManager,
            IRepositoryCloneManager repositoryCloneManager,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IVmrPatchHandler vmrPatchHandler,
            IWorkBranchFactory workBranchFactory,
            IBasicBarClient basicBarClient,
            ILocalLibGit2Client libGit2Client,
            ICoherencyUpdateResolver coherencyUpdateResolver,
            IAssetLocationResolver assetLocationResolver,
            IFileSystem fileSystem,
            ILogger<VmrCodeFlower> logger)
        : base(vmrInfo, sourceManifest, dependencyTracker, dependencyFileManager, vmrCloneManager, repositoryCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, vmrPatchHandler, workBranchFactory, basicBarClient, libGit2Client, coherencyUpdateResolver, assetLocationResolver, fileSystem, logger)
    {
        _sourceManifest = sourceManifest;
        _dependencyTracker = dependencyTracker;
        _vmrCloneManager = vmrCloneManager;
        _repositoryCloneManager = repositoryCloneManager;
    }

    public async Task<(bool HadUpdates, NativePath RepoPath)> FlowBackAsync(
        string mappingName,
        Build build,
        string baseBranch,
        string targetBranch,
        CancellationToken cancellationToken = default)
    {
        (SourceMapping mapping, ILocalGitRepo targetRepo) = await PrepareVmrAndRepo(
            mappingName,
            build,
            baseBranch,
            targetBranch,
            cancellationToken);

        Codeflow lastFlow = await GetLastFlowAsync(mapping, targetRepo, currentIsBackflow: true);

        var hadUpdates = await FlowBackAsync(
            mapping,
            targetRepo,
            lastFlow,
            build.Commit,
            build,
            baseBranch,
            targetBranch,
            true,
            cancellationToken);

        return (hadUpdates, targetRepo.Path);
    }

    private async Task<(SourceMapping, ILocalGitRepo)> PrepareVmrAndRepo(
        string mappingName,
        Build build,
        string baseBranch,
        string targetBranch,
        CancellationToken cancellationToken)
    {
        // Prepare the VMR
        await _vmrCloneManager.PrepareVmrAsync(
            [build.GetRepository()],
            [build.Commit],
            build.Commit,
            cancellationToken);

        // Prepare repo
        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        var remotes = new[] { mapping.DefaultRemote, _sourceManifest.GetRepoVersion(mapping.Name).RemoteUri }
            .Distinct()
            .OrderRemotesByLocalPublicOther()
            .ToList();

        // Check out base branch first
        ILocalGitRepo targetRepo = await _repositoryCloneManager.PrepareCloneAsync(
            mapping,
            remotes,
            baseBranch,
            cancellationToken);

        // Refresh the repo
        await targetRepo.FetchAllAsync(remotes, cancellationToken);

        // Now try to see if the target branch exists already
        try
        {
            targetRepo = await _repositoryCloneManager.PrepareCloneAsync(
                mapping,
                remotes,
                [baseBranch, targetBranch],
                targetBranch,
                cancellationToken);
        }
        catch (NotFoundException)
        {
            // If target branch does not exist, we create it off of the base branch
            await targetRepo.CheckoutAsync(baseBranch);
            await targetRepo.CreateBranchAsync(targetBranch);
        }

        return (mapping, targetRepo);
    }
}
