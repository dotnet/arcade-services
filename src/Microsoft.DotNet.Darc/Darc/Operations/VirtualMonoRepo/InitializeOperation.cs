// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class InitializeOperation : VmrOperationBase
{
    private readonly InitializeCommandLineOptions _options;

    public InitializeOperation(InitializeCommandLineOptions options)
        : base(options)
    {
        _options = options;
    }

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
        => await Provider.GetRequiredService<IVmrInitializer>()
            .InitializeRepository(
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
