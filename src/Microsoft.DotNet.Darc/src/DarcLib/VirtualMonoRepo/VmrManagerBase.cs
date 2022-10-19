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
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public abstract class VmrManagerBase : IVmrManager
{
    // String used to mark the commit as automated
    protected const string AUTOMATION_COMMIT_TAG = "[[ commit created by automation ]]";
    protected const string HEAD = "HEAD";

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyInfo;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly ILogger _logger;

    public IReadOnlyCollection<SourceMapping> Mappings => _dependencyInfo.Mappings;

    protected VmrManagerBase(
        IVmrInfo vmrInfo,
        IVmrDependencyTracker dependencyInfo,
        IVersionDetailsParser versionDetailsParser,
        ILogger<VmrUpdater> logger)
    {
        _logger = logger;
        _vmrInfo = vmrInfo;
        _dependencyInfo = dependencyInfo;
        _versionDetailsParser = versionDetailsParser;
    }

    protected void Commit(string commitMessage, Signature author)
    {
        _logger.LogInformation("Committing..");

        var watch = Stopwatch.StartNew();
        using var repository = new Repository(_vmrInfo.VmrPath);
        var commit = repository.Commit(commitMessage, author, DotnetBotCommitSignature);

        _logger.LogInformation("Created {sha} in {duration} seconds", DarcLib.Commit.GetShortSha(commit.Id.Sha), (int) watch.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// Parses Version.Details.xml of a given mapping and returns the list of source build dependencies (+ their mapping).
    /// </summary>
    protected async Task<IList<(DependencyDetail dependency, SourceMapping mapping)>> GetDependencies(
        SourceMapping mapping,
        CancellationToken cancellationToken)
    {
        var versionDetailsPath = Path.Combine(
            _vmrInfo.GetRepoSourcesPath(mapping),
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

    protected string GetClonePath(SourceMapping mapping) => Path.Combine(_vmrInfo.TmpPath, mapping.Name);

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
            { "oldShaShort", oldSha is null ? string.Empty : DarcLib.Commit.GetShortSha(oldSha) },
            { "newShaShort", newSha is null ? string.Empty : DarcLib.Commit.GetShortSha(newSha) },
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
}
