// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class GenerateTpnOperation : Operation
{
    private readonly GenerateTpnCommandLineOptions _options;

    public GenerateTpnOperation(GenerateTpnCommandLineOptions options)
        : base(options, options.RegisterServices())
    {
        _options = options;
    }

    public override async Task<int> ExecuteAsync()
    {
        var generator = Provider.GetRequiredService<IThirdPartyNoticesGenerator>();
        var dependencyTracker = Provider.GetRequiredService<IVmrDependencyTracker>();
        await dependencyTracker.InitializeSourceMappings();

        await generator.UpdateThirdPartyNotices(_options.TpnTemplate);
        return 0;
    }
}
