// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ScanOperation : Operation
{
    private readonly bool _scanCloaked;
    private readonly bool _scanBinary;

    public ScanOperation(ScanCommandLineOptions options) : base(options, options.RegisterServices())
    {
        _scanBinary = options.Binary;
        _scanCloaked = options.Cloaked;
    }

    public override async Task<int> ExecuteAsync()
    {
        var vmrScanner = Provider.GetRequiredService<IVmrScanner>();
        using var listener = CancellationKeyListener.ListenForCancellation(Logger);

        if (_scanCloaked)
        {
            await vmrScanner.ListCloakedFiles(listener.Token);
        }

        if (_scanBinary)
        {
            await vmrScanner.ListBinaryFiles(listener.Token);
        }

        return 0;
    }
}
