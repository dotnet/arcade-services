// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class GetRepoVersionOperation : Operation
{
    private readonly GetRepoVersionCommandLineOptions _options;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IVmrRepoVersionResolver _vmrManager;
    private readonly ILogger<GetRepoVersionOperation> _logger;

    public GetRepoVersionOperation(
        GetRepoVersionCommandLineOptions options,
        IVmrDependencyTracker dependencyTracker,
        IVmrRepoVersionResolver vmrManager,
        ILogger<GetRepoVersionOperation> logger)
    {
        _options = options;
        _dependencyTracker = dependencyTracker;
        _vmrManager = vmrManager;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        var repositories = _options.Repositories.ToList();

        // If there are no repositories, list all.
        if (!repositories.Any())
        {
            await _dependencyTracker.RefreshMetadata();

            repositories = _dependencyTracker.Mappings.Select(m => m.Name).ToList();
        }

        if (!repositories.Any())
        {
            _logger.LogError("No repositories found in the VMR.");
            return Constants.ErrorCode;
        }


        var maxRepoNameLength = repositories.Max(r => r.Length);
        foreach (var repo in repositories)
        {
            var paddedRepoName = repo.PadRight(maxRepoNameLength);
            Console.WriteLine($"{paddedRepoName} {await _vmrManager.GetVersion(repo)}");
        }

        return 0;
    }
}
