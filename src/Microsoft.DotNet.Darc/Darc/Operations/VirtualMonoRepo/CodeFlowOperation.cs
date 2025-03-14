// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
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

internal abstract class CodeFlowOperation : VmrOperationBase
{
    private readonly ICodeFlowCommandLineOptions _options;
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;

    protected CodeFlowOperation(
        ICodeFlowCommandLineOptions options,
        IVmrInfo vmrInfo,
        IVmrDependencyTracker dependencyTracker,
        ILocalGitRepoFactory localGitRepoFactory,
        ILogger<CodeFlowOperation> logger)
        : base(options, logger)
    {
        _options = options;
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _localGitRepoFactory = localGitRepoFactory;
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

        await _dependencyTracker.RefreshMetadata();

        await FlowAsync(
            repoName,
            new NativePath(targetDirectory),
            cancellationToken);
    }

    protected abstract Task<CodeFlowResult> FlowAsync(
        string mappingName,
        NativePath targetDirectory,
        CancellationToken cancellationToken);

    protected async Task<string> GetBaseBranch(NativePath repoPath)
    {
        var localRepo = _localGitRepoFactory.Create(repoPath);

        if (!string.IsNullOrEmpty(_options.BaseBranch))
        {
            await localRepo.CheckoutAsync(_options.BaseBranch);
            return _options.BaseBranch;
        }

        return await localRepo.GetCheckedOutBranchAsync();
    }

    protected async Task<string> GetTargetBranch(NativePath repoPath)
    {
        if (!string.IsNullOrEmpty(_options.TargetBranch))
        {
            return _options.TargetBranch;
        }

        var localRepo = _localGitRepoFactory.Create(repoPath);
        var targetSha = await localRepo.GetShaForRefAsync(DarcLib.Constants.HEAD);

        return $"codeflow/{targetSha}";
    }
}
