// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Conflicts;

public interface IBackFlowConflictResolver
{
    Task<bool> TryMergingRepoBranch(
        ILocalGitRepo repo,
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
    private readonly IVmrInfo _vmrInfo;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ForwardFlowConflictResolver> _logger;

    protected override string[] AllowedConflicts =>
    [
        VersionFiles.VersionDetailsXml,
        VersionFiles.VersionProps,
    ];

    public BackFlowConflictResolver(IVmrInfo vmrInfo, IFileSystem fileSystem, ILogger<ForwardFlowConflictResolver> logger)
        : base(logger)
    {
        _vmrInfo = vmrInfo;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<bool> TryMergingRepoBranch(
        ILocalGitRepo repo,
        string targetBranch,
        string branchToMerge)
    {
        return await TryMergingBranch(repo, targetBranch, branchToMerge);
    }

    /// <summary>
    /// Resolves the conflicts by using changes from both files and prefering the changes from the PR branch during conflicts.
    /// This needs to merge the file using --ours strategy (different than just checking out the ours version).
    /// </summary>
    protected override async Task<bool> TryResolvingConflict(ILocalGitRepo repo, string filePath)
    {
        MergeFileVersion[] versions =
        [
            // The order matters during the merge-file command
            new("ours", '2'),
            new("base", '1'),
            new("theirs", '3'),
        ];

        foreach (var version in versions)
        {
            var result = await repo.RunGitCommandAsync(["rev-parse", $":{version.RefIndex}:{filePath}"]);
            result.ThrowIfFailed($"Failed to get the {version.Name} version of the conflicted file {filePath}");
            version.ObjectId = result.StandardOutput.Trim();
        }

        var mergeResult = await repo.RunGitCommandAsync([
            "merge-file",
            "--ours",
            "--object-id",
            ..versions.Select(v => v.ObjectId!),
            "-p"]);
        mergeResult.ThrowIfFailed("Failed to merge the file");

        _fileSystem.WriteToFile(repo.Path / filePath, mergeResult.StandardOutput);
        await repo.StageAsync([filePath]);

        _logger.LogDebug("Auto-resolved conflicts in {file}", filePath);
        return true;
    }
}

file class MergeFileVersion(string name, char refIndex)
{
    public string Name { get; } = name;
    public char RefIndex { get; } = refIndex;
    public string? ObjectId { get; set; }
}
