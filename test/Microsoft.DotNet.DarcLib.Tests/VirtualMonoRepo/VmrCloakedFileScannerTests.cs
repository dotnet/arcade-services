// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
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
/// Tests for the VmrCloakedFileScanner constructor to ensure it accepts valid dependencies
/// and creates a usable instance without throwing.
/// Inputs:
///  - dependencyTracker, processManager, vmrInfo, logger: non-null interface mocks.
/// Expected:
///  - Instance of VmrCloakedFileScanner is created successfully and is assignable to VmrScanner.
/// </summary>
[TestFixture]
public class VmrCloakedFileScannerTests
{
    /// <summary>
    /// Ensures the constructor creates an instance when provided with valid (non-null) dependencies.
    /// Inputs:
    ///  - A set of mocks created with the specified MockBehavior.
    /// Expected:
    ///  - The constructed instance is non-null and assignable to VmrScanner.
    /// </summary>
    [Test]
    [TestCase(MockBehavior.Strict)]
    [TestCase(MockBehavior.Loose)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithValidDependencies_CreatesInstance(MockBehavior behavior)
    {
        // Arrange
        var dependencyTracker = new Mock<IVmrDependencyTracker>(behavior);
        var processManager = new Mock<IProcessManager>(behavior);
        var vmrInfo = new Mock<IVmrInfo>(behavior);
        var logger = new Mock<ILogger<VmrScanner>>(behavior);

        // Act
        var sut = new VmrCloakedFileScanner(
            dependencyTracker.Object,
            processManager.Object,
            vmrInfo.Object,
            logger.Object);

        // Assert
        sut.Should().NotBeNull();
        sut.Should().BeAssignableTo<VmrScanner>();
    }
}
