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

    private readonly IVmrDependencyTracker _dependencyInfo;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IProcessManager _processManager;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly ILogger _logger;
    
    private readonly string _tmpPath;

    public IReadOnlyCollection<SourceMapping> Mappings => _dependencyInfo.Mappings;

    protected VmrManagerBase(
        IVmrDependencyTracker dependencyInfo,
        IVmrPatchHandler patchHandler,
        IProcessManager processManager,
        IRemoteFactory remoteFactory,
        IVersionDetailsParser versionDetailsParser,
        ILogger<VmrUpdater> logger,
        string tmpPath)
    {
        _logger = logger;
        _dependencyInfo = dependencyInfo;
        _patchHandler = patchHandler;
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
                    .Replace(content, $"$1{VmrDependencyTracker.VmrSourcesDir}/{mapping.Name}/")
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
