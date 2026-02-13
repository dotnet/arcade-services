// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ResetOperation : Operation
{
    private readonly ResetCommandLineOptions _options;
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrUpdater _vmrUpdater;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IProcessManager _processManager;
    private readonly IBarApiClient _barClient;
    private readonly IRemoteFactory _remoteFactory;
    private readonly ILogger<ResetOperation> _logger;

    public ResetOperation(
        ResetCommandLineOptions options,
        IVmrInfo vmrInfo,
        IVmrUpdater vmrUpdater,
        IVmrDependencyTracker dependencyTracker,
        IProcessManager processManager,
        IBarApiClient barClient,
        IRemoteFactory remoteFactory,
        ILogger<ResetOperation> logger)
    {
        _options = options;
        _vmrInfo = vmrInfo;
        _vmrUpdater = vmrUpdater;
        _dependencyTracker = dependencyTracker;
        _processManager = processManager;
        _barClient = barClient;
        _remoteFactory = remoteFactory;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        if (_options.Build.HasValue && !string.IsNullOrEmpty(_options.Channel))
        {
            _logger.LogError("Cannot specify both --build and --channel options together.");
            return Constants.ErrorCode;
        }

        string mappingName, targetSha = default!;
        int? buildId = null;
        
        if (_options.Build.HasValue || !string.IsNullOrEmpty(_options.Channel))
        {
            // When --build or --channel is provided, Target should only be the mapping name
            mappingName = _options.Target;
            
            if (mappingName.Contains(':'))
            {
                _logger.LogError("When using --build or --channel, the target should only contain the mapping name, not [mapping]:[sha]. Got: {input}", _options.Target);
                return Constants.ErrorCode;
            }
        }
        else
        {
            // Default behavior: Target is in the format [mapping]:[sha]
            var parts = _options.Target.Split(':', 2);
            if (parts.Length != 2)
            {
                _logger.LogError("Invalid format. Expected [mapping]:[sha] but got: {input}", _options.Target);
                return Constants.ErrorCode;
            }

            mappingName = parts[0];
            targetSha = parts[1];
            
            if (string.IsNullOrWhiteSpace(targetSha))
            {
                _logger.LogError("Target SHA cannot be empty. Got: '{sha}'", targetSha);
                return Constants.ErrorCode;
            }
        }

        if (string.IsNullOrWhiteSpace(mappingName))
        {
            _logger.LogError("Mapping name must be provided.");
            return Constants.ErrorCode;
        }

        _vmrInfo.VmrPath = new NativePath(_options.VmrPath);

        // Validate that the mapping exists
        await _dependencyTracker.RefreshMetadataAsync();

        SourceMapping mapping;
        try
        {
            mapping = _dependencyTracker.GetMapping(mappingName);
            _logger.LogInformation("Found mapping '{mapping}' pointing to remote '{remote}'",
                mappingName, mapping.DefaultRemote);
        }
        catch (Exception ex)
        {
            _logger.LogError("Mapping '{mapping}' not found: {error}", mappingName, ex.Message);
            return Constants.ErrorCode;
        }

        // Determine the target SHA from build or channel option
        if (_options.Build.HasValue)
        {
            (targetSha, buildId) = await GetShaFromBuildAsync(_options.Build.Value, mappingName);
        }
        else if (!string.IsNullOrEmpty(_options.Channel))
        {
            (targetSha, buildId) = await GetShaFromChannelAsync(_options.Channel, mapping);
        }

        _logger.LogInformation("Resetting VMR mapping '{mapping}' to SHA '{sha}'", mappingName, targetSha);

        var currentVersion = _dependencyTracker.GetDependencyVersion(mapping);
        if (currentVersion == null)
        {
            _logger.LogError("Could not find current dependency version for mapping '{mapping}'", mappingName);
            return Constants.ErrorCode;
        }

        // Additional remotes are in the form of [mapping name]:[remote URI]
        IReadOnlyCollection<AdditionalRemote> additionalRemotes = Array.Empty<AdditionalRemote>();
        if (_options.AdditionalRemotes != null)
        {
            additionalRemotes = _options.AdditionalRemotes
                .Select(a => a.Split(':', 2))
                .Select(parts => new AdditionalRemote(parts[0], parts[1]))
                .ToImmutableArray();
        }

        // Perform the reset by updating to the target SHA
        // This will erase differences and repopulate content to match the target SHA
        var codeFlowParameters = new CodeFlowParameters(
            AdditionalRemotes: additionalRemotes,
            TpnTemplatePath: null,
            GenerateCodeOwners: false,
            GenerateCredScanSuppressions: false);

        // We will remove everything not-cloaked and replace it with current contents of the source repo
        // When flowing to the VMR, we remove all files but the cloaked files
        List<string> removalFilters =
        [
            .. mapping.Include.Select(VmrPatchHandler.GetInclusionRule),
            .. mapping.Exclude.Select(VmrPatchHandler.GetExclusionRule)
        ];

        try
        {
            // Delete all uncloaked files in the mapping directory
            var targetDir = _vmrInfo.GetRepoSourcesPath(mapping);
            var result = await _processManager.Execute(
                _processManager.GitExecutable,
                ["rm", "-r", "-q", "-f", "--", .. removalFilters],
                workingDir: targetDir);

            result.ThrowIfFailed($"Failed to remove files in {targetDir}");

            // Tell the VMR dependency tracker that the repository has been reset
            _dependencyTracker.UpdateDependencyVersion(new VmrDependencyUpdate(
                mapping,
                mapping.DefaultRemote,
                DarcLib.Constants.EmptyGitObject,
                Parent: null,
                OfficialBuildId: null,
                BarId: buildId));

            await _vmrUpdater.UpdateRepository(
                mappingName,
                targetSha,
                codeFlowParameters,
                resetToRemoteWhenCloningRepo: false,
                keepConflicts: false,
                CancellationToken.None);

            _logger.LogInformation("Successfully reset {mapping} to {sha}", mappingName, targetSha);
            return Constants.SuccessCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while resetting {mapping}", mappingName);
            return Constants.ErrorCode;
        }
    }

    /// <summary>
    /// Gets the commit SHA and build ID from a BAR build ID and validates that the build's repository matches the mapping.
    /// </summary>
    /// <returns>A tuple containing the commit SHA and the build ID.</returns>
    private async Task<(string sha, int buildId)> GetShaFromBuildAsync(int buildId, string mappingName)
    {
        var build = await _barClient.GetBuildAsync(buildId)
            ?? throw new DarcException($"Build with ID {buildId} not found in BAR.");

        // Validate that the build's repository matches the mapping by checking Version.Details.xml
        IRemote remote = await _remoteFactory.CreateRemoteAsync(build.GetRepository());
        var sourceDependency = await remote.GetSourceDependencyAsync(build.GetRepository(), build.Commit);
            
        if (sourceDependency == null || string.IsNullOrEmpty(sourceDependency.Mapping))
        {
            _logger.LogWarning(
                "Build {buildId} is from repository {repo} that does not have a Source tag in Version.Details.xml at commit {commit}. " +
                "Unable to verify that it matches mapping '{mapping}'. Proceeding with the reset.",
                buildId, build.GetRepository(), mappingName, build.Commit);
        }
        else if (!sourceDependency.Mapping.Equals(mappingName, StringComparison.OrdinalIgnoreCase))
        {
            throw new DarcException(
                $"Build {buildId} is from repository {build.GetRepository()} which has mapping '{sourceDependency.Mapping}' in Version.Details.xml, " +
                $"but you specified mapping '{mappingName}'. These must match.");
        }

        return (build.Commit, build.Id);
    }

    /// <summary>
    /// Gets the commit SHA and build ID from the latest build on a channel for the mapping's default remote.
    /// </summary>
    /// <returns>A tuple containing the commit SHA and the build ID.</returns>
    private async Task<(string sha, int buildId)> GetShaFromChannelAsync(string channelName, SourceMapping mapping)
    {
        _logger.LogInformation("Finding latest build for repository '{repo}' on channel '{channel}'...", 
            mapping.DefaultRemote, channelName);

        var channels = await _barClient.GetChannelsAsync();
        var channel = channels
            .FirstOrDefault(c => c.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase))
            ?? throw new DarcException($"Channel '{channelName}' not found.");

        _logger.LogInformation("Found channel '{channel}' (ID: {channelId})", channel.Name, channel.Id);

        var build = await _barClient.GetLatestBuildAsync(mapping.DefaultRemote, channel.Id)
            ?? throw new DarcException($"No builds found for repository '{mapping.DefaultRemote}' on channel '{channel.Name}'.");

        _logger.LogInformation(
            "Found latest build on channel '{channel}': Build {buildId} @ {commit}",
            channel.Name, build.Id, build.Commit);

        return (build.Commit, build.Id);
    }
}
