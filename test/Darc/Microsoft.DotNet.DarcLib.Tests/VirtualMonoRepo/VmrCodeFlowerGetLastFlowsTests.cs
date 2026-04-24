// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrCodeFlowerGetLastFlowsTests
{
    private const string MappingName = "test-repo";
    private const string RepoUri = "https://github.com/dotnet/test-repo";

    private const string LastForwardRepoSha = "forward-repo-sha";
    private const string LastForwardVmrSha = "forward-vmr-sha";
    private const string LastBackflowRepoSha = "backflow-repo-sha";
    private const string LastBackflowVmrSha = "backflow-vmr-sha";

    private readonly NativePath _vmrPath = new("/data/vmr");
    private readonly NativePath _repoPath = new("/data/repo");

    private Mock<IVmrInfo> _vmrInfo = null!;
    private Mock<ISourceManifest> _sourceManifest = null!;
    private Mock<IVmrDependencyTracker> _dependencyTracker = null!;
    private Mock<ILocalGitClient> _localGitClient = null!;
    private Mock<ILocalGitRepoFactory> _localGitRepoFactory = null!;
    private Mock<IVersionDetailsParser> _versionDetailsParser = null!;
    private Mock<IFileSystem> _fileSystem = null!;
    private Mock<ILocalGitRepo> _repoClone = null!;
    private Mock<ILocalGitRepo> _vmrRepo = null!;
    private Mock<ISourceComponent> _repoInVmr = null!;

    private TestVmrCodeFlower _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _vmrInfo = new Mock<IVmrInfo>();
        _vmrInfo.SetupGet(x => x.VmrPath).Returns(_vmrPath);
        _vmrInfo.SetupGet(x => x.SourceManifestPath).Returns(_vmrPath / VmrInfo.DefaultRelativeSourceManifestPath);

        _sourceManifest = new Mock<ISourceManifest>();
        _repoInVmr = new Mock<ISourceComponent>();
        _repoInVmr.SetupGet(x => x.CommitSha).Returns(LastForwardRepoSha);
        _sourceManifest.Setup(x => x.GetRepoVersion(MappingName)).Returns(_repoInVmr.Object);

        _dependencyTracker = new Mock<IVmrDependencyTracker>();
        _dependencyTracker
            .Setup(x => x.RefreshMetadataAsync(It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _localGitClient = new Mock<ILocalGitClient>();
        // Last forward VMR sha comes from blaming source-manifest.json in the VMR.
        _localGitClient
            .Setup(x => x.BlameLineAsync(
                (string)_vmrInfo.Object.SourceManifestPath,
                It.IsAny<Func<string, bool>>(),
                It.IsAny<string?>()))
            .ReturnsAsync(LastForwardVmrSha);
        // Last backflow repo sha comes from blaming Version.Details.xml in the repo.
        _localGitClient
            .Setup(x => x.BlameLineAsync(
                (string)(_repoPath / VersionFiles.VersionDetailsXml),
                It.IsAny<Func<string, bool>>(),
                It.IsAny<string?>()))
            .ReturnsAsync(LastBackflowRepoSha);

        _repoClone = new Mock<ILocalGitRepo>();
        _repoClone.SetupGet(x => x.Path).Returns(_repoPath);

        _vmrRepo = new Mock<ILocalGitRepo>();
        _vmrRepo.SetupGet(x => x.Path).Returns(_vmrPath);

        _localGitRepoFactory = new Mock<ILocalGitRepoFactory>();
        _localGitRepoFactory.Setup(x => x.Create(_vmrPath)).Returns(_vmrRepo.Object);

        _versionDetailsParser = new Mock<IVersionDetailsParser>();
        SetBackflowInVersionDetails(LastBackflowVmrSha);

        _fileSystem = new Mock<IFileSystem>();

        _sut = new TestVmrCodeFlower(
            _vmrInfo.Object,
            _sourceManifest.Object,
            _dependencyTracker.Object,
            _localGitClient.Object,
            _localGitRepoFactory.Object,
            _versionDetailsParser.Object,
            _fileSystem.Object);
    }

    [Test]
    public async Task GetLastFlowsAsync_ReturnsForwardFlow_WhenNoBackflowRecorded()
    {
        SetBackflowInVersionDetails(null);

        var result = await _sut.GetLastFlowsAsync(
            MappingName,
            _repoClone.Object,
            currentIsBackflow: false,
            ignoreNonLinearFlow: false,
            headBranchExisted: false);

        result.LastBackFlow.Should().BeNull();
        result.LastForwardFlow.Should().Be(new ForwardFlow(LastForwardRepoSha, LastForwardVmrSha));
        result.LastFlow.Should().Be(result.LastForwardFlow);
        result.CrossingFlow.Should().BeNull();
    }

    [Test]
    public async Task GetLastFlowsAsync_ForwardFlowDirection_ReturnsLastBackflow_WhenBackflowIsNewer()
    {
        // In forward-flow direction we compare the repo SHAs of the two flows.
        // backflow.RepoSha is newer means lastForward.RepoSha is an ancestor of lastBackflow.RepoSha.
        SetupCommitObjects(_repoClone);
        _repoClone
            .Setup(x => x.IsAncestorCommit(LastForwardRepoSha, LastBackflowRepoSha))
            .ReturnsAsync(true);
        _repoClone
            .Setup(x => x.IsAncestorCommit(LastBackflowRepoSha, LastForwardRepoSha))
            .ReturnsAsync(false);

        var result = await _sut.GetLastFlowsAsync(
            MappingName,
            _repoClone.Object,
            currentIsBackflow: false,
            ignoreNonLinearFlow: false,
            headBranchExisted: true);

        result.LastFlow.Should().Be(new Backflow(LastBackflowVmrSha, LastBackflowRepoSha));
        result.LastBackFlow.Should().Be(new Backflow(LastBackflowVmrSha, LastBackflowRepoSha));
        result.LastForwardFlow.Should().Be(new ForwardFlow(LastForwardRepoSha, LastForwardVmrSha));
    }

    [Test]
    public async Task GetLastFlowsAsync_ForwardFlowDirection_ReturnsLastForwardFlow_WhenForwardFlowIsNewer()
    {
        SetupCommitObjects(_repoClone);
        _repoClone
            .Setup(x => x.IsAncestorCommit(LastBackflowRepoSha, LastForwardRepoSha))
            .ReturnsAsync(true);
        _repoClone
            .Setup(x => x.IsAncestorCommit(LastForwardRepoSha, LastBackflowRepoSha))
            .ReturnsAsync(false);

        var result = await _sut.GetLastFlowsAsync(
            MappingName,
            _repoClone.Object,
            currentIsBackflow: false,
            ignoreNonLinearFlow: false,
            headBranchExisted: false);

        result.LastFlow.Should().Be(new ForwardFlow(LastForwardRepoSha, LastForwardVmrSha));
    }

    [Test]
    public async Task GetLastFlowsAsync_ForwardFlowDirection_IgnoresBackflow_WhenItCameFromDifferentBranch()
    {
        // Simulate a preview-branch scenario: lastBackflow is newer (forward is older),
        // but the current VMR HEAD is not a descendant of lastBackflow.VmrSha, meaning the
        // backflow came from a different branch and should be ignored.
        const string currentVmrSha = "current-vmr-head-sha";
        SetupCommitObjects(_repoClone);
        _repoClone
            .Setup(x => x.IsAncestorCommit(LastForwardRepoSha, LastBackflowRepoSha))
            .ReturnsAsync(true);
        _repoClone
            .Setup(x => x.IsAncestorCommit(LastBackflowRepoSha, LastForwardRepoSha))
            .ReturnsAsync(false);

        _vmrRepo
            .Setup(x => x.GetShaForRefAsync(null))
            .ReturnsAsync(currentVmrSha);
        _vmrRepo
            .Setup(x => x.IsAncestorCommit(LastBackflowVmrSha, currentVmrSha))
            .ReturnsAsync(false);

        var result = await _sut.GetLastFlowsAsync(
            MappingName,
            _repoClone.Object,
            currentIsBackflow: false,
            ignoreNonLinearFlow: false,
            headBranchExisted: false);

        result.LastFlow.Should().Be(new ForwardFlow(LastForwardRepoSha, LastForwardVmrSha));
        result.LastBackFlow.Should().Be(new Backflow(LastBackflowVmrSha, LastBackflowRepoSha));
    }

    [Test]
    public async Task GetLastFlowsAsync_BackflowDirection_ReturnsLastForwardFlow_WhenForwardFlowIsNewer()
    {
        // In backflow direction we compare VMR SHAs and source repo is the VMR.
        SetupCommitObjects(_vmrRepo);
        _vmrRepo
            .Setup(x => x.IsAncestorCommit(LastBackflowVmrSha, LastForwardVmrSha))
            .ReturnsAsync(true);
        _vmrRepo
            .Setup(x => x.IsAncestorCommit(LastForwardVmrSha, LastBackflowVmrSha))
            .ReturnsAsync(false);

        var result = await _sut.GetLastFlowsAsync(
            MappingName,
            _repoClone.Object,
            currentIsBackflow: true,
            ignoreNonLinearFlow: false,
            headBranchExisted: true);

        result.LastFlow.Should().Be(new ForwardFlow(LastForwardRepoSha, LastForwardVmrSha));
    }

    [Test]
    public void GetLastFlowsAsync_ThrowsNonLinearCodeflowException_WhenHistoryIsNotLinear()
    {
        SetupCommitObjects(_repoClone);
        // Neither commit is an ancestor of the other.
        _repoClone
            .Setup(x => x.IsAncestorCommit(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        Func<Task> act = () => _sut.GetLastFlowsAsync(
            MappingName,
            _repoClone.Object,
            currentIsBackflow: false,
            ignoreNonLinearFlow: false,
            headBranchExisted: false);

        act.Should().ThrowAsync<NonLinearCodeflowException>();
    }

    [Test]
    public async Task GetLastFlowsAsync_ReturnsOppositeDirectionFlow_WhenHistoryIsNotLinearAndIgnoreFlagSet()
    {
        SetupCommitObjects(_repoClone);
        _repoClone
            .Setup(x => x.IsAncestorCommit(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var result = await _sut.GetLastFlowsAsync(
            MappingName,
            _repoClone.Object,
            currentIsBackflow: false,
            ignoreNonLinearFlow: true,
            headBranchExisted: false);

        // In forward-flow direction, the same-direction flow (lastForwardFlow) is returned so that
        // the upcoming flow is handled as a same-direction flow.
        result.LastFlow.Should().Be(new ForwardFlow(LastForwardRepoSha, LastForwardVmrSha));
        result.CrossingFlow.Should().BeNull();
    }

    [Test]
    public void GetLastFlowsAsync_ThrowsInvalidSynchronization_WhenShaIsNotACommit()
    {
        _repoClone
            .Setup(x => x.GetObjectTypeAsync(LastBackflowRepoSha))
            .ReturnsAsync(GitObjectType.Tree);
        _repoClone
            .Setup(x => x.GetObjectTypeAsync(LastForwardRepoSha))
            .ReturnsAsync(GitObjectType.Commit);

        Func<Task> act = () => _sut.GetLastFlowsAsync(
            MappingName,
            _repoClone.Object,
            currentIsBackflow: false,
            ignoreNonLinearFlow: false,
            headBranchExisted: false);

        act.Should().ThrowAsync<InvalidSynchronizationException>();
    }

    [Test]
    public async Task GetLastFlowsAsync_InflowCase_ReturnsLastForwardFlow_InForwardFlowDirection()
    {
        // An "inflow" commit: backflow and forward flow target the same repo SHA.
        const string sharedRepoSha = "shared-repo-sha";
        _repoInVmr.SetupGet(x => x.CommitSha).Returns(sharedRepoSha);
        _localGitClient
            .Setup(x => x.BlameLineAsync(
                (string)(_repoPath / VersionFiles.VersionDetailsXml),
                It.IsAny<Func<string, bool>>(),
                It.IsAny<string?>()))
            .ReturnsAsync(sharedRepoSha);

        _repoClone
            .Setup(x => x.GetObjectTypeAsync(sharedRepoSha))
            .ReturnsAsync(GitObjectType.Commit);

        var result = await _sut.GetLastFlowsAsync(
            MappingName,
            _repoClone.Object,
            currentIsBackflow: false,
            ignoreNonLinearFlow: false,
            headBranchExisted: false);

        result.LastFlow.Should().Be(new ForwardFlow(sharedRepoSha, LastForwardVmrSha));
        result.LastBackFlow.Should().Be(new Backflow(LastBackflowVmrSha, sharedRepoSha));
    }

    private void SetBackflowInVersionDetails(string? vmrSha)
    {
        var source = vmrSha is null ? null : new SourceDependency(RepoUri, MappingName, vmrSha, BarId: null);
        _versionDetailsParser
            .Setup(x => x.ParseVersionDetailsFile(_repoPath / VersionFiles.VersionDetailsXml, It.IsAny<bool>()))
            .Returns(new VersionDetails(Array.Empty<DependencyDetail>(), source));
    }

    private void SetupCommitObjects(Mock<ILocalGitRepo> repo)
    {
        repo.Setup(x => x.GetObjectTypeAsync(It.IsAny<string>())).ReturnsAsync(GitObjectType.Commit);
    }

    /// <summary>
    /// Test double that exposes <see cref="VmrCodeFlower.GetLastFlowsAsync"/> without requiring the
    /// full flow-execution machinery. Abstract members that aren't exercised by the tested method
    /// throw so that any accidental call is caught loudly.
    /// </summary>
    private sealed class TestVmrCodeFlower : VmrCodeFlower
    {
        public TestVmrCodeFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            IVmrDependencyTracker dependencyTracker,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IFileSystem fileSystem)
            : base(
                vmrInfo,
                sourceManifest,
                dependencyTracker,
                localGitClient,
                localGitRepoFactory,
                versionDetailsParser,
                fileSystem,
                NullLogger<VmrCodeFlower>.Instance)
        {
        }

        protected override Task<CodeFlowResult> SameDirectionFlowAsync(
            CodeflowOptions codeflowOptions,
            LastFlows lastFlows,
            ILocalGitRepo repo,
            bool headBranchExisted,
            CancellationToken cancellationToken) => throw new NotImplementedException();

        protected override Task<CodeFlowResult> OppositeDirectionFlowAsync(
            CodeflowOptions codeflowOptions,
            LastFlows lastFlows,
            ILocalGitRepo sourceRepo,
            bool headBranchExisted,
            CancellationToken cancellationToken) => throw new NotImplementedException();

        protected override Task<Codeflow?> DetectCrossingFlow(
            Codeflow lastFlow,
            Backflow? lastBackFlow,
            ForwardFlow lastForwardFlow,
            ILocalGitRepo repo) => Task.FromResult<Codeflow?>(null);

        protected override Task<(Codeflow, LastFlows)> UnwindPreviousFlowAsync(
            SourceMapping mapping,
            ILocalGitRepo targetRepo,
            LastFlows previousFlows,
            string branchToCreate,
            string targetBranch,
            bool unsafeFlow,
            CancellationToken cancellationToken) => throw new NotImplementedException();

        protected override Task EnsureCodeflowLinearityAsync(
            ILocalGitRepo repo,
            Codeflow currentFlow,
            LastFlows lastFlows) => Task.CompletedTask;

        protected override NativePath GetEngCommonPath(NativePath sourceRepo) => sourceRepo / "eng" / "common";

        protected override bool TargetRepoIsVmr() => false;
    }
}
