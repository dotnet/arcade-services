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

internal class ForwardFlowOperation(
        ForwardFlowCommandLineOptions options,
        IVmrForwardFlower codeFlower,
        IVmrInfo vmrInfo,
        IVmrDependencyTracker dependencyTracker,
        IVmrPatchHandler patchHandler,
        IDependencyFileManager dependencyFileManager,
        ILocalGitRepoFactory localGitRepoFactory,
        IFileSystem fileSystem,
        IProcessManager processManager,
        ILogger<ForwardFlowOperation> logger)
    : CodeFlowOperation(options, vmrInfo, codeFlower, dependencyTracker, patchHandler, dependencyFileManager, localGitRepoFactory, fileSystem, logger)
{
    private readonly ForwardFlowCommandLineOptions _options = options;
    private readonly IProcessManager _processManager = processManager;

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? sourceDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        var sourceRepoPath = new NativePath(_processManager.FindGitRoot(Environment.CurrentDirectory));

        if (string.IsNullOrEmpty(_options.VmrPath) || _options.VmrPath == sourceRepoPath)
        {
            throw new DarcException("Please specify a path to a local clone of the VMR to flow the changed into.");
        }

        await FlowCodeLocallyAsync(
            sourceRepoPath,
            isForwardFlow: true,
            additionalRemotes,
            cancellationToken);
    }

    protected override IEnumerable<string> GetIgnoredFiles(string mapping) =>
    [
        VmrInfo.DefaultRelativeSourceManifestPath,
        VmrInfo.GitInfoSourcesDir,
        VmrInfo.GetRelativeRepoSourcesPath(mapping) / VersionFiles.VersionDetailsXml,
    ];
}
