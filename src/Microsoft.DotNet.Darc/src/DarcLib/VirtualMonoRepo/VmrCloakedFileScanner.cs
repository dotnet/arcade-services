// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class VmrCloakedFileScanner : VmrScanner
{
    public VmrCloakedFileScanner(
        IVmrDependencyTracker dependencyTracker,
        IProcessManager processManager,
        IVmrInfo vmrInfo,
        ILogger<VmrScanner> logger)
        : base(dependencyTracker, processManager, vmrInfo, logger)
    {
    }

    protected override async Task<IEnumerable<string>> ScanSubRepository(
        SourceMapping sourceMapping, 
        string baselineFilePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var args = new List<string>
        {
            "diff",
            "--name-only",
            Constants.EmptyGitObject
        };

        var baseExcludePath = _vmrInfo.GetRepoSourcesPath(sourceMapping);

        foreach (var exclude in sourceMapping.Exclude)
        {
            args.Add(GetCloakedFileFilter(baseExcludePath / exclude));
        }

        args.AddRange(await GetExclusionFilters(sourceMapping.Name, baselineFilePath));

        return await ScanAndParseResult(args.ToArray(), sourceMapping.Name, cancellationToken);

    }

    protected override string ScanType { get; } = "cloaked";
    private string GetCloakedFileFilter(string file) => $":(attr:!{VmrInfo.KeepAttribute}){file}";

    protected override async Task<IEnumerable<string>> ScanBaseRepository(string baselineFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var args = new List<string>
        {
            "diff",
            "--name-only",
            Constants.EmptyGitObject,
            $":(exclude){VmrInfo.SourcesDir}"
        };

        foreach (var exclude in GetGlobalExcludeFilters())
        {
            args.Add(GetCloakedFileFilter(_vmrInfo.VmrPath / exclude));
        }

        args.AddRange(await GetExclusionFilters(null, baselineFilePath));

        return await ScanAndParseResult(args.ToArray(), "base VMR", cancellationToken);
    }

    private async Task<IEnumerable<string>> ScanAndParseResult(string[] args, string repoName, CancellationToken cancellationToken)
    {
        var ret = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args.ToArray(), cancellationToken);

        ret.ThrowIfFailed($"Failed to scan the {repoName} repository");

        return ret.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private IEnumerable<string> GetGlobalExcludeFilters()
    {
        IEnumerable<string> intersection = _dependencyTracker.Mappings.First().Exclude;

        foreach (var mapping in _dependencyTracker.Mappings.Skip(1))
        {
            intersection = intersection.Intersect(mapping.Exclude);
        }

        return intersection;
    }
}
