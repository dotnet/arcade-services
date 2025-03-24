// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class CodeFlowOperation : VmrOperationBase
{
    private readonly ICodeFlowCommandLineOptions _options;
    private readonly IVmrInfo _vmrInfo;
    private readonly IDependencyFileManager _dependencyFileManager;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IFileSystem _fileSystem;

    protected CodeFlowOperation(
        ICodeFlowCommandLineOptions options,
        IVmrInfo vmrInfo,
        IDependencyFileManager dependencyFileManager,
        ILocalGitRepoFactory localGitRepoFactory,
        IFileSystem fileSystem,
        ILogger<CodeFlowOperation> logger)
        : base(options, logger)
    {
        _options = options;
        _vmrInfo = vmrInfo;
        _dependencyFileManager = dependencyFileManager;
        _localGitRepoFactory = localGitRepoFactory;
        _fileSystem = fileSystem;
    }

    protected async Task VerifyLocalRepositoriesAsync(NativePath repoPath)
    {
        var repo = _localGitRepoFactory.Create(repoPath);
        var vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);

        if (await repo.HasWorkingTreeChangesAsync())
        {
            throw new DarcException($"Repository {repo.Path} has uncommitted changes");
        }

        if (await vmr.HasWorkingTreeChangesAsync())
        {
            throw new DarcException($"The VMR at {_vmrInfo.VmrPath} has uncommitted changes");
        }

        if (!_fileSystem.FileExists(_vmrInfo.SourceManifestPath))
        {
            throw new DarcException($"Failed to find {_vmrInfo.SourceManifestPath}! Current directory is not a VMR!");
        }

        _options.Ref ??= await repo.GetShaForRefAsync();
    }

    protected async Task<string> GetSourceMappingNameAsync(NativePath repoPath, string commit)
    {
        var versionDetails = await _dependencyFileManager.ParseVersionDetailsXmlAsync(repoPath, commit);

        if (string.IsNullOrEmpty(versionDetails.Source?.Mapping))
        {
            throw new DarcException(
                $"The <Source /> tag not found in {VersionFiles.VersionDetailsXml}. " +
                "Make sure the repository is onboarded onto codeflow.");
        }

        return versionDetails.Source.Mapping;
    }
}
