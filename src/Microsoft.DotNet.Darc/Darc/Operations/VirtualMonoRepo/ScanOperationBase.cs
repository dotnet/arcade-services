// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class ScanOperationBase<T> : Operation where T : IVmrScanner
{
    private readonly VmrScanOptions _options;

    public ScanOperationBase(VmrScanOptions options) : base(options, options.RegisterServices())
    {
        _options = options;
    }

    public override async Task<int> ExecuteAsync()
    {
        var vmrScanner = Provider.GetRequiredService<T>();
        using var listener = CancellationKeyListener.ListenForCancellation(Logger);

        var files = await vmrScanner.ScanVmr(_options.BaselineFilePath, listener.Token);

        foreach (var file in files)
        {
            Console.WriteLine(file);
        }

        return files.Any() ? 1 : Constants.SuccessCode;
    }
}
