// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
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
    // These git attributes can override cloaking of files when set it individual repositories
    private const string KeepAttribute = "vmr-preserve";
    private const string IgnoreAttribute = "vmr-ignore";

    /// <summary>
    /// Matches output of `git apply --numstat` which lists files contained in a patch file.
    /// Example output:
    /// 0       14      /s/vmr/src/roslyn-analyzers/eng/Versions.props
    /// -       -       /s/vmr/src/roslyn-analyzers/some-binary.dll
    /// </summary>
    private static readonly Regex GitPatchSummaryLine = new(@"^[\-0-9]+\s+[\-0-9]+\s+(?<file>[^\s]+)$", RegexOptions.Compiled);

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitRepo _localGitRepo;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly IProcessManager _processManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrPatchHandler> _logger;

    public VmrPatchHandler(
        IVmrInfo vmrInfo,
        IVmrDependencyTracker dependencyTracker,
        ILocalGitRepo localGitRepo,
        IRepositoryCloneManager cloneManager,
        IProcessManager processManager,
        IFileSystem fileSystem,
        ILogger<VmrPatchHandler> logger)
    {
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _localGitRepo = localGitRepo;
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
    /// <param name="repoPath">Path to the clone of the repo</param>
    /// <param name="sha1">Diff from this commit</param>
    /// <param name="sha2">Diff to this commit</param>
    /// <param name="destDir">Directory where patches should be placed</param>
    /// <param name="tmpPath">Directory where submodules can be cloned temporarily</param>
    /// <param name="cancellationToken">Cancellation is safe with regards to git operations</param>
    /// <returns>List of patch files that can be applied on the VMR</returns>
    public async Task<List<VmrIngestionPatch>> CreatePatches(
        SourceMapping mapping,
        string repoPath,
        string sha1,
        string sha2,
        string destDir,
        string tmpPath,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating patches for {mapping} in {path}..", mapping.Name, destDir);

        var patches = await CreatePatchesRecursive(mapping, repoPath, sha1, sha2, destDir, tmpPath, mapping.Name, cancellationToken);

        _logger.LogInformation("{count} patch{s} created", patches.Count, patches.Count == 1 ? string.Empty : "es");

        return patches;
    }

    private async Task<List<VmrIngestionPatch>> CreatePatchesRecursive(
        SourceMapping mapping,
        string repoPath,
        string sha1,
        string sha2,
        string destDir,
        string tmpPath,
        string relativePath,
        CancellationToken cancellationToken)
    {
        if (repoPath.EndsWith(".git"))
        {
            repoPath = _fileSystem.GetDirectoryName(repoPath)!;
        }

        var patchName = _fileSystem.PathCombine(destDir, $"{mapping.Name}-{Commit.GetShortSha(sha1)}-{Commit.GetShortSha(sha2)}.patch");
        var patches = new List<VmrIngestionPatch>
        {
            new(patchName, VmrInfo.SourcesDir + '/' + relativePath)
        };

        List<SubmoduleChange> submoduleChanges = GetSubmoduleChanges(repoPath, sha1, sha2);

        var changedRecords = submoduleChanges
            .Select(c => new SubmoduleRecord(relativePath + '/' + c.Path, c.Url, c.After))
            .ToList();

        _dependencyTracker.UpdateSubmodules(changedRecords);

        if (!mapping.Include.Any())
        {
            mapping = mapping with
            {
                Include = new[] { "**/*" }
            };
        }

        var args = new List<string>
        {
            "diff",
            "--patch",
            "--binary", // Include binary contents as base64
            "--output", // Store the diff in a .patch file
            patchName,
            $"{sha1}..{sha2}",
            "--",
        };

        args.AddRange(mapping.Include.Select(p => $":(glob,attr:!{IgnoreAttribute}){p}"));
        args.AddRange(mapping.Exclude.Select(p => $":(exclude,glob,attr:!{KeepAttribute}){p}"));

        // Ignore submodules in the diff, they will be inlined via their own diffs
        if (submoduleChanges.Any())
        {
            args.AddRange(submoduleChanges.Select(c => $":(exclude){c.Path}").Distinct());
        }

        // Other git commands are executed from whichever folder and use `-C [path to repo]` 
        // However, here we must execute in repo's dir because attribute filters work against the working tree
        // We also need to do call this from the repo root and not from repo/.git
        var result = await _processManager.ExecuteGit(
            repoPath,
            args,
            cancellationToken: cancellationToken);

        result.ThrowIfFailed($"Failed to create an initial diff for {mapping.Name}");

        _logger.LogDebug("{output}", result.ToString());

        var countArgs = new[]
        {
            "rev-list",
            "--count",
            $"{sha1}..{sha2}",
        };

        var distance = (await _processManager.ExecuteGit(repoPath, countArgs, cancellationToken)).StandardOutput.Trim();

        _logger.LogInformation("Diff created at {path} - {distance} commit{s}, {size}",
            patchName,
            distance == "0" ? "?" : distance,
            distance == "1" ? string.Empty : "s",
            StringUtils.GetHumanReadableFileSize(patchName));

        // If current mapping hosts VMR's non-src/ content, synchronize it too
        // We only do it when processing the root mapping, not its submodules
        var relativeRepoPath = _vmrInfo.GetRelativeRepoSourcesPath(mapping);
        int i = 1;
        foreach (var (source, destination) in _vmrInfo.AdditionalMappings.Where(m => m.Source.StartsWith(relativeRepoPath)))
        {
            var relativeClonePath = source.Substring(relativeRepoPath.Length + 1);

            _logger.LogInformation("Detected 'non-src/' mapped content in {source}. Creating patch..", source);

            patchName = $"{(destination != null ? destination.Replace('/', '_') : "root")}-{Commit.GetShortSha(sha1)}-{Commit.GetShortSha(sha2)}-{i++}.patch";
            patchName = _fileSystem.PathCombine(destDir, patchName);

            args = new List<string>
            {
                "diff",
                "--patch",
                "--relative", // Relative paths so that we can apply the patch on VMR's root dir
                "--binary", // Include binary contents as base64
                "--output", // Store the diff in a .patch file
                patchName,
                $"{sha1}..{sha2}",
                "--",
                ".", // Apply for a given directory (we will call this from the content dir)
            };

            // We take the content path from the VMR config and map it onto the cloned repo
            var contentDir = _fileSystem.PathCombine(repoPath, relativeClonePath.Replace('/', _fileSystem.DirectorySeparatorChar));

            result = await _processManager.Execute(
                _processManager.GitExecutable,
                args,
                workingDir: contentDir,
                cancellationToken: cancellationToken);

            patches.Add(new VmrIngestionPatch(patchName, destination));
        }

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
    public async Task ApplyPatch(SourceMapping mapping, VmrIngestionPatch patch, CancellationToken cancellationToken)
    {
        var info = _fileSystem.GetFileInfo(patch.Path);
        if (!info.Exists)
        {
            _logger.LogError("Failed to find a patch which was expected at {path}", patch.Path);
            return;
        }

        if (info.Length == 0)
        {
            _logger.LogDebug("No changes in {patch} (maybe only excluded files or submodules changed?)", patch.Path);
            return;
        }

        _logger.LogInformation("Applying patch {patchPath} to {path}...", patch.Path, patch.ApplicationPath ?? "root of the VMR");

        // This will help ignore some CR/LF issues (e.g. files with both endings)
        (await _processManager.ExecuteGit(_vmrInfo.VmrPath, new[] { "config", "apply.ignoreWhitespace", "change" }, cancellationToken: cancellationToken))
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

        // Where to apply the patch into (usualy src/[repo name] but can be root for VMR's non-src/ content)
        if (patch.ApplicationPath != null)
        {
            args.Add("--directory");
            args.Add(patch.ApplicationPath);

            if (!_fileSystem.DirectoryExists(patch.ApplicationPath))
            {
                _fileSystem.CreateDirectory(patch.ApplicationPath);
            }
        }

        args.Add(patch.Path);

        var result = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args, cancellationToken: CancellationToken.None);
        result.ThrowIfFailed($"Failed to apply the patch for {patch.ApplicationPath ?? "/"}");
        _logger.LogDebug("{output}", result.ToString());

        await ResetWorkingTreeDirectory(patch.ApplicationPath ?? ".");
    }

    public async Task RestoreFilesFromPatch(
        SourceMapping mapping,
        string clonePath,
        string patch,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Restoring patched files from a VMR patch `{patch}`..", patch);
        
        var repoSourcesPath = _vmrInfo.GetRepoSourcesPath(mapping);

        foreach (var patchedFile in await GetPatchedFiles(clonePath, patch, cancellationToken))
        {
            // git always works with forward slashes (even on Windows)
            string relativePath = _fileSystem.DirectorySeparatorChar != '/'
                ? patchedFile.Replace('/', _fileSystem.DirectorySeparatorChar)
                : patchedFile;

            var originalFile = _fileSystem.PathCombine(clonePath, relativePath);
            var destination = _fileSystem.PathCombine(repoSourcesPath, relativePath);

            if (_fileSystem.FileExists(originalFile))
            {
                // Copy old revision to VMR
                _logger.LogDebug("Restoring file `{destination}` from original at `{originalFile}`..", destination, originalFile);
                _fileSystem.CopyFile(originalFile, destination, overwrite: true);
            }
            else
            {
                // File is being added by the patch - we need to remove it
                _logger.LogDebug("Removing file `{destination}` which is added by a patch..", destination);
                _fileSystem.DeleteFile(destination);
            }
        }

        // Stage the restored files (all future patches are applied to index directly)
        _localGitRepo.Stage(_vmrInfo.VmrPath, _vmrInfo.GetRelativeRepoSourcesPath(mapping));
    }

    /// <summary>
    /// Resolves a list of all files that are part of a given patch diff.
    /// </summary>
    /// <param name="repoPath">Path (to the repo) the patch applies onto</param>
    /// <param name="patchPath">Path to the patch file</param>
    /// <returns>List of all files (paths relative to repo's root) that are part of a given patch diff</returns>
    public async Task<IReadOnlyCollection<string>> GetPatchedFiles(
        string repoPath,
        string patchPath,
        CancellationToken cancellationToken)
    {
        var result = await _processManager.ExecuteGit(repoPath, new[] { "apply", "--numstat", "--allow-empty", patchPath }, cancellationToken);
        result.ThrowIfFailed($"Failed to enumerate files from a patch at `{patchPath}`");

        var files = new List<string>();
        foreach (var line in result.StandardOutput.Split(Environment.NewLine))
        {
            var match = GitPatchSummaryLine.Match(line);
            if (match.Success)
            {
                files.Add(match.Groups["file"].Value);
            }
        }

        return files;
    }

    /// <summary>
    /// Finds all changes that happened for submodules between given commits.
    /// </summary>
    /// <param name="repoPath">Path to the local git repository</param>
    /// <returns>A pair of submodules (state in SHA1, state in SHA2) where additions/removals are marked by EmptyGitObject</returns>
    private List<SubmoduleChange> GetSubmoduleChanges(string repoPath, string sha1, string sha2)
    {
        List<GitSubmoduleInfo> submodulesBefore = _localGitRepo.GetGitSubmodules(repoPath, sha1);
        List<GitSubmoduleInfo> submodulesAfter = _localGitRepo.GetGitSubmodules(repoPath, sha2);

        var submodulePaths = submodulesBefore
            .Concat(submodulesAfter)
            .Select(s => s.Path)
            .Distinct();

        var submoduleChanges = new List<SubmoduleChange>();

        // Pair submodule state from sha1 and sha2
        // When submodule is added/removed, signal this with well known zero commit
        foreach (string path in submodulePaths)
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
        string destDir,
        string tmpPath,
        string relativePath,
        SubmoduleChange change,
        CancellationToken cancellationToken)
    {
        var checkoutCommit = change.Before == Constants.EmptyGitObject ? change.After : change.Before;
        var clonePath = await _cloneManager.PrepareClone(change.Url, checkoutCommit, cancellationToken);   

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
            mappingName = mappingName.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)[^1];

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
            GetSubmoduleFilters(mapping.Exclude));

        var submodulePath = change.Path;
        if (!string.IsNullOrEmpty(relativePath))
        {
            submodulePath = relativePath + '/' + submodulePath;
        }

        return await CreatePatchesRecursive(
            submoduleMapping,
            clonePath,
            change.Before,
            change.After,
            destDir,
            tmpPath,
            submodulePath,
            cancellationToken);
    }

    private async Task ResetWorkingTreeDirectory(string path)
    {
        // After we apply the diff to the index, working tree won't have the files so they will be missing
        // We have to reset working tree to the index then
        // This will end up having the working tree match what is staged
        _logger.LogInformation("Cleaning the working tree directory {path}...", path);
        var args = new[] { "checkout", path };
        var result = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args, cancellationToken: CancellationToken.None);
        
        if (result.Succeeded)
        {
            _logger.LogDebug("{output}", result.ToString());
        }
        else if (result.StandardError.Contains($"pathspec '{path}' did not match any file(s) known to git"))
        {
            // In case a submodule was removed, it won't be in the index anymore and the checkout will fail
            // We can just remove the working tree folder then
            _logger.LogInformation("A removed submodule detected. Removing files at {path}...", path);
            _fileSystem.DeleteDirectory(_fileSystem.PathCombine(_vmrInfo.VmrPath, path), true);
        }

        // Also remove untracked files (in case files were removed in index)
        args = new[] { "clean", "-df", path };
        result = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args, cancellationToken: CancellationToken.None);
        result.ThrowIfFailed("Failed to clean the working tree!");
    }

    public IReadOnlyCollection<string> GetVmrPatches(SourceMapping mapping)
    {
        if (_vmrInfo.PatchesPath is null)
        {
            return Array.Empty<string>();
        }

        var mappingPatchesPath = _fileSystem.PathCombine(
            _vmrInfo.VmrPath,
            _vmrInfo.PatchesPath.Replace('/', _fileSystem.DirectorySeparatorChar),
            mapping.Name);

        if (!_fileSystem.DirectoryExists(mappingPatchesPath))
        {
            return Array.Empty<string>();
        }

        return _fileSystem.GetFiles(mappingPatchesPath);
    }

    private record SubmoduleChange(string Name, string Path, string Url, string Before, string After);
}
