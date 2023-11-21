// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class VmrBinaryFileScanner : VmrScanner
{
    // Git output from the diff --numstat command, when it finds a binary file
    private const string BinaryFileMarker = "-\t-";
    private const string Utf16Marker = "UTF-16 Unicode text";

    public VmrBinaryFileScanner(
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
            Constants.EmptyGitObject,
            "--numstat",
            _vmrInfo.GetRepoSourcesPath(sourceMapping)
        };

        if (baselineFilePath != null)
        {
            args.AddRange(await GetExclusionFilters(sourceMapping.Name, baselineFilePath));
        }

        return await ScanAndParseResult(args.ToArray(), sourceMapping.Name, cancellationToken);
    }

    protected override async Task<IEnumerable<string>> ScanBaseRepository(string? baselineFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = new List<string>
        {
            "diff",
            Constants.EmptyGitObject,
            "--numstat",
            _vmrInfo.VmrPath,
            $":(exclude){VmrInfo.SourcesDir}"
        };

        if (baselineFilePath != null)
        {
            args.AddRange(await GetExclusionFilters(null, baselineFilePath));
        }

        return await ScanAndParseResult(args.ToArray(), "base VMR", cancellationToken);
    }

    private async Task<IEnumerable<string>> ScanAndParseResult(string[] args, string repoName, CancellationToken cancellationToken)
    {
        var ret = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args.ToArray(), cancellationToken: cancellationToken);

        ret.ThrowIfFailed($"Failed to scan the {repoName} repository");

        return ret.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(BinaryFileMarker))
            .Select(line => line.Split('\t').Last())
            .ToAsyncEnumerable()
            // Git evaluates UTF-16 text files as binary, we want to exclude these
            .WhereAwait(async file => await IsNotUTF16(_vmrInfo.VmrPath / file, cancellationToken))
            .ToEnumerable();
    }

    private async Task<bool> IsNotUTF16(LocalPath filePath, CancellationToken cancellationToken)
    {
        if (Environment.OSVersion.Platform == PlatformID.Unix)
        {
            var ret = await _processManager.Execute(executable: "file", arguments: new string[] { filePath }, cancellationToken: cancellationToken);

            ret.ThrowIfFailed($"Error executing 'file {filePath}'");

            if (ret.StandardOutput.Contains(Utf16Marker))
            {
                return false;
            }
        }

        return true;
    }

    protected override string ScanType { get; } = "binary";
}
