// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// This class handles git patches during VMR synchronization.
/// It can create/apply diffs and handle VMR patches which are additional patches stored in the VMR
/// that are applied on top of individual repositories.
/// </summary>
public class VmrPatchHandler : IVmrPatchHandler
{
    // New git versions have a limit on the size of a patch file that can be applied (1GB)
    private const uint MaxPatchSize = 1_000_000_000;

    /// <summary>
    /// Matches output of `git apply --numstat` which lists files contained in a patch file.
    /// Example output:
    /// 0       14      /s/vmr/src/roslyn-analyzers/eng/Versions.props
    /// -       -       /s/vmr/src/roslyn-analyzers/some-binary.dll
    /// </summary>
    private static readonly Regex GitPatchSummaryLine = new(@"^[\-0-9]+\s+[\-0-9]+\s+(?<file>[^\n\r]+)$", RegexOptions.Compiled);

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitClient _localGitClient;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly IProcessManager _processManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrPatchHandler> _logger;

    public VmrPatchHandler(
        IVmrInfo vmrInfo,
        IVmrDependencyTracker dependencyTracker,
        ILocalGitClient localGitClient,
        IRepositoryCloneManager cloneManager,
        IProcessManager processManager,
        IFileSystem fileSystem,
        ILogger<VmrPatchHandler> logger)
    {
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _localGitClient = localGitClient;
        _cloneManager = cloneManager;
        _processManager = processManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// Creates a patch file (a diff) for given two commits in a repo adhering to the in/exclusion filters of the mapping.
    /// Submodules are recursively inlined into the VMR (patch is created for each submodule separately).
    /// </summary>
    /// <param name="mapping">Individual repository mapping</param>
    /// <param name="sha1">Diff from this commit</param>
    /// <param name="sha2">Diff to this commit</param>
    /// <param name="destDir">Directory where patches should be placed</param>
    /// <param name="tmpPath">Directory where submodules can be cloned temporarily</param>
    /// <param name="cancellationToken">Cancellation is safe with regards to git operations</param>
    /// <returns>List of patch files that can be applied on the VMR</returns>
    public async Task<List<VmrIngestionPatch>> CreatePatches(
        SourceMapping mapping,
        ILocalGitRepo clone,
        string sha1,
        string sha2,
        NativePath destDir,
        NativePath tmpPath,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating patches for {mapping} in {path}..", mapping.Name, destDir);

        var patches = await CreatePatchesRecursive(
            mapping,
            clone,
            sha1,
            sha2,
            destDir,
            tmpPath,
            new UnixPath(mapping.Name),
            cancellationToken);

        _logger.LogInformation("{count} patch{s} created", patches.Count, patches.Count == 1 ? string.Empty : "es");

        return patches;
    }

    private async Task<List<VmrIngestionPatch>> CreatePatchesRecursive(
        SourceMapping mapping,
        ILocalGitRepo clone,
        string sha1,
        string sha2,
        NativePath destDir,
        NativePath tmpPath,
        UnixPath relativePath,
        CancellationToken cancellationToken)
    {
        var repoPath = clone.Path;
        if (_fileSystem.GetFileName(repoPath) == ".git")
        {
            repoPath = new NativePath(_fileSystem.GetDirectoryName(repoPath)!);
        }

        List<SubmoduleChange> submoduleChanges = await GetSubmoduleChanges(clone, sha1, sha2);

        var changedRecords = submoduleChanges
            .Select(c => new SubmoduleRecord(relativePath / c.Path, c.Url, c.After))
            .ToList();

        _dependencyTracker.UpdateSubmodules(changedRecords);

        if (!mapping.Include.Any())
        {
            mapping = mapping with
            {
                Include = new[] { "**/*" }
            };
        }

        List<string> filters =
        [
            .. mapping.Include.Select(GetInclusionRule),
            .. mapping.Exclude.Select(GetExclusionRule),
        ];

        // Ignore submodules in the diff, they will be inlined via their own diffs
        if (submoduleChanges.Any())
        {
            filters.AddRange(submoduleChanges.Select(c => $":(exclude){c.Path}").Distinct());
        }

        var patchName = destDir / $"{mapping.Name}-{Commit.GetShortSha(sha1)}-{Commit.GetShortSha(sha2)}.patch";

        var patches = new List<VmrIngestionPatch>();
        patches.AddRange(await CreatePatches(
            patchName,
            sha1,
            sha2,
            path: null,
            filters,
            relativePaths: false,
            repoPath,
            VmrInfo.SourcesDir / relativePath,
            ignoreLineEndings: false,
            cancellationToken));

        if (!submoduleChanges.Any())
        {
            return patches;
        }

        _logger.LogInformation("Creating diffs for submodules of {repo}..", mapping.Name);

        foreach (var change in submoduleChanges)
        {
            if (change.Before == change.After)
            {
                _logger.LogInformation("No changes for submodule {submodule} of {repo}", change.Name, mapping.Name);
                continue;
            }

            if (change.Before == Constants.EmptyGitObject)
            {
                _logger.LogInformation("New submodule {submodule} was added to {repo} at {path} @ {sha}",
                    change.Name, mapping.Name, change.Path, Commit.GetShortSha(change.After));
            }
            else if (change.After == Constants.EmptyGitObject)
            {
                _logger.LogInformation("Submodule {submodule} of {repo} was removed",
                    change.Name, mapping.Name);
            }
            else
            {
                _logger.LogInformation("Found changes for submodule {submodule} of {repo} ({sha1}..{sha2})",
                    change.Name, mapping.Name, Commit.GetShortSha(change.Before), Commit.GetShortSha(change.After));
            }

            patches.AddRange(await GetPatchesForSubmoduleChange(
                mapping,
                destDir,
                tmpPath,
                relativePath,
                change,
                cancellationToken));

            _logger.LogInformation("Patches created for submodule {submodule} of {repo}", change.Name, mapping.Name);
        }

        return patches;
    }

    /// <summary>
    /// Applies a given patch file onto given mapping's subrepository.
    /// </summary>
    public async Task ApplyPatch(
        VmrIngestionPatch patch,
        NativePath targetDirectory,
        bool removePatchAfter,
        bool reverseApply = false,
        CancellationToken cancellationToken = default)
    {
        var info = _fileSystem.GetFileInfo(patch.Path);
        if (!info.Exists)
        {
            _logger.LogError("Failed to find a patch which was expected at {path}", patch.Path);
            return;
        }

        if (info.Length == 0)
        {
            _logger.LogDebug("No changes in {patch}", patch.Path);
            return;
        }

        _logger.LogInformation((reverseApply ? "Reverse-applying" : "Applying") + " patch {patchPath} to {path}...", patch.Path, patch.ApplicationPath ?? "root of the VMR");

        // This will help ignore some CR/LF issues (e.g. files with both endings)
        (await _processManager.ExecuteGit(targetDirectory, ["config", "apply.ignoreWhitespace", "change"], cancellationToken: cancellationToken))
            .ThrowIfFailed("Failed to set git config whitespace settings");

        var args = new List<string>
        {
            "apply",

            // Apply diff to index right away, not the working tree
            // This is faster when we don't care about the working tree
            // Additionally works around the fact that "git apply" failes with "already exists in working directory"
            // This happens only when case sensitive renames happened in the history
            // More details: https://lore.kernel.org/git/YqEiPf%2FJR%2FMEc3C%2F@camp.crustytoothpaste.net/t/
            "--cached",

            // Options to help with CR/LF and similar problems
            "--ignore-space-change",
        };

        if (reverseApply)
        {
            args.Add("--reverse");
        }

        // Where to apply the patch into (usualy src/[repo name] but can be root for VMR's non-src/ content)
        if (patch.ApplicationPath != null)
        {
            args.Add("--directory");
            args.Add(patch.ApplicationPath);

            if (!_fileSystem.DirectoryExists(targetDirectory / patch.ApplicationPath))
            {
                _fileSystem.CreateDirectory(targetDirectory / patch.ApplicationPath);
            }
        }

        args.Add(patch.Path);

        var result = await _processManager.ExecuteGit(targetDirectory, args, cancellationToken: CancellationToken.None);

        if (!result.Succeeded)
        {
            throw new PatchApplicationFailedException(patch, result, reverseApply);
        }

        _logger.LogDebug("{output}", result.ToString());

        if (removePatchAfter)
        {
            try
            {
                _fileSystem.DeleteFile(patch.Path);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to delete patch {patchPath} after applying it", patch.Path);
            }
        }

        await _localGitClient.ResetWorkingTree(targetDirectory, patch.ApplicationPath);
    }

    /// <summary>
    /// Resolves a list of all files that are part of a given patch diff.
    /// </summary>
    /// <param name="patchPath">Path to the patch file</param>
    /// <returns>List of all files (paths relative to repo's root) that are part of a given patch diff</returns>
    public async Task<IReadOnlyCollection<UnixPath>> GetPatchedFiles(string patchPath, CancellationToken cancellationToken)
    {
        var files = new List<UnixPath>();
        if (_fileSystem.GetFileInfo(patchPath).Length == 0)
        {
            _logger.LogDebug("Patch {patch} is empty. Skipping file enumeration..", patchPath);
            return files;
        }

        var result = await _processManager.ExecuteGit(_vmrInfo.TmpPath, ["apply", "--numstat", patchPath], cancellationToken: cancellationToken);
        result.ThrowIfFailed($"Failed to enumerate files from a patch at `{patchPath}`");

        foreach (var line in result.GetOutputLines())
        {
            var match = GitPatchSummaryLine.Match(line);
            if (match.Success)
            {
                files.Add(new UnixPath(match.Groups["file"].Value));
            }
        }

        return files;
    }

    /// <summary>
    /// Creates patches and if any is > 1GB, splits it into smaller ones.
    /// </summary>
    public async Task<List<VmrIngestionPatch>> CreatePatches(
        string patchName,
        string sha1,
        string sha2,
        UnixPath? path,
        IReadOnlyCollection<string>? filters,
        bool relativePaths,
        NativePath workingDir,
        UnixPath? applicationPath,
        bool ignoreLineEndings = false,
        CancellationToken cancellationToken = default)
    {
        var patch = await CreatePatch(
            patchName,
            sha1,
            sha2,
            path,
            filters,
            relativePaths,
            workingDir,
            applicationPath,
            ignoreLineEndings,
            cancellationToken);

        if (_fileSystem.GetFileInfo(patch.Path).Length < MaxPatchSize)
        {
            return [patch];
        }

        _logger.LogWarning("Patch {name} targeting {path} is too large (>1GB). Repo will be split into smaller patches." +
            "Please note that there might be mismatches in non-global cloaking rules.", patchName, applicationPath);

        _logger.LogDebug("Deleting too large patch {path}", patchName);
        _fileSystem.DeleteFile(patch.Path);

        // If the patch is more than 1GB, new must start over and break it down into smaller patches
        var files = _fileSystem.GetFiles(workingDir);
        var directories = _fileSystem.GetDirectories(workingDir);

        var patches = new List<VmrIngestionPatch>();

        // Add a patch for each directory
        for (var i = 0; i < directories.Length; i++)
        {
            var dirName = directories[i].Substring(workingDir.Length + 1);
            if (Path.GetFileName(dirName) == ".git")
            {
                continue;
            }

            var newPatchname = $"{patchName}.{i + 1}";

            patches.AddRange(await CreatePatches(
                newPatchname,
                sha1,
                sha2,
                UnixPath.CurrentDir,
                filters,
                true,
                workingDir / dirName,
                applicationPath == null ? new UnixPath(dirName) : applicationPath / dirName,
                ignoreLineEndings: false,
                cancellationToken));
        }

        // Add a patch for each file
        for (var i = 0; i < files.Length; i++)
        {
            var fileName = new UnixPath(files[i].Substring(workingDir.Length + 1));
            var newPatchname = $"{patchName}.{i + directories.Length + 1}";

            patch = await CreatePatch(
                newPatchname,
                sha1,
                sha2,
                fileName,
                // Ignore all files except the one we're currently processing
                [.. filters?.Except([GetInclusionRule("**/*")]) ?? []],
                true,
                workingDir,
                applicationPath,
                ignoreLineEndings,
                cancellationToken);

            if (_fileSystem.GetFileInfo(patch.Path).Length > MaxPatchSize)
            {
                throw new Exception($"File {files[i]} is too big (>1GB) to be ingested into VMR via git patches. " +
                    "Please add the file into the VMR manually.");
            }

            patches.Add(patch);
        }

        return patches;
    }

    private async Task<VmrIngestionPatch> CreatePatch(
        string patchName,
        string sha1,
        string sha2,
        UnixPath? path,
        IReadOnlyCollection<string>? filters,
        bool relativePaths,
        NativePath workingDir,
        UnixPath? applicationPath,
        bool ignoreLineEndings,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "diff",
            "--patch",
            "--binary", // Include binary contents as base64
            "--no-color", // Don't colorize the output, it will be stored in a file
            "--output", // Store the diff in a .patch file
            patchName,
        };

        if (ignoreLineEndings)
        {
            args.Add("--ignore-space-at-eol");
            args.Add("--ignore-cr-at-eol");
        }

        if (relativePaths)
        {
            args.Add("--relative");
        }

        args.Add($"{sha1}..{sha2}");
        args.Add("--");

        if (path != null)
        {
            args.Add(path);
        }

        if (filters?.Count > 0)
        {
            args.AddRange(filters);
        }

        var result = await _processManager.Execute(
            _processManager.GitExecutable,
            args,
            workingDir: workingDir,
            cancellationToken: cancellationToken);

        result.ThrowIfFailed($"Failed to create a patch {patchName}");

        return new VmrIngestionPatch(patchName, applicationPath?.Path);
    }

    /// <summary>
    /// Finds all changes that happened for submodules between given commits.
    /// </summary>
    /// <returns>A pair of submodules (state in SHA1, state in SHA2) where additions/removals are marked by EmptyGitObject</returns>
    private static async Task<List<SubmoduleChange>> GetSubmoduleChanges(ILocalGitRepo clone, string sha1, string sha2)
    {
        List<GitSubmoduleInfo> submodulesBefore = await clone.GetGitSubmodulesAsync(sha1);
        List<GitSubmoduleInfo> submodulesAfter = await clone.GetGitSubmodulesAsync(sha2);

        var submodulePaths = submodulesBefore
            .Concat(submodulesAfter)
            .Select(s => s.Path)
            .Distinct();

        var submoduleChanges = new List<SubmoduleChange>();

        // Pair submodule state from sha1 and sha2
        // When submodule is added/removed, signal this with well known zero commit
        foreach (var path in submodulePaths)
        {
            GitSubmoduleInfo? before = submodulesBefore.FirstOrDefault(s => s.Path == path);
            GitSubmoduleInfo? after = submodulesAfter.FirstOrDefault(s => s.Path == path);

            // Submodule was added
            if (before is null && after is not null)
            {
                submoduleChanges.Add(new(after.Name, path, after.Url, Constants.EmptyGitObject, after.Commit));
                continue;
            }

            // Submodule was removed
            if (before is not null && after is null)
            {
                submoduleChanges.Add(new(before.Name, path, before.Url, before.Commit, Constants.EmptyGitObject));
                continue;
            }

            // When submodule points to some new remote, we have to break it down to 2 changes: remove the old, add the new
            // (this is what happened in the remote repo)
            if (before!.Url != after!.Url)
            {
                submoduleChanges.Add(new(before.Name, path, before.Url, before.Commit, Constants.EmptyGitObject));
                submoduleChanges.Add(new(after.Name, path, after.Url, Constants.EmptyGitObject, after.Commit));
                continue;
            }

            // Submodule was not changed or points to a new commit
            // We want to include it both ways though as we need to ignore it when diffing
            submoduleChanges.Add(new(after.Name, path, after.Url, before.Commit, after.Commit));
        }

        return submoduleChanges;
    }

    /// <summary>
    /// Creates and returns path to patch files that inline all submodules recursively.
    /// </summary>
    /// <param name="mapping">Mapping for the current repo/submodule</param>
    /// <param name="destDir">Directory where patches should be placed</param>
    /// <param name="tmpPath">Directory where submodules can be cloned temporarily</param>
    /// <param name="relativePath">Relative path where the currently processed repo/submodule is</param>
    /// <param name="change">Change in the submodule that the patches will be created for</param>
    /// <param name="cancellationToken">Cancellation is safe with regards to git operations</param>
    /// <returns>List of patch files with relatives path respective to the VMR</returns>
    private async Task<List<VmrIngestionPatch>> GetPatchesForSubmoduleChange(
        SourceMapping mapping,
        NativePath destDir,
        NativePath tmpPath,
        UnixPath relativePath,
        SubmoduleChange change,
        CancellationToken cancellationToken)
    {
        var checkoutCommit = change.Before == Constants.EmptyGitObject ? change.After : change.Before;
        var clonePath = await _cloneManager.PrepareCloneAsync(change.Url, checkoutCommit, resetToRemote: false, cancellationToken);   

        // We are only interested in filters specific to submodule's path
        ImmutableArray<string> GetSubmoduleFilters(IReadOnlyCollection<string> filters)
        {
            return filters
                .Where(p => p.StartsWith(change.Path))
                .Select(p => p[change.Path.Length..].TrimStart('/'))
                .ToImmutableArray();
        }

        static string SanitizeName(string mappingName)
        {
            mappingName = mappingName.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)[^1];

            if (mappingName.EndsWith(".git"))
            {
                mappingName = mappingName[..^4];
            }
            
            return mappingName;
        }

        var submoduleMapping = new SourceMapping(
            SanitizeName(change.Name),
            change.Url,
            change.Before,
            GetSubmoduleFilters(mapping.Include),
            GetSubmoduleFilters(mapping.Exclude),
            DisableSynchronization: false);

        var submodulePath = change.Path;
        if (!string.IsNullOrEmpty(relativePath))
        {
            submodulePath = relativePath / submodulePath;
        }

        return await CreatePatchesRecursive(
            submoduleMapping,
            clonePath,
            change.Before,
            change.After,
            destDir,
            tmpPath,
            new UnixPath(submodulePath),
            cancellationToken);
    }

    public static string GetInclusionRule(string path) => $":(glob,attr:!{VmrInfo.IgnoreAttribute}){path}";

    public static string GetExclusionRule(string path) => $":(exclude,glob,attr:!{VmrInfo.KeepAttribute}){path}";

    private record SubmoduleChange(string Name, string Path, string Url, string Before, string After);
}
