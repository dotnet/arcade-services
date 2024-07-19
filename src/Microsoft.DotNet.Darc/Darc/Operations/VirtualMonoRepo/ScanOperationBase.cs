// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class ScanOperationBase<T> : Operation where T : IVmrScanner
{
    private readonly VmrScanOptions _options;
    private readonly IVmrScanner _vmrScanner;
    private readonly ILogger<ScanOperationBase<T>> _logger;

    public ScanOperationBase(
        CommandLineOptions options,
        IVmrScanner vmrScanner,
        ILogger<ScanOperationBase<T>> logger)
    {
        _options = (VmrScanOptions)options;
        _vmrScanner = vmrScanner;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        using var listener = CancellationKeyListener.ListenForCancellation(_logger);

        var files = await _vmrScanner.ScanVmr(_options.BaselineFilePath, listener.Token);

        foreach (var file in files)
        {
            Console.WriteLine(file);
        }

        return files.Any() ? 1 : Constants.SuccessCode;
    }
}
