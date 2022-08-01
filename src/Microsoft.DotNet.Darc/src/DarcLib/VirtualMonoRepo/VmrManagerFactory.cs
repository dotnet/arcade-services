// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrManagerFactory
{
    Task<IVmrManager> CreateVmrManager(string? vmrPath = null, string? tmpPath = null);
}

public class VmrManagerFactory : IVmrManagerFactory
{
    private readonly IProcessManager _processManager;
    private readonly ISourceMappingParser _sourceMappingParser;
    private readonly ILogger _logger;

    public VmrManagerFactory(IProcessManager processManager, ISourceMappingParser sourceMappingParser, ILogger logger)
    {
        _processManager = processManager;
        _sourceMappingParser = sourceMappingParser;
        _logger = logger;
    }

    public async Task<IVmrManager> CreateVmrManager(string? vmrPath = null, string? tmpPath = null)
    {
        vmrPath ??= _processManager.FindGitRoot(Directory.GetCurrentDirectory());

        if (tmpPath is null)
        {
            tmpPath = Path.Combine(_processManager.FindGitRoot(vmrPath), "artifacts", "tmp");
            Directory.CreateDirectory(tmpPath);
        }

        var mappings = await _sourceMappingParser.ParseMappings(vmrPath);

        return new VmrManager(_processManager, _logger, mappings, vmrPath, tmpPath);
    }
}
