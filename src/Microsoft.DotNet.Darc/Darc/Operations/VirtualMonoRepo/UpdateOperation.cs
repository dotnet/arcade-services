// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class UpdateOperation : VmrOperationBase
{
    private readonly UpdateCommandLineOptions _options;
    private readonly IDarcVmrUpdater _vmrUpdater;

    public UpdateOperation(
        UpdateCommandLineOptions options,
        IDarcVmrUpdater vmrUpdater,
        ILogger<UpdateOperation> logger)
        : base(options, logger)
    {
        _options = options;
        _vmrUpdater = vmrUpdater;
    }

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        await _vmrUpdater.UpdateRepository(
            repoName,
            targetRevision,
            targetVersion: null,
            officialBuildId: null,
            barId: null,
            _options.Recursive,
            additionalRemotes,
            _options.TpnTemplate,
            _options.GenerateCodeowners,
            _options.GenerateCredScanSuppressions,
            _options.DiscardPatches,
            reapplyVmrPatches: false,
            _options.EnableBuildLookUp,
            cancellationToken);
    }
}
