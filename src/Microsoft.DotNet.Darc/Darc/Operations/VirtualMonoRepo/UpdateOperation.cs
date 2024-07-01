// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class UpdateOperation : VmrOperationBase
{
    private readonly UpdateCommandLineOptions _options;

    public UpdateOperation(UpdateCommandLineOptions options)
        : base(options)
    {
        _options = options;
    }

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
        => await Provider.GetRequiredService<IVmrUpdater>()
            .UpdateRepository(
                repoName,
                targetRevision,
                targetVersion: null,
                _options.Recursive,
                additionalRemotes,
                _options.ComponentTemplate,
                _options.TpnTemplate,
                _options.GenerateCodeowners,
                _options.GenerateCredScanSuppressions,
                _options.DiscardPatches,
                cancellationToken);
}
