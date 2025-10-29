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
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class CodeFlowOperation(
        ICodeFlowCommandLineOptions options,
        IVmrInfo vmrInfo,
        IVmrCloneManager vmrCloneManager,
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
    private readonly IVmrCloneManager _vmrCloneManager = vmrCloneManager;
    private readonly IVmrDependencyTracker _dependencyTracker = dependencyTracker;
    private readonly IDependencyFileManager _dependencyFileManager = dependencyFileManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IBasicBarClient _barApiClient = barApiClient;
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly ILogger<CodeFlowOperation> _logger = logger;

    protected async Task FlowCodeLocallyAsync(
        NativePath repoPath,
        bool isForwardFlow,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        // If subscription ID is provided, fetch subscription metadata and populate options
        if (!string.IsNullOrEmpty(_options.SubscriptionId))
        {
            await PopulateOptionsFromSubscriptionAsync();
        }

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

        // Tell the VMR clone manager about the local VMR
        await _vmrCloneManager.RegisterVmrAsync(_vmrInfo.VmrPath);

        Codeflow currentFlow = isForwardFlow
            ? new ForwardFlow(_options.Ref, await targetRepo.GetShaForRefAsync())
            : new Backflow(_options.Ref, await targetRepo.GetShaForRefAsync());

        await _dependencyTracker.RefreshMetadataAsync();

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);

        // Remember the original branch/ref for both repos so we can restore them later
        string originalSourceRepoBranch = await sourceRepo.GetCheckedOutBranchAsync();
        string originalTargetRepoBranch = await targetRepo.GetCheckedOutBranchAsync();

        // Parse excluded assets from options
        IReadOnlyList<string> excludedAssets = string.IsNullOrEmpty(_options.ExcludedAssets)
            ? []
            : _options.ExcludedAssets.Split(';').ToList();

        try
        {
            bool hasChanges = await FlowCodeAsync(
                productRepo,
                build,
                currentFlow,
                mapping,
                originalTargetRepoBranch,
                excludedAssets,
                cancellationToken);

            if (!hasChanges)
            {
                _logger.LogInformation("No changes to flow between the VMR and {repo}.", mapping.Name);
                return;
            }

            _logger.LogInformation("Changes staged in {repoPath}", targetRepo.Path);
        }
        finally
        {
            // Restore both repos to their original branch/ref, even when exceptions occur
            await RestoreRepoToOriginalStateAsync(sourceRepo, originalSourceRepoBranch);
            await RestoreRepoToOriginalStateAsync(targetRepo, originalTargetRepoBranch);
        }
    }

    private async Task RestoreRepoToOriginalStateAsync(ILocalGitRepo repo, string originalBranch)
    {
        try
        {
            string currentBranch = await repo.GetCheckedOutBranchAsync();
            
            // Only checkout if we're not already on the original branch
            if (currentBranch != originalBranch)
            {
                _logger.LogInformation("Restoring {repo} to original state: {branch}", repo.Path, originalBranch);
                await repo.CheckoutAsync(originalBranch);
            }
        }
        catch (Exception ex)
        {
            // Log but don't throw - we don't want to mask the original exception
            _logger.LogWarning(ex, "Failed to restore {repo} to original branch {branch}", repo.Path, originalBranch);
        }
    }

    protected abstract Task<bool> FlowCodeAsync(
        ILocalGitRepo productRepo,
        Build build,
        Codeflow currentFlow,
        SourceMapping mapping,
        string headBranch,
        IReadOnlyList<string> excludedAssets,
        CancellationToken cancellationToken);

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

    /// <summary>
    /// Fetch subscription metadata and populate command options based on subscription settings.
    /// This allows the subscription to be simulated using the existing codeflow logic.
    /// </summary>
    private async Task PopulateOptionsFromSubscriptionAsync()
    {
        // Validate that subscription is not used with conflicting options
        if (!string.IsNullOrEmpty(_options.Ref))
        {
            throw new DarcException("The --subscription parameter cannot be used with --ref. The subscription determines which commit to flow.");
        }

        // Parse and validate subscription ID
        if (!Guid.TryParse(_options.SubscriptionId, out Guid subscriptionId))
        {
            throw new DarcException($"Invalid subscription ID '{_options.SubscriptionId}'. Please provide a valid GUID.");
        }

        // Fetch subscription metadata
        Subscription subscription;
        try
        {
            subscription = await _barApiClient.GetSubscriptionAsync(subscriptionId)
                ?? throw new DarcException($"Subscription with ID '{subscriptionId}' not found.");
        }
        catch (RestApiException e) when (e.Response.Status == 404)
        {
            throw new DarcException($"Subscription with ID '{subscriptionId}' not found.", e);
        }

        // Check if subscription is source-enabled (VMR code flow)
        if (!subscription.SourceEnabled)
        {
            throw new DarcException("Only source-enabled subscriptions (VMR code flow) are supported with --subscription for codeflow operations.");
        }

        _logger.LogInformation("Simulating subscription '{Id}':", subscription.Id);
        _logger.LogInformation("  Source: {sourceRepo} (channel: {channelName})", subscription.SourceRepository, subscription.Channel.Name);
        _logger.LogInformation("  Target: {targetRepo}#{targetBranch}", subscription.TargetRepository, subscription.TargetBranch);

        if (!string.IsNullOrEmpty(subscription.SourceDirectory))
        {
            _logger.LogInformation("  Source directory: {sourceDir}", subscription.SourceDirectory);
        }

        if (!string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            _logger.LogInformation("  Target directory: {targetDir}", subscription.TargetDirectory);
        }

        // Set excluded assets from subscription if not already set via command line
        if (string.IsNullOrEmpty(_options.ExcludedAssets) && subscription.ExcludedAssets?.Any() == true)
        {
            _options.ExcludedAssets = string.Join(";", subscription.ExcludedAssets);
            _logger.LogInformation("  Excluded assets: {excludedAssets}", _options.ExcludedAssets);
        }
        else if (!string.IsNullOrEmpty(_options.ExcludedAssets))
        {
            _logger.LogInformation("  Using command-line excluded assets: {excludedAssets}", _options.ExcludedAssets);
        }

        // If build ID is not provided, find the latest build from the source repository on the channel
        if (_options.Build == 0)
        {
            Build latestBuild = await _barApiClient.GetLatestBuildAsync(subscription.SourceRepository, subscription.Channel.Id);
            if (latestBuild is null)
            {
                string channelName = subscription.Channel?.Name ?? "(unknown channel)";
                throw new DarcException($"No builds found for repository '{subscription.SourceRepository}' on channel '{channelName}'.");
            }

            _logger.LogInformation("  Latest build: {buildNumber} (BAR ID: {buildId})", latestBuild.AzureDevOpsBuildNumber, latestBuild.Id);
            _logger.LogInformation("  Build commit: {commit}", latestBuild.Commit);
            _logger.LogInformation("");

            // Set the build to use for the codeflow operation
            _options.Build = latestBuild.Id;
        }
        else
        {
            _logger.LogInformation("  Using provided build ID: {buildId}", _options.Build);
            _logger.LogInformation("");
        }
    }
}
