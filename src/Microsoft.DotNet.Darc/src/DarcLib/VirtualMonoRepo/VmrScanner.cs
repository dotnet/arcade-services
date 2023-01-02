// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;


namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

// This class is able to scan the VMR for cloacked files that shouldn't be inside of it
public class VmrScanner : IVmrScanner
{
    private const string _zeroCommitTag = "zeroCommit";
    private const string _vmrPreserveAttribute = "vmr-preserve";

    private readonly IReadOnlyCollection<SourceMapping> _sourceMappings;
    private readonly IProcessManager _processManager;
    private readonly IVmrInfo _vmrInfo;
    private readonly ILogger<VmrScanner> _logger;

    public VmrScanner(
        IReadOnlyCollection<SourceMapping> sourceMappings,
        IProcessManager processManager,
        IVmrInfo vmrInfo,
        ILogger<VmrScanner> logger)
    {
        _sourceMappings = sourceMappings;
        _processManager = processManager;
        _vmrInfo = vmrInfo;
        _logger = logger;
    }

    public async Task ScanVmr()
    {
        foreach (var sourceMapping in _sourceMappings)
        {
            await ScanRepository(sourceMapping);
        }
    }

    private async Task ScanRepository(SourceMapping sourceMapping)
    {
        _logger.LogInformation("Scanning {repository} repository", sourceMapping.Name);
        var args = new List<string>
        {
            "diff",
            "--name-only",
            Constants.EmptyGitObject
        };

        var baseExcludePath = _vmrInfo.GetRepoSourcesPath(sourceMapping);

        foreach (var exclude in sourceMapping.Exclude)
        {
            args.Add(ExcludeFile(baseExcludePath / exclude));
        }

        var ret = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args.ToArray());

        ret.ThrowIfFailed($"Failed to scan the {sourceMapping.Name} repository");
        var files = ret.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(file => _vmrInfo.VmrPath / file);

        foreach (var file in files)
        {
            _logger.LogWarning($"File {file} is cloaked but present in the VMR\", file", file.ToString());
        }
    }

    private string ExcludeFile(string file)
    {
        return $":(attr:!{_vmrPreserveAttribute}){file}";
    }
}

