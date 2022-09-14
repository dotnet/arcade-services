// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
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
    private const string KeepAttribute = "vmr-preserve";
    private const string IgnoreAttribute = "vmr-ignore";

    /// <summary>
    /// Matches output of `git apply --numstat` which lists files contained in a patch file.
    /// Example output:
    /// 0       14      /s/vmr/src/roslyn-analyzers/eng/Versions.props
    /// -       -       /s/vmr/src/roslyn-analyzers/some-binary.dll
    /// </summary>
    private static readonly Regex GitPatchSummaryLine = new(@"^[\-0-9]+\s+[\-0-9]+\s+(?<file>[^\s]+)$", RegexOptions.Compiled);

    private readonly IVmrDependencyTracker _vmrInfo;
    private readonly IProcessManager _processManager;
    private readonly ILogger<VmrPatchHandler> _logger;

    public VmrPatchHandler(
        IVmrDependencyTracker dependencyInfo,
        IProcessManager processManager,
        ILogger<VmrPatchHandler> logger)
    {
        _vmrInfo = dependencyInfo;
        _processManager = processManager;
        _logger = logger;
    }

    /// <summary>
    /// Creates a patch file (a diff) for given two commits in a repo adhering to the in/exclusion filters of the mapping.
    /// </summary>
    public async Task CreatePatch(
        SourceMapping mapping,
        string repoPath,
        string sha1,
        string sha2,
        string destPath,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating diff in {path}..", destPath);

        var args = new List<string>
        {
            "diff",
            "--patch",
            "--binary", // Include binary contents as base64
            "--output", // Store the diff in a .patch file
            destPath,
            $"{sha1}..{sha2}",
            "--",
        };

        if (!mapping.Include.Any())
        {
            mapping = mapping with
            {
                Include = new[] { "**/*" }
            };
        }

        if (repoPath.EndsWith(".git"))
        {
            repoPath = Path.GetDirectoryName(repoPath)!;
        }

        args.AddRange(mapping.Include.Select(p => $":(glob,attr:!{IgnoreAttribute}){p}"));
        args.AddRange(mapping.Exclude.Select(p => $":(exclude,glob,attr:!{KeepAttribute}){p}"));

        // Other git commands are executed from whichever folder and use `-C [path to repo]` 
        // However, here we must execute in repo's dir because attribute filters work against the working tree
        // We also need to do call this from the repo root and not from repo/.git
        var result = await _processManager.Execute(
            _processManager.GitExecutable,
            args,
            workingDir: repoPath,
            cancellationToken: cancellationToken);

        result.ThrowIfFailed($"Failed to create an initial diff for {mapping.Name}");

        _logger.LogDebug("{output}", result.ToString());

        args = new List<string>
        {
            "rev-list",
            "--count",
            $"{sha1}..{sha2}",
        };

        var distance = (await _processManager.ExecuteGit(repoPath, args, cancellationToken)).StandardOutput.Trim();

        _logger.LogInformation("Diff created at {path} - {distance} commit{s}, {size}",
            destPath, distance, distance == "1" ? string.Empty : "s", StringUtils.GetHumanReadableFileSize(destPath));
    }

    /// <summary>
    /// Applies a given patch file onto given mapping's subrepository.
    /// </summary>
    /// <param name="mapping">Mapping</param>
    /// <param name="patchPath">Path to the patch file with the diff</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ApplyPatch(SourceMapping mapping, string patchPath, CancellationToken cancellationToken)
    {
        // We have to give git a relative path with forward slashes where to apply the patch
        var destPath = _vmrInfo.GetRepoSourcesPath(mapping)
            .Replace(_vmrInfo.VmrPath, null)
            .Replace("\\", "/")
            [1..];

        _logger.LogInformation("Applying patch {patchPath} to {path}...", patchPath, destPath);

        // This will help ignore some CR/LF issues (e.g. files with both endings)
        (await _processManager.ExecuteGit(_vmrInfo.VmrPath, new[] { "config", "apply.ignoreWhitespace", "change" }, cancellationToken: cancellationToken))
            .ThrowIfFailed("Failed to set git config whitespace settings");

        Directory.CreateDirectory(destPath);

        IEnumerable<string> args = new[]
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

            // Where to apply the patch into
            "--directory",
            destPath,

            patchPath,
        };

        var result = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args, cancellationToken: CancellationToken.None);
        result.ThrowIfFailed($"Failed to apply the patch for {destPath}");
        _logger.LogDebug("{output}", result.ToString());

        // After we apply the diff to the index, working tree won't have the files so they will be missing
        // We have to reset working tree to the index then
        // This will end up having the working tree all staged
        _logger.LogInformation("Resetting the working tree...");
        args = new[] { "checkout", destPath };
        result = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args, cancellationToken: CancellationToken.None);
        result.ThrowIfFailed($"Failed to clean the working tree");
        _logger.LogDebug("{output}", result.ToString());
    }

    /// <summary>
    /// For all files for which we have patches in VMR, restore their original version from the repo.
    /// This is because VMR contains already patched versions of these files and new updates from the repo wouldn't apply.
    /// </summary>
    /// <param name="mapping">Mapping</param>
    /// <param name="clonePath">Path were the individual repo was cloned to</param>
    /// <param name="originalRevision">Revision from which we were updating</param>
    public async Task RestorePatchedFilesFromRepo(
        SourceMapping mapping,
        string clonePath,
        string originalRevision,
        CancellationToken cancellationToken)
    {
        if (!mapping.VmrPatches.Any())
        {
            return;
        }

        _logger.LogInformation("Restoring files with patches for {mappingName}..", mapping.Name);

        var localRepo = new LocalGitClient(_processManager.GitExecutable, _logger);
        localRepo.Checkout(clonePath, originalRevision);

        var repoSourcesPath = _vmrInfo.GetRepoSourcesPath(mapping);

        foreach (var patch in mapping.VmrPatches)
        {
            _logger.LogDebug("Processing VMR patch `{patch}`..", patch);

            foreach (var patchedFile in await GetFilesInPatch(clonePath, patch, cancellationToken))
            {
                // git always works with forward slashes (even on Windows)
                string relativePath = Path.DirectorySeparatorChar != '/'
                    ? patchedFile.Replace('/', Path.DirectorySeparatorChar)
                    : patchedFile;

                var originalFile = Path.Combine(clonePath, relativePath);
                var destination = Path.Combine(repoSourcesPath, relativePath);

                _logger.LogDebug("Restoring file `{originalFile}` to `{destination}`..", originalFile, destination);

                // Copy old revision to VMR
                File.Copy(originalFile, destination, overwrite: true);
            }
        }

        // Stage the restored files (all future patches are applied to index directly)
        using var repository = new Repository(_vmrInfo.VmrPath);
        Commands.Stage(repository, repoSourcesPath);

        _logger.LogDebug("Files from VMR patches for {mappingName} restored", mapping.Name);
    }

    /// <summary>
    /// Applies VMR patches onto files of given mapping's subrepository.
    /// These files are stored in the VMR and applied on top of the individual repos.
    /// </summary>
    /// <param name="mapping">Mapping</param>
    public async Task ApplyVmrPatches(SourceMapping mapping, CancellationToken cancellationToken)
    {
        if (!mapping.VmrPatches.Any())
        {
            return;
        }

        _logger.LogInformation("Applying VMR patches for {mappingName}..", mapping.Name);

        foreach (var patchFile in mapping.VmrPatches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Applying {patch}..", patchFile);
            await ApplyPatch(mapping, patchFile, cancellationToken);
        }
    }

    /// <summary>
    /// Resolves a list of all files that are part of a given patch diff.
    /// </summary>
    /// <param name="repoPath">Path (to the repo) the patch applies onto</param>
    /// <param name="patchPath">Path to the patch file</param>
    /// <returns>List of all files (paths relative to repo's root) that are part of a given patch diff</returns>
    private async Task<IReadOnlyCollection<string>> GetFilesInPatch(string repoPath, string patchPath, CancellationToken cancellationToken)
    {
        var result = await _processManager.ExecuteGit(repoPath, new[] { "apply", "--numstat", patchPath }, cancellationToken);
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
}
