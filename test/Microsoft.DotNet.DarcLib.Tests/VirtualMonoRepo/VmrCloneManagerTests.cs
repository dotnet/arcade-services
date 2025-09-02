// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class VmrCloneManagerTests
{
    /// <summary>
    /// Verifies that when IVmrInfo.VmrPath is non-empty, GetClonePath returns the VmrPath value directly,
    /// ignoring TmpPath and dirName.
    /// Inputs:
    ///  - vmrPathStr: a non-empty VMR path string (including whitespace-only or "." which are still non-empty).
    ///  - dirName: arbitrary directory name that should be ignored.
    /// Expected:
    ///  - The returned NativePath equals the provided VmrPath.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(NonEmptyVmrPaths))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetClonePath_VmrPathProvided_ReturnsVmrPath(string vmrPathStr)
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var cloner = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrCloneManager>>(MockBehavior.Loose);

        var vmrPath = new NativePath(vmrPathStr);
        vmrInfo.SetupGet(m => m.VmrPath).Returns(vmrPath);
        vmrInfo.SetupGet(m => m.TmpPath).Returns(new NativePath("tmp"));

        var sut = new TestableVmrCloneManager(
            vmrInfo.Object,
            dependencyTracker.Object,
            sourceManifest.Object,
            cloner.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            telemetry.Object,
            fileSystem.Object,
            logger.Object);

        var dirName = "repo";

        // Act
        var result = sut.CallGetClonePath(dirName);

        // Assert
        result.Should().Be(vmrPath);
    }

    /// <summary>
    /// Verifies that when IVmrInfo.VmrPath is empty (""), GetClonePath returns the combination of TmpPath and dirName.
    /// Inputs:
    ///  - dirName: tested across empty, simple name, path-like with separators, and whitespace to ensure normalization.
    /// Expected:
    ///  - The returned NativePath equals TmpPath / dirName.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("")]
    [TestCase("repo")]
    [TestCase("a/b")]
    [TestCase(" name ")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetClonePath_EmptyVmrPath_CombinesTmpPathWithDirName(string dirName)
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var cloner = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrCloneManager>>(MockBehavior.Loose);

        var tmpRoot = new NativePath("tmp-root");
        vmrInfo.SetupGet(m => m.VmrPath).Returns(new NativePath(""));
        vmrInfo.SetupGet(m => m.TmpPath).Returns(tmpRoot);

        var sut = new TestableVmrCloneManager(
            vmrInfo.Object,
            dependencyTracker.Object,
            sourceManifest.Object,
            cloner.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            telemetry.Object,
            fileSystem.Object,
            logger.Object);

        var expected = tmpRoot / dirName;

        // Act
        var result = sut.CallGetClonePath(dirName);

        // Assert
        result.Should().Be(expected);
    }

    private static IEnumerable NonEmptyVmrPaths()
    {
        yield return "vmr";
        yield return " ";
        yield return ".";
        yield return $"root{Path.DirectorySeparatorChar}vmr";
    }

    private sealed class TestableVmrCloneManager : VmrCloneManager
    {
        public TestableVmrCloneManager(
            IVmrInfo vmrInfo,
            IVmrDependencyTracker dependencyTracker,
            ISourceManifest sourceManifest,
            IGitRepoCloner gitRepoCloner,
            ILocalGitClient localGitRepo,
            ILocalGitRepoFactory localGitRepoFactory,
            ITelemetryRecorder telemetryRecorder,
            IFileSystem fileSystem,
            ILogger<VmrCloneManager> logger)
            : base(vmrInfo, dependencyTracker, sourceManifest, gitRepoCloner, localGitRepo, localGitRepoFactory, telemetryRecorder, fileSystem, logger)
        {
        }

        public NativePath CallGetClonePath(string dirName) => GetClonePath(dirName);
    }
}
