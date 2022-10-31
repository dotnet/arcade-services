// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class UpdateOperation : VmrOperationBase<IVmrUpdater>
{
    private readonly UpdateCommandLineOptions _options;

    public UpdateOperation(UpdateCommandLineOptions options)
        : base(options)
    {
        _options = options;
    }

    protected override async Task ExecuteInternalAsync(
        IVmrUpdater vmrManager,
        SourceMapping mapping,
        string? targetRevision,
        CancellationToken cancellationToken)
        =>
        await vmrManager.UpdateRepository(mapping, targetRevision, null, _options.NoSquash, _options.Recursive, cancellationToken);
}
