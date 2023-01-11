// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
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

        var taskList = new List<Task<IEnumerable<string>>>();

        _logger.LogInformation("Scanning VMR repositories for {type} files", ScanType());

        foreach (var sourceMapping in _dependencyTracker.Mappings)
        {
            taskList.Add(ScanRepository(sourceMapping, cancellationToken));
        }

        await Task.WhenAll(taskList);

        var files = taskList.SelectMany(task => task.Result).OrderBy(file => file);

        if (files.Any())
        {
            _logger.LogInformation("The scanner found {number} {type} files:", files.Count(), ScanType());
            foreach (var file in files)
            {
                _logger.LogWarning(file);
            }
        }

        return taskList.SelectMany(task => task.Result).ToList();
    }

    protected abstract string ScanType();
    protected abstract Task<IEnumerable<string>> ScanRepository(SourceMapping sourceMapping, CancellationToken cancellationToken);
}

