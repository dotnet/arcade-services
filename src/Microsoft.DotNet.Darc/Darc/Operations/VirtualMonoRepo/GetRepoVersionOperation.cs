// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class GetRepoVersionOperation : Operation
{
    private readonly GetRepoVersionCommandLineOptions _options;

    public GetRepoVersionOperation(GetRepoVersionCommandLineOptions options)
        : base(options, options.RegisterServices())
    {
        _options = options;
    }

    public async override Task<int> ExecuteAsync()
    {
        var repositories = _options.Repositories.ToList();

        // If there are no repositories, list all.
        if (!repositories.Any())
        {
            var dependencyTracker = Provider.GetRequiredService<IVmrDependencyTracker>();
            await dependencyTracker.InitializeSourceMappings();

            repositories = dependencyTracker.Mappings.Select(m => m.Name).ToList();
        }

        if (!repositories.Any())
        {
            Logger.LogError("No repositories found in the VMR.");
            return Constants.ErrorCode;
        }

        var vmrManager = Provider.GetRequiredService<IVmrRepoVersionResolver>();

        var maxRepoNameLength = repositories.Max(r => r.Length);
        foreach (var repo in repositories)
        {
            var paddedRepoName = repo.PadRight(maxRepoNameLength);
            Console.WriteLine($"{paddedRepoName} {await vmrManager.GetVersion(repo)}");
        }

        return 0;
    }
}
