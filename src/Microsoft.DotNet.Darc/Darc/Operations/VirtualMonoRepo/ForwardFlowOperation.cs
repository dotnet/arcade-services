// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ForwardFlowOperation(
        ForwardFlowCommandLineOptions options,
        IDarcVmrForwardFlower codeFlower,
        IVmrInfo vmrInfo,
        IDependencyFileManager dependencyFileManager,
        ILocalGitRepoFactory localGitRepoFactory,
        IFileSystem fileSystem,
        IProcessManager processManager,
        ILogger<ForwardFlowOperation> logger)
    : CodeFlowOperation(options, vmrInfo, dependencyFileManager, localGitRepoFactory, fileSystem, logger)
{
    private readonly ForwardFlowCommandLineOptions _options = options;
    private readonly IDarcVmrForwardFlower _codeFlower = codeFlower;
    private readonly ILocalGitRepoFactory _localGitRepoFactory = localGitRepoFactory;
    private readonly IProcessManager _processManager = processManager;

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        var targetRepoPath = new NativePath(_processManager.FindGitRoot(Environment.CurrentDirectory));
        var targetRepo = _localGitRepoFactory.Create(targetRepoPath);

        if (string.IsNullOrEmpty(_options.VmrPath) || _options.VmrPath == targetRepoPath)
        {
            throw new DarcException("Please specify a path to a local clone of the VMR to flow the changed into.");
        }

        await VerifyLocalRepositoriesAsync(targetRepo);

        _options.Ref ??= await targetRepo.GetShaForRefAsync();

        var mappingName = await GetSourceMappingNameAsync(targetRepoPath, _options.Ref);
        var options = new CodeFlowParameters(
            additionalRemotes,
            TpnTemplatePath: null,
            GenerateCodeOwners: false,
            GenerateCredScanSuppressions: false,
            DiscardPatches: false);

        await _codeFlower.FlowForwardAsync(targetRepo, mappingName, _options.Ref, options, cancellationToken);
    }
}
