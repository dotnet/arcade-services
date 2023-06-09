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

        if (!repositories.Any())
        {
            Logger.LogError("Please specify at least one repository to synchronize");
            return Constants.ErrorCode;
        }

        var vmrManager = Provider.GetRequiredService<IVmrRepoVersionResolver>();

        foreach (var repo in repositories)
        {
            Console.WriteLine(await vmrManager.GetVersion(repo));
        }

        return 0;
    }
}
