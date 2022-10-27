// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo.Licenses;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class GenerateTpnOperation : Operation
{
    public GenerateTpnOperation(GenerateTpnCommandLineOptions options)
        : base(options, options.RegisterServices())
    {
    }

    public override async Task<int> ExecuteAsync()
    {
        var generator = Provider.GetRequiredService<IThirdPartyNoticesGenerator>();
        await generator.UpdateThirtPartyNotices();
        return 0;
    }
}
