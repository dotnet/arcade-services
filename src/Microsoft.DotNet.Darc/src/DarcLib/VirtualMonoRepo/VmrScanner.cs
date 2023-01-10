// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// Class that scans the VMR for cloaked files that shouldn't be inside.
/// </summary>
public abstract class VmrScanner : IVmrScanner
{
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IProcessManager _processManager;
    private readonly IVmrInfo _vmrInfo;
    private readonly ILogger<VmrScanner> _logger;

    // Git output from the diff --numstat command, when it finds a binary file
    private const string _binaryFileMarker = "-\t-";

    public VmrScanner(
        IVmrDependencyTracker dependencyTracker,
        IProcessManager processManager,
        IVmrInfo vmrInfo,
        ILogger<VmrScanner> logger)
    {
        _dependencyTracker = dependencyTracker;
        _processManager = processManager;
        _vmrInfo = vmrInfo;
        _logger = logger;
    }

    public async Task<List<string>> ScanVmr(CancellationToken cancellationToken)
    {
        await _dependencyTracker
            .InitializeSourceMappings(_vmrInfo.VmrPath / VmrInfo.SourcesDir / VmrInfo.SourceMappingsFileName);

        var taskList = new List<Task<string[]>>();

        _logger.LogInformation("Scanning VMR repositories for {type} files", ScanType());

        foreach (var sourceMapping in _dependencyTracker.Mappings)
        {
            taskList.Add(ScanRepository(sourceMapping, cancellationToken));
        }

        await Task.WhenAll(taskList);

        var cloakedFiles = taskList.SelectMany(task => task.Result).OrderBy(file => file);

        if (cloakedFiles.Any())
        {
            _logger.LogInformation("The scanner found {number} {type} files:", cloakedFiles.Count(), ScanType());
            foreach (var file in cloakedFiles)
            {
                _logger.LogWarning("File {file} is {type} but present in the VMR", file, ScanType());
            }
        }

        return taskList.SelectMany(task => task.Result).ToList();
    }

    protected abstract string ScanType();
    protected abstract Task<string[]> ScanRepository(SourceMapping sourceMapping, CancellationToken cancellationToken);

    private async Task<string[]> ScanRepositoryForCloackedFiles(SourceMapping sourceMapping, CancellationToken cancellationToken)
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
            args.Add(ExcludeFile(baseExcludePath / exclude, VmrInfo.KeepAttribute));
        }

        var ret = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args.ToArray(), cancellationToken);

        ret.ThrowIfFailed($"Failed to scan the {sourceMapping.Name} repository");

        return ret.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string ExcludeFile(string file, string gitAttribute) => $":(attr:!{gitAttribute}){file}";

    public async Task<List<string>> ListBinaryFiles(CancellationToken cancellationToken)
    {
        await _dependencyTracker
            .InitializeSourceMappings(_vmrInfo.VmrPath / VmrInfo.SourcesDir / VmrInfo.SourceMappingsFileName);

        var binaryFiles = new List<string>();
        var taskList = new List<Task<IEnumerable<string>>>();

        foreach (var mapping in _dependencyTracker.Mappings)
        {
            taskList.Add(ScanRepositoryForBinaryFiles(mapping, cancellationToken));
        }

        await Task.WhenAll(taskList);

        return taskList.SelectMany(task => task.Result).ToList();
    }

    private async Task<IEnumerable<string>> ScanRepositoryForBinaryFiles(SourceMapping sourceMapping, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Scanning {repository} repository", sourceMapping.Name);
        var args = new string[]{
            "diff",
            Constants.EmptyGitObject,
            "--numstat",
            ExcludeFile(_vmrInfo.GetRepoSourcesPath(sourceMapping), VmrInfo.VmrIgnoreBinaryAttribute)
        };

        var ret = await _processManager.ExecuteGit(_vmrInfo.VmrPath, args.ToArray(), cancellationToken);

        ret.ThrowIfFailed($"Failed to scan the {sourceMapping.Name} repository");

        var files = ret.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(_binaryFileMarker))
            .Select(line => line.Split("\t").Last());


        foreach (var file in files)
        {
            _logger.LogWarning("Found binary file {file} in {repo} repo", file, sourceMapping.Name);
        }

        return files;
    }
}

