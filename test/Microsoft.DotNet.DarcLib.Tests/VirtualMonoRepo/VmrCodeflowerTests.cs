// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
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

/// <summary>
/// Unit tests for the VmrCodeFlower constructor.
/// Validates that the constructor:
///  - Accepts valid dependency instances without throwing.
///  - Does not invoke any dependency members during construction.
/// </summary>
[TestFixture]
public class VmrCodeFlowerTests
{
    /// <summary>
    /// Ensures that providing valid dependency instances (both strict and loose mocks)
    /// results in a successfully created instance.
    /// Inputs:
    ///  - All constructor parameters provided via Moq-created mocks.
    ///  - MockBehavior toggled between Strict and Loose to cover both cases.
    /// Expected:
    ///  - No exception is thrown during construction and instance is created.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithValidDependencies_InstanceIsCreated(bool useStrictMocks)
    {
        // Arrange
        var behavior = useStrictMocks ? MockBehavior.Strict : MockBehavior.Loose;

        var vmrInfo = new Mock<IVmrInfo>(behavior);
        var sourceManifest = new Mock<ISourceManifest>(behavior);
        var dependencyTracker = new Mock<IVmrDependencyTracker>(behavior);
        var localGitClient = new Mock<ILocalGitClient>(behavior);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(behavior);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(behavior);
        var fileSystem = new Mock<IFileSystem>(behavior);
        var logger = new Mock<ILogger<VmrCodeFlower>>(behavior);

        // Act
        var instance = new TestableVmrCodeFlower(
            vmrInfo.Object,
            sourceManifest.Object,
            dependencyTracker.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            versionDetailsParser.Object,
            fileSystem.Object,
            logger.Object);

        // Assert
        instance.Should().NotBeNull();
        instance.Should().BeOfType<TestableVmrCodeFlower>();
    }

    /// <summary>
    /// Verifies that the constructor does not interact with any provided dependencies.
    /// Inputs:
    ///  - All dependencies provided as Strict mocks without any setups.
    /// Expected:
    ///  - Construction succeeds.
    ///  - No dependency methods/properties are invoked during construction (VerifyNoOtherCalls passes).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_DoesNotCallDependencies_NoInteractions()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrCodeFlower>>(MockBehavior.Strict);

        // Act
        var instance = new TestableVmrCodeFlower(
            vmrInfo.Object,
            sourceManifest.Object,
            dependencyTracker.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            versionDetailsParser.Object,
            fileSystem.Object,
            logger.Object);

        // Assert
        instance.Should().NotBeNull();
        vmrInfo.VerifyNoOtherCalls();
        sourceManifest.VerifyNoOtherCalls();
        dependencyTracker.VerifyNoOtherCalls();
        localGitClient.VerifyNoOtherCalls();
        localGitRepoFactory.VerifyNoOtherCalls();
        versionDetailsParser.VerifyNoOtherCalls();
        fileSystem.VerifyNoOtherCalls();
        logger.VerifyNoOtherCalls();
    }

    private sealed class TestableVmrCodeFlower : VmrCodeFlower
    {
        public TestableVmrCodeFlower(
            IVmrInfo vmrInfo,
            ISourceManifest sourceManifest,
            IVmrDependencyTracker dependencyTracker,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            IVersionDetailsParser versionDetailsParser,
            IFileSystem fileSystem,
            ILogger<VmrCodeFlower> logger)
            : base(vmrInfo, sourceManifest, dependencyTracker, localGitClient, localGitRepoFactory, versionDetailsParser, fileSystem, logger)
        {
        }

        protected override Task<bool> SameDirectionFlowAsync(
            SourceMapping mapping,
            LastFlows lastFlows,
            Codeflow currentFlow,
            ILocalGitRepo repo,
            Build build,
            IReadOnlyCollection<string> excludedAssets,
            string targetBranch,
            string headBranch,
            bool headBranchExisted,
            CancellationToken cancellationToken)
            => Task.FromResult(false);

        protected override Task<bool> OppositeDirectionFlowAsync(
            SourceMapping mapping,
            LastFlows lastFlows,
            Codeflow currentFlow,
            ILocalGitRepo repo,
            Build build,
            string targetBranch,
            string headBranch,
            bool headBranchExisted,
            CancellationToken cancellationToken)
            => Task.FromResult(false);

        protected override Task<Codeflow> DetectCrossingFlow(
            Codeflow lastFlow,
            Backflow lastBackFlow,
            ForwardFlow lastForwardFlow,
            ILocalGitRepo repo)
            => Task.FromResult(lastFlow);

        protected override Task<(Codeflow, LastFlows)> RewindToPreviousFlowAsync(
            SourceMapping mapping,
            ILocalGitRepo targetRepo,
            int depth,
            LastFlows previousFlows,
            string branchToCreate,
            string targetBranch,
            CancellationToken cancellationToken)
            => Task.FromResult((previousFlows.LastFlow, previousFlows));

        protected override NativePath GetEngCommonPath(NativePath sourceRepo)
            => sourceRepo;

        protected override bool TargetRepoIsVmr()
            => false;
    }
}
