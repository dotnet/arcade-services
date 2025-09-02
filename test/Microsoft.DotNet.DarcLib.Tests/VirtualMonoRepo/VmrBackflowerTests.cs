// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Maestro;
using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class VmrBackFlowerTests
{
    /// <summary>
    /// Ensures the constructor assigns dependencies without invoking them and produces a valid instance.
    /// Inputs:
    ///  - All required dependencies provided as strict mocks to detect any unintended interactions.
    /// Expected:
    ///  - Construction succeeds without throwing.
    ///  - No dependency method/property is called during construction (verified via Strict mocks and VerifyNoOtherCalls).
    ///  - The created instance is of type VmrBackFlower and assignable to VmrCodeFlower.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithValidDependencies_CreatesInstanceWithoutInvokingDependencies()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var vmrCloneManager = new Mock<IVmrCloneManager>(MockBehavior.Strict);
        var repositoryCloneManager = new Mock<IRepositoryCloneManager>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var vmrPatchHandler = new Mock<IVmrPatchHandler>(MockBehavior.Strict);
        var workBranchFactory = new Mock<IWorkBranchFactory>(MockBehavior.Strict);
        var conflictResolver = new Mock<IBackflowConflictResolver>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Strict);

        // Act
        var sut = new VmrBackFlower(
            vmrInfo.Object,
            sourceManifest.Object,
            dependencyTracker.Object,
            vmrCloneManager.Object,
            repositoryCloneManager.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            versionDetailsParser.Object,
            vmrPatchHandler.Object,
            workBranchFactory.Object,
            conflictResolver.Object,
            fileSystem.Object,
            barClient.Object,
            logger.Object);

        // Assert
        sut.Should().NotBeNull();
        sut.Should().BeOfType<VmrBackFlower>();
        sut.Should().BeAssignableTo<VmrCodeFlower>();

        vmrInfo.VerifyNoOtherCalls();
        sourceManifest.VerifyNoOtherCalls();
        dependencyTracker.VerifyNoOtherCalls();
        vmrCloneManager.VerifyNoOtherCalls();
        repositoryCloneManager.VerifyNoOtherCalls();
        localGitClient.VerifyNoOtherCalls();
        localGitRepoFactory.VerifyNoOtherCalls();
        versionDetailsParser.VerifyNoOtherCalls();
        vmrPatchHandler.VerifyNoOtherCalls();
        workBranchFactory.VerifyNoOtherCalls();
        conflictResolver.VerifyNoOtherCalls();
        fileSystem.VerifyNoOtherCalls();
        barClient.VerifyNoOtherCalls();
        logger.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Validates HadUpdates and argument flow for both directions and excludedAssets null/non-null.
    /// Inputs:
    ///  - lastFlowIsBackflow chooses SameDirection vs. OppositeDirection path.
    ///  - simulatedHasChanges controls FlowCodeAsync outcome via overrides.
    ///  - dependencyUpdatesCount controls mergeResult.DependencyUpdates.Count.
    ///  - excludedAssetsIsNull controls whether excludedAssets is null or a non-empty set.
    /// Expected:
    ///  - HadUpdates == simulatedHasChanges || dependencyUpdatesCount > 0.
    ///  - Conflict resolver called once with Backflow having VmrSha == build.Commit and RepoSha == lastFlows.LastFlow.RepoSha.
    ///  - Correct directional override invoked exactly once.
    ///  - ConflictedFiles and DependencyUpdates are propagated to the result.
    /// </summary>
    [TestCase(true, false, 0, true, true, TestName = "FlowBackAsync_SameDirection_NoChanges_NoUpdates_HadUpdatesFalse_ExcludedAssetsNull_HeadExisted")]
    [TestCase(true, false, 2, false, false, TestName = "FlowBackAsync_SameDirection_NoChanges_WithUpdates_HadUpdatesTrue_ExcludedAssetsSet_HeadDidNotExist")]
    [TestCase(false, true, 0, true, false, TestName = "FlowBackAsync_OppositeDirection_WithChanges_NoUpdates_HadUpdatesTrue_ExcludedAssetsNull_HeadDidNotExist")]
    [TestCase(false, false, 0, false, true, TestName = "FlowBackAsync_OppositeDirection_NoChanges_NoUpdates_HadUpdatesFalse_ExcludedAssetsSet_HeadExisted")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task FlowBackAsync_ComposesResultAndInvokesResolverCorrectly(
        bool lastFlowIsBackflow,
        bool simulatedHasChanges,
        int dependencyUpdatesCount,
        bool excludedAssetsIsNull,
        bool headBranchExisted)
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var vmrCloneManager = new Mock<IVmrCloneManager>(MockBehavior.Strict);
        var repositoryCloneManager = new Mock<IRepositoryCloneManager>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var vmrPatchHandler = new Mock<IVmrPatchHandler>(MockBehavior.Strict);
        var workBranchFactory = new Mock<IWorkBranchFactory>(MockBehavior.Strict);
        var conflictResolver = new Mock<IBackflowConflictResolver>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Loose);

        var testFlower = new TestableVmrBackFlower(
            vmrInfo.Object,
            sourceManifest.Object,
            dependencyTracker.Object,
            vmrCloneManager.Object,
            repositoryCloneManager.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            versionDetailsParser.Object,
            vmrPatchHandler.Object,
            workBranchFactory.Object,
            conflictResolver.Object,
            fileSystem.Object,
            barClient.Object,
            logger.Object)
        {
            SimulatedHasChanges = simulatedHasChanges
        };

        var mapping = new SourceMapping(
            Name: "repoX",
            DefaultRemote: "https://example.com/repoX",
            DefaultRef: "main",
            Include: new[] { "*" },
            Exclude: Array.Empty<string>(),
            DisableSynchronization: false,
            Version: null);

        // Create lastFlow with controlled RepoSha and Name
        var lastFlowRepoSha = "last-flow-repo-sha";
        Codeflow lastFlow = lastFlowIsBackflow
            ? new Backflow("some-prev-vmr-sha", lastFlowRepoSha)
            : new ForwardFlow(lastFlowRepoSha, "some-prev-vmr-sha");

        // LastFlows requires also LastForwardFlow (arbitrary valid)
        var lastForwardFlow = new ForwardFlow("ff-repo-sha", "ff-vmr-sha");
        var lastFlows = new LastFlows(lastFlow, LastBackFlow: null, LastForwardFlow: lastForwardFlow, CrossingFlow: null);

        var targetRepo = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        // Path is used to populate result; not asserted here. Let Moq return default.

        var build = new Build(
            id: 123,
            dateProduced: DateTimeOffset.UtcNow,
            staleness: 0,
            released: false,
            stable: false,
            commit: "build-commit-sha",
            channels: new List<Channel>(),
            assets: new List<Asset>(),
            dependencies: new List<BuildRef>(),
            incoherencies: new List<BuildIncoherence>());

        var excludedAssets = excludedAssetsIsNull ? null : (IReadOnlyCollection<string>)new[] { "assetA", "assetB" };
        var targetBranch = "target/branch";
        var headBranch = "head/branch";
        var token = new CancellationTokenSource().Token;

        var conflictedFiles = new List<UnixPath>(); // keep empty; type available via using Microsoft.DotNet.DarcLib.Helpers
        var dependencyUpdates = Enumerable.Range(0, dependencyUpdatesCount).Select(_ => new DependencyUpdate()).ToList();

        SourceMapping? receivedMapping = null;
        LastFlows? receivedLastFlows = null;
        Backflow? receivedCurrentFlow = null;
        ILocalGitRepo? receivedRepo = null;
        Build? receivedBuild = null;
        string? receivedHeadBranch = null;
        string? receivedTargetBranch = null;
        IReadOnlyCollection<string> receivedExcludedAssets = null;
        bool receivedHeadBranchExisted = false;
        CancellationToken receivedToken = default;

        conflictResolver
            .Setup(m => m.TryMergingBranchAndUpdateDependencies(
                It.IsAny<SourceMapping>(),
                It.IsAny<LastFlows>(),
                It.IsAny<Backflow>(),
                It.IsAny<ILocalGitRepo>(),
                It.IsAny<Build>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback<SourceMapping, LastFlows, Backflow, ILocalGitRepo, Build, string, string, IReadOnlyCollection<string>, bool, CancellationToken>(
                (m, lf, cf, r, b, hb, tb, ea, hbe, ct) =>
                {
                    receivedMapping = m;
                    receivedLastFlows = lf;
                    receivedCurrentFlow = cf;
                    receivedRepo = r;
                    receivedBuild = b;
                    receivedHeadBranch = hb;
                    receivedTargetBranch = tb;
                    receivedExcludedAssets = ea;
                    receivedHeadBranchExisted = hbe;
                    receivedToken = ct;
                })
            .ReturnsAsync(new VersionFileUpdateResult(conflictedFiles, dependencyUpdates));

        // Act
        var result = await testFlower.InvokeFlowBackAsync(
            mapping,
            targetRepo.Object,
            lastFlows,
            build,
            excludedAssets,
            targetBranch,
            headBranch,
            headBranchExisted,
            token);

        // Assert
        var expectedHadUpdates = simulatedHasChanges || dependencyUpdatesCount > 0;

        result.HadUpdates.Should().Be(expectedHadUpdates);
        result.ConflictedFiles.Should().BeSameAs(conflictedFiles);
        result.DependencyUpdates.Should().BeSameAs(dependencyUpdates);

        receivedMapping.Should().BeSameAs(mapping);
        receivedLastFlows.Should().BeSameAs(lastFlows);
        receivedRepo.Should().BeSameAs(targetRepo.Object);
        receivedBuild.Should().BeSameAs(build);
        receivedHeadBranch.Should().Be(headBranch);
        receivedTargetBranch.Should().Be(targetBranch);
        receivedExcludedAssets.Should().BeSameAs(excludedAssets);
        receivedHeadBranchExisted.Should().Be(headBranchExisted);
        receivedToken.Should().Be(token);

        receivedCurrentFlow.Should().NotBeNull();
        receivedCurrentFlow!.VmrSha.Should().Be(build.Commit);
        receivedCurrentFlow.RepoSha.Should().Be(lastFlowRepoSha);

        if (lastFlowIsBackflow)
        {
            testFlower.SameDirectionCallCount.Should().Be(1);
            testFlower.OppositeDirectionCallCount.Should().Be(0);
        }
        else
        {
            testFlower.SameDirectionCallCount.Should().Be(0);
            testFlower.OppositeDirectionCallCount.Should().Be(1);
        }

        conflictResolver.Verify(m => m.TryMergingBranchAndUpdateDependencies(
            It.IsAny<SourceMapping>(),
            It.IsAny<LastFlows>(),
            It.IsAny<Backflow>(),
            It.IsAny<ILocalGitRepo>(),
            It.IsAny<Build>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class TestableVmrBackFlower : VmrBackFlower
    {
        public bool SimulatedHasChanges { get; set; }
        public int SameDirectionCallCount { get; private set; }
        public int OppositeDirectionCallCount { get; private set; }

        public TestableVmrBackFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            IVmrDependencyTracker dependencyTracker,
            IVmrCloneManager vmrCloneManager,
            IRepositoryCloneManager repositoryCloneManager,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IVmrPatchHandler vmrPatchHandler,
            IWorkBranchFactory workBranchFactory,
            IBackflowConflictResolver versionFileConflictResolver,
            IFileSystem fileSystem,
            IBasicBarClient barClient,
            ILogger<VmrCodeFlower> logger)
            : base(vmrInfo, sourceManifest, dependencyTracker, vmrCloneManager, repositoryCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, vmrPatchHandler, workBranchFactory, versionFileConflictResolver, fileSystem, barClient, logger)
        {
        }

        public Task<CodeFlowResult> InvokeFlowBackAsync(
            SourceMapping mapping,
            ILocalGitRepo targetRepo,
            LastFlows lastFlows,
            Build build,
            IReadOnlyCollection<string> excludedAssets,
            string targetBranch,
            string headBranch,
            bool headBranchExisted,
            CancellationToken cancellationToken)
        {
            return base.FlowBackAsync(mapping, targetRepo, lastFlows, build, excludedAssets, targetBranch, headBranch, headBranchExisted, cancellationToken);
        }

        protected override Task<bool> SameDirectionFlowAsync(
            SourceMapping mapping,
            LastFlows lastFlows,
            Codeflow currentFlow,
            ILocalGitRepo targetRepo,
            Build build,
            IReadOnlyCollection<string> excludedAssets,
            string targetBranch,
            string headBranch,
            bool headBranchExisted,
            CancellationToken cancellationToken)
        {
            SameDirectionCallCount++;
            return Task.FromResult(SimulatedHasChanges);
        }

        protected override Task<bool> OppositeDirectionFlowAsync(
            SourceMapping mapping,
            LastFlows lastFlows,
            Codeflow currentFlow,
            ILocalGitRepo targetRepo,
            Build build,
            string targetBranch,
            string headBranch,
            bool headBranchExisted,
            CancellationToken cancellationToken)
        {
            OppositeDirectionCallCount++;
            return Task.FromResult(SimulatedHasChanges);
        }

        protected override Task<Codeflow> DetectCrossingFlow(
            Codeflow lastFlow,
            Backflow lastBackFlow,
            ForwardFlow lastForwardFlow,
            ILocalGitRepo repo)
        {
            // Not used by FlowCodeAsync in these tests.
            return Task.FromResult<Codeflow>(lastFlow);
        }
    }

    /// <summary>
    /// Verifies that when lastFlow is a ForwardFlow and a previous Backflow exists,
    /// DetectCrossingFlow delegates to ILocalGitRepo.IsAncestorCommit with (ff.RepoSha, lastBackFlow.RepoSha)
    /// and returns lastForwardFlow only when IsAncestorCommit returns true.
    /// Inputs:
    ///  - lastFlow: ForwardFlow with RepoSha = repoSha.
    ///  - lastBackFlow: Backflow with RepoSha = backflowRepoSha.
    ///  - repo.IsAncestorCommit(ancestor: repoSha, descendant: backflowRepoSha) returns ancestorResult.
    /// Expected:
    ///  - Returns lastForwardFlow when ancestorResult == true; otherwise returns null.
    ///  - ILocalGitRepo.IsAncestorCommit is invoked exactly once with the expected arguments.
    /// </summary>
    [TestCase(true, "repo-123", "repo-789")]
    [TestCase(false, "abc123", "def456")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task DetectCrossingFlow_ForwardFlowWithBackflow_ReturnsForwardFlowOnlyWhenAncestorTrue(
        bool ancestorResult, string repoSha, string backflowRepoSha)
    {
        // Arrange
        var sut = CreateSut();

        var lastFlow = new ForwardFlow(repoSha, "vmr-sha");
        var lastBackFlow = new Backflow("vmr-back", backflowRepoSha);
        var lastForwardFlow = new ForwardFlow("fwd-repo", "fwd-vmr");

        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        repoMock
            .Setup(r => r.IsAncestorCommit(repoSha, backflowRepoSha))
            .ReturnsAsync(ancestorResult);

        // Act
        var result = await sut.CallDetectCrossingFlowAsync(lastFlow, lastBackFlow, lastForwardFlow, repoMock.Object);

        // Assert
        if (ancestorResult)
        {
            result.Should().BeSameAs(lastForwardFlow);
        }
        else
        {
            result.Should().BeNull();
        }

        repoMock.Verify(r => r.IsAncestorCommit(repoSha, backflowRepoSha), Times.Once);
    }

    /// <summary>
    /// Ensures that when lastFlow is not a ForwardFlow or when lastBackFlow is null,
    /// DetectCrossingFlow returns null and does not call ILocalGitRepo.IsAncestorCommit.
    /// Inputs:
    ///  - useForwardFlow: False -> lastFlow is Backflow (non-forward); True with hasLastBackFlow False -> lastBackFlow is null.
    ///  - hasLastBackFlow: When False -> lastBackFlow is null; otherwise a Backflow instance.
    /// Expected:
    ///  - Returns null in all cases.
    ///  - ILocalGitRepo.IsAncestorCommit is never invoked.
    /// </summary>
    [TestCase(false, true, TestName = "DetectCrossingFlow_NonForwardFlow_ReturnsNullWithoutGitCall")]
    [TestCase(true, false, TestName = "DetectCrossingFlow_MissingBackflow_ReturnsNullWithoutGitCall")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task DetectCrossingFlow_NonForwardOrMissingBackflow_ReturnsNullWithoutCallingGit(
        bool useForwardFlow, bool hasLastBackFlow)
    {
        // Arrange
        var sut = CreateSut();

        Codeflow lastFlow = useForwardFlow
            ? new ForwardFlow("repo-ff", "vmr-ff")
            : new Backflow("vmr-bf", "repo-bf");

        Backflow lastBackFlow = hasLastBackFlow
            ? new Backflow("vmr-back", "repo-back")
            : null;

        var lastForwardFlow = new ForwardFlow("repo-last", "vmr-last");

        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        // Protect against accidental invocation
        repoMock
            .Setup(r => r.IsAncestorCommit(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("IsAncestorCommit should not be called"));

        // Act
        var result = await sut.CallDetectCrossingFlowAsync(lastFlow, lastBackFlow, lastForwardFlow, repoMock.Object);

        // Assert
        result.Should().BeNull();
        repoMock.Verify(r => r.IsAncestorCommit(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    private static TestableVmrBackFlower CreateSut()
    {
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var sourceManifest = new Mock<Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo.ISourceManifest>(MockBehavior.Strict);
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var vmrCloneManager = new Mock<IVmrCloneManager>(MockBehavior.Strict);
        var repositoryCloneManager = new Mock<IRepositoryCloneManager>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var vmrPatchHandler = new Mock<IVmrPatchHandler>(MockBehavior.Strict);
        var workBranchFactory = new Mock<IWorkBranchFactory>(MockBehavior.Strict);
        var conflictResolver = new Mock<IBackflowConflictResolver>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Strict);

        return new TestableVmrBackFlower(
            vmrInfo.Object,
            sourceManifest.Object,
            dependencyTracker.Object,
            vmrCloneManager.Object,
            repositoryCloneManager.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            versionDetailsParser.Object,
            vmrPatchHandler.Object,
            workBranchFactory.Object,
            conflictResolver.Object,
            fileSystem.Object,
            barClient.Object,
            logger.Object);
    }

    /// <summary>
    /// Verifies that GetEngCommonPath appends "src/arcade/eng/common" to the provided source repository path
    /// and normalizes directory separators according to the current platform.
    /// Inputs:
    ///  - sourceRepoInput: Variations of base paths (no trailing separator, with trailing separator, nested path).
    /// Expected:
    ///  - Returned path equals Path.Combine(sourceRepoInputNormalized, "src", "arcade", "eng", "common").
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(SourceRepoInputs))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetEngCommonPath_AppendsArcadeEngCommonToSourceRepo(string sourceRepoInput)
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Loose);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Loose);
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Loose);
        var vmrCloneManager = new Mock<IVmrCloneManager>(MockBehavior.Loose);
        var repositoryCloneManager = new Mock<IRepositoryCloneManager>(MockBehavior.Loose);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Loose);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Loose);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var vmrPatchHandler = new Mock<IVmrPatchHandler>(MockBehavior.Loose);
        var workBranchFactory = new Mock<IWorkBranchFactory>(MockBehavior.Loose);
        var conflictResolver = new Mock<IBackflowConflictResolver>(MockBehavior.Loose);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Loose);
        var logger = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Loose);

        var sut = new ExposedVmrBackFlower(
            vmrInfo.Object,
            sourceManifest.Object,
            dependencyTracker.Object,
            vmrCloneManager.Object,
            repositoryCloneManager.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            versionDetailsParser.Object,
            vmrPatchHandler.Object,
            workBranchFactory.Object,
            conflictResolver.Object,
            fileSystem.Object,
            barClient.Object,
            logger.Object);

        var sourceRepo = new NativePath(sourceRepoInput);
        var expected = System.IO.Path.Combine(sourceRepo.ToString(), "src", "arcade", "eng", "common");

        // Act
        var result = sut.InvokeGetEngCommonPath(sourceRepo);

        // Assert
        result.ToString().Should().Be(expected);
    }

    private static IEnumerable SourceRepoInputs()
    {
        var sep = System.IO.Path.DirectorySeparatorChar.ToString();
        yield return "repo";
        yield return "repo" + sep;
        yield return "root/subdir";
    }

    private sealed class ExposedVmrBackFlower : VmrBackFlower
    {
        public ExposedVmrBackFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            IVmrDependencyTracker dependencyTracker,
            IVmrCloneManager vmrCloneManager,
            IRepositoryCloneManager repositoryCloneManager,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IVmrPatchHandler vmrPatchHandler,
            IWorkBranchFactory workBranchFactory,
            IBackflowConflictResolver versionFileConflictResolver,
            IFileSystem fileSystem,
            IBasicBarClient barClient,
            ILogger<VmrCodeFlower> logger)
            : base(vmrInfo, sourceManifest, dependencyTracker, vmrCloneManager, repositoryCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, vmrPatchHandler, workBranchFactory, versionFileConflictResolver, fileSystem, barClient, logger)
        {
        }

        public NativePath InvokeGetEngCommonPath(NativePath sourceRepo) => GetEngCommonPath(sourceRepo);
    }

    /// <summary>
    /// Verifies that TargetRepoIsVmr always returns false.
    /// Inputs:
    ///  - No parameters; method is protected and parameterless.
    /// Expected:
    ///  - The method returns false.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void TargetRepoIsVmr_Always_ReturnsFalse()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict).Object;
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict).Object;
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict).Object;
        var vmrCloneManager = new Mock<IVmrCloneManager>(MockBehavior.Strict).Object;
        var repositoryCloneManager = new Mock<IRepositoryCloneManager>(MockBehavior.Strict).Object;
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict).Object;
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict).Object;
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Strict).Object;
        var vmrPatchHandler = new Mock<IVmrPatchHandler>(MockBehavior.Strict).Object;
        var workBranchFactory = new Mock<IWorkBranchFactory>(MockBehavior.Strict).Object;
        var backflowConflictResolver = new Mock<IBackflowConflictResolver>(MockBehavior.Strict).Object;
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict).Object;
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Strict).Object;

        var sut = new TestVmrBackFlower(
            vmrInfo,
            sourceManifest,
            dependencyTracker,
            vmrCloneManager,
            repositoryCloneManager,
            localGitClient,
            localGitRepoFactory,
            versionDetailsParser,
            vmrPatchHandler,
            workBranchFactory,
            backflowConflictResolver,
            fileSystem,
            barClient,
            logger);

        // Act
        var result = sut.InvokeTargetRepoIsVmr();

        // Assert
        result.Should().BeFalse();
    }

    private sealed class TestVmrBackFlower : VmrBackFlower
    {
        public TestVmrBackFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            IVmrDependencyTracker dependencyTracker,
            IVmrCloneManager vmrCloneManager,
            IRepositoryCloneManager repositoryCloneManager,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IVmrPatchHandler vmrPatchHandler,
            IWorkBranchFactory workBranchFactory,
            IBackflowConflictResolver versionFileConflictResolver,
            IFileSystem fileSystem,
            IBasicBarClient barClient,
            ILogger<VmrCodeFlower> logger)
            : base(
                vmrInfo,
                sourceManifest,
                dependencyTracker,
                vmrCloneManager,
                repositoryCloneManager,
                localGitClient,
                localGitRepoFactory,
                versionDetailsParser,
                vmrPatchHandler,
                workBranchFactory,
                versionFileConflictResolver,
                fileSystem,
                barClient,
                logger)
        {
        }

        public bool InvokeTargetRepoIsVmr() => base.TargetRepoIsVmr();
    }

    /// <summary>
    /// Verifies that the protected virtual property ShouldResetVmr returns the expected value:
    /// - false for the base VmrBackFlower implementation
    /// - true when overridden in a derived class
    /// </summary>
    /// <param name="useOverride">If true, uses a derived class that overrides ShouldResetVmr to return true; otherwise, uses the base implementation.</param>
    /// <param name="expected">The expected property value.</param>
    [TestCase(false, false, TestName = "ShouldResetVmr_BaseImplementation_ReturnsFalse")]
    [TestCase(true, true, TestName = "ShouldResetVmr_OverriddenImplementation_ReturnsTrue")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ShouldResetVmr_BaseAndOverride_ReturnsConfiguredValue(bool useOverride, bool expected)
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Loose).Object;
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Loose).Object;
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Loose).Object;
        var vmrCloneManager = new Mock<IVmrCloneManager>(MockBehavior.Loose).Object;
        var repositoryCloneManager = new Mock<IRepositoryCloneManager>(MockBehavior.Loose).Object;
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Loose).Object;
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Loose).Object;
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose).Object;
        var vmrPatchHandler = new Mock<IVmrPatchHandler>(MockBehavior.Loose).Object;
        var workBranchFactory = new Mock<IWorkBranchFactory>(MockBehavior.Loose).Object;
        var conflictResolver = new Mock<IBackflowConflictResolver>(MockBehavior.Loose).Object;
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose).Object;
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Loose).Object;
        var logger = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Loose).Object;

        VmrBackFlowerAccessor instance = useOverride
            ? new VmrBackFlowerOverride(
                vmrInfo,
                sourceManifest,
                dependencyTracker,
                vmrCloneManager,
                repositoryCloneManager,
                localGitClient,
                localGitRepoFactory,
                versionDetailsParser,
                vmrPatchHandler,
                workBranchFactory,
                conflictResolver,
                fileSystem,
                barClient,
                logger)
            : new VmrBackFlowerAccessor(
                vmrInfo,
                sourceManifest,
                dependencyTracker,
                vmrCloneManager,
                repositoryCloneManager,
                localGitClient,
                localGitRepoFactory,
                versionDetailsParser,
                vmrPatchHandler,
                workBranchFactory,
                conflictResolver,
                fileSystem,
                barClient,
                logger);

        // Act
        var actual = instance.ExposedShouldResetVmr;

        // Assert
        actual.Should().Be(expected);
    }

    private class VmrBackFlowerAccessor : VmrBackFlower
    {
        public VmrBackFlowerAccessor(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            IVmrDependencyTracker dependencyTracker,
            IVmrCloneManager vmrCloneManager,
            IRepositoryCloneManager repositoryCloneManager,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IVmrPatchHandler vmrPatchHandler,
            IWorkBranchFactory workBranchFactory,
            IBackflowConflictResolver versionFileConflictResolver,
            IFileSystem fileSystem,
            IBasicBarClient barClient,
            ILogger<VmrCodeFlower> logger)
            : base(
                  vmrInfo,
                  sourceManifest,
                  dependencyTracker,
                  vmrCloneManager,
                  repositoryCloneManager,
                  localGitClient,
                  localGitRepoFactory,
                  versionDetailsParser,
                  vmrPatchHandler,
                  workBranchFactory,
                  versionFileConflictResolver,
                  fileSystem,
                  barClient,
                  logger)
        {
        }

        public bool ExposedShouldResetVmr => ShouldResetVmr;
    }

    private sealed class VmrBackFlowerOverride : VmrBackFlowerAccessor
    {
        public VmrBackFlowerOverride(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            IVmrDependencyTracker dependencyTracker,
            IVmrCloneManager vmrCloneManager,
            IRepositoryCloneManager repositoryCloneManager,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IVmrPatchHandler vmrPatchHandler,
            IWorkBranchFactory workBranchFactory,
            IBackflowConflictResolver versionFileConflictResolver,
            IFileSystem fileSystem,
            IBasicBarClient barClient,
            ILogger<VmrCodeFlower> logger)
            : base(
                  vmrInfo,
                  sourceManifest,
                  dependencyTracker,
                  vmrCloneManager,
                  repositoryCloneManager,
                  localGitClient,
                  localGitRepoFactory,
                  versionDetailsParser,
                  vmrPatchHandler,
                  workBranchFactory,
                  versionFileConflictResolver,
                  fileSystem,
                  barClient,
                  logger)
        {
        }

        protected override bool ShouldResetVmr => true;
    }
}
