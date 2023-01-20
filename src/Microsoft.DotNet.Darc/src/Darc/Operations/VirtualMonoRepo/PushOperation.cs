// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class PushOperation : Operation
{
    private readonly VmrPushCommandLineOptions _options;

    public PushOperation(VmrPushCommandLineOptions options)
        : base(options, options.RegisterServices())
    {
        _options = options;
    }

    public override async Task<int> ExecuteAsync()
    {
        var vmrPusher = Provider.GetRequiredService<IVmrPusher>();
        using var listener = CancellationKeyListener.ListenForCancellation(Logger);

        await vmrPusher.Push(_options.Remote, _options.Branch,  _options.GitHubApiPat, listener.Token);
        return 0;
    }
}
