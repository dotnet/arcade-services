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

internal class InitializeOperation : VmrOperationBase<IVmrInitializer>
{
    public InitializeOperation(InitializeCommandLineOptions options)
        : base(options)
    {
    }

    protected override async Task ExecuteInternalAsync(
        IVmrInitializer vmrManager,
        SourceMapping mapping,
        string? targetRevision,
        bool recursive,
        CancellationToken cancellationToken)
        =>
        await vmrManager.InitializeRepository(mapping, targetRevision, null, recursive, cancellationToken);
}
