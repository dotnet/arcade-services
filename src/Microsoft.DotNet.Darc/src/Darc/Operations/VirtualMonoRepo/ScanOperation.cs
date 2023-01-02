// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ScanOperation : Operation
{
    public ScanOperation(ScanCommandLineOptions options) : base(options, options.RegisterServices())
    {
    }

    public override async Task<int> ExecuteAsync()
    {
        var vmrScanner = Provider.GetRequiredService<IVmrScanner>();
        await vmrScanner.ScanVmr();
        return 0;
    }
}
