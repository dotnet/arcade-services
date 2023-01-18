// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class ScanOperationBase<T> : Operation where T : IVmrScanner
{
    private readonly VmrScanOptions _options;

    public ScanOperationBase(VmrScanOptions options, IServiceCollection services = null) : base(options, services)
    {
        _options = options;
    }

    public override async Task<int> ExecuteAsync()
    {
        var vmrScanner = Provider.GetRequiredService<T>();
        using var listener = CancellationKeyListener.ListenForCancellation(Logger);

        await vmrScanner.ScanVmr(_options.BaselineFilePath, listener.Token);
        return 0;
    }
}
