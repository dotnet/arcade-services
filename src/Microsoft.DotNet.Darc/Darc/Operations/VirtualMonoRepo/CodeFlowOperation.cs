// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class CodeFlowOperation : VmrOperationBase
{
    private readonly CodeFlowCommandLineOptions _options;
    private readonly IVmrInfo _vmrInfo;

    protected CodeFlowOperation(
        CommandLineOptions options,
        IVmrInfo vmrInfo,
        ILogger<CodeFlowOperation> logger)
        : base(options, logger)
    {
        _options = (CodeFlowCommandLineOptions)options;
        _vmrInfo = vmrInfo;
    }

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        targetDirectory ??= Path.Combine(
            _options.RepositoryDirectory ?? throw new ArgumentException($"No target directory specified for repository {repoName}"),
            repoName);

        if (!Directory.Exists(targetDirectory))
        {
            throw new FileNotFoundException($"Could not find directory {targetDirectory}");
        }

        if (_options.RepositoryDirectory is not null)
        {
            _vmrInfo.TmpPath = new NativePath(_options.RepositoryDirectory);
        }

        if (_options.Build.HasValue && _options.Commit != null)
        {
            throw new ArgumentException("Cannot specify both --build and --commit");
        }

        await FlowAsync(
            repoName,
            new NativePath(targetDirectory),
            _options.Commit,
            cancellationToken);
    }

    protected abstract Task<bool> FlowAsync(
        string mappingName,
        NativePath targetDirectory,
        string? shaToFlow,
        CancellationToken cancellationToken);
}
