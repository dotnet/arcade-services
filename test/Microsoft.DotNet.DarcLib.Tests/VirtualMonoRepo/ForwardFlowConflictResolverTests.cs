// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

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
                );

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

        vmrVersionFileMergerMock.Setup(m => m.MergeVersionDetails(
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
        gitRepoFactoryMock.Setup(f => f.CreateClient(vmrPath))
            .Returns(vmrGitRepoMock.Object);

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
        dependencyFileManagerMock.Verify(m => m.UpdateVersionDetailsXmlSourceTag(
                It.IsAny<XmlDocument>(),
                newSourceDependency),
            Times.Once);
    }

    /// <summary>
    /// Validates that when dotnet-tools.json exists in either source or VMR and the Source tag changes
    /// (different BAR IDs between repo and VMR), the resolver:
    /// - Merges global.json and dotnet-tools.json.
    /// - Updates the Source tag in Version.Details.xml and commits the change via IGitRepoFactory.
    /// - Stages and commits the working tree when there are pending changes.
    /// Inputs:
    ///  - mappingName: "mapping"
    ///  - lastFlow: (RepoSha: "lastFlowRepoSha", VmrSha: "lastFlowVmrSha")
    ///  - currentFlow: (RepoSha: "currentFlowRepoSha", VmrSha: "currentFlowVmrSha")
    ///  - dotnet-tools.json present in both source and VMR
    ///  - repo VersionDetails Source BarId = 2, vmr VersionDetails Source BarId = 1
    /// Expected:
    ///  - MergeJsonAsync called for global.json (allowMissingFiles=false) and dotnet-tools.json (allowMissingFiles=true).
    ///  - MergeVersionDetails called once.
    ///  - UpdateVersionDetailsXmlSourceTag invoked and files committed with message "Update source tag".
    ///  - vmr.StageAsync(".") and vmr.CommitAsync("Update dependencies", allowEmpty: false) are called once.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task MergeDependenciesAsync_DotnetToolsPresent_SourceTagChanged_StagesAndCommits()
    {
        // Arrange
        var vmrRepo = new Mock<ILocalGitRepo>();
        var sourceRepo = new Mock<ILocalGitRepo>();
        var vmrVersionFileMerger = new Mock<IVmrVersionFileMerger>();
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>();
        var dependencyFileManager = new Mock<IDependencyFileManager>();
        var gitRepoFactory = new Mock<IGitRepoFactory>();
        var vmrGitRepo = new Mock<IGitRepo>();
        var vmrInfo = new Mock<IVmrInfo>();
        var fileSystem = new Mock<IFileSystem>();

        var mapping = "mapping";
        var targetBranch = "targetBranch";
        var srcRepoPath = "repoPath";
        var vmrPath = "vmrPath";

        var lastFlowRepoSha = "lastFlowRepoSha";
        var lastFlowVmrSha = "lastFlowVmrSha";
        var currentFlowRepoSha = "currentFlowRepoSha";
        var currentFlowVmrSha = "currentFlowVmrSha";

        var lastFlow = new ForwardFlow(lastFlowRepoSha, lastFlowVmrSha);
        var currentFlow = new ForwardFlow(currentFlowRepoSha, currentFlowVmrSha);

        var relativeSourceMappingPath = VmrInfo.GetRelativeRepoSourcesPath(mapping);
        var vmrVersionDetailsXml = new XmlDocument();
        vmrVersionDetailsXml.AppendChild(vmrVersionDetailsXml.CreateElement("VersionDetails"));

        // dotnet-tools.json existence checks -> true in multiple places
        sourceRepo.Setup(r => r.GetFileFromGitAsync(VersionFiles.DotnetToolsConfigJson, lastFlowRepoSha, It.IsAny<string>()))
                  .ReturnsAsync("exists");
        sourceRepo.Setup(r => r.GetFileFromGitAsync(VersionFiles.DotnetToolsConfigJson, targetBranch, It.IsAny<string>()))
                  .ReturnsAsync("exists");
        vmrRepo.Setup(r => r.GetFileFromGitAsync(relativeSourceMappingPath / VersionFiles.DotnetToolsConfigJson, currentFlowVmrSha, It.IsAny<string>()))
               .ReturnsAsync("exists");
        vmrRepo.Setup(r => r.GetFileFromGitAsync(relativeSourceMappingPath / VersionFiles.DotnetToolsConfigJson, lastFlowVmrSha, It.IsAny<string>()))
               .ReturnsAsync("exists");

        // Paths
        sourceRepo.Setup(r => r.Path).Returns(new NativePath(srcRepoPath));
        vmrRepo.Setup(r => r.Path).Returns(new NativePath(vmrPath));
        vmrInfo.SetupGet(v => v.VmrPath).Returns(new NativePath(vmrPath));
        localGitRepoFactory.Setup(f => f.Create(It.IsAny<NativePath>())).Returns(vmrRepo.Object);

        // No props creation in this test
        dependencyFileManager.Setup(m => m.VersionDetailsPropsExistsAsync(srcRepoPath, null, It.IsAny<UnixPath>()))
                             .ReturnsAsync(false);
        dependencyFileManager.Setup(m => m.VersionDetailsPropsExistsAsync(vmrPath, null, It.IsAny<UnixPath>()))
                             .ReturnsAsync(true);

        // MergeVersionDetails returns no changes (not relevant for this scenario)
        vmrVersionFileMerger.Setup(m => m.MergeVersionDetails(
                    It.IsAny<ILocalGitRepo>(),
                    It.IsAny<string>(),
                    lastFlowVmrSha,
                    targetBranch,
                    sourceRepo.Object,
                    VersionFiles.VersionDetailsXml,
                    lastFlowRepoSha,
                    currentFlowRepoSha,
                    mapping))
            .ReturnsAsync(new VersionFileChanges<DependencyUpdate>(new List<string>(), new Dictionary<string, DependencyUpdate>(), new Dictionary<string, DependencyUpdate>()));

        // Source tag change: repo Source BarId != vmr Source BarId
        dependencyFileManager.Setup(m => m.ParseVersionDetailsXmlAsync(srcRepoPath, currentFlowRepoSha, true, null))
                             .ReturnsAsync(new VersionDetails(new List<DependencyDetail>(), new SourceDependency("uri", mapping, "sha", 2)));
        dependencyFileManager.Setup(m => m.ParseVersionDetailsXmlAsync(vmrPath, targetBranch, true, relativeSourceMappingPath))
                             .ReturnsAsync(new VersionDetails(new List<DependencyDetail>(), new SourceDependency("uri", mapping, "sha", 1)));

        dependencyFileManager.Setup(m => m.ReadVersionDetailsXmlAsync(vmrPath, null, relativeSourceMappingPath))
                             .ReturnsAsync(vmrVersionDetailsXml);

        gitRepoFactory.Setup(f => f.CreateClient(vmrPath)).Returns(vmrGitRepo.Object);

        // Working tree has changes -> stage and commit
        vmrRepo.Setup(r => r.HasWorkingTreeChangesAsync()).ReturnsAsync(true);
        vmrRepo.Setup(r => r.HasStagedChangesAsync()).ReturnsAsync(false);

        var resolver = new ForwardFlowConflictResolver(
            vmrInfo.Object,
            new Mock<ISourceManifest>().Object,
            new Mock<IVmrPatchHandler>().Object,
            fileSystem.Object,
            NullLogger<ForwardFlowConflictResolver>.Instance,
            vmrVersionFileMerger.Object,
            localGitRepoFactory.Object,
            dependencyFileManager.Object,
            gitRepoFactory.Object);

        // Act
        await resolver.MergeDependenciesAsync(
            mapping,
            sourceRepo.Object,
            targetBranch,
            lastFlow,
            currentFlow,
            CancellationToken.None);

        // Assert (Moq verifications)
        vmrVersionFileMerger.Verify(
            m => m.MergeJsonAsync(
                vmrRepo.Object,
                relativeSourceMappingPath / VersionFiles.GlobalJson,
                lastFlowVmrSha,
                targetBranch,
                sourceRepo.Object,
                VersionFiles.GlobalJson,
                lastFlowRepoSha,
                currentFlowRepoSha,
                false),
            Times.Once);

        vmrVersionFileMerger.Verify(
            m => m.MergeJsonAsync(
                vmrRepo.Object,
                relativeSourceMappingPath / VersionFiles.DotnetToolsConfigJson,
                lastFlowVmrSha,
                targetBranch,
                sourceRepo.Object,
                VersionFiles.DotnetToolsConfigJson,
                lastFlowRepoSha,
                currentFlowRepoSha,
                true),
            Times.Once);

        vmrVersionFileMerger.Verify(
            m => m.MergeVersionDetails(
                vmrRepo.Object,
                relativeSourceMappingPath / VersionFiles.VersionDetailsXml,
                lastFlowVmrSha,
                targetBranch,
                sourceRepo.Object,
                VersionFiles.VersionDetailsXml,
                lastFlowRepoSha,
                currentFlowRepoSha,
                mapping),
            Times.Once);

        dependencyFileManager.Verify(m => m.UpdateVersionDetailsXmlSourceTag(
                It.IsAny<XmlDocument>(),
                It.Is<SourceDependency>(s => s.BarId == 2 && s.Mapping == mapping)),
            Times.Once);

        gitRepoFactory.Verify(f => f.CreateClient(vmrPath), Times.AtLeastOnce);
        vmrGitRepo.Verify(g => g.CommitFilesAsync(
                It.IsAny<List<GitFile>>(),
                vmrPath,
                targetBranch,
                "Update source tag"),
            Times.Once);

        vmrRepo.Verify(r => r.StageAsync(It.Is<IEnumerable<string>>(e => e.Single() == "."), It.IsAny<CancellationToken>()), Times.Once);
        vmrRepo.Verify(r => r.CommitAsync("Update dependencies", false, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Ensures no dotnet-tools.json merge occurs when the file does not exist anywhere
    /// and that no final Stage/Commit is performed when the working tree and index have no changes.
    /// Inputs:
    ///  - All dotnet-tools.json existence checks return null.
    ///  - repo and vmr VersionDetails.Source are null (no Source tag update).
    ///  - No Version.Details.props creation required.
    ///  - vmr.HasWorkingTreeChangesAsync == false and vmr.HasStagedChangesAsync == false.
    /// Expected:
    ///  - MergeJsonAsync called only for global.json.
    ///  - No UpdateVersionDetailsXmlSourceTag or CommitFilesAsync calls.
    ///  - No vmr.StageAsync or vmr.CommitAsync calls.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task MergeDependenciesAsync_NoDotnetTools_NoWorkingTree_NoFinalCommit()
    {
        // Arrange
        var vmrRepo = new Mock<ILocalGitRepo>();
        var sourceRepo = new Mock<ILocalGitRepo>();
        var vmrVersionFileMerger = new Mock<IVmrVersionFileMerger>();
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>();
        var dependencyFileManager = new Mock<IDependencyFileManager>();
        var gitRepoFactory = new Mock<IGitRepoFactory>();
        var vmrInfo = new Mock<IVmrInfo>();
        var fileSystem = new Mock<IFileSystem>();

        var mapping = "mapping";
        var targetBranch = "targetBranch";
        var srcRepoPath = "repoPath";
        var vmrPath = "vmrPath";

        var lastFlow = new ForwardFlow("lastFlowRepoSha", "lastFlowVmrSha");
        var currentFlow = new ForwardFlow("currentFlowRepoSha", "currentFlowVmrSha");

        var relativeSourceMappingPath = VmrInfo.GetRelativeRepoSourcesPath(mapping);

        // dotnet-tools.json existence checks -> all null
        sourceRepo.Setup(r => r.GetFileFromGitAsync(VersionFiles.DotnetToolsConfigJson, lastFlow.RepoSha, It.IsAny<string>()))
                  .ReturnsAsync((string)null);
        sourceRepo.Setup(r => r.GetFileFromGitAsync(VersionFiles.DotnetToolsConfigJson, targetBranch, It.IsAny<string>()))
                  .ReturnsAsync((string)null);
        vmrRepo.Setup(r => r.GetFileFromGitAsync(relativeSourceMappingPath / VersionFiles.DotnetToolsConfigJson, currentFlow.VmrSha, It.IsAny<string>()))
               .ReturnsAsync((string)null);
        vmrRepo.Setup(r => r.GetFileFromGitAsync(relativeSourceMappingPath / VersionFiles.DotnetToolsConfigJson, lastFlow.VmrSha, It.IsAny<string>()))
               .ReturnsAsync((string)null);

        // Paths
        sourceRepo.Setup(r => r.Path).Returns(new NativePath(srcRepoPath));
        vmrRepo.Setup(r => r.Path).Returns(new NativePath(vmrPath));
        vmrInfo.SetupGet(v => v.VmrPath).Returns(new NativePath(vmrPath));
        localGitRepoFactory.Setup(f => f.Create(It.IsAny<NativePath>())).Returns(vmrRepo.Object);

        // No props creation
        dependencyFileManager.Setup(m => m.VersionDetailsPropsExistsAsync(srcRepoPath, null, It.IsAny<UnixPath>()))
                             .ReturnsAsync(false);
        dependencyFileManager.Setup(m => m.VersionDetailsPropsExistsAsync(vmrPath, null, It.IsAny<UnixPath>()))
                             .ReturnsAsync(true);

        // MergeVersionDetails any return
        vmrVersionFileMerger.Setup(m => m.MergeVersionDetails(
                    It.IsAny<ILocalGitRepo>(),
                    It.IsAny<string>(),
                    lastFlow.VmrSha,
                    targetBranch,
                    sourceRepo.Object,
                    VersionFiles.VersionDetailsXml,
                    lastFlow.RepoSha,
                    currentFlow.RepoSha,
                    mapping))
            .ReturnsAsync(new VersionFileChanges<DependencyUpdate>(new List<string>(), new Dictionary<string, DependencyUpdate>(), new Dictionary<string, DependencyUpdate>()));

        // No source tag change (Source is null)
        dependencyFileManager.Setup(m => m.ParseVersionDetailsXmlAsync(srcRepoPath, currentFlow.RepoSha, true, null))
                             .ReturnsAsync(new VersionDetails(new List<DependencyDetail>(), null));
        dependencyFileManager.Setup(m => m.ParseVersionDetailsXmlAsync(vmrPath, targetBranch, true, relativeSourceMappingPath))
                             .ReturnsAsync(new VersionDetails(new List<DependencyDetail>(), null));

        // No working or staged changes
        vmrRepo.Setup(r => r.HasWorkingTreeChangesAsync()).ReturnsAsync(false);
        vmrRepo.Setup(r => r.HasStagedChangesAsync()).ReturnsAsync(false);

        var resolver = new ForwardFlowConflictResolver(
            vmrInfo.Object,
            new Mock<ISourceManifest>().Object,
            new Mock<IVmrPatchHandler>().Object,
            fileSystem.Object,
            NullLogger<ForwardFlowConflictResolver>.Instance,
            vmrVersionFileMerger.Object,
            localGitRepoFactory.Object,
            dependencyFileManager.Object,
            gitRepoFactory.Object);

        // Act
        await resolver.MergeDependenciesAsync(
            mapping,
            sourceRepo.Object,
            targetBranch,
            lastFlow,
            currentFlow,
            CancellationToken.None);

        // Assert (Moq verifications)
        vmrVersionFileMerger.Verify(
            m => m.MergeJsonAsync(
                vmrRepo.Object,
                relativeSourceMappingPath / VersionFiles.GlobalJson,
                lastFlow.VmrSha,
                targetBranch,
                sourceRepo.Object,
                VersionFiles.GlobalJson,
                lastFlow.RepoSha,
                currentFlow.RepoSha,
                false),
            Times.Once);

        vmrVersionFileMerger.Verify(
            m => m.MergeJsonAsync(
                vmrRepo.Object,
                relativeSourceMappingPath / VersionFiles.DotnetToolsConfigJson,
                lastFlow.VmrSha,
                targetBranch,
                sourceRepo.Object,
                VersionFiles.DotnetToolsConfigJson,
                lastFlow.RepoSha,
                currentFlow.RepoSha,
                true),
            Times.Never);

        dependencyFileManager.Verify(m => m.UpdateVersionDetailsXmlSourceTag(It.IsAny<XmlDocument>(), It.IsAny<SourceDependency>()), Times.Never);
        gitRepoFactory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);

        vmrRepo.Verify(r => r.StageAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        vmrRepo.Verify(r => r.CommitAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<(string, string)?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that when Version.Details.props exists in the source repo but not in the VMR,
    /// and there are no other Version.Details changes, the resolver initializes Version.Details.props
    /// and commits it through IGitRepoFactory with the expected message.
    /// Inputs:
    ///  - IDependencyFileManager.VersionDetailsPropsExistsAsync(source) == true
    ///  - IDependencyFileManager.VersionDetailsPropsExistsAsync(vmr) == false
    ///  - MergeVersionDetails returns no changes
    ///  - No Source tag change
    ///  - vmr working tree has no changes
    /// Expected:
    ///  - _fileSystem.WriteToFile invoked to create Version.Details.props.
    ///  - IGitRepoFactory.CreateClient(vmrPath).CommitFilesAsync called once with message "Initialize Version.Details.props".
    ///  - No vmr.StageAsync or vmr.CommitAsync at the end (since no working tree changes).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task MergeDependenciesAsync_CreatesVersionDetailsProps_WhenNoOtherChanges_CommitsInitialization()
    {
        // Arrange
        var vmrRepo = new Mock<ILocalGitRepo>();
        var sourceRepo = new Mock<ILocalGitRepo>();
        var vmrVersionFileMerger = new Mock<IVmrVersionFileMerger>();
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>();
        var dependencyFileManager = new Mock<IDependencyFileManager>();
        var gitRepoFactory = new Mock<IGitRepoFactory>();
        var vmrGitRepo = new Mock<IGitRepo>();
        var vmrInfo = new Mock<IVmrInfo>();
        var fileSystem = new Mock<IFileSystem>();

        var mapping = "mapping";
        var targetBranch = "targetBranch";
        var srcRepoPath = "repoPath";
        var vmrPath = "vmrPath";

        var lastFlow = new ForwardFlow("lastFlowRepoSha", "lastFlowVmrSha");
        var currentFlow = new ForwardFlow("currentFlowRepoSha", "currentFlowVmrSha");

        var relativeSourceMappingPath = VmrInfo.GetRelativeRepoSourcesPath(mapping);

        // Paths
        sourceRepo.Setup(r => r.Path).Returns(new NativePath(srcRepoPath));
        vmrRepo.Setup(r => r.Path).Returns(new NativePath(vmrPath));
        vmrInfo.SetupGet(v => v.VmrPath).Returns(new NativePath(vmrPath));
        localGitRepoFactory.Setup(f => f.Create(It.IsAny<NativePath>())).Returns(vmrRepo.Object);

        // Version.Details.props exists in source but not in VMR
        dependencyFileManager.Setup(m => m.VersionDetailsPropsExistsAsync(srcRepoPath, null, null))
                             .ReturnsAsync(true);
        dependencyFileManager.Setup(m => m.VersionDetailsPropsExistsAsync(vmrPath, null, relativeSourceMappingPath))
                             .ReturnsAsync(false);

        // Ensure MergeVersionDetails returns no changes -> triggers props initialization path
        vmrVersionFileMerger.Setup(m => m.MergeVersionDetails(
                    It.IsAny<ILocalGitRepo>(),
                    It.IsAny<string>(),
                    lastFlow.VmrSha,
                    targetBranch,
                    sourceRepo.Object,
                    VersionFiles.VersionDetailsXml,
                    lastFlow.RepoSha,
                    currentFlow.RepoSha,
                    mapping))
            .ReturnsAsync(new VersionFileChanges<DependencyUpdate>(new List<string>(), new Dictionary<string, DependencyUpdate>(), new Dictionary<string, DependencyUpdate>()));

        // ParseVersionDetailsXmlAsync for GenerateVersionDetailsProps and for source&vmr comparisons
        dependencyFileManager.Setup(m => m.ParseVersionDetailsXmlAsync(srcRepoPath, currentFlow.RepoSha, true, null))
                             .ReturnsAsync(new VersionDetails(new List<DependencyDetail>(), null));
        dependencyFileManager.Setup(m => m.ParseVersionDetailsXmlAsync(vmrPath, targetBranch, true, relativeSourceMappingPath))
                             .ReturnsAsync(new VersionDetails(new List<DependencyDetail>(), null));
        dependencyFileManager.Setup(m => m.ParseVersionDetailsXmlAsync(vmrPath, null, true, relativeSourceMappingPath))
                             .ReturnsAsync(new VersionDetails(new List<DependencyDetail>(), null));

        // Commit via IGitRepoFactory
        gitRepoFactory.Setup(f => f.CreateClient(vmrPath)).Returns(vmrGitRepo.Object);

        // No working tree/index changes -> no final vmr commit
        vmrRepo.Setup(r => r.HasWorkingTreeChangesAsync()).ReturnsAsync(false);
        vmrRepo.Setup(r => r.HasStagedChangesAsync()).ReturnsAsync(false);

        var resolver = new ForwardFlowConflictResolver(
            vmrInfo.Object,
            new Mock<ISourceManifest>().Object,
            new Mock<IVmrPatchHandler>().Object,
            fileSystem.Object,
            NullLogger<ForwardFlowConflictResolver>.Instance,
            vmrVersionFileMerger.Object,
            localGitRepoFactory.Object,
            dependencyFileManager.Object,
            gitRepoFactory.Object);

        // Act
        await resolver.MergeDependenciesAsync(
            mapping,
            sourceRepo.Object,
            targetBranch,
            lastFlow,
            currentFlow,
            CancellationToken.None);

        // Assert (Moq verifications)
        fileSystem.Verify(fs => fs.WriteToFile(
                vmrRepo.Object.Path / relativeSourceMappingPath / VersionFiles.VersionDetailsProps,
                string.Empty),
            Times.Once);

        gitRepoFactory.Verify(f => f.CreateClient(vmrPath), Times.AtLeastOnce);
        vmrGitRepo.Verify(g => g.CommitFilesAsync(
                It.IsAny<List<GitFile>>(),
                vmrPath,
                targetBranch,
                "Initialize Version.Details.props"),
            Times.Once);

        vmrRepo.Verify(r => r.StageAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        vmrRepo.Verify(r => r.CommitAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<(string, string)?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}



[TestFixture]
public class ForwardFlowConflictResolverConstructorTests
{
    /// <summary>
    /// Ensures that constructing ForwardFlowConflictResolver with valid dependencies succeeds
    /// and the resulting instance is of the correct type and implements the expected interface.
    /// Inputs:
    ///  - Valid mocks for all required constructor parameters.
    /// Expected:
    ///  - No exception is thrown.
    ///  - Instance is created, implements IForwardFlowConflictResolver, and is assignable to CodeFlowConflictResolver.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ForwardFlowConflictResolver_ValidDependencies_InstanceCreatedAndImplementsExpectedTypes()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict).Object;
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict).Object;
        var patchHandler = new Mock<IVmrPatchHandler>(MockBehavior.Strict).Object;
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger<ForwardFlowConflictResolver>>(MockBehavior.Strict).Object;
        var versionFileMerger = new Mock<IVmrVersionFileMerger>(MockBehavior.Strict).Object;
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict).Object;
        var dependencyFileManager = new Mock<IDependencyFileManager>(MockBehavior.Strict).Object;
        var gitRepoFactory = new Mock<IGitRepoFactory>(MockBehavior.Strict).Object;

        // Act
        var instance = new ForwardFlowConflictResolver(
            vmrInfo,
            sourceManifest,
            patchHandler,
            fileSystem,
            logger,
            versionFileMerger,
            localGitRepoFactory,
            dependencyFileManager,
            gitRepoFactory);

        // Assert
        instance.Should().NotBeNull();
        instance.Should().BeAssignableTo<IForwardFlowConflictResolver>();
        instance.Should().BeAssignableTo<CodeFlowConflictResolver>();
    }

    /// <summary>
    /// Verifies that the constructor does not invoke any dependency members by allowing both strict and loose mocks.
    /// Inputs:
    ///  - A boolean indicating whether mocks are strict or loose.
    /// Expected:
    ///  - No exception is thrown during construction, proving no unexpected interactions occur in the ctor.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ForwardFlowConflictResolver_DependencyInteractionsInCtor_None(bool useStrictMocks)
    {
        // Arrange
        var behavior = useStrictMocks ? MockBehavior.Strict : MockBehavior.Loose;
        var vmrInfo = new Mock<IVmrInfo>(behavior).Object;
        var sourceManifest = new Mock<ISourceManifest>(behavior).Object;
        var patchHandler = new Mock<IVmrPatchHandler>(behavior).Object;
        var fileSystem = new Mock<IFileSystem>(behavior).Object;
        var logger = new Mock<ILogger<ForwardFlowConflictResolver>>(behavior).Object;
        var versionFileMerger = new Mock<IVmrVersionFileMerger>(behavior).Object;
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(behavior).Object;
        var dependencyFileManager = new Mock<IDependencyFileManager>(behavior).Object;
        var gitRepoFactory = new Mock<IGitRepoFactory>(behavior).Object;

        // Act
        var instance = new ForwardFlowConflictResolver(
            vmrInfo,
            sourceManifest,
            patchHandler,
            fileSystem,
            logger,
            versionFileMerger,
            localGitRepoFactory,
            dependencyFileManager,
            gitRepoFactory);

        // Assert
        instance.Should().NotBeNull();
    }

    /// <summary>
    /// Partial test placeholder for null-argument validation.
    /// Inputs:
    ///  - Null for one or more constructor parameters (non-nullable by annotations).
    /// Expected:
    ///  - If the implementation enforces argument validation, ArgumentNullException should be thrown.
    /// Notes:
    ///  - Since parameters are non-nullable and the implementation details are not visible,
    ///    this test is marked inconclusive. Replace with explicit null-argument assertions if
    ///    the constructor adds validation or if test project allows passing nulls for negative tests.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ForwardFlowConflictResolver_NullArguments_ArgumentNullExceptionIfValidated()
    {
        // Arrange/Act/Assert
        // Intentionally left as a guide: if constructor adds explicit null checks in the future,
        // create tests like:
        // () => new ForwardFlowConflictResolver(null, ..., ...).Should().Throw<ArgumentNullException>();
        // For now, mark as inconclusive to avoid violating non-nullable annotations and repository guidelines.
        Assert.Inconclusive("Constructor null-argument validation behavior is not exposed; add explicit null-check tests if/when implemented.");
    }
}
