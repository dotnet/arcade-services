// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

public abstract class VmrManagerBase : IVmrManager
{
    protected const string HEAD = "HEAD";
    private const string KeepAttribute = "vmr-preserve";
    private const string IgnoreAttribute = "vmr-ignore";

    private readonly IVmrDependencyInfo _dependencyInfo;
    private readonly IProcessManager _processManager;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly ILogger _logger;
    
    private readonly string _tmpPath;

    public IReadOnlyCollection<SourceMapping> Mappings => _dependencyInfo.Mappings;

    protected VmrManagerBase(
        IVmrDependencyInfo dependencyInfo,
        IProcessManager processManager,
        IRemoteFactory remoteFactory,
        IVersionDetailsParser versionDetailsParser,
        ILogger<VmrUpdater> logger,
        string tmpPath)
    {
        _logger = logger;
        _dependencyInfo = dependencyInfo;
        _processManager = processManager;
        _remoteFactory = remoteFactory;
        _versionDetailsParser = versionDetailsParser;
        _tmpPath = tmpPath;
    }

    /// <summary>
    /// Prepares a clone of given git repository either by cloning it to temp or if exists, pulling the newest changes.
    /// </summary>
    /// <param name="mapping">Repository mapping</param>
    /// <returns>Path to the cloned repo</returns>
    protected async Task<string> CloneOrPull(SourceMapping mapping)
    {
        var clonePath = GetClonePath(mapping);
        if (Directory.Exists(clonePath))
        {
            _logger.LogInformation("Clone of {repo} found, pulling new changes...", mapping.DefaultRemote);

            var localRepo = new LocalGitClient(_processManager.GitExecutable, _logger);
            localRepo.Checkout(clonePath, mapping.DefaultRef);

            var result = await _processManager.ExecuteGit(clonePath, "pull");
            result.ThrowIfFailed($"Failed to pull new changes from {mapping.DefaultRemote} into {clonePath}");
            _logger.LogDebug("{output}", result.ToString());

            return Path.Combine(clonePath, ".git");
        }

        var remoteRepo = await _remoteFactory.GetRemoteAsync(mapping.DefaultRemote, _logger);
        remoteRepo.Clone(mapping.DefaultRemote, mapping.DefaultRef, clonePath, checkoutSubmodules: false, null);

        return clonePath;
    }

    /// <summary>
    /// Creates a patch file (a diff) for given two commits in a repo adhering to the in/exclusion filters of the mapping.
    /// </summary>
    protected async Task CreatePatch(
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

        args.AddRange(mapping.Include.Select(p => $":(glob,attr:!{IgnoreAttribute}){p}"));
        args.AddRange(mapping.Exclude.Select(p => $":(exclude,glob,attr:!{KeepAttribute}){p}"));

        // Other git commands are executed from whichever folder and use `-C [path to repo]` 
        // However, here we must execute in repo's dir because attribute filters work against the working tree
        // We also need to do call this from the repo root and not from repo/.git
        var result = await _processManager.Execute(
            _processManager.GitExecutable,
            args,
            workingDir: repoPath.EndsWith(".git") ? Path.GetDirectoryName(repoPath) : repoPath,
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
    protected async Task ApplyPatch(SourceMapping mapping, string patchPath, CancellationToken cancellationToken)
    {
        // We have to give git a relative path with forward slashes where to apply the patch
        var destPath = _dependencyInfo.GetRepoSourcesPath(mapping)
            .Replace(_dependencyInfo.VmrPath, null)
            .Replace("\\", "/")
            [1..];

        _logger.LogInformation("Applying patch {patchPath} to {path}...", patchPath, destPath);

        // This will help ignore some CR/LF issues (e.g. files with both endings)
        (await _processManager.ExecuteGit(_dependencyInfo.VmrPath, new[] { "config", "apply.ignoreWhitespace", "change" }, cancellationToken: cancellationToken))
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

        var result = await _processManager.ExecuteGit(_dependencyInfo.VmrPath, args, cancellationToken: CancellationToken.None);
        result.ThrowIfFailed($"Failed to apply the patch for {destPath}");
        _logger.LogDebug("{output}", result.ToString());

        // After we apply the diff to the index, working tree won't have the files so they will be missing
        // We have to reset working tree to the index then
        // This will end up having the working tree all staged
        _logger.LogInformation("Resetting the working tree...");
        args = new[] { "checkout", destPath };
        result = await _processManager.ExecuteGit(_dependencyInfo.VmrPath, args, cancellationToken: CancellationToken.None);
        result.ThrowIfFailed($"Failed to clean the working tree");
        _logger.LogDebug("{output}", result.ToString());
    }

    /// <summary>
    /// Applies VMR patches onto files of given mapping's subrepository.
    /// </summary>
    /// <param name="mapping">Mapping</param>
    protected async Task ApplyVmrPatches(SourceMapping mapping, CancellationToken cancellationToken)
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
    /// Gets information about submodules from individual repos and compiles a .gitmodules file for the VMR.
    /// We also need to replace the submodule paths with the src/[repo] prefixes.
    /// The .gitmodules file is only relevant in the root of the repo. We can leave the old files behind.
    /// The information about the commit the submodule is referencing is stored in the git tree.
    /// </summary>
    protected async Task UpdateGitmodules(CancellationToken cancellationToken)
    {
        const string gitmodulesFileName = ".gitmodules";

        _logger.LogInformation("Updating .gitmodules file..");

        // Matches the 'path = ' setting from the .gitmodules file so that we can prefix it
        var pathSettingRegex = new Regex(@"(\bpath[ \t]*\=[ \t]*\b)");

        using (var vmrGitmodule = File.Open(Path.Combine(_dependencyInfo.VmrPath, gitmodulesFileName), FileMode.Create))
        using (var writer = new StreamWriter(vmrGitmodule) { NewLine = "\n" })
        {
            foreach (var mapping in Mappings)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var repoGitmodulePath = Path.Combine(_dependencyInfo.GetRepoSourcesPath(mapping), gitmodulesFileName);
                if (!File.Exists(repoGitmodulePath))
                {
                    continue;
                }

                _logger.LogDebug("Copying .gitmodules from {repo}..", mapping.Name);

                // Header for the repo
                await writer.WriteAsync("# ");
                await writer.WriteLineAsync(mapping.Name);
                await writer.WriteLineAsync();

                // Copy contents
                var content = await File.ReadAllTextAsync(repoGitmodulePath, cancellationToken);

                // Add src/[repo]/ prefixes to paths
                content = pathSettingRegex
                    .Replace(content, $"$1{VmrDependencyInfo.VmrSourcesDir}/{mapping.Name}/")
                    .Replace("\r\n", "\n");

                await writer.WriteAsync(content);

                // Add some spacing
                await writer.WriteLineAsync();
                await writer.WriteLineAsync();
            }
        }

        (await _processManager.ExecuteGit(_dependencyInfo.VmrPath, new[] { "add", gitmodulesFileName }, cancellationToken))
            .ThrowIfFailed("Failed to stage the .gitmodules file!");
    }

    protected void Commit(string commitMessage, Signature author)
    {
        _logger.LogInformation("Committing..");

        var watch = Stopwatch.StartNew();
        using var repository = new Repository(_dependencyInfo.VmrPath);
        var commit = repository.Commit(commitMessage, author, DotnetBotCommitSignature);

        _logger.LogInformation("Created {sha} in {duration} seconds", ShortenId(commit.Id.Sha), (int) watch.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// Parses Version.Details.xml of a given mapping and returns the list of source build dependencies (+ their mapping).
    /// </summary>
    protected async Task<IList<(DependencyDetail dependency, SourceMapping mapping)>> GetDependencies(
        SourceMapping mapping,
        CancellationToken cancellationToken)
    {
        var versionDetailsPath = Path.Combine(
            _dependencyInfo.GetRepoSourcesPath(mapping),
            VersionFiles.VersionDetailsXml.Replace('/', Path.DirectorySeparatorChar));

        var versionDetailsContent = await File.ReadAllTextAsync(versionDetailsPath, cancellationToken);

        var dependencies = _versionDetailsParser.ParseVersionDetailsXml(versionDetailsContent, true)
            .Where(dep => dep.SourceBuild is not null);

        var result = new List<(DependencyDetail, SourceMapping)>();

        foreach (var dependency in dependencies)
        {
            var dependencyMapping = Mappings.FirstOrDefault(m => m.Name == dependency.SourceBuild.RepoName);

            if (dependencyMapping is null)
            {
                throw new InvalidOperationException(
                    $"No source mapping named '{dependency.SourceBuild.RepoName}' found " +
                    $"for a {VersionFiles.VersionDetailsXml} dependency {dependency.Name}");
            }

            result.Add((dependency, dependencyMapping));
        }

        return result;
    }

    protected string GetPatchFilePath(SourceMapping mapping) => Path.Combine(_tmpPath, $"{mapping.Name}.patch");

    protected string GetClonePath(SourceMapping mapping) => Path.Combine(_tmpPath, mapping.Name);

    /// <summary>
    /// Takes a given commit message template and populates it with given values, URLs and others.
    /// </summary>
    /// <param name="template">Template into which the values are filled into</param>
    /// <param name="mapping">Repository mapping</param>
    /// <param name="oldSha">SHA we are updating from</param>
    /// <param name="newSha">SHA we are updating to</param>
    /// <param name="additionalMessage">Additional message inserted in the commit body</param>
    protected static string PrepareCommitMessage(
        string template,
        SourceMapping mapping,
        string? oldSha = null,
        string? newSha = null,
        string? additionalMessage = null)
    {
        var replaces = new Dictionary<string, string?>
        {
            { "name", mapping.Name },
            { "remote", mapping.DefaultRemote },
            { "oldSha", oldSha },
            { "newSha", newSha },
            { "oldShaShort", oldSha is null ? string.Empty : ShortenId(oldSha) },
            { "newShaShort", newSha is null ? string.Empty : ShortenId(newSha) },
            { "commitMessage", additionalMessage ?? string.Empty },
        };

        foreach (var replace in replaces)
        {
            template = template.Replace($"{{{replace.Key}}}", replace.Value);
        }

        return template;
    }

    protected static LibGit2Sharp.Commit GetCommit(Repository repository, string? sha)
    {
        var commit = sha is null
            ? repository.Commits.FirstOrDefault()
            : repository.Lookup<LibGit2Sharp.Commit>(sha);

        return commit ?? throw new InvalidOperationException($"Failed to find commit {sha} in {repository.Info.Path}");
    }

    protected static Signature DotnetBotCommitSignature => new(Constants.DarcBotName, Constants.DarcBotEmail, DateTimeOffset.Now);

    protected static string ShortenId(string commitSha) => commitSha[..7];
}
