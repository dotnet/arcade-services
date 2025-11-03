// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class AddRepoOperation : VmrOperationBase
{
    private readonly AddRepoCommandLineOptions _options;
    private readonly IVmrInitializer _vmrInitializer;
    private readonly IVmrInfo _vmrInfo;

    public AddRepoOperation(
        AddRepoCommandLineOptions options,
        IVmrInitializer vmrInitializer,
        IVmrInfo vmrInfo,
        ILogger<AddRepoOperation> logger)
        : base(options, logger)
    {
        _options = options;
        _vmrInitializer = vmrInitializer;
        _vmrInfo = vmrInfo;
    }

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(targetRevision))
        {
            throw new ArgumentException($"Repository '{repoName}' must specify a revision in the format NAME:REVISION");
        }

        var sourceMappingsPath = _vmrInfo.VmrPath / VmrInfo.DefaultRelativeSourceMappingsPath;

        await _vmrInitializer.InitializeRepository(
            repoName,
            targetRevision,
            sourceMappingsPath,
            new CodeFlowParameters(
                additionalRemotes,
                VmrInfo.ThirdPartyNoticesFileName,
                GenerateCodeOwners: false,
                GenerateCredScanSuppressions: true),
            cancellationToken);
    }
}
