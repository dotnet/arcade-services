// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
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
        IDependencyFileManager dependencyFileManager,
        ILocalGitRepoFactory localGitRepoFactory,
        IBasicBarClient barApiClient,
        IFileSystem fileSystem,
        ILogger<CodeFlowOperation> logger)
    : VmrOperationBase(options, logger)
{
    private readonly ICodeFlowCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IVmrCodeFlower _codeFlower = codeFlower;
    private readonly IVmrDependencyTracker _dependencyTracker = dependencyTracker;
    private readonly IDependencyFileManager _dependencyFileManager = dependencyFileManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IBasicBarClient _barApiClient = barApiClient;
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly ILogger<CodeFlowOperation> _logger = logger;

    /// <summary>
    /// Returns a list of files that should be ignored when flowing changes forward.
    /// These mostly include code flow metadata which should not get updated in local flows.
    /// </summary>
    protected abstract IEnumerable<string> GetIgnoredFiles(string mappingName);

    /// <summary>
    /// Once code is flow, updates toolset and dependencies (even after a failed patch left behind conflicts).
    /// </summary>
    protected abstract Task UpdateToolsetAndDependenciesAsync(
        SourceMapping mapping,
        LastFlows lastFlows,
        Codeflow currentFlow,
        ILocalGitRepo targetRepo,
        Build build,
        string branch,
        CancellationToken cancellationToken);

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

        Build build = await GetBuildAsync(sourceRepo.Path);
        string mappingName = await GetSourceMappingNameAsync(productRepo.Path);

        await VerifyLocalRepositoriesAsync(productRepo);

        _logger.LogInformation(
            "Flowing {sourceRepo}'s commit {sourceSha} to {targetRepo} at {targetDirectory}...",
            isForwardFlow ? mappingName : "VMR",
            DarcLib.Commit.GetShortSha(_options.Ref),
            !isForwardFlow ? mappingName : "VMR",
            targetRepo.Path);

        Codeflow currentFlow = isForwardFlow
            ? new ForwardFlow(_options.Ref, await targetRepo.GetShaForRefAsync())
            : new Backflow(_options.Ref, await targetRepo.GetShaForRefAsync());

        await _dependencyTracker.RefreshMetadataAsync();

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);

        LastFlows lastFlows;
        try
        {
            lastFlows = await _codeFlower.GetLastFlowsAsync(mapping.Name, productRepo, currentFlow is Backflow);
        }
        catch (InvalidSynchronizationException)
        {
            // We're trying to synchronize an old repo commit on top of a VMR commit that had other synchronization with the repo since.
            throw new InvalidSynchronizationException(
                "Failed to flow changes. The VMR is out of sync with the repository. " +
                "This could be due to a more recent code flow that happened between the checked-out commits. " +
                "Please make sure your repository and the VMR are up to date.");
        }

        string currentTargetRepoBranch = await targetRepo.GetCheckedOutBranchAsync();

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
                currentTargetRepoBranch,
                currentTargetRepoBranch,
                headBranchExisted: false,
                rebase: true,
                forceUpdate: false,
                cancellationToken);
        }
        finally
        {
            await UpdateToolsetAndDependenciesAsync(
                mapping,
                lastFlows,
                currentFlow,
                targetRepo,
                build,
                currentTargetRepoBranch,
                cancellationToken);
        }

        if (!hasChanges)
        {
            _logger.LogInformation("No changes to flow between the VMR and {repo}.", mapping.Name);
            await targetRepo.CheckoutAsync(currentTargetRepoBranch);
            return;
        }

        _logger.LogInformation("Changes staged in {repoPath}", targetRepo.Path);
    }

    private async Task<Build> GetBuildAsync(NativePath sourceRepoPath)
    {
        ILocalGitRepo sourceRepo = _localGitRepoFactory.Create(sourceRepoPath);

        Build build;
        if (_options.Build == 0)
        {
            _options.Ref = await sourceRepo.GetShaForRefAsync(_options.Ref);
            build = new(-1, DateTimeOffset.Now, 0, false, false, _options.Ref, [], [], [], [])
            {
                GitHubRepository = sourceRepo.Path,
            };
        }
        else
        {
            build = await _barApiClient.GetBuildAsync(_options.Build);

            try
            {
                _options.Ref = await sourceRepo.GetShaForRefAsync(build.Commit);
            }
            catch (ProcessFailedException)
            {
                throw new DarcException(
                    $"The commit {build.Commit} associated with build {_options.Build} could not be found in {sourceRepo.Path}. " +
                    "Please make sure you have the latest changes from the remote and that you are using the correct repository.");
            }
        }

        return build;
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
}
