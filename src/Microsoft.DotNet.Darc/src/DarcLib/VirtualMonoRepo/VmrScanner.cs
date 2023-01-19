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
    protected readonly IVmrDependencyTracker _dependencyTracker;
    protected readonly IProcessManager _processManager;
    protected readonly IVmrInfo _vmrInfo;
    protected readonly ILogger<VmrScanner> _logger;
    protected readonly IFileSystem _fileSystem;

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
        _fileSystem = new FileSystem();
    }

    public async Task<List<string>> ScanVmr(string baselineFilePath, CancellationToken cancellationToken)
    {
        await _dependencyTracker.InitializeSourceMappings();

        var taskList = new List<Task<IEnumerable<string>>>();

        _logger.LogInformation("Scanning VMR repositories for {type} files", ScanType);

        foreach (var sourceMapping in _dependencyTracker.Mappings)
        {
            taskList.Add(ScanRepository(sourceMapping, baselineFilePath, cancellationToken));
        }

        await Task.WhenAll(taskList);

        var files = taskList
            .SelectMany(task => task.Result)
            .OrderBy(file => file)
            .ToList();

        _logger.LogInformation("The scanner found {number} {type} files", files.Count, ScanType);

        return files;
    }

    protected abstract string ScanType { get; }
    protected abstract Task<IEnumerable<string>> ScanRepository(
        SourceMapping sourceMapping,
        string baselineFilesPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns a list of files that will be exluded from the scan operation, loaded from the baselineFilesPath
    /// </summary>
    protected async Task<IEnumerable<string>> GetExclusionFilters(string repoName, string baselineFilePath)
    {
        var text = await _fileSystem.ReadAllTextAsync(baselineFilePath);
        return text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith($"src/{repoName}") || line.StartsWith('*'))
            .Select(line =>
            {
                // Ignore comments
                var index = line.IndexOf('#');
                return index >= 0 ? line.Substring(0, index).TrimEnd() : line;
            })
            .Select(filePath => $":(exclude){filePath}");
    }
}

