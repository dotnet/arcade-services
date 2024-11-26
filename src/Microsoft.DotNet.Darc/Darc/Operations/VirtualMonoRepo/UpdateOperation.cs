// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class UpdateOperation : VmrOperationBase
{
    private readonly UpdateCommandLineOptions _options;
    private readonly IVmrUpdater _vmrUpdater;
    private readonly IBarApiClient _barClient;

    public UpdateOperation(
        UpdateCommandLineOptions options,
        IVmrUpdater vmrUpdater,
        ILogger<UpdateOperation> logger,
        IBarApiClient barClient)
        : base(options, logger)
    {
        _options = options;
        _vmrUpdater = vmrUpdater;
        _barClient = barClient;
    }

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
        => await _vmrUpdater.UpdateRepository(
                repoName,
                (await _barClient.GetBuildsAsync(repoName, targetRevision)).SingleOrDefault()
                    ?? throw new Exception($"Failed to, or found multiple builds for {repoName}/{targetRevision}"),
                _options.Recursive,
                additionalRemotes,
                _options.ComponentTemplate,
                _options.TpnTemplate,
                _options.GenerateCodeowners,
                _options.GenerateCredScanSuppressions,
                _options.DiscardPatches,
                cancellationToken);
}
