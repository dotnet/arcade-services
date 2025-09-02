// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maestro;
using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

/// <summary>
/// Tests for VmrUpdater constructor ensuring dependencies are accepted and the instance is created.
/// Inputs:
///  - All required interface dependencies provided as Moq mocks.
/// Expected:
///  - Constructor completes without throwing and returns a valid instance assignable to IVmrUpdater and VmrManagerBase.
/// </summary>
public class VmrUpdaterTests
{
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_ValidDependencies_InitializesWithoutExceptions()
    {
        // Arrange
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var cloneManager = new Mock<IRepositoryCloneManager>(MockBehavior.Strict);
        var patchHandler = new Mock<IVmrPatchHandler>(MockBehavior.Strict);
        var thirdPartyNoticesGenerator = new Mock<IThirdPartyNoticesGenerator>(MockBehavior.Strict);
        var codeownersGenerator = new Mock<ICodeownersGenerator>(MockBehavior.Strict);
        var credScanSuppressionsGenerator = new Mock<ICredScanSuppressionsGenerator>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var gitRepoFactory = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrUpdater>>(MockBehavior.Loose);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);

        // Act
        var sut = new VmrUpdater(
            dependencyTracker.Object,
            cloneManager.Object,
            patchHandler.Object,
            thirdPartyNoticesGenerator.Object,
            codeownersGenerator.Object,
            credScanSuppressionsGenerator.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            gitRepoFactory.Object,
            logger.Object,
            sourceManifest.Object,
            vmrInfo.Object);

        // Assert
        sut.Should().NotBeNull();
        sut.Should().BeAssignableTo<IVmrUpdater>();
        sut.Should().BeAssignableTo<VmrManagerBase>();
    }
}
