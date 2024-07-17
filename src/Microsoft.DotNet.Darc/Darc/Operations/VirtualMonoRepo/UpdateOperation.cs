// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class UpdateOperation : VmrOperationBase
{
    private readonly UpdateCommandLineOptions _options;
    private readonly IVmrUpdater _vmrUpdater;

    public UpdateOperation(
        CommandLineOptions options,
        IVmrUpdater vmrUpdater,
        IBarApiClient barApiClient,
        ILogger<UpdateOperation> logger)
        : base(options, barApiClient, logger)
    {
        _options = (UpdateCommandLineOptions)options;
        _vmrUpdater = vmrUpdater;
    }

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
        => await _vmrUpdater.UpdateRepository(
                repoName,
                targetRevision,
                targetVersion: null,
                _options.Recursive,
                additionalRemotes,
                _options.ComponentTemplate,
                _options.TpnTemplate,
                _options.GenerateCodeowners,
                _options.DiscardPatches,
                cancellationToken);
}
