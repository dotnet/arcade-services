// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class VmrBinaryFileScanner : VmrScanner
{
    // Git output from the diff --numstat command, when it finds a binary file
    private const string BinaryFileMarker = "-\t-";

    public VmrBinaryFileScanner(
        IVmrDependencyTracker dependencyTracker, 
        IProcessManager processManager,
        IVmrInfo vmrInfo, 
        ILogger<VmrScanner> logger) 
        : base(dependencyTracker, processManager, vmrInfo, logger)
    {
    }

    protected override async Task<IEnumerable<string>> ScanRepository(
        SourceMapping sourceMapping,
        string baselineFilePath, 
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = new List<string>
        {
            "diff",
            Constants.EmptyGitObject,
            "--numstat",
            _vmrInfo.GetRepoSourcesPath(sourceMapping)
        };

        args.AddRange(await GetBaselineFilesAsync(sourceMapping.Name, baselineFilePath));

        var ret = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args.ToArray(), cancellationToken);

        ret.ThrowIfFailed($"Failed to scan the {sourceMapping.Name} repository");

        return ret.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(BinaryFileMarker))
            .Select(line => line.Split('\t').Last());
    }

    protected override string ScanType { get; } = "binary";
}
