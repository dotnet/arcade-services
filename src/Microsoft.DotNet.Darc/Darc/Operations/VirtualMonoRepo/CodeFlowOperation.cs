// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class CodeFlowOperation(
        ICodeFlowCommandLineOptions options,
        IVmrInfo vmrInfo,
        IVmrCodeFlower codeFlower,
        IVmrDependencyTracker dependencyTracker,
        IVmrPatchHandler patchHandler,
        IDependencyFileManager dependencyFileManager,
        ILocalGitRepoFactory localGitRepoFactory,
        IFileSystem fileSystem,
        ILogger<CodeFlowOperation> logger)
    : VmrOperationBase(options, logger)
{
    private readonly ICodeFlowCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IVmrCodeFlower _codeFlower = codeFlower;
    private readonly IVmrDependencyTracker _dependencyTracker = dependencyTracker;
    private readonly IVmrPatchHandler _patchHandler = patchHandler;
    private readonly IDependencyFileManager _dependencyFileManager = dependencyFileManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly ILogger<CodeFlowOperation> _logger = logger;

    /// <summary>
    /// Returns a list of files that should be ignored when flowing changes forward.
    /// These mostly include code flow metadata which should not get updated in local flows.
    /// </summary>
    protected abstract IEnumerable<string> GetIgnoredFiles(string mappingName);

    protected async Task FlowCodeLocallyAsync(
        NativePath repoPath,
        bool isForwardFlow,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        ILocalGitRepo productRepo = _localGitRepoFactory.Create(repoPath);
        ILocalGitRepo sourceRepo = isForwardFlow ? productRepo : vmr;
        ILocalGitRepo targetRepo = isForwardFlow ? vmr : productRepo;

        _options.Ref = await sourceRepo.GetShaForRefAsync(_options.Ref);

        await VerifyLocalRepositoriesAsync(productRepo);

        var mappingName = await GetSourceMappingNameAsync(productRepo.Path);

        _logger.LogInformation(
            "Flowing {sourceRepo}'s commit {sourceSha} to {targetRepo} at {targetDirectory}...",
            isForwardFlow ? mappingName : "VMR",
            DarcLib.Commit.GetShortSha(_options.Ref),
            !isForwardFlow ? mappingName : "VMR",
            targetRepo.Path);

        Codeflow currentFlow = isForwardFlow
            ? new ForwardFlow(_options.Ref, await targetRepo.GetShaForRefAsync())
            : new Backflow(_options.Ref, await targetRepo.GetShaForRefAsync());

        await _dependencyTracker.RefreshMetadata();

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);

        LastFlows lastFlows;
        try
        {
            lastFlows = await _codeFlower.GetLastFlowsAsync(mapping, productRepo, currentFlow is Backflow);
        }
        catch (InvalidSynchronizationException)
        {
            // We're trying to synchronize an old repo commit on top of a VMR commit that had other synchronization with the repo since.
            throw new InvalidSynchronizationException(
                "Failed to flow changes. The VMR is out of sync with the repository. " +
                "This could be due to a more recent code flow that happened between the checked-out commits. " +
                "Please make sure your repository and the VMR are up to date.");
        }

        string currentSourceRepoBranch = await sourceRepo.GetCheckedOutBranchAsync();
        string currentTargetRepoBranch = await targetRepo.GetCheckedOutBranchAsync();

        // We create a temporary branch at the current checkout
        // We flow the changes into another temporary branch
        // Later we merge tmpBranch2 into tmpBranch1
        // Then we look at the diff and stage that from the original repo checkout
        // This way user only sees the staged files
        string tmpTargetBranch = "darc/tmp/" + Guid.NewGuid().ToString();
        string tmpHeadBranch = "darc/tmp/" + Guid.NewGuid().ToString();

        try
        {
            await targetRepo.CreateBranchAsync(tmpTargetBranch, true);
            await targetRepo.CreateBranchAsync(tmpHeadBranch, true);

            Build build = new(-1, DateTimeOffset.Now, 0, false, false, currentFlow.SourceSha, [], [], [], [])
            {
                GitHubRepository = sourceRepo.Path,
            };

            bool hasChanges;

            try
            {
                hasChanges = await _codeFlower.FlowCodeAsync(
                    lastFlows,
                    currentFlow,
                    productRepo,
                    mapping,
                    build,
                    excludedAssets: [],
                    tmpTargetBranch,
                    tmpHeadBranch,
                    discardPatches: true,
                    headBranchExisted: false,
                    cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to flow changes between the VMR and {repo}", mappingName);

                throw new InvalidSynchronizationException(
                    "Failed to flow changes on top of the checked out commit - possibly due to conflicts. " +
                    $"Changes are ready in the {tmpHeadBranch} branch (based on older version of the repo).");
            }

            if (!hasChanges)
            {
                _logger.LogInformation("No changes to flow between the VMR and {repo}.", mapping.Name);
                await targetRepo.CheckoutAsync(currentTargetRepoBranch);
                return;
            }

            await StageChangesFromBranch(targetRepo, mappingName, currentTargetRepoBranch, tmpHeadBranch, cancellationToken);
        }
        catch
        {
            await targetRepo.ResetWorkingTree();
            await targetRepo.CheckoutAsync(currentTargetRepoBranch);
            throw;
        }
        finally
        {
            await CleanUp(sourceRepo, targetRepo, currentSourceRepoBranch, tmpTargetBranch, tmpHeadBranch);
        }

        _logger.LogInformation("Changes staged in {repoPath}", targetRepo.Path);
    }

    protected async Task VerifyLocalRepositoriesAsync(ILocalGitRepo repo)
    {
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);

        foreach (var r in new[] { repo, vmr })
        {
            if (await r.HasWorkingTreeChangesAsync())
            {
                throw new DarcException($"Repository at {r.Path} has uncommitted changes");
            }

            if (await r.HasStagedChangesAsync())
            {
                throw new DarcException($"Repository {r.Path} has staged changes");
            }
        }

        if (!_fileSystem.FileExists(_vmrInfo.SourceManifestPath))
        {
            throw new DarcException($"Failed to find {_vmrInfo.SourceManifestPath}! Current directory is not a VMR!");
        }

        if (_fileSystem.FileExists(repo.Path / VmrInfo.DefaultRelativeSourceManifestPath))
        {
            throw new DarcException($"{repo.Path} is not expected to be a VMR!");
        }
    }

    protected async Task<string> GetSourceMappingNameAsync(NativePath repoPath)
    {
        var versionDetails = await _dependencyFileManager.ParseVersionDetailsXmlAsync(repoPath, DarcLib.Constants.HEAD);

        if (string.IsNullOrEmpty(versionDetails.Source?.Mapping))
        {
            throw new DarcException(
                $"The <Source /> tag not found in {VersionFiles.VersionDetailsXml}. " +
                "Make sure the repository is onboarded onto codeflow.");
        }

        return versionDetails.Source.Mapping;
    }

    /// <summary>
    /// Takes a diff between the originally checked out branch and the newly flowed changes
    /// and stages those changes on top of the previously checked out branch.
    /// </summary>
    /// <param name="targetRepo">Repo where to do this</param>
    /// <param name="checkedOutBranch">Previously checked out branch</param>
    /// <param name="branchWithChanges">Branch to diff</param>
    private async Task StageChangesFromBranch(
        ILocalGitRepo targetRepo,
        string mappingName,
        string checkedOutBranch,
        string branchWithChanges,
        CancellationToken cancellationToken)
    {
        await targetRepo.CheckoutAsync(checkedOutBranch);

        string patchName = _vmrInfo.TmpPath / (Guid.NewGuid() + ".patch");
        List<VmrIngestionPatch> patches = await _patchHandler.CreatePatches(
            patchName,
            await targetRepo.GetShaForRefAsync(checkedOutBranch),
            await targetRepo.GetShaForRefAsync(branchWithChanges),
            path: null,
            filters: [.. GetIgnoredFiles(mappingName).Select(VmrPatchHandler.GetExclusionRule)],
            relativePaths: false,
            workingDir: targetRepo.Path,
            applicationPath: null,
            includeAdditionalMappings: false,
            ignoreLineEndings: false,
            cancellationToken);

        foreach (VmrIngestionPatch patch in patches)
        {
            await _patchHandler.ApplyPatch(patch, targetRepo.Path, removePatchAfter: true, cancellationToken: cancellationToken);
        }
    }

    private async Task CleanUp(
        ILocalGitRepo sourceRepo,
        ILocalGitRepo targetRepo,
        string currentSourceRepoBranch,
        string tmpTargetBranch,
        string tmpHeadBranch)
    {
        _logger.LogInformation("Cleaning up...");

        try
        {
            await targetRepo.DeleteBranchAsync(tmpTargetBranch);
        }
        catch
        {
        }
        try
        {
            await targetRepo.DeleteBranchAsync(tmpHeadBranch);
        }
        catch
        {
        }

        await sourceRepo.CheckoutAsync(currentSourceRepoBranch);
    }
}
