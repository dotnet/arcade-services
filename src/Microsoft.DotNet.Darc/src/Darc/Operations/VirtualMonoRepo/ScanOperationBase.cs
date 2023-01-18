// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class ScanOperationBase<T> : Operation where T : IVmrScanner
{
    public ScanOperationBase(CommandLineOptions options, IServiceCollection services = null) : base(options, services)
    {
    }

    public override async Task<int> ExecuteAsync()
    {
        var vmrScanner = Provider.GetRequiredService<T>();
        using var listener = CancellationKeyListener.ListenForCancellation(Logger);

        var files = await vmrScanner.ScanVmr(listener.Token);

        foreach (var file in files)
        {
            Console.WriteLine(file);
        }

        return files.Any() ? 1 : Constants.SuccessCode;
    }
}
