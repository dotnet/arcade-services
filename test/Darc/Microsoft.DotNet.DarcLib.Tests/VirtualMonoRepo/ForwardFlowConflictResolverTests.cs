// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
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
        Mock<ILocalGitRepo> vmrRepo = new();
        Mock<ILocalGitRepo> productRepo = new();
        Mock<ILocalGitRepoFactory> localGitRepoFactoryMock = new();
        Mock<IDependencyFileManager> dependencyFileManagerMock = new();
        Mock<IJsonFileMerger> jsonMergerMock = new();
        Mock<IVersionDetailsFileMerger> versionDetailsFileMergerMock = new();
        Mock<IVersionDetailsParser> versionDetailsParserMock = new();
        Mock<IGitRepo> vmrGitRepoMock = new();

        var lastFlowRepoSha = "lastFlowRepoSha";
        var lastFlowVmrSha = "lastFlowVmrSha";
        var currentFlowRepoSha = "currentFlowRepoSha";
        var currentFlowVmrSha = "currentFlowVmrSha";
        ForwardFlow lastFlow = new(lastFlowRepoSha, lastFlowVmrSha);
        ForwardFlow currentFlow = new(currentFlowRepoSha, currentFlowVmrSha);
        var mapping = "mapping";
        var targetBranch = "targetBranch";
        var repoPath = "repoPath";
        var vmrPath = "vmrPath";
        var doc = new XmlDocument();
        var root = doc.CreateElement("Root");
        doc.AppendChild(root);
        SourceDependency newSourceDependency = new(
            new Build(2, DateTimeOffset.Now, 0, false, true, string.Empty, [], [], [], []),
            "mapping");

        productRepo.Setup(r => r.GetFileFromGitAsync(VersionFiles.DotnetToolsConfigJson, lastFlowRepoSha, It.IsAny<string>()))
            .ReturnsAsync("not important");
        productRepo.Setup(r => r.GetFileFromGitAsync(VersionFiles.DotnetToolsConfigJson, currentFlowRepoSha, It.IsAny<string>()))
            .ReturnsAsync("not important");
        productRepo.Setup(r => r.Path).Returns(new NativePath(repoPath));
        vmrRepo.Setup(r => r.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(mapping) / VersionFiles.DotnetToolsConfigJson, lastFlowVmrSha, It.IsAny<string>()))
            .ReturnsAsync("not important");
        vmrRepo.Setup(r => r.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(mapping) / VersionFiles.DotnetToolsConfigJson, currentFlowVmrSha, It.IsAny<string>()))
            .ReturnsAsync("not important");
        vmrRepo.Setup(r => r.HasWorkingTreeChangesAsync())
            .ReturnsAsync(true);
        vmrRepo.Setup(r => r.Path).Returns(new NativePath(vmrPath));
        localGitRepoFactoryMock.Setup(f => f.Create(It.IsAny<NativePath>()))
            .Returns(vmrRepo.Object);

        versionDetailsFileMergerMock.Setup(m => m.MergeVersionDetails(
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

        dependencyFileManagerMock.Setup(m => m.ParseVersionDetailsXmlAsync(
                repoPath,
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<UnixPath>()))
            .ReturnsAsync(new VersionDetails(
                [],
                new SourceDependency(
                    new Build(
                        2,
                        DateTimeOffset.Now,
                        0,
                        false,
                        true,
                        string.Empty,
                        [],
                        [],
                        [],
                        []),
                    "mapping"
                )));
        dependencyFileManagerMock.Setup(m => m.ParseVersionDetailsXmlAsync(
                vmrPath,
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<UnixPath>()))
            .ReturnsAsync(new VersionDetails(
                [],
                new SourceDependency(
                    new Build(
                        1,
                        DateTimeOffset.Now,
                        0,
                        false,
                        true,
                        string.Empty,
                        [],
                        [],
                        [],
                        []),
                    "mapping"
                )));
        dependencyFileManagerMock.Setup(m => m.ReadVersionDetailsXmlAsync(
                vmrPath, It.IsAny<string>(), It.IsAny<UnixPath>()))
            .ReturnsAsync(doc);

        ForwardFlowConflictResolver resolver = new(
            new Mock<IVmrInfo>().Object,
            new Mock<ISourceManifest>().Object,
            new Mock<IVmrPatchHandler>().Object,
            localGitRepoFactoryMock.Object,
            dependencyFileManagerMock.Object,
            jsonMergerMock.Object,
            versionDetailsFileMergerMock.Object,
            versionDetailsParserMock.Object,
            new Mock<IFileSystem>().Object,
            NullLogger<ForwardFlowConflictResolver>.Instance);

        await resolver.MergeDependenciesAsync(
            new CodeflowOptions(new SourceMapping(mapping, "https://github.com/dotnet/runtime", "main", [], [], false), currentFlow, targetBranch, "main", null!, null, false, false, false),
            productRepo.Object,
            targetBranch,
            lastFlow.RepoSha,
            lastFlow.VmrSha,
            CancellationToken.None);

        jsonMergerMock.Verify(
            m => m.MergeJsonsAsync(
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
        jsonMergerMock.Verify(
            m => m.MergeJsonsAsync(
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
        versionDetailsFileMergerMock.Verify(
            m => m.MergeVersionDetails(
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
        dependencyFileManagerMock.Verify(m => m.UpdateVersionDetailsXmlSourceTag(
                It.IsAny<XmlDocument>(),
                newSourceDependency),
            Times.Once);
    }
}
