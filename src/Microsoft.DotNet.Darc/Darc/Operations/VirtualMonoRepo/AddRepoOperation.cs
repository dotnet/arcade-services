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
    private readonly ISourceMappingManager _sourceMappingManager;

    public AddRepoOperation(
        AddRepoCommandLineOptions options,
        IVmrInitializer vmrInitializer,
        IVmrInfo vmrInfo,
        ISourceMappingManager sourceMappingManager,
        ILogger<AddRepoOperation> logger)
        : base(options, logger)
    {
        _options = options;
        _vmrInitializer = vmrInitializer;
        _vmrInfo = vmrInfo;
        _sourceMappingManager = sourceMappingManager;
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
        
        // Ensure source mapping exists (will add if not present and stage the file)
        await _sourceMappingManager.EnsureSourceMappingExistsAsync(
            repoName,
            defaultRemote: null, // Will default to https://github.com/dotnet/{repoName}
            sourceMappingsPath,
            cancellationToken);

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
