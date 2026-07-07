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

/// <summary>
/// Resets the contents of a single VMR submodule to match the current HEAD of the submodule
/// repository the command is run from and updates its record in source-manifest.json.
/// The submodule to reset is identified by its path (as recorded in source-manifest.json),
/// while the target commit and content are detected from the local repository.
/// Changes are staged only (not committed).
/// </summary>
internal class ResetSubmoduleOperation : Operation
{
    private readonly ResetSubmoduleCommandLineOptions _options;
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly ISourceManifest _sourceManifest;
    private readonly IProcessManager _processManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly ILogger<ResetSubmoduleOperation> _logger;

    public ResetSubmoduleOperation(
        ResetSubmoduleCommandLineOptions options,
        IVmrInfo vmrInfo,
        IVmrDependencyTracker dependencyTracker,
        IVmrPatchHandler patchHandler,
        ISourceManifest sourceManifest,
        IProcessManager processManager,
        ILocalGitRepoFactory localGitRepoFactory,
        ILogger<ResetSubmoduleOperation> logger)
    {
        _options = options;
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _patchHandler = patchHandler;
        _sourceManifest = sourceManifest;
        _processManager = processManager;
        _localGitRepoFactory = localGitRepoFactory;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            var submodulePath = GetSubmodulePath();

            var localRepoPath = ResolveLocalRepositoryRoot();
            var targetSha = await _localGitRepoFactory.Create(localRepoPath).GetShaForRefAsync();
            _logger.LogInformation(
                "Using local repository '{repo}' at commit '{sha}' as the submodule source",
                localRepoPath, targetSha);

            _vmrInfo.VmrPath = new NativePath(_options.VmrPath);
            await _dependencyTracker.RefreshMetadataAsync();

            ISourceComponent submodule = FindSubmodule(submodulePath);
            List<string> filters = ResolveCloakingFilters(submodule);

            _logger.LogInformation("Resetting VMR submodule '{path}' to commit '{sha}'", submodule.Path, targetSha);

            await ResetSubmoduleContentAsync(submodule, localRepoPath, targetSha, filters);
            await UpdateManifestAsync(submodule, localRepoPath, targetSha);

            _logger.LogInformation("Successfully reset submodule '{path}' to '{sha}'", submodule.Path, targetSha);
            return Constants.SuccessCode;
        }
        catch (DarcException ex)
        {
            _logger.LogError("{error}", ex.Message);
            return Constants.ErrorCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while resetting the submodule");
            return Constants.ErrorCode;
        }
    }

    /// <summary>
    /// Normalizes and validates the submodule path from the command line.
    /// </summary>
    private string GetSubmodulePath()
    {
        var submodulePath = _options.Path?.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(submodulePath))
        {
            throw new DarcException("A submodule path must be provided (e.g. runtime/src/external/foo).");
        }

        return submodulePath;
    }

    /// <summary>
    /// Resolves the root of the git repository the command is being run from (the submodule source).
    /// </summary>
    private NativePath ResolveLocalRepositoryRoot()
    {
        try
        {
            return new NativePath(_processManager.FindGitRoot(Environment.CurrentDirectory));
        }
        catch (Exception e)
        {
            throw new DarcException(
                $"Could not resolve a git repository root from '{Environment.CurrentDirectory}'. " +
                "Run this command from a local clone of the submodule's repository.", e);
        }
    }

    /// <summary>
    /// Finds the submodule record to reset in the source manifest.
    /// </summary>
    private ISourceComponent FindSubmodule(string submodulePath)
        => _sourceManifest.Submodules.FirstOrDefault(s => s.Path.Equals(submodulePath, StringComparison.OrdinalIgnoreCase))
            ?? throw new DarcException(
                $"Submodule '{submodulePath}' was not found in {VmrInfo.DefaultRelativeSourceManifestPath}. Known submodules:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, _sourceManifest.Submodules.Select(s => $"  {s.Path}")));

    /// <summary>
    /// Builds the git pathspec filters used to reset the submodule content. The submodule path is
    /// [mapping]/[path within the mapping], so the parent mapping's cloaking filters are scoped down
    /// to the submodule's subtree.
    /// </summary>
    private List<string> ResolveCloakingFilters(ISourceComponent submodule)
    {
        var mappingName = submodule.Path.Split('/', 2)[0];
        if (!_dependencyTracker.TryGetMapping(mappingName, out var mapping))
        {
            throw new DarcException($"Could not resolve mapping '{mappingName}' for submodule '{submodule.Path}'.");
        }

        var pathWithinMapping = submodule.Path.Length > mappingName.Length
            ? submodule.Path[(mappingName.Length + 1)..]
            : string.Empty;

        IReadOnlyCollection<string> includes = mapping.Include.Count == 0 ? new[] { "**/*" } : mapping.Include;
        var scopedIncludes = ScopeFiltersToSubmodule(includes, pathWithinMapping);
        if (scopedIncludes.IsEmpty)
        {
            scopedIncludes = ["**/*"];
        }

        var scopedExcludes = ScopeFiltersToSubmodule(mapping.Exclude, pathWithinMapping);

        return
        [
            .. scopedIncludes.Select(VmrPatchHandler.GetInclusionRule),
            .. scopedExcludes.Select(VmrPatchHandler.GetExclusionRule),
        ];
    }

    /// <summary>
    /// Removes the submodule's current content from the VMR and re-populates it from the local
    /// repository's HEAD, staging the changes.
    /// </summary>
    private async Task ResetSubmoduleContentAsync(
        ISourceComponent submodule,
        NativePath localRepoPath,
        string targetSha,
        IReadOnlyCollection<string> filters)
    {
        var targetDir = _vmrInfo.VmrPath / VmrInfo.SourcesDir / submodule.Path;
        var removeResult = await _processManager.Execute(
            _processManager.GitExecutable,
            ["rm", "-r", "-q", "-f", "--", .. filters],
            workingDir: targetDir);
        removeResult.ThrowIfFailed($"Failed to remove existing submodule content in {targetDir}");

        var patchPath = _vmrInfo.TmpPath / $"{SanitizePathForFileName(submodule.Path)}.patch";
        var patches = await _patchHandler.CreatePatches(
            patchPath,
            DarcLib.Constants.EmptyGitObject,
            targetSha,
            path: null,
            filters,
            relativePaths: false,
            workingDir: localRepoPath,
            applicationPath: VmrInfo.SourcesDir / submodule.Path,
            ignoreLineEndings: false,
            CancellationToken.None);

        await _patchHandler.ApplyPatches(
            patches,
            _vmrInfo.VmrPath,
            removePatchAfter: true,
            keepConflicts: false,
            cancellationToken: CancellationToken.None);
    }

    /// <summary>
    /// Updates the submodule's record in source-manifest.json and stages the manifest.
    /// </summary>
    private async Task UpdateManifestAsync(ISourceComponent submodule, NativePath localRepoPath, string targetSha)
    {
        _dependencyTracker.UpdateSubmodules([new SubmoduleRecord(submodule.Path, localRepoPath, targetSha)]);

        var stageManifest = await _processManager.Execute(
            _processManager.GitExecutable,
            ["add", "--", _vmrInfo.SourceManifestPath],
            workingDir: _vmrInfo.VmrPath);
        stageManifest.ThrowIfFailed("Failed to stage source-manifest.json");
    }

    /// <summary>
    /// Keeps only the filters that target the submodule's subtree and rebases them to the submodule root.
    /// </summary>
    private static ImmutableArray<string> ScopeFiltersToSubmodule(IReadOnlyCollection<string> filters, string pathWithinMapping)
    {
        if (string.IsNullOrEmpty(pathWithinMapping))
        {
            return [.. filters];
        }

        return filters
            .Where(p => p.StartsWith(pathWithinMapping, StringComparison.Ordinal))
            .Select(p => p[pathWithinMapping.Length..].TrimStart('/'))
            .Where(p => !string.IsNullOrEmpty(p))
            .ToImmutableArray();
    }

    private static string SanitizePathForFileName(string path)
        => path.Replace('/', '-').Replace('\\', '-');
}
