// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrManager
{
    IReadOnlyCollection<SourceMapping> Mappings { get; }

    Task UpdateRepo(
        string name,
        string? targetRevision,
        bool oneByOne,
        bool ignoreWorkingTree,
        CancellationToken cancellationToken);

    Task InitializeRepo(
        string name,
        string? targetRevision,
        bool ignoreWorkingTree,
        CancellationToken cancellationToken);
}

public class VmrManager : IVmrManager
{
    private readonly ILogger _logger;
    private readonly IProcessManager _processManager;
    private readonly string _vmrPath;
    private readonly string _tmpPath;

    public IReadOnlyCollection<SourceMapping> Mappings { get; }

    public VmrManager(IProcessManager processManager, ILogger logger, IReadOnlyCollection<SourceMapping> mappings, string vmrPath, string tmpPath)
    {
        _logger = logger;
        _processManager = processManager;
        _vmrPath = vmrPath;
        _tmpPath = tmpPath;
        Mappings = mappings;
    }

    public Task InitializeRepo(string name, string? targetRevision, bool ignoreWorkingTree, CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();
    }

    public Task UpdateRepo(string name, string? targetRevision, bool oneByOne, bool ignoreWorkingTree, CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();
    }
}
