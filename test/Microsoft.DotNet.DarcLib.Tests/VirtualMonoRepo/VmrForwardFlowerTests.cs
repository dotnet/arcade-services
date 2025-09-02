using LibGit2Sharp;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class VmrForwardFlowerTests
{
    /// <summary>
    /// Ensures the constructor accepts all required dependencies and does not interact with them during construction.
    /// Inputs:
    ///  - Strict mocks for all constructor dependencies (to detect any unintended calls).
    /// Expected:
    ///  - Instance is created successfully (non-null) and no mock interactions occurred.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Ctor_WithAllDependencies_DoesNotInvokeDependencies()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var vmrUpdater = new Mock<ICodeFlowVmrUpdater>(MockBehavior.Strict);
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var vmrCloneManager = new Mock<IVmrCloneManager>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var codeflowChangeAnalyzer = new Mock<ICodeflowChangeAnalyzer>(MockBehavior.Strict);
        var conflictResolver = new Mock<IForwardFlowConflictResolver>(MockBehavior.Strict);
        var workBranchFactory = new Mock<IWorkBranchFactory>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Strict);

        // Act
        var instance = new VmrForwardFlower(
            vmrInfo.Object,
            sourceManifest.Object,
            vmrUpdater.Object,
            dependencyTracker.Object,
            vmrCloneManager.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            versionDetailsParser.Object,
            codeflowChangeAnalyzer.Object,
            conflictResolver.Object,
            workBranchFactory.Object,
            processManager.Object,
            barClient.Object,
            fileSystem.Object,
            logger.Object);

        // Assert
        instance.Should().NotBeNull();

        vmrInfo.VerifyNoOtherCalls();
        sourceManifest.VerifyNoOtherCalls();
        vmrUpdater.VerifyNoOtherCalls();
        dependencyTracker.VerifyNoOtherCalls();
        vmrCloneManager.VerifyNoOtherCalls();
        localGitClient.VerifyNoOtherCalls();
        localGitRepoFactory.VerifyNoOtherCalls();
        versionDetailsParser.VerifyNoOtherCalls();
        codeflowChangeAnalyzer.VerifyNoOtherCalls();
        conflictResolver.VerifyNoOtherCalls();
        workBranchFactory.VerifyNoOtherCalls();
        processManager.VerifyNoOtherCalls();
        barClient.VerifyNoOtherCalls();
        fileSystem.VerifyNoOtherCalls();
        logger.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that SameDirectionFlowAsync propagates the updater's boolean result
    /// and forwards the ShouldResetClones flag to ICodeFlowVmrUpdater.UpdateRepository.
    /// Inputs:
    ///  - updaterResult: whether the updater reports changes.
    ///  - shouldResetClones: value returned by overridden ShouldResetClones.
    /// Expected:
    ///  - Method returns updaterResult.
    ///  - UpdateRepository is invoked once with resetToRemoteWhenCloningRepo == shouldResetClones.
    /// </summary>
    [TestCase(true, false)]
    [TestCase(false, true)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task SameDirectionFlowAsync_UpdaterOutcomePropagated_AndResetFlagForwarded(bool updaterResult, bool shouldResetClones)
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        var vmrUpdaterMock = new Mock<ICodeFlowVmrUpdater>(MockBehavior.Strict);
        var dependencyTrackerMock = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var cloneManagerMock = new Mock<IVmrCloneManager>(MockBehavior.Strict);
        var localGitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactoryMock = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var versionDetailsParserMock = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var changeAnalyzerMock = new Mock<ICodeflowChangeAnalyzer>(MockBehavior.Strict);
        var conflictResolverMock = new Mock<IForwardFlowConflictResolver>(MockBehavior.Strict);
        var workBranchFactoryMock = new Mock<IWorkBranchFactory>(MockBehavior.Strict);
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Strict);

        var mapping = new SourceMapping("repo", "origin", "main", new List<string>(), new List<string>(), false);
        var lastFlows = new LastFlows(new ForwardFlow("r1", "v1"), null, new ForwardFlow("r1", "v1"), null);
        var currentFlow = new ForwardFlow("r2", "v2");
        var sourceRepoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var build = new Build(1, DateTimeOffset.UtcNow, 0, false, false, "sha-upd", new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());

        vmrUpdaterMock
            .Setup(m => m.UpdateRepository(
                mapping,
                build,
                It.Is<string[]>(a => a != null && a.Length > 0),
                null,
                shouldResetClones,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updaterResult);

        var sut = new TestableVmrForwardFlower(
            vmrInfoMock.Object,
            sourceManifestMock.Object,
            vmrUpdaterMock.Object,
            dependencyTrackerMock.Object,
            cloneManagerMock.Object,
            localGitClientMock.Object,
            localGitRepoFactoryMock.Object,
            versionDetailsParserMock.Object,
            changeAnalyzerMock.Object,
            conflictResolverMock.Object,
            workBranchFactoryMock.Object,
            processManagerMock.Object,
            barClientMock.Object,
            fileSystemMock.Object,
            loggerMock.Object,
            shouldResetClones);

        // Act
        var result = await sut.InvokeSameDirectionFlowAsync(
            mapping,
            lastFlows,
            currentFlow,
            sourceRepoMock.Object,
            build,
            new List<string>(),
            "target/branch",
            "head/branch",
            headBranchExisted: false,
            cancellationToken: CancellationToken.None);

        // Assert
        result.Should().Be(updaterResult);
        vmrUpdaterMock.Verify(m => m.UpdateRepository(
            mapping,
            build,
            It.IsAny<string[]>(),
            null,
            shouldResetClones,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Ensures that when ICodeFlowVmrUpdater.UpdateRepository throws PatchApplicationFailedException
    /// and headBranchExisted is true, the method throws ConflictInPrBranchException with parsed file names.
    /// Inputs:
    ///  - mapping with Name "repo".
    ///  - UpdateRepository throws PatchApplicationFailedException with StandardError containing a merge conflict line.
    /// Expected:
    ///  - ConflictInPrBranchException is thrown.
    ///  - Exception message mentions the target branch.
    ///  - ConflictedFiles contain the normalized filename ("file.txt").
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task SameDirectionFlowAsync_ExistingHeadBranch_ThrowsConflictInPrBranchException()
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        var vmrUpdaterMock = new Mock<ICodeFlowVmrUpdater>(MockBehavior.Strict);
        var dependencyTrackerMock = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var cloneManagerMock = new Mock<IVmrCloneManager>(MockBehavior.Strict);
        var localGitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactoryMock = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var versionDetailsParserMock = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var changeAnalyzerMock = new Mock<ICodeflowChangeAnalyzer>(MockBehavior.Strict);
        var conflictResolverMock = new Mock<IForwardFlowConflictResolver>(MockBehavior.Strict);
        var workBranchFactoryMock = new Mock<IWorkBranchFactory>(MockBehavior.Strict);
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Strict);

        // Allow any logger.Log(...) since LogInformation uses extension methods.
        loggerMock
            .Setup(l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()))
            .Verifiable();

        var mapping = new SourceMapping("repo", "origin", "main", new List<string>(), new List<string>(), false);
        var lastFlows = new LastFlows(new ForwardFlow("r1", "v1"), null, new ForwardFlow("r1", "v1"), null);
        var currentFlow = new ForwardFlow("r2", "v2");
        var sourceRepoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var build = new Build(2, DateTimeOffset.UtcNow, 0, false, false, "sha-conflict", new List<Channel>(), new List<Asset>(), new List<BuildRef>(), new List<BuildIncoherence>());

        var per = new ProcessExecutionResult { StandardError = "CONFLICT (content): Merge conflict in src/repo/file.txt" };
        var patch = new VmrIngestionPatch("patch.diff", (string)null);
        var targetBranch = "target/branch";

        vmrUpdaterMock
            .Setup(m => m.UpdateRepository(
                mapping,
                build,
                It.Is<string[]>(a => a != null && a.Length > 0),
                null,
                false,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PatchApplicationFailedException(patch, per, reverseApply: false));

        var sut = new TestableVmrForwardFlower(
            vmrInfoMock.Object,
            sourceManifestMock.Object,
            vmrUpdaterMock.Object,
            dependencyTrackerMock.Object,
            cloneManagerMock.Object,
            localGitClientMock.Object,
            localGitRepoFactoryMock.Object,
            versionDetailsParserMock.Object,
            changeAnalyzerMock.Object,
            conflictResolverMock.Object,
            workBranchFactoryMock.Object,
            processManagerMock.Object,
            barClientMock.Object,
            fileSystemMock.Object,
            loggerMock.Object,
            shouldResetClones: false);

        // Act
        ConflictInPrBranchException thrown = null;
        try
        {
            await sut.InvokeSameDirectionFlowAsync(
                mapping,
                lastFlows,
                currentFlow,
                sourceRepoMock.Object,
                build,
                new List<string>(),
                targetBranch,
                "head/branch",
                headBranchExisted: true,
                cancellationToken: CancellationToken.None);
        }
        catch (ConflictInPrBranchException e)
        {
            thrown = e;
        }

        // Assert
        thrown.Should().NotBeNull();
        thrown!.Message.Should().Contain(targetBranch);
        thrown.ConflictedFiles.Should().BeEquivalentTo(new[] { "file.txt" });
        loggerMock.VerifyAll();
    }

    /// <summary>
    /// Partial test placeholder for the branch recreation path when a PatchApplicationFailedException occurs
    /// and headBranchExisted == false. This path internally calls non-virtual protected logic in the base class
    /// (RecreatePreviousFlowsAndApplyChanges), which depends on complex Git and filesystem operations.
    /// Inputs:
    ///  - UpdateRepository throws PatchApplicationFailedException.
    ///  - headBranchExisted == false.
    /// Expected:
    ///  - The method attempts to recreate previous flows and, if changes occur, amends the commit.
    /// Notes:
    ///  - This test is marked inconclusive because the base method cannot be mocked or overridden.
    ///    To complete this test, refactor the production code to allow injecting a strategy for previous-flow recreation
    ///    or make the method virtual so it can be overridden and observed in tests.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SameDirectionFlowAsync_NewHeadBranch_RecreatesPreviousFlows_PartialTest()
    {
        Assert.Inconclusive("RecreatePreviousFlowsAndApplyChanges is non-virtual and performs complex operations; inject a test seam or make it virtual to enable isolated testing.");
    }

    private sealed class TestableVmrForwardFlower : VmrForwardFlower
    {
        private readonly bool _shouldResetClones;

        public TestableVmrForwardFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            ICodeFlowVmrUpdater vmrUpdater,
            IVmrDependencyTracker dependencyTracker,
            IVmrCloneManager vmrCloneManager,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            ICodeflowChangeAnalyzer codeflowChangeAnalyzer,
            IForwardFlowConflictResolver conflictResolver,
            IWorkBranchFactory workBranchFactory,
            IProcessManager processManager,
            IBasicBarClient barClient,
            IFileSystem fileSystem,
            ILogger<VmrCodeFlower> logger,
            bool shouldResetClones)
            : base(vmrInfo, sourceManifest, vmrUpdater, dependencyTracker, vmrCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, codeflowChangeAnalyzer, conflictResolver, workBranchFactory, processManager, barClient, fileSystem, logger)
        {
            _shouldResetClones = shouldResetClones;
        }

        protected override bool ShouldResetClones => _shouldResetClones;

        public Task<bool> InvokeSameDirectionFlowAsync(
            SourceMapping mapping,
            LastFlows lastFlows,
            Codeflow currentFlow,
            ILocalGitRepo sourceRepo,
            Build build,
            IReadOnlyCollection<string> excludedAssets,
            string targetBranch,
            string headBranch,
            bool headBranchExisted,
            CancellationToken cancellationToken)
            => base.SameDirectionFlowAsync(mapping, lastFlows, currentFlow, sourceRepo, build, excludedAssets, targetBranch, headBranch, headBranchExisted, cancellationToken);
    }

    /// <summary>
    /// Ensures DetectCrossingFlow returns null when the last flow is not a Backflow.
    /// Inputs:
    ///  - lastFlow: ForwardFlow instance.
    ///  - lastBackFlow: null (unused).
    ///  - lastForwardFlow: arbitrary ForwardFlow.
    ///  - repo: mocked ILocalGitRepo.
    /// Expected:
    ///  - Returns null.
    ///  - ILocalGitRepoFactory.Create is not invoked (short-circuit).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task DetectCrossingFlow_LastFlowNotBackflow_ReturnsNullAndDoesNotCreateVmrRepoAsync()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var vmrUpdater = new Mock<ICodeFlowVmrUpdater>(MockBehavior.Strict);
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var vmrCloneManager = new Mock<IVmrCloneManager>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var codeflowChangeAnalyzer = new Mock<ICodeflowChangeAnalyzer>(MockBehavior.Strict);
        var conflictResolver = new Mock<IForwardFlowConflictResolver>(MockBehavior.Strict);
        var workBranchFactory = new Mock<IWorkBranchFactory>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Loose);

        var sut = new TestableVmrForwardFlower(
            vmrInfo.Object,
            sourceManifest.Object,
            vmrUpdater.Object,
            dependencyTracker.Object,
            vmrCloneManager.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            versionDetailsParser.Object,
            codeflowChangeAnalyzer.Object,
            conflictResolver.Object,
            workBranchFactory.Object,
            processManager.Object,
            barClient.Object,
            fileSystem.Object,
            logger.Object);

        var lastFlow = new ForwardFlow("repo-sha-1", "vmr-sha-1");
        ForwardFlow lastForwardFlow = new ForwardFlow("repo-sha-2", "vmr-sha-2");
        var repo = new Mock<ILocalGitRepo>(MockBehavior.Strict).Object;

        // Act
        var result = await sut.DetectCrossingFlowPublicAsync(lastFlow, null, lastForwardFlow, repo);

        // Assert
        result.Should().BeNull();
        localGitRepoFactory.Verify(f => f.Create(It.IsAny<NativePath>()), Times.Never);
    }

    /// <summary>
    /// Validates that when the last flow is a Backflow, DetectCrossingFlow returns lastForwardFlow
    /// only if the backflow's VMR SHA is an ancestor of the last forward flow's VMR SHA; otherwise null.
    /// Inputs (parameterized):
    ///  - isAncestor: whether vmr.IsAncestorCommit returns true or false.
    /// Expected:
    ///  - When isAncestor == true => returns the same instance as lastForwardFlow.
    ///  - When isAncestor == false => returns null.
    ///  - ILocalGitRepoFactory.Create and ILocalGitRepo.IsAncestorCommit are invoked exactly once.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task DetectCrossingFlow_Backflow_AncestorDeterminesReturnAsync(bool isAncestor)
    {
        // Arrange
        const string backflowVmrSha = "bf-vmr-sha";
        const string backflowRepoSha = "bf-repo-sha";
        const string forwardVmrSha = "ff-vmr-sha";
        const string forwardRepoSha = "ff-repo-sha";

        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupGet(v => v.VmrPath).Returns(default(NativePath));

        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var vmrUpdater = new Mock<ICodeFlowVmrUpdater>(MockBehavior.Strict);
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var vmrCloneManager = new Mock<IVmrCloneManager>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var vmrRepo = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        vmrRepo.Setup(r => r.IsAncestorCommit(backflowVmrSha, forwardVmrSha)).ReturnsAsync(isAncestor);

        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        localGitRepoFactory
            .Setup(f => f.Create(It.IsAny<NativePath>()))
            .Returns(vmrRepo.Object);

        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var codeflowChangeAnalyzer = new Mock<ICodeflowChangeAnalyzer>(MockBehavior.Strict);
        var conflictResolver = new Mock<IForwardFlowConflictResolver>(MockBehavior.Strict);
        var workBranchFactory = new Mock<IWorkBranchFactory>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Loose);

        var sut = new TestableVmrForwardFlower(
            vmrInfo.Object,
            sourceManifest.Object,
            vmrUpdater.Object,
            dependencyTracker.Object,
            vmrCloneManager.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            versionDetailsParser.Object,
            codeflowChangeAnalyzer.Object,
            conflictResolver.Object,
            workBranchFactory.Object,
            processManager.Object,
            barClient.Object,
            fileSystem.Object,
            logger.Object);

        var lastFlow = new Backflow(backflowVmrSha, backflowRepoSha);
        var lastForwardFlow = new ForwardFlow(forwardRepoSha, forwardVmrSha);
        var repo = new Mock<ILocalGitRepo>(MockBehavior.Strict).Object;

        // Act
        var result = await sut.DetectCrossingFlowPublicAsync(lastFlow, null, lastForwardFlow, repo);

        // Assert
        if (isAncestor)
        {
            result.Should().BeSameAs(lastForwardFlow);
        }
        else
        {
            result.Should().BeNull();
        }

        localGitRepoFactory.Verify(f => f.Create(It.IsAny<NativePath>()), Times.Once);
        vmrRepo.Verify(r => r.IsAncestorCommit(backflowVmrSha, forwardVmrSha), Times.Once);
    }

    /// <summary>
    /// Validates that GetEngCommonPath appends the 'eng/common' path to the provided source repository path,
    /// respecting current platform directory separators and handling various base path shapes (relative, absolute,
    /// trailing separators, empty, and mixed separators).
    /// Inputs:
    ///  - basePath: Different shapes of repository root paths (e.g., "repo", "repo/", "", ".", absolute paths).
    /// Expected:
    ///  - The returned NativePath equals new NativePath(basePath) / Constants.CommonScriptFilesPath.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(GetEngCommonPathCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetEngCommonPath_VariousSourceRepoShapes_AppendsEngCommonWithProperSeparator(string basePath)
    {
        // Arrange
        var sut = CreateSut();
        var sourceRepo = new NativePath(basePath);
        var expected = new NativePath(basePath) / Constants.CommonScriptFilesPath;

        // Act
        var actual = sut.InvokeGetEngCommonPath(sourceRepo);

        // Assert
        actual.Path.Should().Be(expected.Path);
    }

    private static IEnumerable<TestCaseData> GetEngCommonPathCases()
    {
        var sep = System.IO.Path.DirectorySeparatorChar;
        var otherSep = sep == '/' ? '\\' : '/';

        yield return new TestCaseData("repo").SetName("GetEngCommonPath_AppendsCommonToRelativePath");
        yield return new TestCaseData("repo" + sep).SetName("GetEngCommonPath_AppendsCommonToPathWithTrailingSeparator");
        yield return new TestCaseData("repo" + otherSep).SetName("GetEngCommonPath_NormalizesOppositeTrailingSeparator");
        yield return new TestCaseData(string.Empty).SetName("GetEngCommonPath_AppendsCommonToEmptyBase");
        yield return new TestCaseData(".").SetName("GetEngCommonPath_AppendsCommonToCurrentDirectory");

        if (sep == '\\')
        {
            yield return new TestCaseData(@"C:\base\dir").SetName("GetEngCommonPath_WindowsAbsolutePath");
            yield return new TestCaseData(@"C:/mixed/dir").SetName("GetEngCommonPath_WindowsMixedSeparators");
        }
        else
        {
            yield return new TestCaseData("/usr/local").SetName("GetEngCommonPath_UnixAbsolutePath");
            yield return new TestCaseData("var/log/").SetName("GetEngCommonPath_UnixRelativeWithTrailingSlash");
        }
    }

    private static TestableVmrForwardFlower CreateSut()
    {
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Loose).Object;
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Loose).Object;
        var vmrUpdater = new Mock<ICodeFlowVmrUpdater>(MockBehavior.Loose).Object;
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Loose).Object;
        var vmrCloneManager = new Mock<IVmrCloneManager>(MockBehavior.Loose).Object;
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Loose).Object;
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Loose).Object;
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose).Object;
        var codeflowChangeAnalyzer = new Mock<ICodeflowChangeAnalyzer>(MockBehavior.Loose).Object;
        var conflictResolver = new Mock<IForwardFlowConflictResolver>(MockBehavior.Loose).Object;
        var workBranchFactory = new Mock<IWorkBranchFactory>(MockBehavior.Loose).Object;
        var processManager = new Mock<IProcessManager>(MockBehavior.Loose).Object;
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Loose).Object;
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose).Object;
        var logger = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Loose).Object;

        return new TestableVmrForwardFlower(
            vmrInfo,
            sourceManifest,
            vmrUpdater,
            dependencyTracker,
            vmrCloneManager,
            localGitClient,
            localGitRepoFactory,
            versionDetailsParser,
            codeflowChangeAnalyzer,
            conflictResolver,
            workBranchFactory,
            processManager,
            barClient,
            fileSystem,
            logger);
    }

    /// <summary>
    /// Ensures the forward flower identifies the target repository as a VMR.
    /// Inputs:
    ///  - No inputs (method is parameterless). All required constructor dependencies are mocked.
    /// Expected:
    ///  - TargetRepoIsVmr returns true.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void TargetRepoIsVmr_Always_ReturnsTrue()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Loose);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Loose);
        var vmrUpdater = new Mock<ICodeFlowVmrUpdater>(MockBehavior.Loose);
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Loose);
        var vmrCloneManager = new Mock<IVmrCloneManager>(MockBehavior.Loose);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Loose);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Loose);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var codeflowChangeAnalyzer = new Mock<ICodeflowChangeAnalyzer>(MockBehavior.Loose);
        var conflictResolver = new Mock<IForwardFlowConflictResolver>(MockBehavior.Loose);
        var workBranchFactory = new Mock<IWorkBranchFactory>(MockBehavior.Loose);
        var processManager = new Mock<IProcessManager>(MockBehavior.Loose);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Loose);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var logger = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Loose);

        var sut = new TestableVmrForwardFlower(
            vmrInfo.Object,
            sourceManifest.Object,
            vmrUpdater.Object,
            dependencyTracker.Object,
            vmrCloneManager.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            versionDetailsParser.Object,
            codeflowChangeAnalyzer.Object,
            conflictResolver.Object,
            workBranchFactory.Object,
            processManager.Object,
            barClient.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        var result = sut.TargetRepoIsVmr_Public();

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the protected property ShouldResetVmr:
    /// - returns false by default (no override),
    /// - returns the overridden value when subclass overrides it.
    /// Scenarios:
    /// - "default": uses a non-overriding subclass; expected false.
    /// - "override_true": overrides property to true; expected true.
    /// - "override_false": overrides property to false; expected false.
    /// </summary>
    /// <param name="scenario">Determines whether to use default behavior or an override.</param>
    /// <param name="expected">The expected boolean value of the property.</param>
    [Test]
    [TestCase("default", false)]
    [TestCase("override_true", true)]
    [TestCase("override_false", false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ShouldResetVmr_DefaultAndOverride_ReturnsExpected(string scenario, bool expected)
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>().Object;
        var sourceManifest = new Mock<ISourceManifest>().Object;
        var vmrUpdater = new Mock<ICodeFlowVmrUpdater>().Object;
        var dependencyTracker = new Mock<IVmrDependencyTracker>().Object;
        var vmrCloneManager = new Mock<IVmrCloneManager>().Object;
        var localGitClient = new Mock<ILocalGitClient>().Object;
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>().Object;
        var versionDetailsParser = new Mock<IVersionDetailsParser>().Object;
        var codeflowChangeAnalyzer = new Mock<ICodeflowChangeAnalyzer>().Object;
        var conflictResolver = new Mock<IForwardFlowConflictResolver>().Object;
        var workBranchFactory = new Mock<IWorkBranchFactory>().Object;
        var processManager = new Mock<IProcessManager>().Object;
        var barClient = new Mock<IBasicBarClient>().Object;
        var fileSystem = new Mock<IFileSystem>().Object;
        var logger = new Mock<ILogger<VmrCodeFlower>>().Object;

        bool result;

        // Act
        if (scenario == "default")
        {
            var sut = new TestableVmrForwardFlower(
                vmrInfo,
                sourceManifest,
                vmrUpdater,
                dependencyTracker,
                vmrCloneManager,
                localGitClient,
                localGitRepoFactory,
                versionDetailsParser,
                codeflowChangeAnalyzer,
                conflictResolver,
                workBranchFactory,
                processManager,
                barClient,
                fileSystem,
                logger);

            result = sut.ReadShouldResetVmr();
        }
        else
        {
            bool overrideValue = scenario == "override_true";
            var sut = new OverridingVmrForwardFlower(
                vmrInfo,
                sourceManifest,
                vmrUpdater,
                dependencyTracker,
                vmrCloneManager,
                localGitClient,
                localGitRepoFactory,
                versionDetailsParser,
                codeflowChangeAnalyzer,
                conflictResolver,
                workBranchFactory,
                processManager,
                barClient,
                fileSystem,
                logger,
                overrideValue);

            result = sut.ReadShouldResetVmr();
        }

        // Assert
        result.Should().Be(expected);
    }

    private sealed class OverridingVmrForwardFlower : VmrForwardFlower
    {
        private readonly bool _value;

        public OverridingVmrForwardFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            ICodeFlowVmrUpdater vmrUpdater,
            IVmrDependencyTracker dependencyTracker,
            IVmrCloneManager vmrCloneManager,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            ICodeflowChangeAnalyzer codeflowChangeAnalyzer,
            IForwardFlowConflictResolver conflictResolver,
            IWorkBranchFactory workBranchFactory,
            IProcessManager processManager,
            IBasicBarClient barClient,
            IFileSystem fileSystem,
            ILogger<VmrCodeFlower> logger,
            bool value)
            : base(vmrInfo, sourceManifest, vmrUpdater, dependencyTracker, vmrCloneManager, localGitClient, localGitRepoFactory, versionDetailsParser, codeflowChangeAnalyzer, conflictResolver, workBranchFactory, processManager, barClient, fileSystem, logger)
        {
            _value = value;
        }

        protected override bool ShouldResetVmr => _value;

        public bool ReadShouldResetVmr() => ShouldResetVmr;
    }
}