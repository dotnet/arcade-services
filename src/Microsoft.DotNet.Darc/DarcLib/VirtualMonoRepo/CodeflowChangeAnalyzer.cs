// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface ICodeflowChangeAnalyzer
{
    Task<bool> ForwardFlowHasMeaningfulChangesAsync(string mappingName, string headBranch, string targetBranch);
}

/// <summary>
/// This class can analyze changes during codeflows and tell if there are any meaningful ones that are worth flowing.
/// </summary>
public class CodeflowChangeAnalyzer : ICodeflowChangeAnalyzer
{
    // List of characters that belong to the git diff format.
    // Example diff output:
    // diff --git a/src/product-repo1/eng/Versions.props b/src/product-repo1/eng/Versions.props
    // index fb13f6d..76d73de 100644
    // --- a/src/product-repo1/eng/Versions.props
    // +++ b/src/product-repo1/eng/Versions.props
    // @@ -10,2 +10,2 @@
    // -    <PackageA1PackageVersion>1.0.0</PackageA1PackageVersion>
    // -    <PackageB1PackageVersion>2.0.0</PackageB1PackageVersion>
    // +    <PackageA1PackageVersion>2.0.1</PackageA1PackageVersion>
    // +    <PackageB1PackageVersion>2.0.1</PackageB1PackageVersion>
    private static readonly IReadOnlyCollection<string> IgnoredDiffLines = ["diff --git", "index ", "@@ ", "--- ", "+++ "];

    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IBasicBarClient _barClient;
    private readonly IVmrInfo _vmrInfo;
    private readonly ILogger<CodeflowChangeAnalyzer> _logger;

    public CodeflowChangeAnalyzer(
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IBasicBarClient barClient,
        IVmrInfo vmrInfo,
        ILogger<CodeflowChangeAnalyzer> logger)
    {
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _barClient = barClient;
        _vmrInfo = vmrInfo;
        _logger = logger;
    }

    /// <summary>
    /// Checks whether the flow contains meaningful changes that warrant a PR creation.
    /// If it only contains version file changes that happened during the last backflow, we can skip it.
    /// Example:
    ///   - Changed files are only `source-manifest.json` and version files (Version.Details.xml, Versions.props,
    ///   global.json...)
    ///   - The version file changes are only bumps of dependencies that were backflowed
    /// </summary>
    /// <returns> True, if there are no meaningful changes that warrant a PR creation</returns>
    public async Task<bool> ForwardFlowHasMeaningfulChangesAsync(
        string mappingName,
        string headBranch,
        string targetBranch)
    {
        _logger.LogInformation("Checking if the flow can be skipped for {mappingName}", mappingName);

        ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);

        var commonAncestor = await vmr.GetMergeBaseAsync(headBranch, targetBranch);
        var changedFiles = await vmr.GetChangedFilesAsync(commonAncestor, headBranch);

        if (HasSourceChanges(mappingName, changedFiles))
        {
            return true;
        }

        if (await HasMeaningfulVersioningChanges(mappingName, headBranch, commonAncestor))
        {
            return true;
        }

        _logger.LogInformation("No meaningful changes detected, code flow can be skipped");
        return false;
    }

    private bool HasSourceChanges(string mappingName, IReadOnlyCollection<string> changedFiles)
    {
        var mappingSrc = VmrInfo.GetRelativeRepoSourcesPath(mappingName);

        var versionDetailsPropsFile =
            VmrInfo.GetRelativeRepoSourcesPath(mappingName) / VersionFiles.VersionDetailsProps;

        var repoVersionFilesInVmr = DependencyFileManager.CodeflowDependencyFiles
            .Select(file => new UnixPath(mappingSrc) / file)
            .Append(versionDetailsPropsFile)
            .Select(unixPath => unixPath.ToString())
            .ToHashSet();

        var meaningfulChanges = changedFiles
            .Where(file => file.StartsWith(mappingSrc, StringComparison.OrdinalIgnoreCase))
            .Where(file => !repoVersionFilesInVmr.Any(
                v => v.Equals(file, StringComparison.OrdinalIgnoreCase)));

        if (mappingName != VmrInfo.ArcadeMappingName)
        {
            // for repos that are not arcade, we don't include their eng/common folder
            var mappingEngCommon = mappingSrc / Constants.CommonScriptFilesPath;

            meaningfulChanges = meaningfulChanges
                .Where(file => !file.StartsWith(mappingEngCommon, StringComparison.OrdinalIgnoreCase));
        }

        if (meaningfulChanges.Any())
        {
            _logger.LogInformation("Flow contains source changes that warrant PR creation");
            return true;
        }

        return false;
    }

    private async Task<bool> HasMeaningfulVersioningChanges(
        string mappingName,
        string headBranch,
        string ancestorCommit)
    {
        ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);

        var versionFileInclusionRules = DependencyFileManager.CodeflowDependencyFiles
            .Select(VmrPatchHandler.GetInclusionRule)
            .ToList();

        var result = await vmr.ExecuteGitCommand(
        [
            "diff",
            "-U0",
            $"{ancestorCommit}..{headBranch}",
            "--",
            ..versionFileInclusionRules
        ]);

        result.ThrowIfFailed($"Failed to get the changes between {ancestorCommit} and {headBranch}");

        // We load all different pieces of build information that would be expected in the diff output
        Build? build1 = await GetBuildFromSourceTag(vmr, mappingName, ancestorCommit);
        Build? build2 = await GetBuildFromSourceTag(vmr, mappingName, headBranch);

        List<string> expectedContents =
        [
            ..GetExpectedContentsForBuild(build1),
            ..GetExpectedContentsForBuild(build2),
        ];

        IEnumerable<string> diffLines = result.GetOutputLines();

        if (diffLines.Any(line => ContainsUnexpectedChange(line, expectedContents)))
        {
            _logger.LogInformation("Flow contains version file changes that warrant PR creation");
            return true;
        }

        return false;
    }

    private async Task<Build?> GetBuildFromSourceTag(ILocalGitRepo vmr, string mappingName, string branch)
    {
        string? versionDetailsContent = await vmr.GetFileFromGitAsync(
            VmrInfo.GetRelativeRepoSourcesPath(mappingName) / VersionFiles.VersionDetailsXml,
            branch);

        if (versionDetailsContent == null)
        {
            _logger.LogWarning("Version details of repo {repo} in branch {branch} could not be loaded.",
                mappingName,
                branch);
            return null;
        }

        VersionDetails versionDetails = _versionDetailsParser.ParseVersionDetailsXml(versionDetailsContent);

        return versionDetails.Source?.BarId == null
            ? null
            : await _barClient.GetBuildAsync(versionDetails.Source.BarId.Value);
    }

    private static bool ContainsUnexpectedChange(string line, IEnumerable<string> expectedContents)
    {
        // Characters belonging to the diff command output
        if (IgnoredDiffLines.Any(line.StartsWith))
        {
            return false;
        }

        // Known build data that would appear if only build related changes were made
        if (expectedContents.Any(c => line.Contains(c, StringComparison.InvariantCultureIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<string> GetExpectedContentsForBuild(Build? b)
    {
        if (b == null)
        {
            return [];
        }

        return
        [
            b.Id.ToString(),
            b.Commit,
            b.GetRepository(),
            ..b.Assets.Select(a => a.Version).Distinct(),
        ];
    }
}
