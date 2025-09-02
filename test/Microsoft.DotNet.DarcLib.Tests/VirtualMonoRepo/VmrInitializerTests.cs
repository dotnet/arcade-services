// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maestro;
using Maestro.Common;
using Microsoft;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

/// <summary>
/// Unit tests for the VmrInitializer constructor to ensure it correctly accepts valid dependencies,
/// calls the base constructor, and produces a usable instance of VmrInitializer that implements
/// IVmrInitializer and derives from VmrManagerBase.
/// Inputs:
///  - All required non-null interface dependencies provided as mocks.
/// Expected:
///  - Constructor completes without throwing and returns a non-null instance assignable to both
///    IVmrInitializer and VmrManagerBase.
/// </summary>
public class VmrInitializerTests
{
    /// <summary>
    /// Verifies that the constructor succeeds with valid dependencies using different Moq behaviors.
    /// Inputs:
    ///  - All required dependencies mocked using the provided MockBehavior (Loose or Strict).
    /// Expected:
    ///  - No exception is thrown.
    ///  - The created object is non-null, is of type VmrInitializer, implements IVmrInitializer,
    ///    and derives from VmrManagerBase.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(MockBehavior.Loose)]
    [TestCase(MockBehavior.Strict)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithAllValidDependencies_CreatesInstanceAndImplementsInterfaces(MockBehavior behavior)
    {
        // Arrange
        var dependencyTracker = new Mock<IVmrDependencyTracker>(behavior).Object;
        var patchHandler = new Mock<IVmrPatchHandler>(behavior).Object;
        var versionDetailsParser = new Mock<IVersionDetailsParser>(behavior).Object;
        var cloneManager = new Mock<IRepositoryCloneManager>(behavior).Object;
        var thirdPartyNoticesGenerator = new Mock<IThirdPartyNoticesGenerator>(behavior).Object;
        var codeownersGenerator = new Mock<ICodeownersGenerator>(behavior).Object;
        var credScanSuppressionsGenerator = new Mock<ICredScanSuppressionsGenerator>(behavior).Object;
        var localGitClient = new Mock<ILocalGitClient>(behavior).Object;
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(behavior).Object;
        var dependencyFileManager = new Mock<IDependencyFileManager>(behavior).Object;
        var workBranchFactory = new Mock<IWorkBranchFactory>(behavior).Object;
        var fileSystem = new Mock<IFileSystem>(behavior).Object;
        var logger = new Mock<ILogger<VmrUpdater>>(behavior).Object;
        var sourceManifest = new Mock<ISourceManifest>(behavior).Object;
        var vmrInfo = new Mock<IVmrInfo>(behavior).Object;

        // Act
        var sut = new VmrInitializer(
            dependencyTracker,
            patchHandler,
            versionDetailsParser,
            cloneManager,
            thirdPartyNoticesGenerator,
            codeownersGenerator,
            credScanSuppressionsGenerator,
            localGitClient,
            localGitRepoFactory,
            dependencyFileManager,
            workBranchFactory,
            fileSystem,
            logger,
            sourceManifest,
            vmrInfo);

        // Assert
        sut.Should().NotBeNull();
        sut.Should().BeOfType<VmrInitializer>();
        sut.Should().BeAssignableTo<IVmrInitializer>();
        sut.Should().BeAssignableTo<VmrManagerBase>();
    }
}
