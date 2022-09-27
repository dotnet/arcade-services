// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private readonly IProcessManager _processManager;
    private readonly IRemoteFactory _remoteFactory;
    private readonly ILocalGitRepo _localGitRepo;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly ILogger _logger;
    
    private readonly string _tmpPath;

    public IReadOnlyCollection<SourceMapping> Mappings => _dependencyInfo.Mappings;

    protected VmrManagerBase(
        IVmrDependencyTracker dependencyInfo,
        IProcessManager processManager,
        IRemoteFactory remoteFactory,
        ILocalGitRepo localGitRepo,
        IVersionDetailsParser versionDetailsParser,
        ILogger<VmrUpdater> logger,
        string tmpPath)
    {
        _logger = logger;
        _dependencyInfo = dependencyInfo;
        _processManager = processManager;
        _remoteFactory = remoteFactory;
        _localGitRepo = localGitRepo;
        _versionDetailsParser = versionDetailsParser;
        _tmpPath = tmpPath;
    }

    // TODO (https://github.com/dotnet/arcade/issues/10870): Merge with IRemote.Clone
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

            _localGitRepo.Checkout(clonePath, mapping.DefaultRef);

            var result = await _processManager.ExecuteGit(clonePath, "pull");
            result.ThrowIfFailed($"Failed to pull new changes from {mapping.DefaultRemote} into {clonePath}");
            _logger.LogDebug("{output}", result.ToString());

            return Path.Combine(clonePath, ".git");
        }

        var remoteRepo = await _remoteFactory.GetRemoteAsync(mapping.DefaultRemote, _logger);
        remoteRepo.Clone(mapping.DefaultRemote, mapping.DefaultRef, clonePath, checkoutSubmodules: false, null);

        return clonePath;
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

    public static string ShortenId(string commitSha) => commitSha[..7];
}
