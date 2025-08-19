// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

[TestFixture]
public class ForwardFlowConflictResolverTests
{
    [Test]
    public async Task ForwardFlowConflictResolverMergesDependenciesCorrectly()
    {
        Mock<ILocalGitRepo> vmrRepo = new Mock<ILocalGitRepo>();
        Mock<ILocalGitRepo> productRepo = new Mock<ILocalGitRepo>();
        Mock<IVmrVersionFileMerger> vmrVersionFileMergerMock = new();
        Mock<ILocalGitRepoFactory> localGitRepoFactoryMock = new();
        Mock<IDependencyFileManager> dependencyFileManagerMock = new();
        Mock<IGitRepoFactory> gitRepoFactoryMock = new();

        var lastFlowRepoSha = "lastFlowRepoSha";
        var lastFlowVmrSha = "lastFlowVmrSha";
        var currentFlowRepoSha = "currentFlowRepoSha";
        var currentFlowVmrSha = "currentFlowVmrSha";
        ForwardFlow lastFlow = new(lastFlowRepoSha, lastFlowVmrSha);
        ForwardFlow currentFlow = new(currentFlowRepoSha, currentFlowVmrSha);
        var mapping = "mapping";
        var targetBranch = "targetBranch";

        productRepo.Setup(r => r.GetFileFromGitAsync(VersionFiles.DotnetToolsConfigJson, lastFlowRepoSha, It.IsAny<string>()))
            .ReturnsAsync("not important");
        productRepo.Setup(r => r.GetFileFromGitAsync(VersionFiles.DotnetToolsConfigJson, currentFlowRepoSha, It.IsAny<string>()))
            .ReturnsAsync("not important");
        productRepo.Setup(r => r.Path).Returns(new NativePath("path"));
        vmrRepo.Setup(r => r.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(mapping) / VersionFiles.DotnetToolsConfigJson, lastFlowVmrSha, It.IsAny<string>()))
            .ReturnsAsync("not important");
        vmrRepo.Setup(r => r.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(mapping) / VersionFiles.DotnetToolsConfigJson, currentFlowVmrSha, It.IsAny<string>()))
            .ReturnsAsync("not important");
        vmrRepo.Setup(r => r.HasWorkingTreeChangesAsync())
            .ReturnsAsync(true);
        vmrRepo.Setup(r => r.Path).Returns(new NativePath("vmrPath"));
        localGitRepoFactoryMock.Setup(f => f.Create(It.IsAny<NativePath>()))
            .Returns(vmrRepo.Object);

        vmrVersionFileMergerMock.Setup(m => m.MergeVersionDetails(
                It.IsAny<ForwardFlow>(),
                It.IsAny<ILocalGitRepo>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ILocalGitRepo>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(new VersionFileChanges<DependencyUpdate>([], [], []));

        ForwardFlowConflictResolver resolver = new(
            new Mock<IVmrInfo>().Object,
            new Mock<ISourceManifest>().Object,
            new Mock<IVmrPatchHandler>().Object,
            new Mock<IFileSystem>().Object,
            NullLogger<ForwardFlowConflictResolver>.Instance,
            vmrVersionFileMergerMock.Object,
            localGitRepoFactoryMock.Object,
            dependencyFileManagerMock.Object,
            gitRepoFactoryMock.Object);

        await resolver.MergeDependenciesAsync(
            mapping,
            productRepo.Object,
            targetBranch,
            lastFlow,
            currentFlow,
            CancellationToken.None);

        vmrVersionFileMergerMock.Verify(
            m => m.MergeJsonAsync(
                lastFlow,
                vmrRepo.Object,
                VmrInfo.GetRelativeRepoSourcesPath(mapping) / VersionFiles.GlobalJson,
                lastFlowVmrSha,
                targetBranch,
                productRepo.Object,
                VersionFiles.GlobalJson,
                lastFlowRepoSha,
                currentFlowRepoSha,
                false),
            Times.Once);
        vmrVersionFileMergerMock.Verify(
            m => m.MergeJsonAsync(
                lastFlow,
                vmrRepo.Object,
                VmrInfo.GetRelativeRepoSourcesPath(mapping) / VersionFiles.DotnetToolsConfigJson,
                lastFlowVmrSha,
                targetBranch,
                productRepo.Object,
                VersionFiles.DotnetToolsConfigJson,
                lastFlowRepoSha,
                currentFlowRepoSha,
                true),
            Times.Once);
        vmrVersionFileMergerMock.Verify(
            m => m.MergeVersionDetails(
                lastFlow,
                vmrRepo.Object,
                VmrInfo.GetRelativeRepoSourcesPath(mapping) / VersionFiles.VersionDetailsXml,
                lastFlowVmrSha,
                targetBranch,
                productRepo.Object,
                VersionFiles.VersionDetailsXml,
                lastFlowRepoSha,
                currentFlowRepoSha,
                mapping),
            Times.Once);
        vmrRepo.Verify(r => r.HasWorkingTreeChangesAsync(),
            Times.Once);
        vmrRepo.Verify(r => r.CommitAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<(string, string)?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

    }
}
