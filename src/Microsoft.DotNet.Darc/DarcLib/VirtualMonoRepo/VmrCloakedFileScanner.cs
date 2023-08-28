// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
        string? baselineFilePath,
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

        if (baselineFilePath != null)
        {
            args.AddRange(await GetExclusionFilters(sourceMapping.Name, baselineFilePath));
        }

        var ret = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args.ToArray(), cancellationToken: cancellationToken);

        ret.ThrowIfFailed($"Failed to scan the {sourceMapping.Name} repository");

        return ret.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    protected override string ScanType { get; } = "cloaked";
    private string GetCloakedFileFilter(string file) => $":(attr:!{VmrInfo.KeepAttribute}){file}";

    protected override Task<IEnumerable<string>> ScanBaseRepository(string? baselineFilePath, CancellationToken cancellationToken) 
        => Task.FromResult(Enumerable.Empty<string>());
}
