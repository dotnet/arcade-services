// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LibGit2Sharp;
using Maestro.Common;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IBackFlowConflictResolver
{
    Task<bool> TryMergingRepoBranch(
        ILocalGitRepo repo,
        Build build,
        string baseBranch,
        string targetBranch);
}

/// <summary>
/// This class is responsible for resolving well-known conflicts that can occur during a backflow operation.
/// The conflicts can happen when backward and forward flow PRs get merged out of order.
/// This can be shown on the following schema (the order of events is numbered):
/// 
///     repo                   VMR
///       O────────────────────►O 
///       │                 2.  │ 
///     1.O────────────────O    │ 
///       │  4.            │    │ 
///       │    O───────────┼────O 3. 
///       │    │           │    │ 
///       │    │           │    │ 
///     6.O◄───┘           └───►O 5.
///       │    7.               │ 
///       │     O───────────────| 
///     8.O◄────┘               │ 
///       │                     │
///
/// The conflict arises in step 8. and is caused by the fact that:
///   - When the backflow PR branch is being opened in 7., the last sync (from the point of view of 5.) is from 1.
///   - This means that the PR branch will be based on 1. (the real PR branch will be a commit on top of 1.)
///   - This means that when 6. merged, Version.Details.xml got updated with the SHA of the 3.
///   - So the Source tag in Version.Details.xml in 6. contains the SHA of 3.
///   - The backflow PR branch contains the SHA of 5.
///   - So the Version.Details.xml file conflicts on the SHA (3. vs 5.)
///   - There's also a similar conflict in the package versions that got updated in those commits.
///   - However, if only the version files are in conflict, we can try merging 6. into 7. and resolve the conflict.
///   - This is because basically we know we want to set the version files to point at 5.
/// </summary>
public class BackFlowConflictResolver : CodeFlowConflictResolver, IBackFlowConflictResolver
{
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IGitRepoFactory _gitClientFactory;
    private readonly IBasicBarClient _barClient;
    private readonly IRemoteTokenProvider _tokenProvider;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ForwardFlowConflictResolver> _logger;

    protected override string[] AllowedConflicts =>
    [
        VersionFiles.VersionDetailsXml,
        VersionFiles.VersionProps,
    ];

    public BackFlowConflictResolver(
        IVersionDetailsParser versionDetailsParser,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        IRemoteFactory remoteFactory,
        IGitRepoFactory gitClientFactory,
        IBasicBarClient barClient,
        IRemoteTokenProvider tokenProvider,
        IFileSystem fileSystem,
        ILogger<ForwardFlowConflictResolver> logger)
        : base(logger)
    {
        _versionDetailsParser = versionDetailsParser;
        _coherencyUpdateResolver = coherencyUpdateResolver;
        _remoteFactory = remoteFactory;
        _gitClientFactory = gitClientFactory;
        _barClient = barClient;
        _tokenProvider = tokenProvider;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<bool> TryMergingRepoBranch(
        ILocalGitRepo repo,
        Build build,
        string targetBranch,
        string branchToMerge)
    {
        return await TryMergingBranch(repo, build, targetBranch, branchToMerge);
    }

    protected override async Task<bool> TryResolveConflicts(ILocalGitRepo repo, Build build, IEnumerable<UnixPath> conflictedFiles)
    {
        var result = await repo.RunGitCommandAsync(["checkout", "--theirs", VersionFiles.VersionDetailsXml]);
        result.ThrowIfFailed($"Failed to check out the conflicted file's content: {VersionFiles.VersionDetailsXml}");

        var local = new Local(_tokenProvider, _logger);
        IEnumerable<AssetData> assetData = build.Assets.Select(
            a => new AssetData(a.NonShipping)
            {
                Name = a.Name,
                Version = a.Version
            });

        var dependencies = _versionDetailsParser.ParseVersionDetailsFile(repo.Path / VersionFiles.VersionDetailsXml);
        var updates = _coherencyUpdateResolver.GetRequiredNonCoherencyUpdates(
            build.GetRepository(),
            build.Commit,
            assetData,
            dependencies.Dependencies);

        await local.UpdateDependenciesAsync(
            [.. updates.Select(u => u.To)],
            _remoteFactory,
            _gitClientFactory,
            _barClient);

        _logger.LogInformation("Auto-resolved conflicts in version files");
        return true;
    }

    protected override Task<bool> TryResolvingConflict(ILocalGitRepo repo, Build build, string filePath)
        => throw new NotImplementedException();
}

file class MergeFileVersion(string name, char refIndex)
{
    public string Name { get; } = name;
    public char RefIndex { get; } = refIndex;
    public string? ObjectId { get; set; }
}
