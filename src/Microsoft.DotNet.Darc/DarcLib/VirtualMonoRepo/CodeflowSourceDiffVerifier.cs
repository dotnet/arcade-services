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
    /// Verifies that a forward-flow codeflow PR (source repo -> VMR) faithfully contains the source
    /// repo's commit diff (oldSha...newSha), accounting for the expected divergences (path remap,
    /// excludes, eng/common, version files, no-ops).
    /// </summary>
    Task<bool> VerifyForwardFlowAsync(
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
    private readonly ISourceManifest _sourceManifest;
    private readonly ILogger<CodeflowSourceDiffVerifier> _logger;

    public CodeflowSourceDiffVerifier(
        IVmrCloneManager vmrCloneManager,
        IRepositoryCloneManager cloneManager,
        IVmrDependencyTracker dependencyTracker,
        ISourceManifest sourceManifest,
        ILogger<CodeflowSourceDiffVerifier> logger)
    {
        _vmrCloneManager = vmrCloneManager;
        _cloneManager = cloneManager;
        _dependencyTracker = dependencyTracker;
        _sourceManifest = sourceManifest;
        _logger = logger;
    }

    public async Task<bool> VerifyForwardFlowAsync(
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

        // Make sure both the target and the PR head branches are available locally in the VMR.
        ILocalGitRepo vmr = await _vmrCloneManager.PrepareVmrAsync(
            [vmrUri],
            [vmrTargetBranch, vmrHeadBranch],
            vmrHeadBranch,
            resetToRemote: true,
            cancellationToken);

        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);

        var exclusionPathspecs = GetDiffFilters(mapping, _sourceManifest);

        // Obtain a local clone of the source repo containing both SHAs.
        ILocalGitRepo sourceRepo = await _cloneManager.PrepareCloneAsync(
            mapping,
            [sourceRepoUri],
            [oldSha, newSha],
            newSha,
            resetToRemote: false,
            cancellationToken);

        var srcMappingPrefix = srcMappingPath + "/";
        HashSet<string> sourceRepoChanges = await GetChangedMappingFilesAsync(
            sourceRepo, mappingName, oldSha, newSha, exclusionPathspecs: exclusionPathspecs, cancellationToken: cancellationToken);
        HashSet<string> vmrPrChanges = await GetChangedMappingFilesAsync(
            vmr, mappingName, vmrTargetBranch, vmrHeadBranch, relativePath: srcMappingPrefix, cancellationToken: cancellationToken);

        var intersection = sourceRepoChanges.Where(vmrPrChanges.Contains).ToList();
        var sourceRepoOnlyChanges = sourceRepoChanges.Where(f => !vmrPrChanges.Contains(f)).ToList();
        var unexpectedFiles = vmrPrChanges.Where(f => !sourceRepoChanges.Contains(f)).ToList();

        // The PR changed files that the source diff didn't - the codeflow can't be trusted.
        if (unexpectedFiles.Count > 0)
        {
            _logger.LogInformation(
                "Source diff verification for {mappingName} failed: {unexpected} file(s) changed in the PR but not in the source diff",
                mappingName,
                unexpectedFiles.Count);
            return false;
        }

        // Per-file content compare on the intersection.
        foreach (var file in intersection)
        {
            if (!await ChangedLinesMatchAsync(sourceRepo, vmr, file, srcMappingPath, oldSha, newSha, vmrTargetBranch, vmrHeadBranch, cancellationToken))
            {
                _logger.LogInformation(
                    "Source diff verification for {mappingName} failed: changes to {file} don't match the source diff",
                    mappingName,
                    file);
                return false;
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
                return false;
            }
        }

        _logger.LogInformation("Source diff verification for {mappingName} passed", mappingName);
        return true;
    }

    /// <summary>
    /// Builds the git pathspec exclusion rules the same way VmrDiffOperation.GetDiffFilters does:
    /// the mapping's excludes plus submodule paths under the mapping, turned into git exclusion rules.
    /// </summary>
    private static List<string> GetDiffFilters(SourceMapping mapping, ISourceManifest manifest)
    {
        var submodules = manifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/', StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1));

        return (mapping.Exclude ?? [])
            .Concat(submodules)
            .Select(VmrPatchHandler.GetExclusionRule)
            .ToList();
    }

    /// <summary>
    /// Runs a three-dot name-only diff (<paramref name="fromRef"/>...<paramref name="toRef"/>) scoped to the
    /// mapping's location in the repo and returns the mapping-relative paths, after dropping eng/common
    /// (non-arcade) and version/metadata files. Used for both the source repo diff (mapping lives at the repo
    /// root, so <paramref name="relativePath"/> is empty) and the target repo (VMR) diff (mapping lives
    /// under src/&lt;mapping&gt;/, which is stripped from the resulting paths).
    /// </summary>
    private static async Task<HashSet<string>> GetChangedMappingFilesAsync(
        ILocalGitRepo repo,
        string mappingName,
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
            files = files
                .Where(f => f.StartsWith(relativePath, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Substring(relativePath.Length));
        }

        return FilterMappingFiles(files, mappingName);
    }

    /// <summary>
    /// Drops eng/common (for non-arcade mappings) and codeflow version/metadata files, which are
    /// expected to legitimately diverge between the source repo and the VMR.
    /// </summary>
    private static HashSet<string> FilterMappingFiles(IEnumerable<string> files, string mappingName)
    {
        var engCommonPrefix = Constants.CommonScriptFilesPath + "/";
        var dropEngCommon = mappingName != VmrInfo.ArcadeMappingName;

        return files
            .Where(f => !DependencyFileManager.CodeflowDependencyFiles.Contains(f, StringComparer.OrdinalIgnoreCase))
            .Where(f => !dropEngCommon || !f.StartsWith(engCommonPrefix, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compares the zero-context change lines of a single file between the source diff and the PR.
    /// Lines that belong to the diff format (and are expected to differ) are ignored before comparing.
    /// </summary>
    private static async Task<bool> ChangedLinesMatchAsync(
        ILocalGitRepo sourceRepo,
        ILocalGitRepo vmr,
        string file,
        string srcMappingPath,
        string oldSha,
        string newSha,
        string vmrTargetBranch,
        string vmrHeadBranch,
        CancellationToken cancellationToken)
    {
        var sourceResult = await sourceRepo.ExecuteGitCommand(["diff", "-U0", $"{oldSha}...{newSha}", "--", file], cancellationToken);
        sourceResult.ThrowIfFailed($"Failed to get the source diff of {file} between {oldSha} and {newSha}");

        var vmrResult = await vmr.ExecuteGitCommand(["diff", "-U0", $"{vmrTargetBranch}...{vmrHeadBranch}", "--", $"{srcMappingPath}/{file}"], cancellationToken);
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
                // Start of a hunk; the lines that follow (until the next hunk or file) are the changes.
                insideHunk = true;
            }
            else if (line.StartsWith("diff --git"))
            {
                // Start of a new file's header section, whose "--- "/"+++ " headers precede its first hunk.
                insideHunk = false;
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
        string srcMappingPath,
        string newSha,
        string vmrHeadBranch)
    {
        var sourceContent = await sourceRepo.GetFileFromGitAsync(file, newSha);
        var vmrContent = await vmr.GetFileFromGitAsync($"{srcMappingPath}/{file}", vmrHeadBranch);

        if (sourceContent == null && vmrContent == null)
        {
            return true;
        }

        return string.Equals(sourceContent, vmrContent, StringComparison.Ordinal);
    }
}
