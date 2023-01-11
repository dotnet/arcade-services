// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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

    protected override async Task<IEnumerable<string>> ScanRepository(SourceMapping sourceMapping, CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "diff",
            "--name-only",
            Constants.EmptyGitObject
        };

        var baseExcludePath = _vmrInfo.GetRepoSourcesPath(sourceMapping);

        foreach (var exclude in sourceMapping.Exclude)
        {
            args.Add(GetExclusionFilter(baseExcludePath / exclude));
        }

        var ret = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args.ToArray(), cancellationToken);

        ret.ThrowIfFailed($"Failed to scan the {sourceMapping.Name} repository");

        return ret.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    protected override string ScanType() => "cloaked";
    private string GetExclusionFilter(string file) => $":(attr:!{GetExclusionAttribute()}){file}";
    private string GetExclusionAttribute() => VmrInfo.KeepAttribute;

}
