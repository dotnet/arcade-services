// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface ICodeflowSourceDiffVerifier
{
    /// <summary>
    /// Returns the mapping-relative paths where a forward-flow codeflow PR (source repo -> VMR)
    /// differs from the source repo's commit diff (oldSha...newSha), accounting for the expected
    /// divergences (path remap, excludes, eng/common, version files, no-ops).
    /// </summary>
    Task<IReadOnlyList<string>> ForwardFlowMatchesSourceDiffAsync(
        string sourceRepoUri,
        string vmrUri,
        string mappingName,
        string oldSha,
        string newSha,
        string vmrTargetBranch,
        string vmrHeadBranch,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Verifies that a forward-flow codeflow PR faithfully reflects the source repo's commit diff,
/// accounting for the expected, legitimate divergences between a source repo and its VMR copy
/// (path remap, cloaked/excluded paths, eng/common, version/metadata files and no-ops).
/// </summary>
public class CodeflowSourceDiffVerifier : ICodeflowSourceDiffVerifier
{
    private readonly IVmrCloneManager _vmrCloneManager;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILogger<CodeflowSourceDiffVerifier> _logger;

    public CodeflowSourceDiffVerifier(
        IVmrCloneManager vmrCloneManager,
        IRepositoryCloneManager cloneManager,
        IVmrDependencyTracker dependencyTracker,
        ILogger<CodeflowSourceDiffVerifier> logger)
    {
        _vmrCloneManager = vmrCloneManager;
        _cloneManager = cloneManager;
        _dependencyTracker = dependencyTracker;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> ForwardFlowMatchesSourceDiffAsync(
        string sourceRepoUri,
        string vmrUri,
        string mappingName,
        string oldSha,
        string newSha,
        string vmrTargetBranch,
        string vmrHeadBranch,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Verifying forward flow PR for {mappingName} against source diff {oldSha}...{newSha}",
            mappingName,
            oldSha,
            newSha);

        var srcMappingPath = VmrInfo.GetRelativeRepoSourcesPath(mappingName);

        ILocalGitRepo vmr = await _vmrCloneManager.PrepareVmrAsync(
            [vmrUri],
            [vmrTargetBranch, vmrHeadBranch],
            vmrHeadBranch,
            resetToRemote: true,
            cancellationToken);

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);
        HashSet<string> changedSubmodulePaths = await GetChangedSubmodulePathsAsync(
            vmr,
            mappingName,
            vmrTargetBranch,
            vmrHeadBranch,
            cancellationToken);

        var srcMappingPrefix = srcMappingPath + "/";
        List<string> sourceExclusionPathspecs =
        [
            .. GetSourceExclusionPathspecs(mapping),
            .. changedSubmodulePaths.Select(VmrPatchHandler.GetExclusionRule),
        ];
        List<string> vmrExclusionPathspecs =
        [
            .. GetStandardExclusionPathspecs(mappingName, srcMappingPrefix),
            .. changedSubmodulePaths.Select(path => VmrPatchHandler.GetExclusionRule(srcMappingPrefix + path)),
        ];

        ILocalGitRepo sourceRepo = await _cloneManager.PrepareCloneAsync(
            mapping,
            [sourceRepoUri],
            [oldSha, newSha],
            newSha,
            resetToRemote: false,
            cancellationToken);

        HashSet<string> sourceRepoChangedFiles = await GetChangedMappingFilesAsync(
            sourceRepo, oldSha, newSha, exclusionPathspecs: sourceExclusionPathspecs, cancellationToken: cancellationToken);
        HashSet<string> vmrPrChangedFiles = await GetChangedMappingFilesAsync(
            vmr, vmrTargetBranch, vmrHeadBranch, relativePath: srcMappingPrefix, exclusionPathspecs: vmrExclusionPathspecs, cancellationToken: cancellationToken);

        var filesChangedInBoth = sourceRepoChangedFiles.Where(vmrPrChangedFiles.Contains).ToList();
        var sourceRepoOnlyChanges = sourceRepoChangedFiles.Where(f => !vmrPrChangedFiles.Contains(f)).ToList();
        var filesChangedInPrOnly = vmrPrChangedFiles.Where(f => !sourceRepoChangedFiles.Contains(f)).ToList();
        var mismatchedFiles = new List<string>(filesChangedInPrOnly);

        if (filesChangedInPrOnly.Count > 0)
        {
            _logger.LogInformation(
                "Source diff verification for {mappingName} failed: {unexpected} file(s) changed in the PR but not in the source diff",
                mappingName,
                filesChangedInPrOnly.Count);
        }

        // Per-file content compare on the intersection.
        foreach (var file in filesChangedInBoth)
        {
            if (!await ChangedLinesMatchAsync(sourceRepo, vmr, file, srcMappingPath, oldSha, newSha, vmrTargetBranch, vmrHeadBranch, cancellationToken))
            {
                _logger.LogInformation(
                    "Source diff verification for {mappingName} failed: changes to {file} don't match the source diff",
                    mappingName,
                    file);
                mismatchedFiles.Add(file);
            }
        }

        // No-op check on files the source changed but the PR did not.
        foreach (var file in sourceRepoOnlyChanges)
        {
            if (!await IsLegitimateNoOpAsync(sourceRepo, vmr, file, srcMappingPath, newSha, vmrHeadBranch))
            {
                _logger.LogInformation(
                    "Source diff verification for {mappingName} failed: {file} changed in the source diff but is not reflected in the PR",
                    mappingName,
                    file);
                mismatchedFiles.Add(file);
            }
        }

        if (mismatchedFiles.Count == 0)
        {
            _logger.LogInformation("Source diff verification for {mappingName} passed", mappingName);
        }

        return mismatchedFiles
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<HashSet<string>> GetChangedSubmodulePathsAsync(
        ILocalGitRepo vmr,
        string mappingName,
        string vmrTargetBranch,
        string vmrHeadBranch,
        CancellationToken cancellationToken)
    {
        var mergeBaseResult = await vmr.ExecuteGitCommand(
            ["merge-base", vmrTargetBranch, vmrHeadBranch],
            cancellationToken);
        mergeBaseResult.ThrowIfFailed(
            $"Failed to find the merge base between {vmrTargetBranch} and {vmrHeadBranch}");

        string mergeBase = mergeBaseResult.StandardOutput.Trim();
        SourceManifest oldManifest = await GetSourceManifestAsync(vmr, mergeBase);
        SourceManifest newManifest = await GetSourceManifestAsync(vmr, vmrHeadBranch);
        string mappingPrefix = mappingName + "/";
        Dictionary<string, (string RemoteUri, string CommitSha)> oldSubmodules = oldManifest
            .GetSubmodulesForMapping(mappingName)
            .ToDictionary(
                submodule => submodule.Path,
                submodule => (submodule.RemoteUri, submodule.CommitSha),
                StringComparer.Ordinal);
        Dictionary<string, (string RemoteUri, string CommitSha)> newSubmodules = newManifest
            .GetSubmodulesForMapping(mappingName)
            .ToDictionary(
                submodule => submodule.Path,
                submodule => (submodule.RemoteUri, submodule.CommitSha),
                StringComparer.Ordinal);

        return oldSubmodules.Keys
            .Concat(newSubmodules.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(path =>
                !oldSubmodules.TryGetValue(path, out var oldSubmodule) ||
                !newSubmodules.TryGetValue(path, out var newSubmodule) ||
                oldSubmodule != newSubmodule)
            .Select(path => path.Substring(mappingPrefix.Length))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<SourceManifest> GetSourceManifestAsync(
        ILocalGitRepo vmr,
        string revision)
    {
        string? manifestContents = await vmr.GetFileFromGitAsync(
            VmrInfo.DefaultRelativeSourceManifestPath,
            revision);
        return manifestContents == null
            ? new SourceManifest([], [])
            : SourceManifest.FromJson(manifestContents);
    }

    /// <summary>
    /// Builds the source repository's Git pathspec exclusions from the mapping's excludes and the standard
    /// codeflow-managed paths. Submodule exclusions are added separately based on the manifest comparison.
    /// </summary>
    private static List<string> GetSourceExclusionPathspecs(SourceMapping mapping)
    {
        return
        [
            .. (mapping.Exclude ?? []).Select(VmrPatchHandler.GetExclusionRule),
            .. GetStandardExclusionPathspecs(mapping.Name, string.Empty),
        ];
    }

    private static IEnumerable<string> GetStandardExclusionPathspecs(
        string mappingName,
        string pathPrefix)
    {
        IEnumerable<string> paths = DependencyFileManager.CodeflowDependencyFiles
            .Select(path => pathPrefix + path);
        if (mappingName != VmrInfo.ArcadeMappingName)
        {
            paths = paths.Append(pathPrefix + Constants.CommonScriptFilesPath);
        }

        return paths.Select(VmrPatchHandler.GetExclusionRule);
    }

    /// <summary>
    /// Runs a three-dot name-only diff (<paramref name="fromRef"/>...<paramref name="toRef"/>) scoped to the
    /// mapping's location in the repo and returns mapping-relative paths after applying the supplied exclusions.
    /// </summary>
    private static async Task<HashSet<string>> GetChangedMappingFilesAsync(
        ILocalGitRepo repo,
        string fromRef,
        string toRef,
        string? relativePath = null,
        IReadOnlyCollection<string>? exclusionPathspecs = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<string> pathspecs = string.IsNullOrEmpty(relativePath)
            ? [".", .. exclusionPathspecs ?? []]
            : [relativePath, .. exclusionPathspecs ?? []];

        var result = await repo.ExecuteGitCommand(
            ["diff", "--name-only", $"{fromRef}...{toRef}", "--", .. pathspecs],
            cancellationToken);
        result.ThrowIfFailed($"Failed to get the diff between {fromRef} and {toRef}");

        IEnumerable<string> files = result.GetOutputLines();
        if (!string.IsNullOrEmpty(relativePath))
        {
            files = files.Select(f => f.Substring(relativePath.Length));
        }

        return files.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Compares the zero-context change lines of a single file between the source diff and the PR.
    /// Lines that belong to the diff format (and are expected to differ) are ignored before comparing.
    /// </summary>
    private static async Task<bool> ChangedLinesMatchAsync(
        ILocalGitRepo sourceRepo,
        ILocalGitRepo vmr,
        string file,
        UnixPath srcMappingPath,
        string oldSha,
        string newSha,
        string vmrTargetBranch,
        string vmrHeadBranch,
        CancellationToken cancellationToken)
    {
        var sourceResult = await sourceRepo.ExecuteGitCommand(["diff", "-U0", $"{oldSha}...{newSha}", "--", file], cancellationToken);
        sourceResult.ThrowIfFailed($"Failed to get the source diff of {file} between {oldSha} and {newSha}");

        var vmrResult = await vmr.ExecuteGitCommand(["diff", "-U0", $"{vmrTargetBranch}...{vmrHeadBranch}", "--", srcMappingPath / file], cancellationToken);
        vmrResult.ThrowIfFailed($"Failed to get the VMR diff of {file} between {vmrTargetBranch} and {vmrHeadBranch}");

        var sourceChanges = GetChangeLines(sourceResult.GetOutputLines());
        var vmrChanges = GetChangeLines(vmrResult.GetOutputLines());

        return sourceChanges.SequenceEqual(vmrChanges);
    }

    /// <summary>
    /// Keeps only the +/- change lines from a zero-context diff. Only lines inside a hunk (after an
    /// "@@" header) are collected, so the per-file "--- a/file" / "+++ b/file" headers - which share a
    /// prefix with genuine content lines such as a removed "-- comment" (rendered as "--- comment") - are
    /// excluded structurally rather than by an ambiguous textual prefix match.
    /// </summary>
    private static List<string> GetChangeLines(IReadOnlyCollection<string> lines)
    {
        var changeLines = new List<string>();
        var insideHunk = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("@@"))
            {
                // Start adding change lines after this hunk header.
                insideHunk = true;
            }
            else if (insideHunk && (line.StartsWith('+') || line.StartsWith('-')))
            {
                changeLines.Add(line);
            }
        }

        return changeLines;
    }

    /// <summary>
    /// A file the source changed but the PR did not is only legitimate when the VMR copy is already
    /// at the source's new state (equal content, or both absent for an already-reconciled deletion).
    /// </summary>
    private static async Task<bool> IsLegitimateNoOpAsync(
        ILocalGitRepo sourceRepo,
        ILocalGitRepo vmr,
        string file,
        UnixPath srcMappingPath,
        string newSha,
        string vmrHeadBranch)
    {
        var sourceContent = await sourceRepo.GetFileFromGitAsync(file, newSha);
        var vmrContent = await vmr.GetFileFromGitAsync(srcMappingPath / file, vmrHeadBranch);

        if (sourceContent == null && vmrContent == null)
        {
            return true;
        }

        return string.Equals(sourceContent, vmrContent, StringComparison.Ordinal);
    }
}
