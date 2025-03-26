// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class BackflowOperation(
    BackflowCommandLineOptions options,
    IVmrInfo vmrInfo,
    IVmrBackFlower backFlower,
    IVmrDependencyTracker dependencyTracker,
    IVmrPatchHandler patchHandler,
    ILocalGitRepoFactory localGitRepoFactory,
    IDependencyFileManager dependencyFileManager,
    IProcessManager processManager,
    IFileSystem fileSystem,
    ILogger<BackflowOperation> logger)
    : CodeFlowOperation(options, vmrInfo, backFlower, dependencyTracker, patchHandler, dependencyFileManager, localGitRepoFactory, fileSystem, logger)
{
    private readonly BackflowCommandLineOptions _options = options;
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IProcessManager _processManager = processManager;
    private readonly ILogger<BackflowOperation> _logger = logger;

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
        var targetRepoPath = new NativePath(_processManager.FindGitRoot(targetDirectory));
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        var targetRepo = _localGitRepoFactory.Create(targetRepoPath);

        await VerifyLocalRepositoriesAsync(targetRepo);

        _options.Ref ??= await vmr.GetShaForRefAsync();

        var mappingName = await GetSourceMappingNameAsync(targetRepoPath, DarcLib.Constants.HEAD);
        var options = new CodeFlowParameters(
            additionalRemotes,
            TpnTemplatePath: null,
            GenerateCodeOwners: false,
            GenerateCredScanSuppressions: false,
            DiscardPatches: false);

        string shaToFlow = await vmr.GetShaForRefAsync(_options.Ref);

        _logger.LogInformation(
            "Flowing VMR's commit {sourceSha} to {repo} at {targetDirectory}...",
            Commit.GetShortSha(shaToFlow),
            mappingName,
            targetRepo.Path);

        await FlowCodeLocallyAsync(
            targetRepo,
            mappingName,
            new Backflow(shaToFlow, await targetRepo.GetShaForRefAsync()),
            options,
            cancellationToken);
    }

    protected override IEnumerable<string> GetIgnoredFiles(string mapping)
        => DependencyFileManager.DependencyFiles;
}
