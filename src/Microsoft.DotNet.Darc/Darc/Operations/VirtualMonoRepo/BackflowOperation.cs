﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class BackflowOperation(
    BackflowCommandLineOptions options,
    IDarcVmrBackFlower backFlower,
    IVmrInfo vmrInfo,
    ILocalGitRepoFactory localGitRepoFactory,
    IVersionDetailsParser versionDetailsParser,
    IProcessManager processManager,
    IFileSystem fileSystem,
    ILogger<BackflowOperation> logger)
    : CodeFlowOperation(options, vmrInfo, versionDetailsParser, localGitRepoFactory, fileSystem, logger)
{
    private readonly BackflowCommandLineOptions _options = options;
    private readonly IDarcVmrBackFlower _backFlower = backFlower;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IProcessManager _processManager = processManager;

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(targetDirectory))
        {
            throw new DarcException("Please specify path to a local repository to flow to");
        }

        _vmrInfo.VmrPath = new NativePath(_options.VmrPath ?? _processManager.FindGitRoot(Environment.CurrentDirectory));
        var targetRepo = new NativePath(_processManager.FindGitRoot(targetDirectory));

        await VerifyLocalRepositoriesAsync(targetRepo);
        await _backFlower.FlowBackAsync(targetRepo, GetSourceMappingNameAsync(targetRepo), additionalRemotes);
    }
}
