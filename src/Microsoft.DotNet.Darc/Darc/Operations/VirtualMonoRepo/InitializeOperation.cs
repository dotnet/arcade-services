// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class InitializeOperation : VmrOperationBase
{
    private readonly InitializeCommandLineOptions _options;
    private readonly IVmrInitializer _vmrInitializer;

    public InitializeOperation(
        InitializeCommandLineOptions options,
        IVmrInitializer vmrInitializer,
        ILogger<InitializeOperation> logger)
        : base(options, logger)
    {
        _options = options;
        _vmrInitializer = vmrInitializer;
    }

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        await _vmrInitializer.InitializeRepository(
            repoName,
            targetRevision,
            null,
            _options.Recursive,
            new NativePath(_options.SourceMappings),
            additionalRemotes,
            _options.TpnTemplate,
            _options.GenerateCodeowners,
            _options.GenerateCredScanSuppressions,
            _options.DiscardPatches,
            _options.EnableBuildLookUp,
            cancellationToken);
    }
}
