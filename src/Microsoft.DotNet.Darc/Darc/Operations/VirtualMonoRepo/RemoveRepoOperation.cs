// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class RemoveRepoOperation : VmrOperationBase
{
    private readonly RemoveRepoCommandLineOptions _options;
    private readonly IVmrRemover _vmrRemover;

    public RemoveRepoOperation(
        RemoveRepoCommandLineOptions options,
        IVmrRemover vmrRemover,
        ILogger<RemoveRepoOperation> logger)
        : base(options, logger)
    {
        _options = options;
        _vmrRemover = vmrRemover;
    }

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        await _vmrRemover.RemoveRepository(
            repoName,
            new CodeFlowParameters(
                additionalRemotes,
                VmrInfo.ThirdPartyNoticesFileName,
                GenerateCodeOwners: false,
                GenerateCredScanSuppressions: true),
            cancellationToken);
    }
}
