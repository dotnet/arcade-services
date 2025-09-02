// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class CloneManagerTests
{
    /// <summary>
    /// Verifies that the CloneManager constructor accepts valid non-null dependencies
    /// and produces a non-null instance without throwing.
    /// Inputs:
    ///  - Valid IVmrInfo instance and mocks for all other required dependencies.
    /// Expected:
    ///  - No exception thrown and the created instance is non-null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithValidDependencies_Succeeds()
    {
        // Arrange
        var vmrInfo = CreateVmrInfo("/vmr", "/tmp");
        var gitRepoCloner = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Strict);

        // Act
        var sut = new TestableCloneManager(
            vmrInfo,
            gitRepoCloner.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            telemetryRecorder.Object,
            fileSystem.Object,
            logger.Object);

        // Assert
        sut.Should().NotBeNull();
    }

    /// <summary>
    /// Ensures that GetClonePath uses the IVmrInfo provided to the constructor by returning TmpPath combined with dirName.
    /// Inputs:
    ///  - Various tmpPath and dirName values including whitespace and special characters.
    /// Expected:
    ///  - Result equals vmrInfo.TmpPath / dirName for each case.
    /// </summary>
    [TestCase("/var/tmp", "repo")]
    [TestCase("/var/tmp", "")]
    [TestCase("/var/tmp", "  ")]
    [TestCase("/var/tmp", "name-with-special_chars!@#$%^&()")]
    [TestCase("/var/tmp", "nested/path/segments")]
    [TestCase("/tmp-path-ünicode", "répó-子")]
    [TestCase("/t", "a")]
    [TestCase("/very/long/tmp", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetClonePath_UsesInjectedVmrInfoTmpPath_ReturnsCombinedPath(string tmpPath, string dirName)
    {
        // Arrange
        var vmrInfo = CreateVmrInfo("/vmr", tmpPath);
        var gitRepoCloner = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Strict);

        var sut = new TestableCloneManager(
            vmrInfo,
            gitRepoCloner.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            telemetryRecorder.Object,
            fileSystem.Object,
            logger.Object);

        var expected = vmrInfo.TmpPath / dirName;

        // Act
        var result = sut.InvokeGetClonePath(dirName);

        // Assert
        result.Should().Be(expected);
    }

    private static VmrInfo CreateVmrInfo(string vmrPath, string tmpPath)
    {
        return new VmrInfo(new NativePath(vmrPath), new NativePath(tmpPath));
    }

    private sealed class TestableCloneManager : CloneManager
    {
        public TestableCloneManager(
            IVmrInfo vmrInfo,
            IGitRepoCloner gitRepoCloner,
            ILocalGitClient localGitRepo,
            ILocalGitRepoFactory localGitRepoFactory,
            ITelemetryRecorder telemetryRecorder,
            IFileSystem fileSystem,
            ILogger logger)
            : base(vmrInfo, gitRepoCloner, localGitRepo, localGitRepoFactory, telemetryRecorder, fileSystem, logger)
        {
        }

        public NativePath InvokeGetClonePath(string dirName) => base.GetClonePath(dirName);
    }

    private class TestCloneManager : CloneManager
    {
        public TestCloneManager(
            IVmrInfo vmrInfo,
            IGitRepoCloner gitRepoCloner,
            ILocalGitClient localGitRepo,
            ILocalGitRepoFactory localGitRepoFactory,
            ITelemetryRecorder telemetryRecorder,
            IFileSystem fileSystem,
            ILogger logger)
            : base(vmrInfo, gitRepoCloner, localGitRepo, localGitRepoFactory, telemetryRecorder, fileSystem, logger)
        {
        }

        public Task<ILocalGitRepo> InvokePrepareCloneInternalAsync(
            string dirName,
            IReadOnlyCollection<string> remoteUris,
            IReadOnlyCollection<string> requestedRefs,
            string checkoutRef,
            bool resetToRemote,
            CancellationToken cancellationToken)
            => PrepareCloneInternalAsync(dirName, remoteUris, requestedRefs, checkoutRef, resetToRemote, cancellationToken);
    }

    /// <summary>
    /// Validates that providing no remote URIs results in an ArgumentException.
    /// Inputs:
    ///  - dirName: "repo"
    ///  - remoteUris: empty collection
    ///  - requestedRefs: any (e.g., "main")
    /// Expected:
    ///  - Throws ArgumentException with message "No remote URIs provided to clone".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task PrepareCloneInternalAsync_NoRemoteUris_ThrowsArgumentException()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var gitRepoCloner = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var telemetryScope = new Mock<ITelemetryScope>(MockBehavior.Loose);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Loose);
        telemetry.Setup(t => t.RecordGitOperation(It.IsAny<TrackedGitOperation>(), It.IsAny<string>())).Returns(telemetryScope.Object);

        var sut = new TestCloneManager(vmrInfo.Object, gitRepoCloner.Object, localGitClient.Object, localRepoFactory.Object, telemetry.Object, fileSystem.Object, logger.Object);

        // Act
        Func<Task> act = async () => await sut.InvokePrepareCloneInternalAsync(
            dirName: "repo",
            remoteUris: Array.Empty<string>(),
            requestedRefs: new[] { "main" },
            checkoutRef: "main",
            resetToRemote: false,
            cancellationToken: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("No remote URIs provided to clone");
    }

    /// <summary>
    /// Ensures that when refs are found only on the second remote and the ref is a remote branch,
    /// the method creates a local tracking branch and proceeds to checkout successfully.
    /// Inputs:
    ///  - remoteUris: ["https://example.com/first.git", "https://example.com/second.git"]
    ///  - requestedRefs: ["main"]
    ///  - GetRefType sequence: Unknown (first), RemoteRef (second)
    /// Expected:
    ///  - AddRemoteIfMissingAsync invoked to get remote name.
    ///  - RunGitCommandAsync invoked to create tracking branch.
    ///  - ILocalGitRepoFactory.Create and repo.CheckoutAsync called with 'main'.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task PrepareCloneInternalAsync_RefsFoundInSecondRemote_RemoteBranch_CreatesLocalTrackingBranch()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupGet(v => v.TmpPath).Returns(new NativePath("tmp"));

        var gitRepoCloner = new Mock<IGitRepoCloner>(MockBehavior.Strict);

        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        // PrepareCloneInternal path: directory exists -> cleanup then add remote + update
        // Use true on first call, false on second.
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystem.Setup(fs => fs.DirectoryExists(It.Is<string>(s => s.Contains("tmp")))).Returns(true);

        // Cleanup reset --hard succeeds
        localGitClient
            .Setup(c => c.RunGitCommandAsync(It.IsAny<string>(), It.Is<string[]>(a => a.Length >= 2 && a[0] == "reset" && a[1] == "--hard"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        // AddRemoteIfMissingAsync called from PrepareCloneInternal and from branch creation
        localGitClient
            .Setup(c => c.AddRemoteIfMissingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("origin");

        // Update fetch for each remote
        localGitClient
            .Setup(c => c.UpdateRemoteAsync(It.IsAny<string>(), "origin", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Ref discovery sequence: first Unknown, then RemoteRef
        localGitClient
            .SetupSequence(c => c.GetRefType(It.IsAny<string>(), "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitObjectType.Unknown)
            .ReturnsAsync(GitObjectType.RemoteRef);

        // Create tracking branch succeeds
        localGitClient
            .Setup(c => c.RunGitCommandAsync(It.IsAny<string>(), It.Is<string[]>(a => a.Length >= 5 && a[0] == "branch" && a[1] == "-f" && a[2] == "--track" && a[3] == "main" && a[4] == "origin/main"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        var localRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var repo = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        localRepoFactory.Setup(f => f.Create(It.IsAny<NativePath>())).Returns(repo.Object);
        repo.Setup(r => r.CheckoutAsync("main")).Returns(Task.CompletedTask);

        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var telemetryScope = new Mock<ITelemetryScope>(MockBehavior.Loose);
        telemetryScope.Setup(s => s.SetSuccess());
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        telemetry.Setup(t => t.RecordGitOperation(It.IsAny<TrackedGitOperation>(), It.IsAny<string>())).Returns(telemetryScope.Object);

        var sut = new TestCloneManager(vmrInfo.Object, gitRepoCloner.Object, localGitClient.Object, localRepoFactory.Object, telemetry.Object, fileSystem.Object, logger.Object);

        var remoteUris = new[] { "https://example.com/first.git", "https://example.com/second.git" };
        var requestedRefs = new[] { "main" };

        // Act
        var resultRepo = await sut.InvokePrepareCloneInternalAsync(
            dirName: "repo",
            remoteUris: remoteUris,
            requestedRefs: requestedRefs,
            checkoutRef: "main",
            resetToRemote: false,
            cancellationToken: CancellationToken.None);

        // Assert
        resultRepo.Should().BeSameAs(repo.Object);
        localGitClient.Verify(c => c.AddRemoteIfMissingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        localGitClient.Verify(c => c.RunGitCommandAsync(It.IsAny<string>(), It.Is<string[]>(a => a.Length >= 5 && a[0] == "branch" && a[3] == "main" && a[4] == "origin/main"), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.CheckoutAsync("main"), Times.Once);
    }

    /// <summary>
    /// Verifies that a NotFoundException is thrown when none of the remotes contain all requested refs.
    /// Inputs:
    ///  - remoteUris: ["https://example.com/first.git"]
    ///  - requestedRefs: ["a", "b"]
    ///  - GetRefType: Unknown for all lookups
    /// Expected:
    ///  - Throws LibGit2Sharp.NotFoundException with a message containing the refs and remotes.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task PrepareCloneInternalAsync_AllRefsNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupGet(v => v.TmpPath).Returns(new NativePath("tmp"));

        var gitRepoCloner = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystem.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(true);

        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        // cleanup reset --hard succeeds
        localGitClient
            .Setup(c => c.RunGitCommandAsync(It.IsAny<string>(), It.Is<string[]>(a => a.Length >= 2 && a[0] == "reset" && a[1] == "--hard"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });
        // prepare clone: add remote + update
        localGitClient.Setup(c => c.AddRemoteIfMissingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("origin");
        localGitClient.Setup(c => c.UpdateRemoteAsync(It.IsAny<string>(), "origin", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        // ref lookups unknown
        localGitClient.Setup(c => c.GetRefType(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(GitObjectType.Unknown);

        var localRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var telemetryScope = new Mock<ITelemetryScope>(MockBehavior.Loose);
        telemetryScope.Setup(s => s.SetSuccess());
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        telemetry.Setup(t => t.RecordGitOperation(It.IsAny<TrackedGitOperation>(), It.IsAny<string>())).Returns(telemetryScope.Object);

        var sut = new TestCloneManager(vmrInfo.Object, gitRepoCloner.Object, localGitClient.Object, localRepoFactory.Object, telemetry.Object, fileSystem.Object, logger.Object);

        var remoteUris = new[] { "https://example.com/first.git" };
        var requestedRefs = new[] { "a", "b" };

        // Act
        Func<Task> act = async () => await sut.InvokePrepareCloneInternalAsync(
            dirName: "repo",
            remoteUris: remoteUris,
            requestedRefs: requestedRefs,
            checkoutRef: "main",
            resetToRemote: false,
            cancellationToken: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*a, b*https://example.com/first.git*");
    }

    /// <summary>
    /// Ensures that when resetToRemote is true, the upstream branch is determined and the branch is reset and cleaned.
    /// Inputs:
    ///  - remoteUris: ["https://example.com/repo.git"]
    ///  - requestedRefs: ["main"] (found as Commit)
    ///  - resetToRemote: true
    /// Expected:
    ///  - RunGitCommandAsync called to get upstream (for-each-ref), then to reset --hard upstream, and clean -fdqx .
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task PrepareCloneInternalAsync_ResetToRemote_RunsResetAndClean()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupGet(v => v.TmpPath).Returns(new NativePath("tmp"));

        var gitRepoCloner = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        // Force clone path to not exist to avoid cleanup/reset in PrepareCloneInternal and simplify call verifications
        fileSystem.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(false);

        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);

        // Clone in PrepareCloneInternal
        var telemetryScope = new Mock<ITelemetryScope>(MockBehavior.Loose);
        telemetryScope.Setup(s => s.SetSuccess());
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        telemetry.SetupSequence(t => t.RecordGitOperation(It.IsAny<TrackedGitOperation>(), It.IsAny<string>()))
            .Returns(telemetryScope.Object); // Clone
        gitRepoCloner.Setup(c => c.CloneNoCheckoutAsync(It.IsAny<string>(), It.IsAny<string>(), null)).Returns(Task.CompletedTask);

        // Requested ref found as Commit
        localGitClient
            .Setup(c => c.GetRefType(It.IsAny<string>(), "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitObjectType.Commit);

        // After checkout, resetToRemote flow
        localGitClient
            .Setup(c => c.RunGitCommandAsync(
                It.IsAny<string>(),
                It.Is<string[]>(a => a.Length >= 3 && a[0] == "for-each-ref" && a[2].StartsWith("refs/heads/")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = " origin/main \n" });

        localGitClient
            .Setup(c => c.RunGitCommandAsync(It.IsAny<string>(), It.Is<string[]>(a => a.Length == 3 && a[0] == "reset" && a[1] == "--hard" && a[2] == "origin/main"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        localGitClient
            .Setup(c => c.RunGitCommandAsync(It.IsAny<string>(), It.Is<string[]>(a => a.Length == 3 && a[0] == "clean" && a[1] == "-fdqx" && a[2] == "."), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        var localRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var repo = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        localRepoFactory.Setup(f => f.Create(It.IsAny<NativePath>())).Returns(repo.Object);
        repo.Setup(r => r.CheckoutAsync("main")).Returns(Task.CompletedTask);

        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new TestCloneManager(vmrInfo.Object, gitRepoCloner.Object, localGitClient.Object, localRepoFactory.Object, telemetry.Object, fileSystem.Object, logger.Object);

        // Act
        var resultRepo = await sut.InvokePrepareCloneInternalAsync(
            dirName: "repo",
            remoteUris: new[] { "https://example.com/repo.git" },
            requestedRefs: new[] { "main" },
            checkoutRef: "main",
            resetToRemote: true,
            cancellationToken: CancellationToken.None);

        // Assert
        resultRepo.Should().BeSameAs(repo.Object);
        localGitClient.Verify(c => c.RunGitCommandAsync(It.IsAny<string>(), It.Is<string[]>(a => a[0] == "for-each-ref" && a.Any(s => s.Contains("refs/heads/main"))), It.IsAny<CancellationToken>()), Times.Once);
        localGitClient.Verify(c => c.RunGitCommandAsync(It.IsAny<string>(), It.Is<string[]>(a => a.SequenceEqual(new[] { "reset", "--hard", "origin/main" })), It.IsAny<CancellationToken>()), Times.Once);
        localGitClient.Verify(c => c.RunGitCommandAsync(It.IsAny<string>(), It.Is<string[]>(a => a.SequenceEqual(new[] { "clean", "-fdqx", "." })), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.CheckoutAsync("main"), Times.Once);
    }

    /// <summary>
    /// Validates that refs beginning with the EmptyGitObject are filtered out and thus no ref verification calls are made.
    /// Inputs:
    ///  - remoteUris: ["https://example.com/repo.git"]
    ///  - requestedRefs: ["4b825dc642"] (prefix of EmptyGitObject)
    /// Expected:
    ///  - ILocalGitClient.GetRefType is never called.
    ///  - Checkout is still performed successfully.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task PrepareCloneInternalAsync_RequestedRefFilteredOutByEmptyGitObjectPrefix_SkipsRefVerification()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupGet(v => v.TmpPath).Returns(new NativePath("tmp"));

        var gitRepoCloner = new Mock<IGitRepoCloner>(MockBehavior.Strict);

        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystem.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(false);

        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);

        var telemetryScope = new Mock<ITelemetryScope>(MockBehavior.Loose);
        telemetryScope.Setup(s => s.SetSuccess());
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        telemetry.Setup(t => t.RecordGitOperation(It.IsAny<TrackedGitOperation>(), It.IsAny<string>())).Returns(telemetryScope.Object);

        gitRepoCloner.Setup(c => c.CloneNoCheckoutAsync(It.IsAny<string>(), It.IsAny<string>(), null)).Returns(Task.CompletedTask);

        var localRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var repo = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        localRepoFactory.Setup(f => f.Create(It.IsAny<NativePath>())).Returns(repo.Object);
        repo.Setup(r => r.CheckoutAsync("main")).Returns(Task.CompletedTask);

        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new TestCloneManager(vmrInfo.Object, gitRepoCloner.Object, localGitClient.Object, localRepoFactory.Object, telemetry.Object, fileSystem.Object, logger.Object);

        // Act
        var resultRepo = await sut.InvokePrepareCloneInternalAsync(
            dirName: "repo",
            remoteUris: new[] { "https://example.com/repo.git" },
            requestedRefs: new[] { "4b825dc642" },
            checkoutRef: "main",
            resetToRemote: false,
            cancellationToken: CancellationToken.None);

        // Assert
        resultRepo.Should().BeSameAs(repo.Object);
        localGitClient.Verify(c => c.GetRefType(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.CheckoutAsync("main"), Times.Once);
    }

    /// <summary>
    /// Ensures that when the provided CancellationToken is already canceled, the operation throws OperationCanceledException.
    /// Inputs:
    ///  - cancellationToken: canceled
    ///  - remoteUris: ["https://example.com/repo.git"]
    ///  - requestedRefs: ["main"]
    /// Expected:
    ///  - OperationCanceledException is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task PrepareCloneInternalAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var gitRepoCloner = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var telemetryScope = new Mock<ITelemetryScope>(MockBehavior.Loose);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Loose);
        telemetry.Setup(t => t.RecordGitOperation(It.IsAny<TrackedGitOperation>(), It.IsAny<string>())).Returns(telemetryScope.Object);

        var sut = new TestCloneManager(vmrInfo.Object, gitRepoCloner.Object, localGitClient.Object, localRepoFactory.Object, telemetry.Object, fileSystem.Object, logger.Object);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await sut.InvokePrepareCloneInternalAsync(
            dirName: "repo",
            remoteUris: new[] { "https://example.com/repo.git" },
            requestedRefs: new[] { "main" },
            checkoutRef: "main",
            resetToRemote: false,
            cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Ensures that when the target clone directory does not exist,
    /// the method clones the repository without fetching, records telemetry as successful, and returns the clone path.
    /// Inputs:
    ///  - performCleanup: false
    ///  - _fileSystem.DirectoryExists: false (no existing clone)
    /// Expected:
    ///  - IGitRepoCloner.CloneNoCheckoutAsync is called once with the computed clone path
    ///  - ILocalGitClient.UpdateRemoteAsync is never called
    ///  - Returns the expected clone path
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task PrepareCloneInternal_NewClone_ClonesAndReturnsPath()
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Loose);
        var clonerMock = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var gitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var gitFactoryMock = new Mock<ILocalGitRepoFactory>(MockBehavior.Loose);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var telemetryScopeMock = new Mock<ITelemetryScope>(MockBehavior.Strict);
        var telemetryMock = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        telemetryMock
            .Setup(t => t.RecordGitOperation(It.IsAny<TrackedGitOperation>(), It.IsAny<string>()))
            .Returns(telemetryScopeMock.Object);
        telemetryScopeMock.Setup(s => s.SetSuccess());
        telemetryScopeMock.Setup(s => s.Dispose());

        var baseTmp = new NativePath("tmp");
        var sut = new TestableCloneManager(vmrInfoMock.Object, clonerMock.Object, gitClientMock.Object, gitFactoryMock.Object, telemetryMock.Object, fileSystemMock.Object, loggerMock.Object, baseTmp);

        var dirName = "repo1";
        var remoteUri = "https://example.org/repo.git";
        var expectedPath = sut.ExpectedClonePath(dirName).ToString();

        fileSystemMock
            .Setup(fs => fs.DirectoryExists(It.IsAny<string>()))
            .Returns(false);

        clonerMock
            .Setup(c => c.CloneNoCheckoutAsync(remoteUri, expectedPath, null))
            .Returns(Task.CompletedTask);

        // Act
        var result = await sut.InvokePrepareCloneInternal(remoteUri, dirName, performCleanup: false, CancellationToken.None);

        // Assert
        result.ToString().Should().Be(expectedPath);
        clonerMock.Verify(c => c.CloneNoCheckoutAsync(remoteUri, expectedPath, null), Times.Once);
        gitClientMock.Verify(c => c.UpdateRemoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        telemetryMock.Verify(t => t.RecordGitOperation(TrackedGitOperation.Clone, remoteUri), Times.Once);
        telemetryScopeMock.Verify(s => s.SetSuccess(), Times.Once);
    }

    /// <summary>
    /// Verifies that when a clone already exists and cleanup is requested, the repository is reset successfully,
    /// then the remote is added/ensured and a fetch (remote update) is performed.
    /// Inputs:
    ///  - performCleanup: true
    ///  - _fileSystem.DirectoryExists: true (existing clone)
    ///  - _localGitRepo.RunGitCommandAsync("reset", "--hard"): success
    ///  - _localGitRepo.AddRemoteIfMissingAsync: returns "origin"
    /// Expected:
    ///  - Reset is executed and succeeds
    ///  - UpdateRemoteAsync is called for "origin"
    ///  - Telemetry for fetch is marked as success
    ///  - Cloning is not attempted
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task PrepareCloneInternal_ExistingCloneWithCleanupSucceeds_UpdatesRemote()
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Loose);
        var clonerMock = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var gitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var gitFactoryMock = new Mock<ILocalGitRepoFactory>(MockBehavior.Loose);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var telemetryScopeMock = new Mock<ITelemetryScope>(MockBehavior.Strict);
        var telemetryMock = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        telemetryMock
            .Setup(t => t.RecordGitOperation(It.IsAny<TrackedGitOperation>(), It.IsAny<string>()))
            .Returns(telemetryScopeMock.Object);
        telemetryScopeMock.Setup(s => s.SetSuccess());
        telemetryScopeMock.Setup(s => s.Dispose());

        var baseTmp = new NativePath("tmp");
        var sut = new TestableCloneManager(vmrInfoMock.Object, clonerMock.Object, gitClientMock.Object, gitFactoryMock.Object, telemetryMock.Object, fileSystemMock.Object, loggerMock.Object, baseTmp);

        var dirName = "repo2";
        var remoteUri = "https://example.org/repo.git";
        var expectedPath = sut.ExpectedClonePath(dirName).ToString();

        fileSystemMock
            .Setup(fs => fs.DirectoryExists(It.IsAny<string>()))
            .Returns(true);

        gitClientMock
            .Setup(c => c.RunGitCommandAsync(expectedPath, It.Is<string[]>(a => a.Length >= 2 && a[0] == "reset" && a[1] == "--hard"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        gitClientMock
            .Setup(c => c.AddRemoteIfMissingAsync(expectedPath, remoteUri, It.IsAny<CancellationToken>()))
            .ReturnsAsync("origin");

        gitClientMock
            .Setup(c => c.UpdateRemoteAsync(expectedPath, "origin", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await sut.InvokePrepareCloneInternal(remoteUri, dirName, performCleanup: true, CancellationToken.None);

        // Assert
        result.ToString().Should().Be(expectedPath);
        clonerMock.Verify(c => c.CloneNoCheckoutAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        gitClientMock.Verify(c => c.RunGitCommandAsync(expectedPath, It.Is<string[]>(a => a[0] == "reset" && a[1] == "--hard"), It.IsAny<CancellationToken>()), Times.Once);
        gitClientMock.Verify(c => c.AddRemoteIfMissingAsync(expectedPath, remoteUri, It.IsAny<CancellationToken>()), Times.Once);
        gitClientMock.Verify(c => c.UpdateRemoteAsync(expectedPath, "origin", It.IsAny<CancellationToken>()), Times.Once);
        telemetryMock.Verify(t => t.RecordGitOperation(TrackedGitOperation.Fetch, remoteUri), Times.Once);
        telemetryScopeMock.Verify(s => s.SetSuccess(), Times.Once);
    }

    /// <summary>
    /// Ensures that when cleanup fails on an existing clone, the directory is deleted and the repository is re-cloned.
    /// Inputs:
    ///  - performCleanup: true
    ///  - _fileSystem.DirectoryExists: true on first attempt, then false on re-try
    ///  - _localGitRepo.RunGitCommandAsync("reset", "--hard"): failure (ExitCode != 0)
    /// Expected:
    ///  - _fileSystem.DeleteDirectory is called with recursive: true
    ///  - IGitRepoCloner.CloneNoCheckoutAsync is called once in the re-try flow
    ///  - No remote update is performed
    ///  - Returns the expected clone path
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task PrepareCloneInternal_ExistingCloneCleanupFails_Reclones()
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Loose);
        var clonerMock = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var gitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var gitFactoryMock = new Mock<ILocalGitRepoFactory>(MockBehavior.Loose);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var telemetryScopeMock = new Mock<ITelemetryScope>(MockBehavior.Strict);
        var telemetryMock = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        telemetryMock
            .Setup(t => t.RecordGitOperation(It.IsAny<TrackedGitOperation>(), It.IsAny<string>()))
            .Returns(telemetryScopeMock.Object);
        telemetryScopeMock.Setup(s => s.SetSuccess());
        telemetryScopeMock.Setup(s => s.Dispose());

        var baseTmp = new NativePath("tmp");
        var sut = new TestableCloneManager(vmrInfoMock.Object, clonerMock.Object, gitClientMock.Object, gitFactoryMock.Object, telemetryMock.Object, fileSystemMock.Object, loggerMock.Object, baseTmp);

        var dirName = "repo3";
        var remoteUri = "https://example.org/repo.git";
        var expectedPath = sut.ExpectedClonePath(dirName).ToString();

        fileSystemMock
            .SetupSequence(fs => fs.DirectoryExists(It.IsAny<string>()))
            .Returns(true)   // first attempt: existing clone
            .Returns(false); // second attempt (re-try): no clone exists -> clone

        gitClientMock
            .Setup(c => c.RunGitCommandAsync(expectedPath, It.Is<string[]>(a => a.Length >= 2 && a[0] == "reset" && a[1] == "--hard"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 1 }); // failure

        fileSystemMock
            .Setup(fs => fs.DeleteDirectory(expectedPath, true));

        clonerMock
            .Setup(c => c.CloneNoCheckoutAsync(remoteUri, expectedPath, null))
            .Returns(Task.CompletedTask);

        // Act
        var result = await sut.InvokePrepareCloneInternal(remoteUri, dirName, performCleanup: true, CancellationToken.None);

        // Assert
        result.ToString().Should().Be(expectedPath);
        fileSystemMock.Verify(fs => fs.DeleteDirectory(expectedPath, true), Times.Once);
        clonerMock.Verify(c => c.CloneNoCheckoutAsync(remoteUri, expectedPath, null), Times.Once);
        gitClientMock.Verify(c => c.UpdateRemoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Validates that when the existing directory is not a valid git repository (AddRemoteIfMissingAsync throws a specific message),
    /// the directory is deleted and the repository is re-cloned.
    /// Inputs:
    ///  - performCleanup: false
    ///  - _fileSystem.DirectoryExists: true on first attempt, then false on re-try
    ///  - _localGitRepo.AddRemoteIfMissingAsync: throws "fatal: not a git repository"
    /// Expected:
    ///  - _fileSystem.DeleteDirectory is called with recursive: true
    ///  - IGitRepoCloner.CloneNoCheckoutAsync is called once in the re-try flow
    ///  - No remote update is performed
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task PrepareCloneInternal_AddRemoteThrowsNotGitRepo_Reclones()
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Loose);
        var clonerMock = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var gitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var gitFactoryMock = new Mock<ILocalGitRepoFactory>(MockBehavior.Loose);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var telemetryScopeMock = new Mock<ITelemetryScope>(MockBehavior.Strict);
        var telemetryMock = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        telemetryMock
            .Setup(t => t.RecordGitOperation(It.IsAny<TrackedGitOperation>(), It.IsAny<string>()))
            .Returns(telemetryScopeMock.Object);
        telemetryScopeMock.Setup(s => s.SetSuccess());
        telemetryScopeMock.Setup(s => s.Dispose());

        var baseTmp = new NativePath("tmp");
        var sut = new TestableCloneManager(vmrInfoMock.Object, clonerMock.Object, gitClientMock.Object, gitFactoryMock.Object, telemetryMock.Object, fileSystemMock.Object, loggerMock.Object, baseTmp);

        var dirName = "repo4";
        var remoteUri = "https://example.org/repo.git";
        var expectedPath = sut.ExpectedClonePath(dirName).ToString();

        fileSystemMock
            .SetupSequence(fs => fs.DirectoryExists(It.IsAny<string>()))
            .Returns(true)   // existing dir -> will try AddRemoteIfMissing and fail
            .Returns(false); // re-try -> will clone

        gitClientMock
            .Setup(c => c.AddRemoteIfMissingAsync(expectedPath, remoteUri, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("fatal: not a git repository"));

        fileSystemMock
            .Setup(fs => fs.DeleteDirectory(expectedPath, true));

        clonerMock
            .Setup(c => c.CloneNoCheckoutAsync(remoteUri, expectedPath, null))
            .Returns(Task.CompletedTask);

        // Act
        var result = await sut.InvokePrepareCloneInternal(remoteUri, dirName, performCleanup: false, CancellationToken.None);

        // Assert
        result.ToString().Should().Be(expectedPath);
        fileSystemMock.Verify(fs => fs.DeleteDirectory(expectedPath, true), Times.Once);
        clonerMock.Verify(c => c.CloneNoCheckoutAsync(remoteUri, expectedPath, null), Times.Once);
        gitClientMock.Verify(c => c.UpdateRemoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Confirms that once a remote has been processed and marked up-to-date with an existing path,
    /// subsequent calls short-circuit and reuse the cached path without any git operations.
    /// Inputs:
    ///  - First call: _fileSystem.DirectoryExists: false (clone)
    ///  - Second call: _fileSystem.DirectoryExists: true (cached path exists)
    /// Expected:
    ///  - First call clones the repository
    ///  - Second call performs no clone/update and returns immediately with the same path
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task PrepareCloneInternal_UpToDateAndPathExists_ReturnsCachedWithoutWork()
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Loose);
        var clonerMock = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var gitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var gitFactoryMock = new Mock<ILocalGitRepoFactory>(MockBehavior.Loose);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var telemetryScopeMock = new Mock<ITelemetryScope>(MockBehavior.Strict);
        var telemetryMock = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        telemetryMock
            .Setup(t => t.RecordGitOperation(It.IsAny<TrackedGitOperation>(), It.IsAny<string>()))
            .Returns(telemetryScopeMock.Object);
        telemetryScopeMock.Setup(s => s.SetSuccess());
        telemetryScopeMock.Setup(s => s.Dispose());

        var baseTmp = new NativePath("tmp");
        var sut = new TestableCloneManager(vmrInfoMock.Object, clonerMock.Object, gitClientMock.Object, gitFactoryMock.Object, telemetryMock.Object, fileSystemMock.Object, loggerMock.Object, baseTmp);

        var dirName = "repo5";
        var remoteUri = "https://example.org/repo.git";
        var expectedPath = sut.ExpectedClonePath(dirName).ToString();

        fileSystemMock
            .SetupSequence(fs => fs.DirectoryExists(It.IsAny<string>()))
            .Returns(false)  // first call -> clone
            .Returns(true);  // second call -> cached path exists

        clonerMock
            .Setup(c => c.CloneNoCheckoutAsync(remoteUri, expectedPath, null))
            .Returns(Task.CompletedTask);

        // Act
        var first = await sut.InvokePrepareCloneInternal(remoteUri, dirName, performCleanup: false, CancellationToken.None);

        // reset invocation tracking but keep setups
        clonerMock.Invocations.Clear();
        gitClientMock.Invocations.Clear();

        var second = await sut.InvokePrepareCloneInternal(remoteUri, dirName, performCleanup: false, CancellationToken.None);

        // Assert
        first.ToString().Should().Be(expectedPath);
        second.ToString().Should().Be(expectedPath);
        clonerMock.Verify(c => c.CloneNoCheckoutAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        gitClientMock.Verify(c => c.AddRemoteIfMissingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        gitClientMock.Verify(c => c.UpdateRemoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Validates that a pre-canceled token causes the method to throw OperationCanceledException immediately,
    /// without invoking any file or git operations.
    /// Inputs:
    ///  - CancellationToken: already canceled
    /// Expected:
    ///  - OperationCanceledException is thrown
    ///  - No clone, cleanup, or update operations are called
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void PrepareCloneInternal_CanceledToken_ThrowsImmediately()
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Loose);
        var clonerMock = new Mock<IGitRepoCloner>(MockBehavior.Strict);
        var gitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var gitFactoryMock = new Mock<ILocalGitRepoFactory>(MockBehavior.Loose);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var telemetryMock = new Mock<ITelemetryRecorder>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var baseTmp = new NativePath("tmp");
        var sut = new TestableCloneManager(vmrInfoMock.Object, clonerMock.Object, gitClientMock.Object, gitFactoryMock.Object, telemetryMock.Object, fileSystemMock.Object, loggerMock.Object, baseTmp);

        var dirName = "repo-cancel";
        var remoteUri = "https://example.org/repo.git";
        var canceled = new CancellationToken(true);

        // Act + Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await sut.InvokePrepareCloneInternal(remoteUri, dirName, performCleanup: false, canceled));

        clonerMock.Verify(c => c.CloneNoCheckoutAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        gitClientMock.Verify(c => c.RunGitCommandAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
        gitClientMock.Verify(c => c.AddRemoteIfMissingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        gitClientMock.Verify(c => c.UpdateRemoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that GetClonePath combines IVmrInfo.TmpPath and dirName using the native directory separator,
    /// avoiding duplicate separators and normalizing mixed-slash inputs.
    /// Inputs:
    ///  - Various tmp base paths with and without trailing separator.
    ///  - dirName with none/leading separator, empty, whitespace, and mixed slashes.
    /// Expected:
    ///  - Returned NativePath.Path equals the correctly combined and normalized string with at most one separator between parts.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(GetClonePathCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetClonePath_CombineWithTmpPath_UsesNativeSeparatorAndAvoidsDuplicateSeparators(string tmpBase, string dirName)
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupGet(v => v.TmpPath).Returns(new NativePath(tmpBase));

        var sut = new TestableCloneManager(
            vmrInfo.Object,
            new Mock<IGitRepoCloner>(MockBehavior.Loose).Object,
            new Mock<ILocalGitClient>(MockBehavior.Loose).Object,
            new Mock<ILocalGitRepoFactory>(MockBehavior.Loose).Object,
            new Mock<ITelemetryRecorder>(MockBehavior.Loose).Object,
            new Mock<IFileSystem>(MockBehavior.Loose).Object,
            new Mock<ILogger>(MockBehavior.Loose).Object);

        var expected = CombineExpected(tmpBase, dirName);

        // Act
        var result = sut.CallGetClonePath(dirName);

        // Assert
        result.Path.Should().Be(expected);
    }

    private static IEnumerable<TestCaseData> GetClonePathCases()
    {
        yield return new TestCaseData("tmp", "repo").SetName("NoTrailing_NoLeading_SingleSeparator");
        yield return new TestCaseData("tmp", "/repo").SetName("NoTrailing_LeadingSlash_SingleSeparator");
        yield return new TestCaseData("tmp/", "repo").SetName("Trailing_NoLeading_SingleSeparator");
        yield return new TestCaseData("tmp/", "/repo").SetName("Trailing_LeadingSlash_NoDoubleSeparator");
        yield return new TestCaseData("root/tmp", "sub/dir").SetName("MixedSlashes_RightForwardSlashes_Normalized");
        yield return new TestCaseData("root/tmp/", "sub\\dir").SetName("MixedSlashes_RightBackslashes_Normalized");
        yield return new TestCaseData("tmp", "").SetName("EmptyDirName_AppendsSeparator");
        yield return new TestCaseData("tmp/", "").SetName("EmptyDirName_WithTrailingBase_KeepsBase");
        yield return new TestCaseData("tmp", " ").SetName("WhitespaceOnlyDirName_CombinedWithSeparator");
        yield return new TestCaseData("tmp/", " leading").SetName("LeadingSpaceDirName_CombinedWithoutDoubleSeparator");
        yield return new TestCaseData("tmp", "\\repo").SetName("NoTrailing_LeadingBackslash_SingleSeparator");
        yield return new TestCaseData("root/tmp", "sub:dir").SetName("SpecialCharacters_NoValidation_Combined");
    }

    private static string CombineExpected(string left, string right)
    {
        var ds = Path.DirectorySeparatorChar;
        left = Normalize(left, ds);
        right = Normalize(right, ds);

        var leftEndsWith = left.EndsWith(ds.ToString(), StringComparison.Ordinal);
        var rightStartsWith = right.StartsWith(ds.ToString(), StringComparison.Ordinal);

        if (!leftEndsWith && !rightStartsWith)
        {
            return left + ds + right;
        }

        if (leftEndsWith ^ rightStartsWith)
        {
            return left + right;
        }

        // both true
        return left + (right.Length > 0 ? right.Substring(1) : string.Empty);
    }

    private static string Normalize(string s, char ds)
    {
        return ds == '/'
            ? s.Replace('\\', '/')
            : s.Replace('/', '\\');
    }

}
