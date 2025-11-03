// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class is able to initialize an individual repository within the VMR for the first time.
/// It pulls in the new sources adhering to cloaking rules, accommodating for patched files, resolving submodules.
/// It can also initialize all other repositories recursively based on the dependencies stored in Version.Details.xml.
/// </summary>
public class VmrInitializer : VmrManagerBase, IVmrInitializer
{
    // Message shown when initializing an individual repo for the first time
    private const string InitializationCommitMessage =
        $$"""
        [{name}] Initial pull of the individual repository ({newShaShort})

        Original commit: {remote}/commit/{newSha}

        {{Constants.AUTOMATION_COMMIT_TAG}}
        """;

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly IWorkBranchFactory _workBranchFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILocalGitClient _localGitClient;
    private readonly ILogger<VmrUpdater> _logger;

    public VmrInitializer(
            IVmrDependencyTracker dependencyTracker,
            IVmrPatchHandler patchHandler,
            IRepositoryCloneManager cloneManager,
            IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
            ICodeownersGenerator codeownersGenerator,
            ICredScanSuppressionsGenerator credScanSuppressionsGenerator,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IWorkBranchFactory workBranchFactory,
            IFileSystem fileSystem,
            ILogger<VmrUpdater> logger,
            IVmrInfo vmrInfo)
        : base(vmrInfo, dependencyTracker, patchHandler, thirdPartyNoticesGenerator, codeownersGenerator, credScanSuppressionsGenerator, localGitClient, localGitRepoFactory, logger)
    {
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _cloneManager = cloneManager;
        _workBranchFactory = workBranchFactory;
        _fileSystem = fileSystem;
        _localGitClient = localGitClient;
        _logger = logger;
    }

    public async Task InitializeRepository(
        string mappingName,
        string? targetRevision,
        string? remoteUri,
        LocalPath sourceMappingsPath,
        CodeFlowParameters codeFlowParameters,
        CancellationToken cancellationToken)
    {
        // Ensure source mapping exists before initializing
        await EnsureSourceMappingExistsAsync(mappingName, remoteUri, sourceMappingsPath, cancellationToken);

        await _dependencyTracker.RefreshMetadataAsync(sourceMappingsPath);
        var mapping = _dependencyTracker.GetMapping(mappingName);

        if (_dependencyTracker.GetDependencyVersion(mapping) is not null)
        {
            throw new EmptySyncException($"Repository {mapping.Name} already exists");
        }

        var workBranchName = $"init/{mapping.Name}";
        if (targetRevision != null)
        {
            workBranchName += $"/{targetRevision}";
        }

        IWorkBranch workBranch = await _workBranchFactory.CreateWorkBranchAsync(GetLocalVmr(), workBranchName);

        var update = new VmrDependencyUpdate(
            mapping,
            mapping.DefaultRemote,
            targetRevision ?? mapping.DefaultRef,
            Parent: null,
            OfficialBuildId: null,
            BarId: null);

        try
        {
            var sourcesPath = _vmrInfo.GetRepoSourcesPath(update.Mapping);
            if (_fileSystem.DirectoryExists(sourcesPath)
                && _fileSystem.GetFiles(sourcesPath).Length > 1
                && _dependencyTracker.GetDependencyVersion(update.Mapping) == null)
            {
                throw new InvalidOperationException(
                    $"Sources for {update.Mapping.Name} already exists but repository is not initialized properly. " +
                     "Please investigate!");
            }

            await InitializeRepository(update, codeFlowParameters, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning(
                InterruptedSyncExceptionMessage,
                workBranch.OriginalBranchName.StartsWith("sync") || workBranch.OriginalBranchName.StartsWith("init") ?
                "the original" : workBranch.OriginalBranchName);
            throw;
        }

        await workBranch.RebaseAsync(cancellationToken);

        _logger.LogInformation("Repository {repo} added (staged)", mapping.Name);
    }

    private async Task InitializeRepository(
        VmrDependencyUpdate update,
        CodeFlowParameters codeFlowParameters,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing {name} at {revision}..", update.Mapping.Name, update.TargetRevision);

        var remotes = codeFlowParameters.AdditionalRemotes
            .Where(r => r.Mapping == update.Mapping.Name)
            .Select(r => r.RemoteUri)
            .Prepend(update.RemoteUri)
            .ToArray();

        ILocalGitRepo clone = await _cloneManager.PrepareCloneAsync(
            update.Mapping,
            remotes,
            new[] { update.TargetRevision },
            update.TargetRevision,
            resetToRemote: false,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        update = update with
        {
            TargetRevision = await clone.GetShaForRefAsync(update.TargetRevision)
        };

        string commitMessage = PrepareCommitMessage(
            InitializationCommitMessage,
            update.Mapping.Name,
            update.RemoteUri,
            newSha: update.TargetRevision);

        await UpdateRepoToRevisionAsync(
            update,
            clone,
            Constants.EmptyGitObject,
            commitMessage,
            restoreVmrPatches: false,
            keepConflicts: false,
            codeFlowParameters,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Initialization of {name} finished", update.Mapping.Name);
    }

    private async Task<bool> EnsureSourceMappingExistsAsync(
        string repoName,
        string? defaultRemote,
        LocalPath sourceMappingsPath,
        CancellationToken cancellationToken)
    {
        // Refresh metadata to load existing mappings
        await _dependencyTracker.RefreshMetadataAsync(sourceMappingsPath);

        // Check if mapping already exists
        if (_dependencyTracker.TryGetMapping(repoName, out _))
        {
            _logger.LogInformation("Source mapping for '{repoName}' already exists", repoName);
            return false;
        }

        // Read the existing source-mappings.json file
        var json = await _fileSystem.ReadAllTextAsync(sourceMappingsPath);

        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault | JsonIgnoreCondition.WhenWritingNull,
        };

        var sourceMappingFile = JsonSerializer.Deserialize<SourceMappingFile>(json, options)
            ?? throw new Exception($"Failed to deserialize {VmrInfo.SourceMappingsFileName}");

        // Determine the default remote URL
        // If not provided, use GitHub dotnet org
        defaultRemote ??= $"https://github.com/dotnet/{repoName}";

        // Add the new mapping
        var newMapping = new SourceMappingSetting
        {
            Name = repoName,
            DefaultRemote = defaultRemote,
        };

        sourceMappingFile.Mappings.Add(newMapping);

        // Write the updated source-mappings.json file
        var updatedJson = JsonSerializer.Serialize(sourceMappingFile, options);
        _fileSystem.WriteToFile(sourceMappingsPath, updatedJson);

        // Stage the source-mappings.json file
        await _localGitClient.StageAsync(_vmrInfo.VmrPath, [sourceMappingsPath], cancellationToken);

        _logger.LogInformation("Added source mapping for '{repoName}' with remote '{defaultRemote}' and staged {file}",
            repoName, defaultRemote, VmrInfo.SourceMappingsFileName);

        return true;
    }
}
