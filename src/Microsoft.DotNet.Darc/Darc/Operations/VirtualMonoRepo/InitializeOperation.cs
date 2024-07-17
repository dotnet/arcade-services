// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class InitializeOperation : VmrOperationBase
{
    private readonly InitializeCommandLineOptions _options;
    private readonly IVmrInitializer _vmrInitializer;

    public InitializeOperation(
        CommandLineOptions options,
        IVmrInitializer vmrInitializer,
        IBarApiClient barClient,
        ILogger<InitializeOperation> logger)
        : base(options, barClient, logger)
    {
        _options = (InitializeCommandLineOptions)options;
        _vmrInitializer = vmrInitializer;
    }

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
        => await _vmrInitializer.InitializeRepository(
            repoName,
            targetRevision,
            null,
            _options.Recursive,
            new NativePath(_options.SourceMappings),
            additionalRemotes,
            _options.ComponentTemplate,
            _options.TpnTemplate,
            _options.GenerateCodeowners,
            _options.DiscardPatches,
            cancellationToken);
}
