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

using BarBuild = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class CodeFlowOperation(
        ICodeFlowCommandLineOptions options,
        IVmrForwardFlower forwardFlower,
        IVmrBackFlower backFlower,
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
    private readonly IVmrForwardFlower _forwardFlower = forwardFlower;
    private readonly IVmrBackFlower _backFlower = backFlower;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IVmrCloneManager _vmrCloneManager = vmrCloneManager;
    private readonly IVmrDependencyTracker _dependencyTracker = dependencyTracker;
    private readonly IDependencyFileManager _dependencyFileManager = dependencyFileManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IBasicBarClient _barApiClient = barApiClient;
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly ILogger<CodeFlowOperation> _logger = logger;

    protected async Task<CodeFlowResult> FlowCodeLocallyAsync(
        NativePath repoPath,
        bool isForwardFlow,
        BarBuild build,
        Subscription? subscription,
        CancellationToken cancellationToken)
    {
        ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        ILocalGitRepo productRepo = _localGitRepoFactory.Create(repoPath);
        ILocalGitRepo sourceRepo = isForwardFlow ? productRepo : vmr;
        ILocalGitRepo targetRepo = isForwardFlow ? vmr : productRepo;

        Codeflow currentFlow = isForwardFlow
            ? new ForwardFlow(_options.Ref ?? build.Commit, await targetRepo.GetShaForRefAsync())
            : new Backflow(_options.Ref ?? build.Commit, await targetRepo.GetShaForRefAsync());

        string mappingName = await GetSourceMappingNameAsync(productRepo.Path, currentFlow.RepoSha);

        await VerifyLocalRepositoriesAsync(productRepo);

        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Flowing {sourceRepo}'s commit {sourceSha} to {targetRepo} at {targetDirectory}...",
            isForwardFlow ? mappingName : "VMR",
            DarcLib.Commit.GetShortSha(currentFlow.SourceSha),
            !isForwardFlow ? mappingName : "VMR",
            targetRepo.Path);

        // Tell the VMR clone manager about the local VMR
        await _vmrCloneManager.RegisterCloneAsync(_vmrInfo.VmrPath);

        await _dependencyTracker.RefreshMetadataAsync();

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);

        // Remember the original state of the source repo so we can restore it later
        // We capture both branch name and SHA to handle detached HEAD states
        string originalSourceRepoBranch = await sourceRepo.GetCheckedOutBranchAsync();
        string originalSourceRepoSha = await sourceRepo.GetShaForRefAsync();

        string currentTargetRepoBranch = await targetRepo.GetCheckedOutBranchAsync();
        // If we're not on a branch (e.g. we checked out a specific commit), create a temporary branch we'll apply changes to
        if (currentTargetRepoBranch.Equals(DarcLib.Constants.HEAD, StringComparison.OrdinalIgnoreCase))
        {
            currentTargetRepoBranch = $"darc-{Guid.NewGuid()}";
            _logger.LogInformation("Creating branch '{branch}' for code flow operations.", currentTargetRepoBranch);
            await targetRepo.CreateBranchAsync(currentTargetRepoBranch);
        }

        // Parse excluded assets from options
        IReadOnlyList<string> excludedAssets = string.IsNullOrEmpty(_options.ExcludedAssets)
            ? []
            : _options.ExcludedAssets.Split(';').ToList();

        cancellationToken.ThrowIfCancellationRequested();

        CodeFlowResult result;
        try
        {
            result = currentFlow is ForwardFlow
                ? await FlowForwardAsync(
                    productRepo,
                    build,
                    mapping,
                    currentTargetRepoBranch,
                    excludedAssets,
                    subscription?.TargetRepository,
                    cancellationToken)
                : await _backFlower.FlowBackAsync(
                    mapping.Name,
                    productRepo.Path,
                    build,
                    excludedAssets: excludedAssets,
                    currentTargetRepoBranch,
                    currentTargetRepoBranch,
                    enableRebase: true,
                    forceUpdate: true,
                    unsafeFlow: _options.UnsafeFlow,
                    cancellationToken);
        }
        finally
        {
            // Restore source repo to its original state, even when exceptions occur
            await RestoreRepoToOriginalStateAsync(sourceRepo, originalSourceRepoBranch, originalSourceRepoSha);
        }

        if (result.HadConflicts)
        {
            _logger.LogWarning(
                "Conflicts occurred during the synchronization of {name}. Changes are staged and conflict left to be resolved in the working tree.",
                mapping.Name);

            // We need to make sure that working tree matches the staged changes
            await targetRepo.ExecuteGitCommand(["clean", "-xfd"], cancellationToken: cancellationToken);

            IEnumerable<string> dirtyFiles = await targetRepo.GetDirtyFilesAsync();
            dirtyFiles = dirtyFiles.Except(result.ConflictedFiles.Select(e => e.Path));

            // Reset only non-conflicted files
            foreach (var file in dirtyFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await targetRepo.CheckoutAsync(file);
            }

            return result;
        }

        if (!result.HadUpdates)
        {
            _logger.LogInformation("No changes to flow between the VMR and {repo}.", mapping.Name);
        }
        else
        {
            _logger.LogInformation("Changes staged in {repoPath}", targetRepo.Path);
        }

        return result;
    }

    private async Task RestoreRepoToOriginalStateAsync(ILocalGitRepo repo, string originalBranch, string originalSha)
    {
        try
        {
            string currentSha = await repo.GetShaForRefAsync();

            // If the original state was a detached HEAD, checkout the SHA
            // Otherwise, checkout the branch name
            string refToCheckout = originalBranch == DarcLib.Constants.HEAD ? originalSha : originalBranch;
            _logger.LogDebug("Restoring {repo} to original state: {ref}", repo.Path, refToCheckout);
            await repo.CheckoutAsync(refToCheckout);
        }
        catch (Exception ex)
        {
            // Log but don't throw - we don't want to mask the original exception
            _logger.LogWarning(ex, "Failed to restore {repo} to original state", repo.Path);
        }
    }

    protected async Task<CodeFlowResult> FlowForwardAsync(
        ILocalGitRepo productRepo,
        BarBuild build,
        SourceMapping mapping,
        string headBranch,
        IReadOnlyList<string> excludedAssets,
        string? targetRepoUri,
        CancellationToken cancellationToken)
    {
        ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);

        if (targetRepoUri == null)
        {
            var remotes = await vmr.GetRemotesAsync();
            targetRepoUri = remotes.First().Uri;
        }

        CodeFlowResult result = await _forwardFlower.FlowForwardAsync(
            mapping.Name,
            productRepo.Path,
            build,
            excludedAssets,
            headBranch,
            headBranch,
            targetRepoUri,
            enableRebase: true,
            forceUpdate: true,
            unsafeFlow: _options.UnsafeFlow,
            cancellationToken);

        // Update source-manifest.json by getting the latest and overwriting the entry for the flowed repo
        var sourceManifestContent = await vmr.GetFileFromGitAsync(VmrInfo.DefaultRelativeSourceManifestPath, headBranch);
        var sourceManifest = SourceManifest.FromJson(sourceManifestContent!);
        sourceManifest.UpdateVersion(mapping.Name, build.GetRepository(), build.Commit, build.Id);
        _fileSystem.WriteToFile(_vmrInfo.SourceManifestPath, sourceManifest.ToJson());
        await vmr.StageAsync([_vmrInfo.SourceManifestPath], cancellationToken);

        return result;
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

    protected async Task<string> GetSourceMappingNameAsync(NativePath repoPath, string gitRef)
    {
        var versionDetails = await _dependencyFileManager.ParseVersionDetailsXmlAsync(repoPath, gitRef);

        if (string.IsNullOrEmpty(versionDetails.Source?.Mapping))
        {
            throw new DarcException(
                $"The <Source /> tag not found in {VersionFiles.VersionDetailsXml}. " +
                "Make sure the repository is onboarded onto codeflow.");
        }

        return versionDetails.Source.Mapping;
    }

    protected async Task<Subscription?> GetSubscriptionAsync()
    {
        if (string.IsNullOrEmpty(_options.SubscriptionId))
        {
            return null;
        }

        if (!Guid.TryParse(_options.SubscriptionId, out Guid subscriptionId))
        {
            throw new DarcException($"Invalid subscription ID '{_options.SubscriptionId}'. Please provide a valid GUID.");
        }

        try
        {
            return await _barApiClient.GetSubscriptionAsync(subscriptionId)
                ?? throw new DarcException($"Subscription with ID '{subscriptionId}' not found.");
        }
        catch (RestApiException e) when (e.Response.Status == 404)
        {
            throw new DarcException($"Subscription with ID '{subscriptionId}' not found.", e);
        }
    }

    /// <summary>
    /// Populates command options from subscription settings and returns the build to flow.
    /// </summary>
    /// <param name="subscription">The subscription to use for populating options.</param>
    /// <returns>The build to flow.</returns>
    protected async Task<BarBuild> PopulateOptionsAndGetBuildFromSubscriptionAsync(Subscription subscription)
    {
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
        if (string.IsNullOrEmpty(_options.ExcludedAssets) && subscription.ExcludedAssets?.Count > 0)
        {
            _options.ExcludedAssets = string.Join(";", subscription.ExcludedAssets);
            _logger.LogInformation("  Excluded assets: {excludedAssets}", _options.ExcludedAssets);
        }
        else if (!string.IsNullOrEmpty(_options.ExcludedAssets))
        {
            _logger.LogInformation("  Using command-line excluded assets: {excludedAssets}", _options.ExcludedAssets);
        }

        BarBuild build;

        // If build ID is not provided, find the latest build from the source repository on the channel
        if (_options.Build == 0)
        {
            build = await _barApiClient.GetLatestBuildAsync(subscription.SourceRepository, subscription.Channel.Id);
            if (build is null)
            {
                string channelName = subscription.Channel?.Name ?? "(unknown channel)";
                throw new DarcException($"No builds found for repository '{subscription.SourceRepository}' on channel '{channelName}'.");
            }

            _logger.LogInformation("  Latest build: {buildNumber} (BAR ID: {buildId})", build.AzureDevOpsBuildNumber, build.Id);
            _options.Build = build.Id;
        }
        else
        {
            _logger.LogInformation("  Using provided build ID: {buildId}", _options.Build);

            build = await _barApiClient.GetBuildAsync(_options.Build)
                ?? throw new DarcException($"Build with ID '{_options.Build}' not found.");
        }

        _logger.LogInformation("  Build commit: {commit}", build.Commit);
        _logger.LogInformation(string.Empty);

        return build;
    }

    private async Task<BarBuild?> PopulateOptionsAndBuildFromSubscription()
    {
        var subscription = await GetSubscriptionAsync();
        if (subscription == null)
        {
            return null;
        }

        // Check if subscription is source-enabled (VMR code flow)
        if (!subscription.SourceEnabled)
        {
            throw new DarcException("Only source-enabled subscriptions (VMR code flow) are supported with --subscription for codeflow operations.");
        }

        return await PopulateOptionsAndGetBuildFromSubscriptionAsync(subscription);
    }

    /// <summary>
    /// Fetch subscription metadata and populate command options based on subscription settings.
    /// This allows the subscription to be simulated using the existing codeflow logic.
    /// </summary>
    protected async Task<BarBuild> ParseOptionsAndGetBuildToFlowAsync(ILocalGitRepo sourceRepo)
    {
        // Validate that subscription is not used with conflicting options
        if (!string.IsNullOrEmpty(_options.Ref) && !string.IsNullOrEmpty(_options.SubscriptionId))
        {
            throw new DarcException("The --subscription parameter cannot be used with --ref. The subscription determines which commit to flow.");
        }

        // Parse and validate subscription ID
        BarBuild? build = await PopulateOptionsAndBuildFromSubscription();

        return await ResolveBuildFromOptionsAsync(sourceRepo, build);
    }

    /// <summary>
    /// Gets the build to flow, either from the provided build, from the --build option, or creates a synthetic build from the current ref.
    /// </summary>
    protected async Task<BarBuild> ResolveBuildFromOptionsAsync(ILocalGitRepo sourceRepo, BarBuild? build)
    {
        if (_options.Build != 0 && build == null)
        {
            build = await _barApiClient.GetBuildAsync(_options.Build);
        }

        if (build == null)
        {
            _options.Ref = await sourceRepo.GetShaForRefAsync(_options.Ref);

            _logger.LogInformation("Flowing {sha}...", DarcLib.Commit.GetShortSha(_options.Ref));

            build = new(-1, DateTimeOffset.Now, 0, false, false, _options.Ref, [], [], [], [])
            {
                GitHubRepository = sourceRepo.Path,
            };
        }
        else
        {
            try
            {
                _options.Ref = await sourceRepo.GetShaForRefAsync(build.Commit);

                _logger.LogInformation("Flowing build {buildNumber} ({buildId}) of commit {sha}...",
                    build.AzureDevOpsBuildNumber,
                    build.Id,
                    DarcLib.Commit.GetShortSha(_options.Ref));
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
}
