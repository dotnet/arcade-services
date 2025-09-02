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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class CodeownersGeneratorTests
{
    /// <summary>
    /// Ensures the constructor assigns provided dependencies without throwing.
    /// Inputs:
    ///  - Non-null instances (via mocks) for IVmrInfo, ISourceManifest, ILocalGitClient, IFileSystem, and ILogger.
    /// Expected:
    ///  - Instance of CodeownersGenerator is created successfully and is not null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithValidDependencies_Succeeds()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger<CodeownersGenerator>>(MockBehavior.Strict);

        // Act
        var instance = new CodeownersGenerator(
            vmrInfo.Object,
            sourceManifest.Object,
            localGitClient.Object,
            fileSystem.Object,
            logger.Object);

        // Assert
        instance.Should().NotBeNull();
    }
}
