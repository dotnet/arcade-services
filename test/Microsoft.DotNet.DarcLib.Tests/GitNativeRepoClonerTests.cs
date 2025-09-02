// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;


public class GitNativeRepoClonerTests
{
    private static ProcessExecutionResult CreateResult(int exitCode)
    {
        return new ProcessExecutionResult
        {
            ExitCode = exitCode,
            TimedOut = false,
            StandardError = string.Empty,
            StandardOutput = string.Empty
        };
    }

    /// <summary>
    /// Verifies that CloneNoCheckoutAsync always triggers a clone with the '--no-checkout' flag
    /// and does not perform a subsequent 'git checkout' step, regardless of whether gitDirectory is provided.
    /// Inputs:
    ///  - repoUri: a sample repository URI.
    ///  - targetDirectory: a sample target directory for the clone.
    ///  - gitDirectory: null to omit the --git-dir argument, or a non-null path to include it.
    /// Expected:
    ///  - IProcessManager.ExecuteGit is called once for the clone with arguments including '--no-checkout' and not '--recurse-submodules'.
    ///  - IProcessManager.ExecuteGit is called once for 'config core.longpaths true'.
    ///  - IProcessManager.ExecuteGit is never called with 'checkout' since commit is null for CloneNoCheckoutAsync.
    /// </summary>
    [TestCase(null)]
    [TestCase("C:\\temp\\.git")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CloneNoCheckoutAsync_GitDirectoryVariants_DisablesCheckoutAndInvokesConfig(string gitDirectory)
    {
        // Arrange
        var repoUri = "https://example.com/repo.git";
        var targetDirectory = "C:\\work\\repo";

        var gitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        gitClientMock
            .Setup(m => m.AddGitAuthHeader(
                It.IsAny<IList<string>>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);

        processManagerMock
            .Setup(m => m.ExecuteGit(
                It.Is<string>(wd => wd == Environment.CurrentDirectory),
                It.Is<IEnumerable<string>>(a =>
                    a.Contains("clone") &&
                    a.Contains("-q") &&
                    a.Contains("--no-checkout") &&
                    !a.Contains("--recurse-submodules") &&
                    a.Contains(repoUri) &&
                    a.Contains(targetDirectory) &&
                    (gitDirectory == null
                        ? !a.Contains("--git-dir")
                        : a.Contains("--git-dir") && a.Contains(gitDirectory))),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        processManagerMock
            .Setup(m => m.ExecuteGit(
                It.Is<string>(wd => wd == targetDirectory),
                It.Is<string[]>(args => args.Length == 3 && args[0] == "config" && args[1] == "core.longpaths" && args[2] == "true")))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);
        var sut = new GitNativeRepoCloner(gitClientMock.Object, processManagerMock.Object, loggerMock.Object);

        // Act
        await sut.CloneNoCheckoutAsync(repoUri, targetDirectory, gitDirectory);

        // Assert
        processManagerMock.Verify(m => m.ExecuteGit(
            Environment.CurrentDirectory,
            It.Is<IEnumerable<string>>(a =>
                a.Contains("clone") &&
                a.Contains("-q") &&
                a.Contains("--no-checkout") &&
                !a.Contains("--recurse-submodules") &&
                a.Contains(repoUri) &&
                a.Contains(targetDirectory) &&
                (gitDirectory == null
                    ? !a.Contains("--git-dir")
                    : a.Contains("--git-dir") && a.Contains(gitDirectory))),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        processManagerMock.Verify(m => m.ExecuteGit(
            It.Is<string>(wd => wd == targetDirectory),
            It.Is<string[]>(args => args.Length == 3 && args[0] == "config" && args[1] == "core.longpaths" && args[2] == "true")),
            Times.Once);

        processManagerMock.Verify(m => m.ExecuteGit(
            It.Is<string>(wd => wd == targetDirectory),
            It.Is<string[]>(args => args.Length >= 1 && args[0] == "checkout")),
            Times.Never);

        gitClientMock.Verify(m => m.AddGitAuthHeader(
            It.IsAny<IList<string>>(),
            It.IsAny<IDictionary<string, string>>(),
            It.Is<string>(u => u == repoUri)),
            Times.Once);
    }


    /// <summary>
    /// Ensures the constructor accepts valid dependency instances and performs no interactions with them.
    /// Inputs:
    ///  - localGitClient: a mocked ILocalGitClient (strict or loose).
    ///  - processManager: a mocked IProcessManager (strict or loose).
    ///  - logger: a mocked ILogger (strict or loose).
    /// Expected:
    ///  - Constructor completes without throwing.
    ///  - No calls are made to the provided dependencies during construction.
    ///  - The created instance is not null.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithValidDependencies_CreatesInstanceWithoutSideEffects(bool strictBehavior)
    {
        // Arrange
        var behavior = strictBehavior ? MockBehavior.Strict : MockBehavior.Loose;

        var gitClientMock = new Mock<ILocalGitClient>(behavior);
        var processManagerMock = new Mock<IProcessManager>(behavior);
        var loggerMock = new Mock<ILogger>(behavior);

        // Act
        var sut = new GitNativeRepoCloner(gitClientMock.Object, processManagerMock.Object, loggerMock.Object);

        // Assert
        sut.Should().NotBeNull();

        gitClientMock.VerifyNoOtherCalls();
        processManagerMock.VerifyNoOtherCalls();
        loggerMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that when commit is null:
    /// - If checkoutSubmodules is true, '--recurse-submodules' is passed to 'git clone' and '--no-checkout' is not.
    /// - If checkoutSubmodules is false, neither '--recurse-submodules' nor '--no-checkout' is passed.
    /// - '--git-dir' is included only when gitDirectory is provided.
    /// - The 'config core.longpaths true' command is executed.
    /// - No 'git checkout' is performed.
    /// Inputs:
    ///  - checkoutSubmodules: true/false to toggle submodule recursion.
    ///  - gitDirectory: null or a path to include '--git-dir'.
    /// Expected:
    ///  - Correct flags are passed to clone, config is executed, and no checkout occurs.
    /// </summary>
    [TestCase(true, null)]
    [TestCase(true, "C:\\temp\\.git")]
    [TestCase(false, null)]
    [TestCase(false, "C:\\temp\\.git")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task CloneAsync_CommitNull_FlagsBasedOnCheckoutSubmodulesAndNoCheckoutStep(bool checkoutSubmodules, string gitDirectory)
    {
        // Arrange
        var repoUri = "https://example.com/repo.git";
        var targetDirectory = "C:\\work\\repo";

        var gitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        gitClientMock
            .Setup(m => m.AddGitAuthHeader(
                It.IsAny<IList<string>>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);

        processManagerMock
            .Setup(m => m.ExecuteGit(
                It.Is<string>(wd => wd == Environment.CurrentDirectory),
                It.Is<IEnumerable<string>>(a =>
                    a != null &&
                    a.Contains("clone") &&
                    a.Contains("-q") &&
                    (checkoutSubmodules
                        ? a.Contains("--recurse-submodules") && !a.Contains("--no-checkout")
                        : !a.Contains("--recurse-submodules") && !a.Contains("--no-checkout")) &&
                    a.Contains(repoUri) &&
                    a.Contains(targetDirectory) &&
                    (gitDirectory == null
                        ? !a.Contains("--git-dir")
                        : a.Contains("--git-dir") && a.Contains(gitDirectory))),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        processManagerMock
            .Setup(m => m.ExecuteGit(
                It.Is<string>(wd => wd == targetDirectory),
                It.Is<string[]>(args => args.Length == 3 && args[0] == "config" && args[1] == "core.longpaths" && args[2] == "true")))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);
        var sut = new GitNativeRepoCloner(gitClientMock.Object, processManagerMock.Object, loggerMock.Object);

        // Act
        await sut.CloneAsync(repoUri, null, targetDirectory, checkoutSubmodules, gitDirectory);

        // Assert
        processManagerMock.Verify(m => m.ExecuteGit(
            Environment.CurrentDirectory,
            It.Is<IEnumerable<string>>(a =>
                a.Contains("clone") &&
                a.Contains("-q") &&
                (checkoutSubmodules
                    ? a.Contains("--recurse-submodules") && !a.Contains("--no-checkout")
                    : !a.Contains("--recurse-submodules") && !a.Contains("--no-checkout")) &&
                a.Contains(repoUri) &&
                a.Contains(targetDirectory) &&
                (gitDirectory == null
                    ? !a.Contains("--git-dir")
                    : a.Contains("--git-dir") && a.Contains(gitDirectory))),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        processManagerMock.Verify(m => m.ExecuteGit(
            It.Is<string>(wd => wd == targetDirectory),
            It.Is<string[]>(args => args.Length == 3 && args[0] == "config" && args[1] == "core.longpaths" && args[2] == "true")),
            Times.Once);

        processManagerMock.Verify(m => m.ExecuteGit(
            It.Is<string>(wd => wd == targetDirectory),
            It.Is<string[]>(args => args.Length >= 1 && args[0] == "checkout")),
            Times.Never);

        gitClientMock.Verify(m => m.AddGitAuthHeader(
            It.IsAny<IList<string>>(),
            It.IsAny<IDictionary<string, string>>(),
            It.Is<string>(u => u == repoUri)),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when commit is provided:
    /// - '--no-checkout' is always passed to 'git clone' regardless of checkoutSubmodules.
    /// - '--recurse-submodules' is not passed.
    /// - '--git-dir' is included only when gitDirectory is provided.
    /// - The 'config core.longpaths true' command is executed.
    /// - A 'git checkout {commit}' is performed.
    /// Inputs:
    ///  - checkoutSubmodules: true/false (should not affect clone flags when commit is provided).
    ///  - commit: non-null commit to checkout after cloning.
    ///  - gitDirectory: null or a path to include '--git-dir'.
    /// Expected:
    ///  - Clone includes '--no-checkout', not '--recurse-submodules'; config runs; checkout runs with provided commit.
    /// </summary>
    [TestCase(true, "abc123", null)]
    [TestCase(true, "abc123", "C:\\temp\\.git")]
    [TestCase(false, "abc123", null)]
    [TestCase(false, "abc123", "C:\\temp\\.git")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task CloneAsync_CommitProvided_UsesNoCheckoutAndPerformsCheckout(bool checkoutSubmodules, string commit, string gitDirectory)
    {
        // Arrange
        var repoUri = "https://example.com/repo.git";
        var targetDirectory = "C:\\work\\repo";

        var gitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        gitClientMock
            .Setup(m => m.AddGitAuthHeader(
                It.IsAny<IList<string>>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);

        processManagerMock
            .Setup(m => m.ExecuteGit(
                It.Is<string>(wd => wd == Environment.CurrentDirectory),
                It.Is<IEnumerable<string>>(a =>
                    a != null &&
                    a.Contains("clone") &&
                    a.Contains("-q") &&
                    a.Contains("--no-checkout") &&
                    !a.Contains("--recurse-submodules") &&
                    a.Contains(repoUri) &&
                    a.Contains(targetDirectory) &&
                    (gitDirectory == null
                        ? !a.Contains("--git-dir")
                        : a.Contains("--git-dir") && a.Contains(gitDirectory))),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        processManagerMock
            .Setup(m => m.ExecuteGit(
                It.Is<string>(wd => wd == targetDirectory),
                It.Is<string[]>(args => args.Length == 3 && args[0] == "config" && args[1] == "core.longpaths" && args[2] == "true")))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        processManagerMock
            .Setup(m => m.ExecuteGit(
                It.Is<string>(wd => wd == targetDirectory),
                It.Is<string[]>(args => args.Length == 2 && args[0] == "checkout" && args[1] == commit)))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);
        var sut = new GitNativeRepoCloner(gitClientMock.Object, processManagerMock.Object, loggerMock.Object);

        // Act
        await sut.CloneAsync(repoUri, commit, targetDirectory, checkoutSubmodules, gitDirectory);

        // Assert
        processManagerMock.Verify(m => m.ExecuteGit(
            Environment.CurrentDirectory,
            It.Is<IEnumerable<string>>(a =>
                a.Contains("clone") &&
                a.Contains("-q") &&
                a.Contains("--no-checkout") &&
                !a.Contains("--recurse-submodules") &&
                a.Contains(repoUri) &&
                a.Contains(targetDirectory) &&
                (gitDirectory == null
                    ? !a.Contains("--git-dir")
                    : a.Contains("--git-dir") && a.Contains(gitDirectory))),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        processManagerMock.Verify(m => m.ExecuteGit(
            It.Is<string>(wd => wd == targetDirectory),
            It.Is<string[]>(args => args.Length == 3 && args[0] == "config" && args[1] == "core.longpaths" && args[2] == "true")),
            Times.Once);

        processManagerMock.Verify(m => m.ExecuteGit(
            It.Is<string>(wd => wd == targetDirectory),
            It.Is<string[]>(args => args.Length == 2 && args[0] == "checkout" && args[1] == commit)),
            Times.Once);

        gitClientMock.Verify(m => m.AddGitAuthHeader(
            It.IsAny<IList<string>>(),
            It.IsAny<IDictionary<string, string>>(),
            It.Is<string>(u => u == repoUri)),
            Times.Once);
    }

    /// <summary>
    /// Ensures that when the 'git config core.longpaths true' step fails (non-zero exit code),
    /// CloneNoCheckoutAsync logs a warning and still completes without throwing.
    /// Inputs:
    ///  - repoUri: a sample repo.
    ///  - targetDirectory: a sample target directory.
    ///  - gitDirectory: null or a path; both are supported.
    /// Expected:
    ///  - A warning is logged.
    ///  - Method completes successfully (no exception).
    ///  - Checkout is not invoked.
    /// </summary>
    [TestCase(null)]
    [TestCase("C:\\alt\\.git")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CloneNoCheckoutAsync_ConfigFails_LogsWarningAndContinues(string gitDirectory)
    {
        // Arrange
        var repoUri = "https://example.com/repo.git";
        var targetDirectory = "C:\\work\\repo";

        var gitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        gitClientMock
            .Setup(m => m.AddGitAuthHeader(
                It.IsAny<IList<string>>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);

        processManagerMock
            .Setup(m => m.ExecuteGit(
                It.Is<string>(wd => wd == Environment.CurrentDirectory),
                It.Is<IEnumerable<string>>(a =>
                    a.Contains("clone") &&
                    a.Contains("-q") &&
                    a.Contains("--no-checkout") &&
                    !a.Contains("--recurse-submodules") &&
                    a.Contains(repoUri) &&
                    a.Contains(targetDirectory) &&
                    (gitDirectory == null
                        ? !a.Contains("--git-dir")
                        : a.Contains("--git-dir") && a.Contains(gitDirectory))),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        processManagerMock
            .Setup(m => m.ExecuteGit(
                It.Is<string>(wd => wd == targetDirectory),
                It.Is<string[]>(args => args.Length == 3 && args[0] == "config" && args[1] == "core.longpaths" && args[2] == "true")))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 1 });

        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new GitNativeRepoCloner(gitClientMock.Object, processManagerMock.Object, loggerMock.Object);

        // Act
        await sut.CloneNoCheckoutAsync(repoUri, targetDirectory, gitDirectory);

        // Assert
        loggerMock.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == LogLevel.Warning),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, t) => state.ToString()!.Contains("Failed to set core.longpaths to true")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);

        processManagerMock.Verify(m => m.ExecuteGit(
            It.Is<string>(wd => wd == targetDirectory),
            It.Is<string[]>(args => args.Length >= 1 && args[0] == "checkout")),
            Times.Never);
    }

    /// <summary>
    /// Validates that when the initial 'git clone' step fails (non-zero exit code),
    /// CloneNoCheckoutAsync throws a ProcessFailedException and does not proceed to the config or checkout steps.
    /// Inputs:
    ///  - repoUri: a sample repo.
    ///  - targetDirectory: a sample target directory.
    ///  - gitDirectory: null (omitted).
    /// Expected:
    ///  - ProcessFailedException is thrown.
    ///  - No subsequent 'config' or 'checkout' commands are executed.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CloneNoCheckoutAsync_CloneFails_ThrowsProcessFailedException()
    {
        // Arrange
        var repoUri = "https://example.com/repo.git";
        var targetDirectory = "C:\\work\\repo";

        var gitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        gitClientMock
            .Setup(m => m.AddGitAuthHeader(
                It.IsAny<IList<string>>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);

        processManagerMock
            .Setup(m => m.ExecuteGit(
                It.Is<string>(wd => wd == Environment.CurrentDirectory),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 1 });

        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);
        var sut = new GitNativeRepoCloner(gitClientMock.Object, processManagerMock.Object, loggerMock.Object);

        // Act & Assert
        Assert.ThrowsAsync<ProcessFailedException>(() => sut.CloneNoCheckoutAsync(repoUri, targetDirectory, null));

        processManagerMock.Verify(m => m.ExecuteGit(
            It.Is<string>(wd => wd == targetDirectory),
            It.IsAny<string[]>()),
            Times.Never);
    }
}
