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
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IBarApiClient _barClient;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly ILogger<ResetOperation> _logger;

    public ResetOperation(
        ResetCommandLineOptions options,
        IVmrInfo vmrInfo,
        IVmrUpdater vmrUpdater,
        IVmrDependencyTracker dependencyTracker,
        IProcessManager processManager,
        ILocalGitRepoFactory localGitRepoFactory,
        IBarApiClient barClient,
        IRemoteFactory remoteFactory,
        IVersionDetailsParser versionDetailsParser,
        ILogger<ResetOperation> logger)
    {
        _options = options;
        _vmrInfo = vmrInfo;
        _vmrUpdater = vmrUpdater;
        _dependencyTracker = dependencyTracker;
        _processManager = processManager;
        _localGitRepoFactory = localGitRepoFactory;
        _barClient = barClient;
        _remoteFactory = remoteFactory;
        _versionDetailsParser = versionDetailsParser;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        if (_options.Build.HasValue && !string.IsNullOrEmpty(_options.Channel))
        {
            _logger.LogError("Cannot specify both --build and --channel options together.");
            return Constants.ErrorCode;
        }

        bool usingBuildOrChannel = _options.Build.HasValue || !string.IsNullOrEmpty(_options.Channel);

        // 1. Resolve which mapping to reset and where the target SHA will come from.
        //    The SHA itself is resolved later, once the mapping is known.
        string mappingName;
        string? explicitSha = null;
        NativePath? currentRepoPath = null;

        if (!string.IsNullOrWhiteSpace(_options.Target))
        {
            // A target was provided explicitly - use it
            if (usingBuildOrChannel)
            {
                // When --build or --channel is provided, the target should only be the mapping name
                if (_options.Target.Contains(':'))
                {
                    _logger.LogError("When using --build or --channel, the target should only contain the mapping name, not [mapping]:[sha]. Got: {input}", _options.Target);
                    return Constants.ErrorCode;
                }

                mappingName = _options.Target;
            }
            else
            {
                // Default behavior: Target is in the format [mapping]:[sha]
                var parts = _options.Target.Split(':', 2);
                if (parts.Length != 2)
                {
                    _logger.LogError(
                        "Invalid format. Expected [mapping]:[sha], use --build/--channel, or run from the source repository, but got: {input}",
                        _options.Target);
                    return Constants.ErrorCode;
                }

                mappingName = parts[0];
                explicitSha = parts[1];

                if (string.IsNullOrWhiteSpace(mappingName))
                {
                    _logger.LogError("Mapping name cannot be empty in [mapping]:[sha]. Got: '{input}'", _options.Target);
                    return Constants.ErrorCode;
                }

                if (string.IsNullOrWhiteSpace(explicitSha))
                {
                    _logger.LogError("Target SHA cannot be empty. Got: '{input}'", _options.Target);
                    return Constants.ErrorCode;
                }
            }
        }
        else
        {
            // No target provided - infer the mapping from the current repository's Source tag, like forward flow does
            CurrentRepositoryInfo? currentRepository = TryResolveCurrentRepository();
            if (currentRepository == null)
            {
                return Constants.ErrorCode;
            }

            mappingName = currentRepository.MappingName;
            currentRepoPath = currentRepository.RepositoryPath;
            _logger.LogInformation("Resolved mapping '{mapping}' from repository '{repo}'", mappingName, currentRepoPath);
        }

        _vmrInfo.VmrPath = new NativePath(_options.VmrPath);

        // 2. Validate that the mapping exists
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

        // 3. Resolve the target SHA from the single source that was specified:
        //    --build, --channel, the explicit [mapping]:[sha], or the current repository's HEAD.
        string targetSha;
        NativePath? localRepositoryRemote = null;
        ProductConstructionService.Client.Models.Build? build = null;

        if (_options.Build.HasValue)
        {
            build = await GetBuildAsync(_options.Build.Value, mappingName);
            targetSha = build.Commit;
        }
        else if (!string.IsNullOrEmpty(_options.Channel))
        {
            build = await GetBuildFromChannelAsync(_options.Channel, mapping);
            targetSha = build.Commit;
        }
        else if (explicitSha != null)
        {
            targetSha = explicitSha;
        }
        else
        {
            // Reset to whatever is currently checked out locally (like forward flow)
            targetSha = await _localGitRepoFactory.Create(currentRepoPath!).GetShaForRefAsync();
            localRepositoryRemote = currentRepoPath;
            _logger.LogInformation("Resolved current commit '{sha}' from repository '{repo}'", targetSha, currentRepoPath);
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

        if (localRepositoryRemote != null)
        {
            additionalRemotes =
            [
                .. additionalRemotes,
                new AdditionalRemote(mappingName, localRepositoryRemote)
            ];
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
                BarId: build?.Id));

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
    /// Gets the build from BAR and validates that the build's repository matches the mapping.
    /// </summary>
    /// <returns>The Build object.</returns>
    private async Task<ProductConstructionService.Client.Models.Build> GetBuildAsync(int buildId, string mappingName)
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

        return build;
    }

    /// <summary>
    /// Gets the build from the latest build on a channel for the mapping's default remote.
    /// </summary>
    /// <returns>The Build object.</returns>
    private async Task<ProductConstructionService.Client.Models.Build> GetBuildFromChannelAsync(string channelName, SourceMapping mapping)
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

        return build;
    }

    private CurrentRepositoryInfo? TryResolveCurrentRepository()
    {
        NativePath currentRepoPath;
        try
        {
            currentRepoPath = new(_processManager.FindGitRoot(Environment.CurrentDirectory));
        }
        catch (Exception)
        {
            _logger.LogError(
                "Could not resolve a git repository root from '{path}'. Run this command from a source repository with a VMR Source tag, or specify [mapping]:[sha] explicitly instead.",
                Environment.CurrentDirectory);
            return null;
        }

        string? mappingName;
        try
        {
            var versionDetails = _versionDetailsParser.ParseVersionDetailsFile(currentRepoPath / VersionFiles.VersionDetailsXml);
            mappingName = versionDetails?.Source?.Mapping;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Could not read {file} from repository '{repo}': {error} " +
                "Run this command from a source repository that has a VMR Source tag, or specify [mapping]:[sha] explicitly instead.",
                VersionFiles.VersionDetailsXml, currentRepoPath, ex.Message);
            return null;
        }

        if (string.IsNullOrEmpty(mappingName))
        {
            _logger.LogError(
                "Current repository '{repo}' is missing a Source tag in {file}. " +
                "Run this command from a source repository that has a VMR Source tag, or specify [mapping]:[sha] explicitly instead.",
                currentRepoPath, VersionFiles.VersionDetailsXml);
            return null;
        }

        return new CurrentRepositoryInfo(mappingName, currentRepoPath);
    }

    private sealed record CurrentRepositoryInfo(string MappingName, NativePath RepositoryPath);
}
