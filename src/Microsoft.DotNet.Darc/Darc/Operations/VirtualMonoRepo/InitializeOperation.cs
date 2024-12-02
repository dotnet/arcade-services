// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class InitializeOperation : VmrOperationBase
{
    private readonly InitializeCommandLineOptions _options;
    private readonly IVmrInitializer _vmrInitializer;
    private readonly IBarApiClient _barClient;

    public InitializeOperation(
        InitializeCommandLineOptions options,
        IVmrInitializer vmrInitializer,
        ILogger<InitializeOperation> logger,
        IBarApiClient barClient)
        : base(options, logger)
    {
        _options = options;
        _vmrInitializer = vmrInitializer;
        _barClient = barClient;
    }

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        Maestro.Client.Models.Build? build = null;

        if (_options.EnableBuildLookUp)
        {
            build = (await _barClient.GetBuildsAsync(repoName, targetRevision)).FirstOrDefault();
        }

        await _vmrInitializer.InitializeRepository(
            repoName,
            targetRevision,
            null,
            build?.AzureDevOpsBuildNumber,
            build?.Id,
            _options.Recursive,
            new NativePath(_options.SourceMappings),
            additionalRemotes,
            _options.ComponentTemplate,
            _options.TpnTemplate,
            _options.GenerateCodeowners,
            _options.GenerateCredScanSuppressions,
            _options.DiscardPatches,
            cancellationToken);
    }
}
