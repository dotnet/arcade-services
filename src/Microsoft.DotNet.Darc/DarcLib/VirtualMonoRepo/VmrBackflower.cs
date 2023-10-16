// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrBackflower
{
    Task BackflowAsync(string repoName, string targetDirectory, IReadOnlyCollection<AdditionalRemote> additionalRemotes);
}

public class VmrBackflower : IVmrBackflower
{
    private readonly ISourceManifest _sourceManifest;
    private readonly ILocalGitRepo _localGitClient;
    private readonly IVmrPatchHandler _vmrPatchHandler;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<VmrBackflower> _logger;

    public VmrBackflower(
        ISourceManifest sourceManifest,
        ILocalGitRepo localGitClient,
        IVmrPatchHandler vmrPatchHandler,
        IFileSystem fileSystem,
        ILogger<VmrBackflower> logger)
    {
        _sourceManifest = sourceManifest;
        _localGitClient = localGitClient;
        _vmrPatchHandler = vmrPatchHandler;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task BackflowAsync(string repoName, string targetDirectory, IReadOnlyCollection<AdditionalRemote> additionalRemotes)
    {

        await Task.CompletedTask;
    }
}
