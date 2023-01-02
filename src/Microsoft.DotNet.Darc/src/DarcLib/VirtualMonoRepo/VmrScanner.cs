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
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

// This class is able to scan the VMR for cloacked files that shouldn't be inside of it
public class VmrScanner : IVmrScanner
{
    private const string _zeroCommitTag = "zeroCommit";

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
        var preservedFiles = GetVmrPreservedFiles(sourceMapping);

        foreach (var exclude in sourceMapping.Exclude)
        {
            args.Add(baseExcludePath / exclude);
        }

        var ret = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args.ToArray());

        ret.ThrowIfFailed($"Failed to scan the {sourceMapping.Name} repository");
        var files = ret.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(file => _vmrInfo.VmrPath / file);

        foreach (var file in files)
        {
            if (preservedFiles.Contains(file))
            {
                continue;
            }
            _logger.LogWarning($"File {file} is cloaked but present in the VMR\", file", file.ToString());
        }
    }

    private HashSet<NativePath> GetVmrPreservedFiles(SourceMapping sourceMapping)
    {
        var files = Directory.GetFiles(_vmrInfo.GetRepoSourcesPath(sourceMapping), ".gitattributes", SearchOption.AllDirectories);

        return files.Select(file =>
            (fileName: file, Attributes: File.ReadAllLines(file)
                        .Where(line => line.Contains("vmr-preserve"))
                        .Select(line => line.Split(" ").First())
                        .ToList()))
                .Where(entry => entry.Attributes.Count > 0)
                .SelectMany(entry => entry.Attributes
                    .Select(attribute => new NativePath(Path.Join(Path.GetDirectoryName(entry.fileName), attribute))))
                .ToHashSet();
    }
}

