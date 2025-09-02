// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Maestro;
using Maestro.Common;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.UnitTests;


public class LocalGitClientTests
{
    /// <summary>
    /// Ensures the constructor accepts all required dependencies and creates a usable instance without throwing.
    /// Inputs:
    ///  - Five mocked dependencies (IRemoteTokenProvider, ITelemetryRecorder, IProcessManager, IFileSystem, ILogger).
    ///  - Mock behavior toggled between Strict and Loose.
    /// Expected:
    ///  - No exception is thrown and a non-null LocalGitClient instance is created.
    /// </summary>
    [TestCase(true, TestName = "Constructor_WithStrictMocks_InstanceCreated")]
    [TestCase(false, TestName = "Constructor_WithLooseMocks_InstanceCreated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_WithAllDependenciesProvided_InstanceCreated(bool useStrict)
    {
        // Arrange
        var behavior = useStrict ? MockBehavior.Strict : MockBehavior.Loose;

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(behavior).Object;
        var telemetryRecorder = new Mock<ITelemetryRecorder>(behavior).Object;
        var processManager = new Mock<IProcessManager>(behavior).Object;
        var fileSystem = new Mock<IFileSystem>(behavior).Object;
        var logger = new Mock<ILogger>(behavior).Object;

        // Act
        var client = new LocalGitClient(remoteTokenProvider, telemetryRecorder, processManager, fileSystem, logger);

        // Assert
        // Intentionally avoiding external assertion frameworks per constraints.
        // Test passes if no exception is thrown and instance is created.
        if (client == null)
        {
            throw new Exception("LocalGitClient instance should not be null after construction.");
        }
    }

    /// <summary>
    /// Partial test documenting behavior when a non-empty branch is provided.
    /// This path is exercised by simulating a successful git invocation via IProcessManager.
    /// Inputs:
    ///  - repoPath: any path.
    ///  - relativeFilePath: any file path.
    ///  - branch: non-empty string.
    /// Expected:
    ///  - GetFileContentsAsync returns the StandardOutput provided by the mocked IProcessManager for git.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetFileContentsAsync_BranchProvided_ReturnsContentFromGitMock()
    {
        // Arrange
        var repoPath = "/repo/path";
        var relativeFilePath = "dir/file.txt";
        var branch = "main";
        var expectedContent = "file-content-from-git";

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Loose);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Loose);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var successResult = new ProcessExecutionResult
        {
            ExitCode = 0,
            TimedOut = false,
            StandardOutput = expectedContent,
            StandardError = string.Empty
        };

        // Cover common ExecuteGit overloads to ensure whichever is used by GetFileContentsAsync returns our content
        processManager
            .Setup(m => m.ExecuteGit(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResult);

        processManager
            .Setup(m => m.ExecuteGit(
                It.IsAny<string>(),
                It.IsAny<string[]>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResult);

        processManager
            .Setup(m => m.ExecuteGit(
                It.IsAny<string>(),
                It.IsAny<string[]>()))
            .ReturnsAsync(successResult);

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act: invoke GetFileContentsAsync via reflection to avoid tight coupling to exact signature
        var method = typeof(LocalGitClient)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .FirstOrDefault(m =>
            {
                if (m.Name != "GetFileContentsAsync")
                {
                    return false;
                }

                var p = m.GetParameters();
                return p.Length >= 3
                       && p[0].ParameterType == typeof(string)
                       && p[1].ParameterType == typeof(string)
                       && p[2].ParameterType == typeof(string);
            });

        if (method == null)
        {
            throw new Exception("Expected LocalGitClient.GetFileContentsAsync(repoPath, relativeFilePath, branch[, CancellationToken]) to exist.");
        }

        var parameters = method.GetParameters();
        object[] args;
        if (parameters.Length >= 4 && parameters[3].ParameterType == typeof(CancellationToken))
        {
            args = new object[] { repoPath, relativeFilePath, branch, CancellationToken.None };
        }
        else
        {
            args = new object[] { repoPath, relativeFilePath, branch };
        }

        var invokeResult = method.Invoke(sut, args);
        if (invokeResult is Task task)
        {
            await task.ConfigureAwait(false);
        }
        else
        {
            throw new Exception("Expected GetFileContentsAsync to return a Task or Task<string>.");
        }

        string actualContent = null;
        var taskType = invokeResult.GetType();
        if (taskType.IsGenericType && taskType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resProp = taskType.GetProperty("Result");
            actualContent = resProp?.GetValue(invokeResult) as string;
        }

        // Assert
        if (!string.Equals(expectedContent, actualContent, StringComparison.Ordinal))
        {
            throw new Exception($"Unexpected content. Expected: '{expectedContent}', Actual: '{actualContent ?? "<null>"}'");
        }
    }

    /// <summary>
    /// Verifies that CheckoutAsync passes the correct repo path and arguments ["checkout", ref]
    /// to IProcessManager.ExecuteGit and completes without throwing when the process succeeds.
    /// Inputs:
    ///  - Various repoPath and refToCheckout strings (including empty, whitespace, special chars, long).
    /// Expected:
    ///  - IProcessManager.ExecuteGit is invoked once with the exact arguments.
    ///  - No exception is thrown.
    /// </summary>
    [TestCaseSource(nameof(ValidCheckoutArgs))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CheckoutAsync_ValidInputs_ExecutesGitCheckoutAndDoesNotThrow(string repoPath, string refToCheckout)
    {
        // Arrange
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        processManagerMock
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.IsAny<string[]>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });

        var sut = CreateSut(processManagerMock.Object);

        // Act
        await sut.CheckoutAsync(repoPath, refToCheckout);

        // Assert
        processManagerMock.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(args => args.Length == 2 && args[0] == "checkout" && args[1] == refToCheckout),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))),
            Times.Once);
    }

    private static LocalGitClient CreateSut(IProcessManager processManager)
    {
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Loose).Object;
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Loose).Object;
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose).Object;
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;

        return new LocalGitClient(remoteTokenProvider, telemetryRecorder, processManager, fileSystem, logger);
    }

    private static IEnumerable<TestCaseData> ValidCheckoutArgs()
    {
        yield return new TestCaseData("/repo/path", "main").SetName("Repo_Normal_Ref_Main");
        yield return new TestCaseData("", "").SetName("Repo_Empty_Ref_Empty");
        yield return new TestCaseData("   ", " \t\n").SetName("Repo_Whitespace_Ref_Whitespace");
        yield return new TestCaseData("C:\\root\\repo", "feature/JIRA-1234_fix-äöü-ß").SetName("Repo_WindowsPath_Ref_SpecialChars");
        yield return new TestCaseData(new string('a', 1024), new string('b', 1024)).SetName("Repo_Long_Ref_Long");
    }

    private static IEnumerable<TestCaseData> FailureCases()
    {
        // Non-zero exit code (no timeout)
        yield return new TestCaseData("/repo/path", "dev", false, 1).SetName("Failure_NonZeroExit_NoTimeout");
        // Timed out (exit code may be -2 or any), ensure failure due to timeout
        yield return new TestCaseData("/repo/path", "release", true, -2).SetName("Failure_Timeout");
        // Edge inputs with failure
        yield return new TestCaseData("", "", false, 137).SetName("Failure_EdgeInputs_NonZeroExit");
    }

    /// <summary>
    /// Ensures DeleteBranchAsync invokes git with the correct arguments and completes without throwing
    /// when the underlying process succeeds.
    /// Inputs:
    ///  - repoPath and branchName values including typical, empty, whitespace, spaces-in-values, and unicode.
    /// Expected:
    ///  - IProcessManager.ExecuteGit is called once with ["branch", "-D", branchName].
    ///  - No exception is thrown by DeleteBranchAsync.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("/repo/path", "feature/x")]
    [TestCase("C:\\repo path", "branch with spaces")]
    [TestCase("", "")]
    [TestCase(" ", " ")]
    [TestCase("/r/😃", "weird-😃")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task DeleteBranchAsync_ValidInputs_ExecutesCorrectGitCommandAndDoesNotThrow(string repoPath, string branchName)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        var expectedArgs = new[] { "branch", "-D", branchName };
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        // Act
        await sut.DeleteBranchAsync(repoPath, branchName);

        // Assert
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))), Times.Once);
    }

    private static IEnumerable StageArgsCases
    {
        get
        {
            yield return new TestCaseData("repo", new string[0]).SetName("Repo_Normal_NoPaths");
            yield return new TestCaseData("repo", new[] { "file.txt" }).SetName("Repo_Normal_SinglePath");
            yield return new TestCaseData("", new[] { "dup", "dup" }).SetName("Repo_Empty_DuplicatePaths");
            yield return new TestCaseData("C:/repo with spaces", new[] { "a b.txt", "c\\d", "e/f", "weird,chars" }).SetName("Repo_WithSpaces_MultipleSpecialPaths");
        }
    }

    /// <summary>
    /// Verifies that StageAsync prepends "add" to the provided paths, forwards the cancellation token,
    /// and calls IProcessManager.ExecuteGit with the exact argument sequence.
    /// Inputs:
    ///  - repoPath: varied (normal, empty, with spaces).
    ///  - pathsToStage: empty, single item, duplicates, and items with special characters.
    /// Expected:
    ///  - ExecuteGit is invoked exactly once with arguments ["add", <pathsToStage...>] and the provided token.
    ///  - No exception is thrown when the process succeeds.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(StageArgsCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task StageAsync_ArgumentsPrependAdd_ExecutesGitAndSucceeds(string repoPath, string[] pathsToStage)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var expectedArgs = new[] { "add" }.Concat(pathsToStage).ToArray();
        var tokenSource = new CancellationTokenSource();
        var token = tokenSource.Token;

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(args => args.SequenceEqual(expectedArgs)),
                It.IsAny<Dictionary<string, string>>(),
                token))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        await sut.StageAsync(repoPath, pathsToStage, token);

        // Assert
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<IEnumerable<string>>(args => args.SequenceEqual(expectedArgs)),
            It.IsAny<Dictionary<string, string>>(),
            token), Times.Once);
    }

    /// <summary>
    /// Verifies that PullAsync calls 'git pull' via IProcessManager.ExecuteGit with the provided repo path and cancellation token,
    /// and completes without throwing when the process succeeds.
    /// Inputs:
    ///  - repoPath variations (including empty and whitespace)
    ///  - cancellation token states (canceled and not canceled)
    /// Expected:
    ///  - IProcessManager.ExecuteGit is invoked once with arguments ["pull"], null env vars, and the same CancellationToken.
    ///  - PullAsync does not throw any exception.
    /// </summary>
    [TestCaseSource(nameof(RepoAndCancelCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task PullAsync_ExecutesGitPullAndDoesNotThrow_WhenSucceeded(string repoPath, bool cancelled)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var token = new CancellationToken(cancelled);

        processManager
            .Setup(m => m.ExecuteGit(
                It.IsAny<string>(),
                It.IsAny<string[]>(),
                It.Is<Dictionary<string, string>>(d => d == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = "ok",
                StandardError = ""
            });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        await sut.PullAsync(repoPath, token);

        // Assert
        processManager.Verify(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 1 && a[0] == "pull"),
                It.Is<Dictionary<string, string>>(d => d == null),
                It.Is<CancellationToken>(ct => ct.Equals(token))),
            Times.Once);
    }

    /// <summary>
    /// Ensures that PullAsync throws when the underlying git command fails (non-zero exit code or timeout),
    /// and that the exception message contains the target repository path.
    /// Inputs:
    ///  - TimedOut (true/false), ExitCode (0/non-zero) combinations causing failure.
    /// Expected:
    ///  - An exception is thrown.
    ///  - Exception message contains "Failed to pull updates in {repoPath}".
    /// </summary>
    [TestCase(false, 1)]
    [TestCase(true, 0)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task PullAsync_CommandFailure_ThrowsWithRepoPathInMessage(bool timedOut, int exitCode)
    {
        // Arrange
        var repoPath = "/tmp/repo🚀";
        var token = CancellationToken.None;

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 1 && a[0] == "pull"),
                It.Is<Dictionary<string, string>>(d => d == null),
                token))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut,
                StandardOutput = "",
                StandardError = "err"
            });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        Exception caught = null;
        try
        {
            await sut.PullAsync(repoPath, token);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Assert
        if (caught == null)
        {
            throw new Exception("Expected an exception to be thrown when git pull fails, but none was thrown.");
        }

        if (!caught.Message.Contains($"Failed to pull updates in {repoPath}", StringComparison.Ordinal))
        {
            throw new Exception($"Exception message did not contain the expected repository path. Actual: {caught.Message}");
        }

        processManager.VerifyAll();
    }

    private static IEnumerable RepoAndCancelCases()
    {
        yield return new TestCaseData("C:\\repo", false);
        yield return new TestCaseData("", false);
        yield return new TestCaseData("   ", true);
        yield return new TestCaseData("/tmp/repo🚀", false);
        yield return new TestCaseData(new string('a', 512), true);
    }

    private static LocalGitClient CreateSut(IProcessManager processManager, ILogger logger)
    {
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Loose).Object;
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Loose).Object;
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose).Object;

        return new LocalGitClient(
            remoteTokenProvider,
            telemetryRecorder,
            processManager,
            fileSystem,
            logger);
    }

    /// <summary>
    /// Verifies the happy-path behavior when no auth token is available:
    ///  - ls-remote retrieves the URL.
    ///  - remote update and fetch are executed without auth-related args/env vars.
    ///  - Token provider is consulted twice (once per AddGitAuthHeader call).
    /// Inputs:
    ///  - Valid repoPath and remoteName.
    ///  - Token provider returns null token.
    /// Expected:
    ///  - Two git commands are executed with expected arguments and empty env vars.
    ///  - No auth header argument is injected.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task UpdateRemoteAsync_SuccessWithoutToken_InvokesUpdateAndFetchWithNoAuthHeaders()
    {
        // Arrange
        var repoPath = "/repo";
        var remoteName = "origin";
        var remoteUrl = "https://github.com/org/repo.git";
        var ct = CancellationToken.None;

        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        // First: ls-remote --get-url <remoteName>
        processManager
            .Setup(pm => pm.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.SequenceEqual(new[] { "ls-remote", "--get-url", remoteName })),
                It.Is<Dictionary<string, string>>(d => d == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = remoteUrl + Environment.NewLine
            });

        // Token provider returns null -> AddGitAuthHeader is a no-op (called twice)
        tokenProvider
            .Setup(tp => tp.GetTokenForRepositoryAsync(remoteUrl))
            .ReturnsAsync((string)null);
        tokenProvider
            .Setup(tp => tp.GetTokenForRepositoryAsync(remoteUrl))
            .ReturnsAsync((string)null);

        // Second: git remote update <remoteName> with no auth args/env vars
        processManager
            .Setup(pm => pm.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(a => a.SequenceEqual(new[] { "remote", "update", remoteName })),
                It.Is<Dictionary<string, string>>(env => env != null && env.Count == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        // Third: git fetch --tags --force <remoteName> with no auth args/env vars
        processManager
            .Setup(pm => pm.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(a => a.SequenceEqual(new[] { "fetch", "--tags", "--force", remoteName })),
                It.Is<Dictionary<string, string>>(env => env != null && env.Count == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        var sut = new LocalGitClient(tokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        // Act
        await sut.UpdateRemoteAsync(repoPath, remoteName, ct);

        // Assert
        processManager.VerifyAll();
        tokenProvider.Verify(tp => tp.GetTokenForRepositoryAsync(remoteUrl), Times.Exactly(2));
    }

    private static IEnumerable GetStagedFiles_Success_Cases()
    {
        yield return new TestCaseData(
            "file1.cs\nfile2.cs",
            new[] { "file1.cs", "file2.cs" })
            .SetName("GetStagedFiles_GitSucceeds_UnixNewlines_ReturnsLines");

        yield return new TestCaseData(
            " file1.cs \r\n \r\nfile2.cs\r\n",
            new[] { "file1.cs", "file2.cs" })
            .SetName("GetStagedFiles_GitSucceeds_MixedNewlinesAndWhitespace_TrimsAndRemovesEmpty");

        yield return new TestCaseData(
            "\r\n\r\n  a.txt  \n b.txt \n  a.txt  \n",
            new[] { "a.txt", "b.txt", "a.txt" })
            .SetName("GetStagedFiles_GitSucceeds_DuplicatesPreserved_OrderMaintained");

        yield return new TestCaseData(
            "\r\n \n \r \n",
            Array.Empty<string>())
            .SetName("GetStagedFiles_GitSucceeds_OnlyWhitespaceAndNewlines_ReturnsEmpty");
    }

    // TestCaseSource providing diverse repoPath inputs to exercise string edge cases
    public static IEnumerable<string> RepoPaths()
    {
        yield return "C:\\repo";
        yield return "";
        yield return "   ";
        yield return "/unix/style/path";
        yield return "C:\\path with spaces\\repo";
        yield return "C:\\path\\with\\unicode-测试";
        yield return new string('a', 260);
    }

    /// <summary>
    /// Ensures that HasStagedChangesAsync forwards the provided repoPath unchanged,
    /// and calls git with the exact expected arguments: diff --cached --exit-code --quiet.
    /// Inputs:
    ///  - Various repoPath strings including empty and whitespace.
    /// Expected:
    ///  - IProcessManager.ExecuteGit is invoked once with the same repoPath and exact arguments.
    ///  - No other calls are made to the process manager.
    /// </summary>
    [TestCase("C:\\repo", TestName = "HasStagedChangesAsync_WindowsAbsolutePath_ExecutesWithExactArgs")]
    [TestCase("/home/user/repo", TestName = "HasStagedChangesAsync_UnixAbsolutePath_ExecutesWithExactArgs")]
    [TestCase("", TestName = "HasStagedChangesAsync_EmptyPath_ExecutesWithExactArgs")]
    [TestCase("   ", TestName = "HasStagedChangesAsync_WhitespaceOnlyPath_ExecutesWithExactArgs")]
    [TestCase("C:\\répo🚀", TestName = "HasStagedChangesAsync_UnicodePath_ExecutesWithExactArgs")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task HasStagedChangesAsync_PassesExpectedGitArgumentsAndRepoPath_CallsExecuteGitWithExactParameters(string repoPath)
    {
        // Arrange
        var remoteConfigurationMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorderMock = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var processResult = new ProcessExecutionResult
        {
            TimedOut = false,
            ExitCode = 0
        };

        processManagerMock
            .Setup(m => m.ExecuteGit(It.IsAny<string>(), It.IsAny<string[]>()))
            .ReturnsAsync(processResult);

        var client = new LocalGitClient(
            remoteConfigurationMock.Object,
            telemetryRecorderMock.Object,
            processManagerMock.Object,
            fileSystemMock.Object,
            loggerMock.Object);

        var expectedArgs = new[] { "diff", "--cached", "--exit-code", "--quiet" };

        // Act
        var _ = await client.HasStagedChangesAsync(repoPath);

        // Assert
        processManagerMock.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))), Times.Once);
        processManagerMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Placeholder to validate the exception branch when an unknown GitRepoType is encountered.
    /// Inputs:
    ///  - A repoUri that would result in a repo type not handled by the switch expression.
    /// Expected:
    ///  - Exception is thrown with a message indicating the unsupported repo type.
    /// Notes:
    ///  - This path is unreachable via GitRepoUrlUtils.ParseTypeFromUri using public inputs,
    ///    and the static method cannot be mocked. If the implementation changes or becomes injectable,
    ///    replace this Inconclusive with a real test that forces an unknown enum value.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void AddGitAuthHeader_UnknownRepoType_ThrowsException_Partial()
    {
        Assert.Inconclusive("Cannot reach the default switch branch using GitRepoUrlUtils.ParseTypeFromUri; replace with a concrete test if the repo type detection becomes injectable.");
    }

    private static IEnumerable RunGitCommandAsync_ForwardingCases()
    {
        yield return new TestCaseData(
            "",
            Array.Empty<string>(),
            false
        ).SetName("EmptyRepoPath_EmptyArgs_NoCancellation");

        yield return new TestCaseData(
            " ",
            new[] { "status" },
            false
        ).SetName("WhitespaceRepoPath_SingleArg_NoCancellation");

        yield return new TestCaseData(
            "C:\\repo",
            new[] { "commit", "-m", "feat: add ✨ feature", "--allow-empty" },
            false
        ).SetName("WindowsPath_MultipleArgsWithUnicode_NoCancellation");

        yield return new TestCaseData(
            "/tmp/repo",
            new[] { "log", "--oneline", "--grep=fix\\s+bug", "--max-count", "1000" },
            true
        ).SetName("UnixPath_ArgsWithRegex_CancellationRequested");

        yield return new TestCaseData(
            "/a/b",
            new[] { "diff", "--name-only", "HEAD~1", "HEAD", "--", "path with spaces/file.txt" },
            false
        ).SetName("UnixPath_ArgsWithSpaces_NoCancellation");
    }

    /// <summary>
    /// Verifies that SetConfigValue invokes IProcessManager.ExecuteGit with the exact expected arguments
    /// and completes without throwing when the process succeeds.
    /// Inputs:
    ///  - repoPath, setting, value (including empty and special-character cases).
    /// Expected:
    ///  - ExecuteGit is called once with ("config", setting, value).
    ///  - No exception is thrown.
    /// </summary>
    [Test]
    [TestCase("repo", "user.name", "John Doe")]
    [TestCase("", "", "")]
    [TestCase("C:\\path with spaces\\repo", "http.proxy", "http://user:pa ss@host:8080")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task SetConfigValue_ExecutesGitWithExpectedArguments_Succeeds(string repoPath, string setting, string value)
    {
        // Arrange
        var remoteProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Loose);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Loose);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var successResult = new ProcessExecutionResult
        {
            ExitCode = 0,
            TimedOut = false,
            StandardOutput = string.Empty,
            StandardError = string.Empty
        };

        processManager
            .Setup(m => m.ExecuteGit(repoPath, "config", setting, value))
            .ReturnsAsync(successResult);

        var sut = new LocalGitClient(
            remoteProvider.Object,
            telemetry.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        await sut.SetConfigValue(repoPath, setting, value);

        // Assert
        processManager.Verify(m => m.ExecuteGit(repoPath, "config", setting, value), Times.Once);
    }

    /// <summary>
    /// Verifies that ResolveConflict calls 'git checkout' with the correct side (--ours/--theirs)
    /// followed by 'git add', and completes without throwing when both commands succeed.
    /// Inputs:
    ///  - repoPath: "repo/path"
    ///  - file: "conflicted.txt"
    ///  - ours: true or false
    /// Expected:
    ///  - _processManager.ExecuteGit is called with ("checkout", "--ours"/"--theirs", file) then ("add", file)
    ///  - No exception is thrown.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ResolveConflict_Success_CheckoutWithCorrectSideAndStage(bool ours)
    {
        // Arrange
        var repoPath = "repo/path";
        var file = "conflicted.txt";
        var expectedSide = ours ? "--ours" : "--theirs";

        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        processManagerMock
            .Setup(m => m.ExecuteGit(repoPath, "checkout", expectedSide, file))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });
        processManagerMock
            .Setup(m => m.ExecuteGit(repoPath, "add", file))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });

        var remoteTokenProviderMock = new Mock<IRemoteTokenProvider>(MockBehavior.Loose);
        var telemetryRecorderMock = new Mock<ITelemetryRecorder>(MockBehavior.Loose);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(
            remoteTokenProviderMock.Object,
            telemetryRecorderMock.Object,
            processManagerMock.Object,
            fileSystemMock.Object,
            loggerMock.Object);

        // Act
        await sut.ResolveConflict(repoPath, file, ours);

        // Assert
        processManagerMock.Verify(m => m.ExecuteGit(repoPath, "checkout", expectedSide, file), Times.Once);
        processManagerMock.Verify(m => m.ExecuteGit(repoPath, "add", file), Times.Once);
    }


    /// <summary>
    /// Ensures that CheckoutAsync throws ProcessFailedException when the git process fails (non-zero exit code or timeout),
    /// and that the exception message includes both the target ref and the repository path for diagnostics.
    /// Inputs:
    ///  - timedOut: indicates whether the process timed out.
    ///  - exitCode: process exit code (0 or non-zero).
    /// Expected:
    ///  - ProcessFailedException is thrown.
    ///  - Exception message contains "Failed to check out {ref} in {repoPath}".
    /// </summary>
    [TestCase(false, 1, TestName = "CheckoutAsync_CommandFailure_NonZeroExitCode_ThrowsWithMessage")]
    [TestCase(true, 0, TestName = "CheckoutAsync_CommandFailure_TimedOut_ThrowsWithMessage")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task CheckoutAsync_CommandFailure_ThrowsProcessFailedExceptionWithRefAndRepoInMessage(bool timedOut, int exitCode)
    {
        // Arrange
        var repoPath = "/r/with space/🚀";
        var refToCheckout = "feature/💡-branch";
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);

        processManagerMock
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(args => args.Length == 2 && args[0] == "checkout" && args[1] == refToCheckout)))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut,
                StandardOutput = "stdout",
                StandardError = "stderr"
            });

        var sut = CreateSut(processManagerMock.Object);

        // Act
        ProcessFailedException captured = null;
        try
        {
            await sut.CheckoutAsync(repoPath, refToCheckout);
        }
        catch (ProcessFailedException ex)
        {
            captured = ex;
        }

        // Assert
        processManagerMock.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(args => args.Length == 2 && args[0] == "checkout" && args[1] == refToCheckout)),
            Times.Once);

        if (captured == null)
        {
            throw new Exception("Expected ProcessFailedException was not thrown.");
        }

        var expectedSnippet = $"Failed to check out {refToCheckout} in {repoPath}";
        if (!captured.Message.Contains(expectedSnippet, StringComparison.Ordinal))
        {
            throw new Exception($"Exception message does not contain expected snippet. Expected to find: '{expectedSnippet}'. Actual: '{captured.Message}'.");
        }
    }

    /// <summary>
    /// Ensures DeleteBranchAsync throws when the underlying git command fails (non-zero exit code or timeout),
    /// and that the exception message contains both the repoPath and branchName for diagnostics.
    /// Inputs:
    ///  - timedOut: whether the process timed out (true/false).
    ///  - exitCode: process exit code (0 indicates success only if not timed out; non-zero indicates failure).
    /// Expected:
    ///  - An exception is thrown.
    ///  - Exception message contains "Failed to delete branch {branchName} in {repoPath}".
    ///  - IProcessManager.ExecuteGit is called with ["branch", "-D", branchName].
    /// </summary>
    [TestCase(false, 1)]
    [TestCase(true, 0)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task DeleteBranchAsync_CommandFailure_ThrowsWithRepoAndBranchInMessage(bool timedOut, int exitCode)
    {
        // Arrange
        var repoPath = "repo/path with space";
        var branchName = "branch name";
        var expectedArgs = new[] { "branch", "-D", branchName };

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = exitCode, TimedOut = timedOut });

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        // Act
        var ex = Assert.ThrowsAsync<Exception>(async () => await sut.DeleteBranchAsync(repoPath, branchName));

        // Assert
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain($"Failed to delete branch {branchName} in {repoPath}"));
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))), Times.Once);
        processManager.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies CommitAsync composes git arguments with the default author when 'author' is null,
    /// and conditionally includes '--allow-empty' based on 'allowEmpty' flag, while forwarding the cancellation token.
    /// Inputs:
    ///  - repoPath: varied (normal, empty, unicode).
    ///  - message: varied (empty, unicode, special characters).
    ///  - allowEmpty: true/false.
    /// Expected:
    ///  - IProcessManager.ExecuteGit is called once with exact args:
    ///      ["commit", "-m", message, (optional "--allow-empty"), "--author", "dotnet-maestro[bot] <dotnet-maestro[bot]@users.noreply.github.com>"]
    ///  - The provided CancellationToken is forwarded unchanged.
    ///  - No exception is thrown when the process succeeds.
    /// </summary>
    [TestCaseSource(nameof(CommitDefaultAuthorCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task CommitAsync_DefaultAuthorAndAllowEmptyFlag_BuildsExpectedArgumentsAndForwardsToken(string repoPath, string message, bool allowEmpty)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var tokenSource = new CancellationTokenSource();
        var token = tokenSource.Token;

        var expectedArgs = new List<string> { "commit", "-m", message };
        if (allowEmpty)
        {
            expectedArgs.Add("--allow-empty");
        }
        expectedArgs.Add("--author");
        expectedArgs.Add($"{Constants.DarcBotName} <{Constants.DarcBotEmail}>");

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(args => args.SequenceEqual(expectedArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                token))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        await sut.CommitAsync(repoPath, message, allowEmpty, author: null, cancellationToken: token);

        // Assert
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<IEnumerable<string>>(args => args.SequenceEqual(expectedArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            token), Times.Once);
    }

    /// <summary>
    /// Verifies CommitAsync composes git arguments with a provided custom author and includes '--allow-empty'
    /// when requested, while forwarding the cancellation token.
    /// Inputs:
    ///  - repoPath: varied (normal, empty, spaces).
    ///  - message: varied (single-line, multi-line).
    ///  - allowEmpty: true/false.
    ///  - authorName/authorEmail: custom values including unicode and special characters.
    /// Expected:
    ///  - IProcessManager.ExecuteGit is called once with exact args:
    ///      ["commit", "-m", message, (optional "--allow-empty"), "--author", $"{authorName} <{authorEmail}>"]
    ///  - The provided CancellationToken is forwarded unchanged.
    ///  - No exception is thrown when the process succeeds.
    /// </summary>
    [TestCaseSource(nameof(CommitCustomAuthorCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task CommitAsync_WithAllowEmptyAndCustomAuthor_BuildsExpectedArgumentsAndForwardsToken(string repoPath, string message, bool allowEmpty, string authorName, string authorEmail)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var tokenSource = new CancellationTokenSource();
        var token = tokenSource.Token;

        var expectedArgs = new List<string> { "commit", "-m", message };
        if (allowEmpty)
        {
            expectedArgs.Add("--allow-empty");
        }
        expectedArgs.Add("--author");
        expectedArgs.Add($"{authorName} <{authorEmail}>");

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(args => args.SequenceEqual(expectedArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                token))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        await sut.CommitAsync(repoPath, message, allowEmpty, author: (authorName, authorEmail), cancellationToken: token);

        // Assert
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<IEnumerable<string>>(args => args.SequenceEqual(expectedArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            token), Times.Once);
    }

    private static IEnumerable<TestCaseData> CommitDefaultAuthorCases()
    {
        yield return new TestCaseData("C:\\repo", "Initial commit", false)
            .SetName("CommitAsync_DefaultAuthor_NoAllowEmpty_NormalInputs");
        yield return new TestCaseData("", "", false)
            .SetName("CommitAsync_DefaultAuthor_NoAllowEmpty_EmptyRepoPathAndMessage");
        yield return new TestCaseData("/r/😃", "ユニコード", true)
            .SetName("CommitAsync_DefaultAuthor_WithAllowEmpty_UnicodeInputs");
        yield return new TestCaseData("C:\\path with spaces\\repo", "special !@#$%^&*()[]{};:',.<>?|\\\" \n second line", false)
            .SetName("CommitAsync_DefaultAuthor_NoAllowEmpty_SpecialCharsAndMultilineMessage");
    }

    private static IEnumerable<TestCaseData> CommitCustomAuthorCases()
    {
        yield return new TestCaseData("repo", "message", true, "John Doe", "john.doe+test@example.com")
            .SetName("CommitAsync_CustomAuthor_WithAllowEmpty_StandardEmailWithPlus");
        yield return new TestCaseData("", "empty", false, "N, Jr.", "e@x.y")
            .SetName("CommitAsync_CustomAuthor_NoAllowEmpty_CommaInNameAndShortEmail");
        yield return new TestCaseData("C:\\path with spaces\\repo", "Multi-line\nLine2", true, "Äuthor Ø", "unicode-ç@example.co.uk")
            .SetName("CommitAsync_CustomAuthor_WithAllowEmpty_UnicodeNameAndEmail");
    }

    /// <summary>
    /// Ensures StageAsync throws when the underlying git command fails (non-zero exit code or timeout),
    /// and that the exception message contains the repo path and the list of staged files.
    /// Inputs:
    ///  - timedOut: true/false.
    ///  - exitCode: 0 or non-zero.
    /// Expected:
    ///  - An exception is thrown by StageAsync.
    ///  - Exception message contains "Failed to stage a.txt, b.txt in /repo".
    /// </summary>
    [TestCase(false, 1, TestName = "StageAsync_CommandFailure_NonZeroExitCode_ThrowsWithMessage")]
    [TestCase(true, 0, TestName = "StageAsync_CommandFailure_TimedOut_ThrowsWithMessage")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task StageAsync_CommandFailure_ThrowsWithRepoPathAndPathsInMessage(bool timedOut, int exitCode)
    {
        // Arrange
        var repoPath = "/repo";
        var pathsToStage = new[] { "a.txt", "b.txt" };
        var expectedMessageFragment = "Failed to stage a.txt, b.txt in /repo";

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(args => args.SequenceEqual(new[] { "add" }.Concat(pathsToStage))),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = exitCode, TimedOut = timedOut });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        var ex = Assert.ThrowsAsync<Exception>(async () => await sut.StageAsync(repoPath, pathsToStage, CancellationToken.None));

        // Assert
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain(expectedMessageFragment));
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<IEnumerable<string>>(args => args.SequenceEqual(new[] { "add" }.Concat(pathsToStage))),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that GetRootDirAsync uses Environment.CurrentDirectory when repoPath is null,
    /// calls 'git rev-parse --show-toplevel' with null env vars and the provided cancellation token,
    /// and returns a trimmed StandardOutput.
    /// Inputs:
    ///  - repoPath: null
    ///  - cancellationToken: a non-default token
    ///  - mocked ExecuteGit returns ExitCode 0 with padded StandardOutput
    /// Expected:
    ///  - ExecuteGit called once with repoPath == Environment.CurrentDirectory and args ["rev-parse", "--show-toplevel"]
    ///  - Returned string equals StandardOutput.Trim()
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetRootDirAsync_RepoPathNull_UsesCurrentDirectoryAndReturnsTrimmedOutput()
    {
        // Arrange
        var tokenSource = new CancellationTokenSource();
        var token = tokenSource.Token;

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        var currentDir = Environment.CurrentDirectory;
        var output = "   /expected/root \r\n\t";
        var expectedTrimmed = "/expected/root";

        processManager
            .Setup(m => m.ExecuteGit(
                currentDir,
                It.Is<string[]>(args => args.Length == 2 && args[0] == "rev-parse" && args[1] == "--show-toplevel"),
                It.Is<Dictionary<string, string>>(env => env == null),
                token))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = output
            });

        // Act
        var result = await sut.GetRootDirAsync(null, token);

        // Assert
        processManager.Verify(m => m.ExecuteGit(
            currentDir,
            It.Is<string[]>(args => args.Length == 2 && args[0] == "rev-parse" && args[1] == "--show-toplevel"),
            It.Is<Dictionary<string, string>>(env => env == null),
            token), Times.Once);

        Expect(result).To.Equal(expectedTrimmed);
    }

    /// <summary>
    /// Ensures GetRootDirAsync forwards a provided repoPath unchanged, invokes the expected git arguments,
    /// and returns a trimmed StandardOutput.
    /// Inputs:
    ///  - Diverse repoPath values including empty, whitespace, Windows/Unix, and unicode paths.
    /// Expected:
    ///  - ExecuteGit called once with the exact repoPath and args ["rev-parse", "--show-toplevel"].
    ///  - Returned value equals trimmed StandardOutput.
    /// </summary>
    [TestCase("", TestName = "GetRootDirAsync_EmptyRepoPath_ForwardsPathAndReturnsTrimmedOutput")]
    [TestCase("   ", TestName = "GetRootDirAsync_WhitespaceRepoPath_ForwardsPathAndReturnsTrimmedOutput")]
    [TestCase("C:\\repo", TestName = "GetRootDirAsync_WindowsPath_ForwardsPathAndReturnsTrimmedOutput")]
    [TestCase("/home/user/repo", TestName = "GetRootDirAsync_UnixPath_ForwardsPathAndReturnsTrimmedOutput")]
    [TestCase("C:\\répo🚀", TestName = "GetRootDirAsync_UnicodePath_ForwardsPathAndReturnsTrimmedOutput")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetRootDirAsync_RepoPathProvided_ForwardsPathAndReturnsTrimmedOutput(string repoPath)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        var token = CancellationToken.None;
        var output = " \n\t/root/dir \n";
        var expected = "/root/dir";

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(args => args.Length == 2 && args[0] == "rev-parse" && args[1] == "--show-toplevel"),
                It.Is<Dictionary<string, string>>(env => env == null),
                token))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = output
            });

        // Act
        var actual = await sut.GetRootDirAsync(repoPath, token);

        // Assert
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(args => args.Length == 2 && args[0] == "rev-parse" && args[1] == "--show-toplevel"),
            It.Is<Dictionary<string, string>>(env => env == null),
            token), Times.Once);

        Expect(actual).To.Equal(expected);
    }

    /// <summary>
    /// Validates that GetRootDirAsync throws a ProcessFailedException when the underlying git command fails
    /// due to either timeout or non-zero exit code, and that the exception message contains the expected hint.
    /// Inputs:
    ///  - repoPath: "repo"
    ///  - timedOut/exitCode combinations that cause failure via ThrowIfFailed (TimedOut || ExitCode != 0)
    /// Expected:
    ///  - A ProcessFailedException is thrown.
    ///  - Exception message contains "Root directory of the repo was not found".
    /// </summary>
    [TestCase(false, 1, TestName = "GetRootDirAsync_GitNonZeroExit_ThrowsProcessFailedExceptionWithHint")]
    [TestCase(true, 0, TestName = "GetRootDirAsync_GitTimeout_ThrowsProcessFailedExceptionWithHint")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetRootDirAsync_CommandFailure_ThrowsWithExpectedMessage(bool timedOut, int exitCode)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        var repoPath = "repo";
        var token = CancellationToken.None;

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(args => args.Length == 2 && args[0] == "rev-parse" && args[1] == "--show-toplevel"),
                It.Is<Dictionary<string, string>>(env => env == null),
                token))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut,
                StandardOutput = ""
            });

        // Act
        ProcessFailedException caught = null;
        try
        {
            await sut.GetRootDirAsync(repoPath, token);
        }
        catch (ProcessFailedException ex)
        {
            caught = ex;
        }

        // Assert
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(args => args.Length == 2 && args[0] == "rev-parse" && args[1] == "--show-toplevel"),
            It.Is<Dictionary<string, string>>(env => env == null),
            token), Times.Once);

        Expect(caught != null).To.Equal(true);
        Expect(caught.Message.Contains("Root directory of the repo was not found")).To.Equal(true);
    }

    /// <summary>
    /// Ensures that when repoPath is null, GetGitCommitAsync uses Environment.CurrentDirectory,
    /// executes "git rev-parse HEAD", and returns the trimmed StandardOutput from the process result.
    /// Inputs:
    ///  - repoPath: null
    ///  - cancellationToken: default
    /// Expected:
    ///  - IProcessManager.ExecuteGit is invoked once with repoPath == Environment.CurrentDirectory and args ["rev-parse", "HEAD"].
    ///  - The returned value is the trimmed StandardOutput.
    /// </summary>
    [Test]
    [Category("GetGitCommitAsync")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetGitCommitAsync_RepoPathNull_UsesCurrentDirectoryAndReturnsTrimmedCommit()
    {
        // Arrange
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        var sut = CreateSut(processManagerMock.Object);

        var originalCwd = Environment.CurrentDirectory;
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        try
        {
            Environment.CurrentDirectory = tmpDir;

            processManagerMock
                .Setup(m => m.ExecuteGit(
                    It.Is<string>(p => p == tmpDir),
                    It.Is<string[]>(args => args.Length == 2 && args[0] == "rev-parse" && args[1] == "HEAD"),
                    It.Is<Dictionary<string, string>>(env => env == null),
                    It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
                .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false, StandardOutput = " 12345 \r\n" });

            // Act
            var commit = await sut.GetGitCommitAsync(null);

            // Assert
            Assert.That(commit, Is.EqualTo("12345"));
            processManagerMock.VerifyAll();
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(tmpDir))
            {
                try { Directory.Delete(tmpDir, true); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// Verifies that GetGitCommitAsync forwards the provided repoPath unchanged, passes through the given CancellationToken,
    /// executes "git rev-parse HEAD", and returns the trimmed StandardOutput.
    /// Inputs:
    ///  - repoPath: varied (absolute, empty, whitespace, unicode).
    ///  - cancelled: whether the provided CancellationToken is already canceled.
    /// Expected:
    ///  - IProcessManager.ExecuteGit is invoked with the exact repoPath and args ["rev-parse", "HEAD"] and the same token.
    ///  - The returned commit equals trimmed process output.
    /// </summary>
    [TestCase("C:\\repo", false, TestName = "GetGitCommitAsync_WindowsPath_TokenNotCancelled_ReturnsTrimmed")]
    [TestCase("/home/user/repo", true, TestName = "GetGitCommitAsync_UnixPath_TokenCancelled_ReturnsTrimmed")]
    [TestCase("", false, TestName = "GetGitCommitAsync_EmptyPath_TokenNotCancelled_ReturnsTrimmed")]
    [TestCase("   ", false, TestName = "GetGitCommitAsync_WhitespaceOnlyPath_TokenNotCancelled_ReturnsTrimmed")]
    [TestCase("C:\\répo🚀", true, TestName = "GetGitCommitAsync_UnicodePath_TokenCancelled_ReturnsTrimmed")]
    [Category("GetGitCommitAsync")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetGitCommitAsync_RepoPathProvided_ExecutesGitAndReturnsTrimmed(string repoPath, bool cancelled)
    {
        // Arrange
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        var sut = CreateSut(processManagerMock.Object);

        var cts = new CancellationTokenSource();
        if (cancelled) cts.Cancel();
        var token = cts.Token;

        processManagerMock
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(args => args.Length == 2 && args[0] == "rev-parse" && args[1] == "HEAD"),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(token))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false, StandardOutput = " deadbeef \n" });

        // Act
        var commit = await sut.GetGitCommitAsync(repoPath, token);

        // Assert
        Assert.That(commit, Is.EqualTo("deadbeef"));
        processManagerMock.VerifyAll();
    }

    /// <summary>
    /// Ensures that GetGitCommitAsync throws a ProcessFailedException when the underlying git command fails
    /// (either timing out or returning a non-zero exit code), and that the exception message contains the expected context.
    /// Inputs:
    ///  - timedOut: true/false
    ///  - exitCode: non-zero/zero (combined to simulate failure)
    /// Expected:
    ///  - A ProcessFailedException is thrown.
    ///  - Exception message contains the user-friendly failure message.
    ///  - ExecuteGit is invoked with args ["rev-parse", "HEAD"].
    /// </summary>
    [TestCase(true, 0, TestName = "GetGitCommitAsync_CommandTimedOut_ThrowsProcessFailedException")]
    [TestCase(false, 1, TestName = "GetGitCommitAsync_NonZeroExitCode_ThrowsProcessFailedException")]
    [Category("GetGitCommitAsync")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetGitCommitAsync_CommandFailure_ThrowsWithExpectedMessage(bool timedOut, int exitCode)
    {
        // Arrange
        var repoPath = "/repo/path";
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);

        processManagerMock
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(args => args.Length == 2 && args[0] == "rev-parse" && args[1] == "HEAD"),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut,
                StandardOutput = string.Empty,
                StandardError = "error"
            });

        var sut = CreateSut(processManagerMock.Object);

        // Act
        var ex = Assert.ThrowsAsync<ProcessFailedException>(async () => await sut.GetGitCommitAsync(repoPath));

        // Assert
        Assert.That(ex.Message, Does.Contain("Commit was not resolved. Check if git is installed and that a .git directory exists in the root of your repository."));
        processManagerMock.VerifyAll();
    }

    /// <summary>
    /// Verifies that GetCheckedOutBranchAsync executes "git rev-parse --abbrev-ref HEAD" with the exact arguments
    /// and returns the StandardOutput trimmed of whitespace.
    /// Inputs:
    ///  - Various repoPath values (absolute, empty, whitespace-only, unicode).
    /// Expected:
    ///  - IProcessManager.ExecuteGit is called once with the repoPath and ["rev-parse", "--abbrev-ref", "HEAD"].
    ///  - The returned branch name equals the trimmed StandardOutput from the process result.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(RepoPaths))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetCheckedOutBranchAsync_SuccessfulExecution_ReturnsTrimmedOutputAndPassesExactGitArgs(string repoPath)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var expectedArgs = new[] { "rev-parse", "--abbrev-ref", "HEAD" };
        var rawStdOut = "  feature/xyz \r\n";
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = rawStdOut
            });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        var result = await sut.GetCheckedOutBranchAsync(new NativePath(repoPath));

        // Assert
        Assert.That(result, Is.EqualTo("feature/xyz"));
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))), Times.Once);
    }

    /// <summary>
    /// Ensures that GetCheckedOutBranchAsync throws when the underlying git command fails
    /// due to timeout or non-zero exit code, and that the exception message includes the repo path.
    /// Inputs:
    ///  - timedOut = true with exitCode = 0
    ///  - timedOut = false with exitCode != 0
    /// Expected:
    ///  - ProcessFailedException is thrown by ThrowIfFailed.
    ///  - Exception message contains "Failed to get the current branch for {repoPath}".
    /// </summary>
    [Test]
    [TestCase(true, 0)]
    [TestCase(false, 1)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetCheckedOutBranchAsync_CommandFailure_ThrowsWithRepoPathInMessage(bool timedOut, int exitCode)
    {
        // Arrange
        var repoPath = "/repo/path with spaces/😃";
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var expectedArgs = new[] { "rev-parse", "--abbrev-ref", "HEAD" };
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut,
                StandardOutput = "some output",
                StandardError = "some error"
            });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        var ex = Assert.ThrowsAsync<ProcessFailedException>(async () =>
            await sut.GetCheckedOutBranchAsync(new NativePath(repoPath)));

        // Assert
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain($"Failed to get the current branch for {repoPath}"));
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))), Times.Once);
    }

    /// <summary>
    /// Verifies that when git outputs only whitespace or newlines, the result is an empty string after Trim().
    /// Inputs:
    ///  - Whitespace-only StandardOutput variants: " ", "\t", "\r\n", " \r\n\t ".
    /// Expected:
    ///  - GetCheckedOutBranchAsync returns string.Empty.
    /// </summary>
    [Test]
    [TestCase(" ")]
    [TestCase("\t")]
    [TestCase("\r\n")]
    [TestCase(" \r\n\t ")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetCheckedOutBranchAsync_WhitespaceOnlyStdOut_ReturnsEmptyString(string rawOutput)
    {
        // Arrange
        var repoPath = "C:\\repo";
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var expectedArgs = new[] { "rev-parse", "--abbrev-ref", "HEAD" };
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = rawOutput
            });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        var result = await sut.GetCheckedOutBranchAsync(new NativePath(repoPath));

        // Assert
        Assert.That(result, Is.EqualTo(string.Empty));
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))), Times.Once);
    }

    /// <summary>
    /// Verifies that when the provided repoUrl is already present in 'git remote -v' output,
    /// the method returns the existing remote name and does not attempt to add a new remote.
    /// Inputs:
    ///  - repoPath: varied paths
    ///  - repoUrl: exact URL to search for
    ///  - stdout: lines containing existing remotes in mixed spacing and with (fetch)/(push) suffixes
    /// Expected:
    ///  - ExecuteGit is called once with ["remote", "-v"].
    ///  - No "remote add" call is made.
    ///  - Returned remote name equals the matching remote from the output.
    /// </summary>
    [TestCaseSource(nameof(ExistingRemoteCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddRemoteIfMissingAsync_RemoteAlreadyExists_ReturnsExistingNameAndDoesNotAdd(
        string repoPath,
        string repoUrl,
        string stdout,
        string expectedRemote)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        processManager
            .Setup(pm => pm.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 2 && a[0] == "remote" && a[1] == "-v"),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = stdout
            });

        // Act
        var result = await sut.AddRemoteIfMissingAsync(repoPath, repoUrl, default);

        // Assert
        processManager.Verify(pm => pm.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.Length == 2 && a[0] == "remote" && a[1] == "-v"),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.IsAny<CancellationToken>()), Times.Once);

        processManager.Verify(pm => pm.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.Length >= 1 && a[0] == "remote" && a[1] == "add"),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Never);

        Assert.That(result, Is.EqualTo(expectedRemote));
    }

    /// <summary>
    /// Ensures that when the repoUrl is not present among existing remotes, the method computes a stable name
    /// using StringUtils.GetXxHash64, runs 'git remote add', and returns that computed name.
    /// Inputs:
    ///  - repoPath: includes typical, empty, whitespace, and unicode paths.
    ///  - repoUrl: includes typical, empty, whitespace, and unicode URLs.
    /// Expected:
    ///  - First ExecuteGit call is ["remote", "-v"] and succeeds.
    ///  - Second ExecuteGit call is ["remote", "add", <computedName>, repoUrl] with the same CancellationToken.
    ///  - Returned name equals the computed hash name.
    /// </summary>
    [TestCaseSource(nameof(AddRemoteCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddRemoteIfMissingAsync_RemoteMissing_AddsComputedNameAndReturnsIt(string repoPath, string repoUrl, bool cancelled)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        var tokenSource = new CancellationTokenSource();
        if (cancelled)
        {
            tokenSource.Cancel();
        }
        var token = tokenSource.Token;

        // First call: list remotes -> none matches repoUrl
        processManager
            .Setup(pm => pm.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 2 && a[0] == "remote" && a[1] == "-v"),
                It.Is<Dictionary<string, string>>(env => env == null),
                token))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = "origin https://example.com/other.git (fetch)\nupstream\thttps://example.com/up.git\t(push)"
            });

        string expectedName = StringUtils.GetXxHash64(repoUrl);

        processManager
            .Setup(pm => pm.ExecuteGit(
                repoPath,
                It.Is<string[]>(a =>
                    a.Length == 4 &&
                    a[0] == "remote" &&
                    a[1] == "add" &&
                    a[2] == expectedName &&
                    a[3] == repoUrl),
                It.Is<Dictionary<string, string>>(env => env == null),
                token))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = string.Empty
            });

        // Act
        var result = await sut.AddRemoteIfMissingAsync(repoPath, repoUrl, token);

        // Assert
        processManager.Verify(pm => pm.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.Length == 2 && a[0] == "remote" && a[1] == "-v"),
            It.Is<Dictionary<string, string>>(env => env == null),
            token), Times.Once);

        processManager.Verify(pm => pm.ExecuteGit(
            repoPath,
            It.Is<string[]>(a =>
                a.Length == 4 &&
                a[0] == "remote" &&
                a[1] == "add" &&
                a[2] == expectedName &&
                a[3] == repoUrl),
            It.Is<Dictionary<string, string>>(env => env == null),
            token), Times.Once);

        Assert.That(result, Is.EqualTo(expectedName));
    }

    /// <summary>
    /// Validates that when the 'git remote -v' command fails or times out, the method throws ProcessFailedException
    /// and includes the repository path in the error message.
    /// Inputs:
    ///  - timedOut: true/false
    ///  - exitCode: non-zero or zero
    /// Expected:
    ///  - A ProcessFailedException is thrown with a message containing "Failed to get remotes for {repoPath}".
    ///  - No attempt is made to add a remote.
    /// </summary>
    [TestCase(true, 0)]
    [TestCase(false, 1)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddRemoteIfMissingAsync_RemoteListFails_ThrowsProcessFailedException(bool timedOut, int exitCode)
    {
        // Arrange
        string repoPath = "/repo/path";
        string repoUrl = "https://example.com/repo.git";

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        processManager
            .Setup(pm => pm.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 2 && a[0] == "remote" && a[1] == "-v"),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut,
                StandardOutput = string.Empty,
                StandardError = "simulated failure"
            });

        // Act
        var ex = Assert.ThrowsAsync<ProcessFailedException>(() => sut.AddRemoteIfMissingAsync(repoPath, repoUrl, default));

        // Assert
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain($"Failed to get remotes for {repoPath}"));

        processManager.Verify(pm => pm.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.Length >= 1 && a[0] == "remote" && a[1] == "add"),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Ensures that when adding a missing remote fails (non-zero exit or timeout), the method throws ProcessFailedException
    /// with a message indicating the computed remote name, the URL, and the repo path.
    /// Inputs:
    ///  - timedOut: true/false
    ///  - exitCode: non-zero or zero
    /// Expected:
    ///  - A ProcessFailedException is thrown with a message containing "Failed to add remote {computedName} ({repoUrl}) to {repoPath}".
    /// </summary>
    [TestCase(true, 0)]
    [TestCase(false, 2)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddRemoteIfMissingAsync_AddRemoteFails_ThrowsWithComputedNameAndRepoInfo(bool timedOut, int exitCode)
    {
        // Arrange
        string repoPath = "C:\\r with space\\p";
        string repoUrl = "https://host/repo.git";
        string expectedName = StringUtils.GetXxHash64(repoUrl);

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // First call: list remotes succeeds with no match
        processManager
            .Setup(pm => pm.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 2 && a[0] == "remote" && a[1] == "-v"),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = "origin https://example.com/other.git (fetch)"
            });

        // Second call: add remote fails
        processManager
            .Setup(pm => pm.ExecuteGit(
                repoPath,
                It.Is<string[]>(a =>
                    a.Length == 4 &&
                    a[0] == "remote" &&
                    a[1] == "add" &&
                    a[2] == expectedName &&
                    a[3] == repoUrl),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut,
                StandardOutput = string.Empty,
                StandardError = "add failed"
            });

        // Act
        var ex = Assert.ThrowsAsync<ProcessFailedException>(() => sut.AddRemoteIfMissingAsync(repoPath, repoUrl, default));

        // Assert
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain($"Failed to add remote {expectedName} ({repoUrl}) to {repoPath}"));
    }

    private static IEnumerable ExistingRemoteCases()
    {
        yield return new TestCaseData(
            "/repo/path",
            "https://example.com/repo.git",
            "origin https://example.com/repo.git (fetch)\norigin https://example.com/repo.git (push)",
            "origin").SetName("AddRemoteIfMissingAsync_RemotePresentInBothFetchAndPush_ReturnsOrigin");

        yield return new TestCaseData(
            "C:\\repo",
            "https://example.com/up.git",
            "upstream\thttps://example.com/up.git\t(fetch)\norigin\thttps://example.com/other.git\t(push)",
            "upstream").SetName("AddRemoteIfMissingAsync_RemotePresentWithTabs_ReturnsUpstream");

        yield return new TestCaseData(
            "/r s",
            "ssh://git@host:repo/with-space.git",
            "other https://example.com/one.git (fetch)\nspace-remote ssh://git@host:repo/with-space.git (push)",
            "space-remote").SetName("AddRemoteIfMissingAsync_RemoteMatchOnSecondLine_ReturnsMatchingName");
    }

    private static IEnumerable AddRemoteCases()
    {
        yield return new TestCaseData("/repo/path", "https://example.com/repo.git", false)
            .SetName("AddRemoteIfMissingAsync_NoExistingRemote_AddsAndReturnsComputedName_Normal");
        yield return new TestCaseData("", "", false)
            .SetName("AddRemoteIfMissingAsync_EmptyInputs_AddsAndReturnsComputedName");
        yield return new TestCaseData("   ", "   ", true)
            .SetName("AddRemoteIfMissingAsync_WhitespaceInputs_CancelledToken_StillInvokesWithToken");
        yield return new TestCaseData("C:\\répo🚀", "https://host/🚀.git", false)
            .SetName("AddRemoteIfMissingAsync_UnicodeInputs_AddsAndReturnsComputedName");
    }

    /// <summary>
    /// Ensures that when the provided commit equals Constants.EmptyGitObject, the method
    /// returns an empty collection immediately and does not invoke any git operations.
    /// Inputs:
    ///  - repoPath: "repo"
    ///  - commit: Constants.EmptyGitObject
    /// Expected:
    ///  - Returns an empty list.
    ///  - IProcessManager.ExecuteGit is never called.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetGitSubmodulesAsync_CommitIsEmptyGitObject_ReturnsEmptyWithoutExecutingGit()
    {
        // Arrange
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var sut = CreateSut(processManager.Object);
        var repoPath = "repo";
        var commit = Constants.EmptyGitObject;

        // Act
        var result = await sut.GetGitSubmodulesAsync(repoPath, commit);

        // Assert
        result.Should().BeEmpty();
        processManager.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that if '.gitmodules' content cannot be retrieved (git show fails),
    /// the method returns an empty list and does not attempt to resolve any submodule SHAs.
    /// Inputs:
    ///  - repoPath: "repo"
    ///  - commit: "deadbeef00000000000000000000000000000000"
    /// Expected:
    ///  - A single git 'show {commit}:.gitmodules' call is made and returns non-success.
    ///  - The method returns an empty list and performs no further git calls.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetGitSubmodulesAsync_GitmodulesMissingOrInaccessible_ReturnsEmpty()
    {
        // Arrange
        var repoPath = "repo";
        var commit = "deadbeef00000000000000000000000000000000";

        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(args =>
                    args.SequenceEqual(new[] { "show", $"{commit}:.gitmodules" })),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 1 });

        var sut = CreateSut(processManager.Object);

        // Act
        var result = await sut.GetGitSubmodulesAsync(repoPath, commit);

        // Assert
        result.Should().BeEmpty();
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<IEnumerable<string>>(args =>
                args.SequenceEqual(new[] { "show", $"{commit}:.gitmodules" })),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        processManager.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Validates the happy path for a single well-formed submodule:
    /// the .gitmodules content is parsed, and the submodule commit is resolved via 'rev-parse'.
    /// Inputs:
    ///  - repoPath: "repo"
    ///  - commit: "aabbccddeeff00112233445566778899aabbccdd"
    ///  - .gitmodules content: one submodule with name, path, url.
    /// Expected:
    ///  - One 'show' and one 'rev-parse' git call.
    ///  - Returned submodule has the expected Name, Path, Url, and resolved Commit (trimmed).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetGitSubmodulesAsync_SingleValidSubmodule_ParsesAndResolvesCommit()
    {
        // Arrange
        var repoPath = "repo";
        var commit = "aabbccddeeff00112233445566778899aabbccdd";
        var subName = "sub-α";
        var subPath = "src/module-one";
        var subUrl = "https://example.com/repo.git";
        var resolvedSha = "1234567890abcdef1234567890abcdef12345678\n";

        var gitmodules = $@"
[submodule ""{subName}""]
    path = {subPath}
    url = {subUrl}
";

        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);

        // 'git show {commit}:.gitmodules' -> succeeds with content
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(args =>
                    args.SequenceEqual(new[] { "show", $"{commit}:.gitmodules" })),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = gitmodules });

        // 'git rev-parse {commit}:{subPath}' -> succeeds with resolved SHA
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 2 && a[0] == "rev-parse" && a[1] == $"{commit}:{subPath}"),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = resolvedSha });

        var sut = CreateSut(processManager.Object);

        // Act
        var result = await sut.GetGitSubmodulesAsync(repoPath, commit);

        // Assert
        result.Should().HaveCount(1);
        var sub = result.Single();
        sub.Name.Should().Be(subName);
        sub.Path.Should().Be(subPath);
        sub.Url.Should().Be(subUrl);
        sub.Commit.Should().Be(resolvedSha.Trim());

        processManager.VerifyAll();
    }

    /// <summary>
    /// Ensures multiple submodules are parsed and finalized correctly:
    ///  - Finalization happens when a new header is encountered and at EOF.
    /// Inputs:
    ///  - repoPath: "repo"
    ///  - commit: "cafebabecafebabecafebabecafebabecafebabe"
    ///  - .gitmodules content: two submodules each with path and url.
    /// Expected:
    ///  - One 'show' and two 'rev-parse' calls with correct arguments.
    ///  - Returned list contains both submodules with corresponding resolved commits.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetGitSubmodulesAsync_MultipleValidSubmodules_ParsesAllAndResolvesEachCommit()
    {
        // Arrange
        var repoPath = "repo";
        var commit = "cafebabecafebabecafebabecafebabecafebabe";

        var name1 = "libA";
        var path1 = "third_party/libA";
        var url1 = "https://example.com/libA.git";
        var sha1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n";

        var name2 = "libB";
        var path2 = "deps/libB";
        var url2 = "ssh://git@example.com/libB.git";
        var sha2 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n";

        var gitmodules = $@"
[submodule ""{name1}""]
  path = {path1}
  url = {url1}

[submodule ""{name2}""]
    url = {url2}
    path = {path2}
";

        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);

        // show .gitmodules
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(args =>
                    args.SequenceEqual(new[] { "show", $"{commit}:.gitmodules" })),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = gitmodules });

        // rev-parse for first submodule
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 2 && a[0] == "rev-parse" && a[1] == $"{commit}:{path1}"),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = sha1 });

        // rev-parse for second submodule
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 2 && a[0] == "rev-parse" && a[1] == $"{commit}:{path2}"),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = sha2 });

        var sut = CreateSut(processManager.Object);

        // Act
        var result = await sut.GetGitSubmodulesAsync(repoPath, commit);

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be(name1);
        result[0].Path.Should().Be(path1);
        result[0].Url.Should().Be(url1);
        result[0].Commit.Should().Be(sha1.Trim());

        result[1].Name.Should().Be(name2);
        result[1].Path.Should().Be(path2);
        result[1].Url.Should().Be(url2);
        result[1].Commit.Should().Be(sha2.Trim());

        processManager.VerifyAll();
    }

    /// <summary>
    /// Validates error scenarios where a submodule is missing a required field (Url or Path).
    /// The method should throw with a clear error message before attempting to resolve a SHA.
    /// Inputs:
    ///  - Two cases:
    ///     1) Missing URL (has name, path only)
    ///     2) Missing Path (has name, url only)
    /// Expected:
    ///  - An exception is thrown with message containing "... has no URL" or "... has no path".
    ///  - No 'rev-parse' invocation occurs.
    /// </summary>
    [TestCaseSource(nameof(InvalidSubmoduleCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetGitSubmodulesAsync_SubmoduleMissingField_ThrowsWithDescriptiveMessage(string gitmodules, string expectedMessageFragment, string subName)
    {
        // Arrange
        var repoPath = "repo";
        var commit = "ffffffffffffffffffffffffffffffffffffffff";

        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);

        // 'show' returns provided gitmodules content
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(args =>
                    args.SequenceEqual(new[] { "show", $"{commit}:.gitmodules" })),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = gitmodules });

        var sut = CreateSut(processManager.Object);

        string thrownMessage = null;

        // Act
        try
        {
            await sut.GetGitSubmodulesAsync(repoPath, commit);
        }
        catch (Exception ex)
        {
            thrownMessage = ex.Message;
        }

        // Assert
        thrownMessage.Should().NotBeNull();
        thrownMessage.Should().Contain(expectedMessageFragment.Replace("{name}", subName));
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.Length == 2 && a[0] == "rev-parse"),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        processManager.VerifyAll();
    }

    /// <summary>
    /// Ensures that if the 'rev-parse' command fails when resolving a submodule commit,
    /// the method propagates a ProcessFailedException with a descriptive message.
    /// Inputs:
    ///  - repoPath: "repo"
    ///  - commit: "1111111111111111111111111111111111111111"
    ///  - .gitmodules contains one valid submodule (with name, path, url).
    /// Expected:
    ///  - 'rev-parse' returns non-success and ProcessFailedException is thrown.
    ///  - The exception message contains the expected "Failed to find SHA..." fragment.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetGitSubmodulesAsync_RevParseFailure_ThrowsProcessFailedException()
    {
        // Arrange
        var repoPath = "repo";
        var commit = "1111111111111111111111111111111111111111";
        var subName = "broken";
        var subPath = "lib/broken";
        var subUrl = "https://example.com/broken.git";

        var gitmodules = $@"
[submodule ""{subName}""]
    path = {subPath}
    url = {subUrl}
";

        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);

        // show .gitmodules succeeds
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(args =>
                    args.SequenceEqual(new[] { "show", $"{commit}:.gitmodules" })),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = gitmodules });

        // rev-parse fails
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 2 && a[0] == "rev-parse" && a[1] == $"{commit}:{subPath}"),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 1, StandardOutput = "", StandardError = "not found" });

        var sut = CreateSut(processManager.Object);

        // Act
        string thrownMessage = null;
        try
        {
            await sut.GetGitSubmodulesAsync(repoPath, commit);
        }
        catch (ProcessFailedException ex)
        {
            thrownMessage = ex.Message;
        }

        // Assert
        thrownMessage.Should().NotBeNull();
        thrownMessage.Should().Contain($"Failed to find SHA of commit where submodule {subPath} points to");
        processManager.VerifyAll();
    }

    private static IEnumerable InvalidSubmoduleCases()
    {
        var name1 = "no-url";
        var content1 = $@"
[submodule ""{name1}""]
    path = libs/{name1}
";
        yield return new TestCaseData(content1, "Submodule {name} has no URL", name1)
            .SetName("GetGitSubmodulesAsync_MissingUrl_Throws");

        var name2 = "no-path";
        var content2 = $@"
[submodule ""{name2}""]
    url = https://example.com/{name2}.git
";
        yield return new TestCaseData(content2, "Submodule {name} has no path", name2)
            .SetName("GetGitSubmodulesAsync_MissingPath_Throws");
    }

    /// <summary>
    /// Verifies that when blameFromCommit is null, the method:
    ///  - Executes git with args: ["blame","--first-parent","HEAD","-wslL","{line},{line}",relativeFilePath]
    ///  - Returns the first whitespace-delimited token from StandardOutput (after Trim).
    /// Inputs:
    ///  - Diverse repoPath/relativeFilePath strings and boundary line values.
    /// Expected:
    ///  - IProcessManager.ExecuteGit invoked once with exact expected arguments, null env vars, default token.
    ///  - Returned value equals the first token from mocked StandardOutput.
    /// </summary>
    [TestCaseSource(nameof(Blame_NullCommit_Cases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BlameLineAsync_NullBlameFromCommit_UsesHeadAndReturnsFirstToken(
        string repoPath,
        string relativeFilePath,
        int line,
        string standardOutput,
        string expectedFirstToken)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        var expectedArgs = new[]
        {
            "blame",
            "--first-parent",
            Constants.HEAD,
            "-wslL",
            $"{line},{line}",
            relativeFilePath
        };

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(a => a.SequenceEqual(expectedArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false, StandardOutput = standardOutput });

        // Act
        var actual = await sut.BlameLineAsync(repoPath, relativeFilePath, line, null);

        // Assert
        Assert.That(actual, Is.EqualTo(expectedFirstToken));
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<IEnumerable<string>>(a => a.SequenceEqual(expectedArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);
    }

    /// <summary>
    /// Verifies that when blameFromCommit is provided, the method:
    ///  - Uses "{blameFromCommit}^" as the third argument.
    ///  - Returns the first token from StandardOutput.
    /// Inputs:
    ///  - repoPath, relativeFilePath, line, and various blameFromCommit values (including unicode).
    /// Expected:
    ///  - IProcessManager.ExecuteGit invoked once with expected arguments.
    ///  - Returned value matches the first token of StandardOutput.
    /// </summary>
    [TestCaseSource(nameof(Blame_WithCommit_Cases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BlameLineAsync_WithBlameFromCommit_AppendsCaretAndReturnsFirstToken(
        string repoPath,
        string relativeFilePath,
        int line,
        string blameFromCommit,
        string standardOutput,
        string expectedFirstToken)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        var expectedArgs = new[]
        {
            "blame",
            "--first-parent",
            blameFromCommit + '^',
            "-wslL",
            $"{line},{line}",
            relativeFilePath
        };

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(a => a.SequenceEqual(expectedArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false, StandardOutput = standardOutput });

        // Act
        var actual = await sut.BlameLineAsync(repoPath, relativeFilePath, line, blameFromCommit);

        // Assert
        Assert.That(actual, Is.EqualTo(expectedFirstToken));
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<IEnumerable<string>>(a => a.SequenceEqual(expectedArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);
    }

    /// <summary>
    /// Ensures that when the git command fails (timed out or non-zero exit code),
    /// the method throws ProcessFailedException whose message contains the repo path and target file.
    /// Inputs:
    ///  - Combinations of TimedOut and ExitCode that indicate failure.
    /// Expected:
    ///  - ProcessFailedException is thrown.
    ///  - Exception message contains "Failed to blame line {line} of {repoPath}{Path.DirectorySeparatorChar}{relativeFilePath}".
    /// </summary>
    [TestCaseSource(nameof(Blame_Failure_Cases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BlameLineAsync_CommandFails_ThrowsProcessFailedExceptionWithContext(bool timedOut, int exitCode)
    {
        // Arrange
        var repoPath = "/repo/path";
        var relativeFilePath = "file with spaces.cs";
        var line = 10;

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        var expectedArgs = new[]
        {
            "blame",
            "--first-parent",
            Constants.HEAD,
            "-wslL",
            $"{line},{line}",
            relativeFilePath
        };

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(a => a.SequenceEqual(expectedArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut,
                StandardOutput = "",
                StandardError = "simulated error"
            });

        var expectedMessageFragment = $"Failed to blame line {line} of {repoPath}{Path.DirectorySeparatorChar}{relativeFilePath}";

        // Act
        var ex = Assert.ThrowsAsync<ProcessFailedException>(async () =>
        {
            await sut.BlameLineAsync(repoPath, relativeFilePath, line, null);
        });

        // Assert
        Assert.That(ex.Message, Does.Contain(expectedMessageFragment));
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<IEnumerable<string>>(a => a.SequenceEqual(expectedArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);
    }

    private static IEnumerable Blame_NullCommit_Cases()
    {
        yield return new TestCaseData("C:\\repo", "file.txt", 1, "abc123 Some rest", "abc123")
            .SetName("BlameLineAsync_NullCommit_WindowsPath_Line1_ReturnsFirstToken");
        yield return new TestCaseData("/r/ä🚀", "dir with space/file.cs", 0, "  deadbeef  more", "deadbeef")
            .SetName("BlameLineAsync_NullCommit_UnicodePaths_Line0_ReturnsFirstToken");
        yield return new TestCaseData("", "", -1, "   abc   other   ", "abc")
            .SetName("BlameLineAsync_NullCommit_EmptyPaths_NegativeLine_ReturnsFirstToken");
        yield return new TestCaseData("   ", "f", int.MaxValue, "xyz", "xyz")
            .SetName("BlameLineAsync_NullCommit_WhitespaceRepo_MaxIntLine_ReturnsFirstToken");
    }

    private static IEnumerable Blame_WithCommit_Cases()
    {
        yield return new TestCaseData("repo", "path.cs", 5, "cafebabe", "abcdef more", "abcdef")
            .SetName("BlameLineAsync_WithCommit_SimpleCommit_AppendsCaretAndReturnsFirstToken");
        yield return new TestCaseData("/repo", "x", int.MinValue, "weird-😃", " 000 rest", "000")
            .SetName("BlameLineAsync_WithCommit_UnicodeCommit_MinIntLine_ReturnsFirstToken");
    }

    private static IEnumerable Blame_Failure_Cases()
    {
        yield return new TestCaseData(false, 1).SetName("BlameLineAsync_Failure_NonZeroExitCode_Throws");
        yield return new TestCaseData(true, 0).SetName("BlameLineAsync_Failure_TimedOut_Throws");
    }

    /// <summary>
    /// Ensures that when the object behind the git ref is a known local git object (commit/blob/tree/tag),
    /// GitRefExists returns true without querying remote branches.
    /// Inputs:
    ///  - repoPath and gitRef variants.
    ///  - cat-file outputs: "commit", "blob", "tree", "tag".
    /// Expected:
    ///  - IProcessManager.ExecuteGit is called once with ["cat-file","-t", gitRef].
    ///  - No remote branch listing is attempted.
    ///  - GitRefExists returns true.
    /// </summary>
    [TestCase("C:\\repo", "abcd123", "commit", TestName = "GitRefExists_LocalCommit_ReturnsTrue_NoRemoteQuery")]
    [TestCase("/r/éß🚀", "refs/tags/v1.0.0", "tag", TestName = "GitRefExists_LocalTag_ReturnsTrue_NoRemoteQuery")]
    [TestCase("", "feature/branch", "tree", TestName = "GitRefExists_LocalTree_ReturnsTrue_NoRemoteQuery")]
    [TestCase(" ", "weird ref", "blob", TestName = "GitRefExists_LocalBlob_ReturnsTrue_NoRemoteQuery")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GitRefExists_LocalObjectTypes_ReturnsTrue_WithoutRemoteLookup(string repoPath, string gitRef, string catFileOutput)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetryRecorder.Object, processManager.Object, fileSystem.Object, logger.Object);

        var catArgs = new[] { "cat-file", "-t", gitRef };
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(catArgs))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = catFileOutput
            });

        // Act
        var exists = await sut.GitRefExists(repoPath, gitRef, default);

        // Assert
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(catArgs))), Times.Once);
        Assert.That(exists, Is.True);
    }

    /// <summary>
    /// When the ref is not a known local object, verifies that a remote branch lookup is performed and
    /// a match in output yields true. Also verifies that the provided CancellationToken is forwarded.
    /// Inputs:
    ///  - repoPath and gitRef variants.
    /// Expected:
    ///  - First git call: ["cat-file","-t", gitRef] -> unknown.
    ///  - Second git call: ["branch","-a","--list","*/" + gitRef] with env null and provided token.
    ///  - GitRefExists returns true.
    /// </summary>
    [TestCase("C:\\repo", "feature/x", TestName = "GitRefExists_RemoteBranchFound_ReturnsTrue_TokenForwarded_WindowsPath")]
    [TestCase("/home/user/repo", "bugfix/123", TestName = "GitRefExists_RemoteBranchFound_ReturnsTrue_TokenForwarded_UnixPath")]
    [TestCase(" ", "weird-😃", TestName = "GitRefExists_RemoteBranchFound_ReturnsTrue_TokenForwarded_WeirdChars")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GitRefExists_RemoteBranchFound_ReturnsTrue_TokenForwarded(string repoPath, string gitRef)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var tokenSource = new CancellationTokenSource();
        var token = tokenSource.Token;

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetryRecorder.Object, processManager.Object, fileSystem.Object, logger.Object);

        var catArgs = new[] { "cat-file", "-t", gitRef };
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(catArgs))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = "" // unknown -> triggers remote lookup
            });

        var remoteArgs = new[] { "branch", "-a", "--list", "*/" + gitRef };
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.SequenceEqual(remoteArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(token))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = $"  remotes/origin/{gitRef}\n"
            });

        // Act
        var exists = await sut.GitRefExists(repoPath, gitRef, token);

        // Assert
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(catArgs))), Times.Once);
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.SequenceEqual(remoteArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(token))), Times.Once);
        Assert.That(exists, Is.True);
    }

    /// <summary>
    /// When the ref is not a known local object and remote listing doesn't include the ref,
    /// verifies that GitRefExists returns false.
    /// Inputs:
    ///  - repoPath and gitRef variants
    /// Expected:
    ///  - cat-file produces unknown
    ///  - branch -a --list "*/ref" succeeds but doesn't contain the ref
    ///  - GitRefExists returns false
    /// </summary>
    [TestCase("repo", "no-match", TestName = "GitRefExists_RemoteBranchMissing_ReturnsFalse")]
    [TestCase("", " ", TestName = "GitRefExists_RemoteBranchMissing_WhitespaceInputs_ReturnsFalse")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GitRefExists_RemoteBranchMissing_ReturnsFalse(string repoPath, string gitRef)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var token = new CancellationTokenSource().Token;

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetryRecorder.Object, processManager.Object, fileSystem.Object, logger.Object);

        var catArgs = new[] { "cat-file", "-t", gitRef };
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(catArgs))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = "unknown"
            });

        var remoteArgs = new[] { "branch", "-a", "--list", "*/" + gitRef };
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.SequenceEqual(remoteArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = "  remotes/origin/other-branch\n"
            });

        // Act
        var exists = await sut.GitRefExists(repoPath, gitRef, token);

        // Assert
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(catArgs))), Times.Once);
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.SequenceEqual(remoteArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(exists, Is.False);
    }

    /// <summary>
    /// Verifies that when remote branch listing fails (e.g., non-zero exit code or timeout),
    /// the exception thrown contains the ref and repo path information.
    /// Inputs:
    ///  - timedOut: true/false
    ///  - exitCode: 0/non-zero
    /// Expected:
    ///  - An exception is thrown by GitRefExists with message containing
    ///    "Failed to determine git ref type for '{gitRef}' in {repoPath}".
    /// </summary>
    [TestCase(false, 1, TestName = "GitRefExists_RemoteList_NonZeroExit_ThrowsWithContext")]
    [TestCase(true, 0, TestName = "GitRefExists_RemoteList_TimedOut_ThrowsWithContext")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GitRefExists_RemoteListFailure_ThrowsWithRepoAndRefInMessage(bool timedOut, int exitCode)
    {
        // Arrange
        var repoPath = "/repo/path";
        var gitRef = "feature/failing";
        var token = new CancellationTokenSource().Token;

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetryRecorder.Object, processManager.Object, fileSystem.Object, logger.Object);

        var catArgs = new[] { "cat-file", "-t", gitRef };
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(catArgs))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = "" // unknown -> triggers remote lookup
            });

        var remoteArgs = new[] { "branch", "-a", "--list", "*/" + gitRef };
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.SequenceEqual(remoteArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(token))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut,
                StandardOutput = ""
            });

        // Act + Assert
        var ex = Assert.ThrowsAsync<Exception>(async () => await sut.GitRefExists(repoPath, gitRef, token));
        Assert.That(ex!.Message, Does.Contain($"Failed to determine git ref type for '{gitRef}' in {repoPath}"));

        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(catArgs))), Times.Once);
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.SequenceEqual(remoteArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(token))), Times.Once);
    }

    /// <summary>
    /// Verifies that when git cat-file -t identifies the object type (commit/blob/tree/tag),
    /// GetRefType returns the same type and does not perform the remote branch lookup.
    /// Inputs:
    ///  - repoPath: "repo"
    ///  - gitRef: "ref"
    ///  - catFileOutput: one of "commit", "blob", "tree", "tag"
    /// Expected:
    ///  - Returned GitObjectType maps exactly to catFileOutput.
    ///  - No "git branch -a --list" command is executed.
    /// </summary>
    [TestCase("commit", GitObjectType.Commit, TestName = "GetRefType_CatFileReturnsCommit_ReturnsCommit_WithoutRemoteLookup")]
    [TestCase("blob", GitObjectType.Blob, TestName = "GetRefType_CatFileReturnsBlob_ReturnsBlob_WithoutRemoteLookup")]
    [TestCase("tree", GitObjectType.Tree, TestName = "GetRefType_CatFileReturnsTree_ReturnsTree_WithoutRemoteLookup")]
    [TestCase("tag", GitObjectType.Tag, TestName = "GetRefType_CatFileReturnsTag_ReturnsTag_WithoutRemoteLookup")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetRefType_CatFileKnownType_ReturnsExpected_WithoutRemoteLookup(string catFileOutput, GitObjectType expected)
    {
        // Arrange
        var repoPath = "repo";
        var gitRef = "ref";

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        // cat-file -t returns a known type
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 3 && a[0] == "cat-file" && a[1] == "-t" && a[2] == gitRef)))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = catFileOutput });

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        // Act
        var actual = await sut.GetRefType(repoPath, gitRef);

        // Assert
        Assert.That(actual, Is.EqualTo(expected));

        // Verify cat-file was executed
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.Length == 3 && a[0] == "cat-file" && a[1] == "-t" && a[2] == gitRef)), Times.Once);

        // Verify no remote branch lookup happened
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.Length == 4 && a[0] == "branch" && a[1] == "-a" && a[2] == "--list" && a[3] == "*/" + gitRef),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that when cat-file does not recognize the ref (Unknown) but the remote branch list contains the ref,
    /// GetRefType returns RemoteRef and forwards the provided CancellationToken.
    /// Inputs:
    ///  - repoPath variations and gitRef with edge cases.
    ///  - cat-file output: empty (Unknown).
    ///  - branch list output contains the gitRef substring.
    /// Expected:
    ///  - Returns GitObjectType.RemoteRef.
    ///  - Executes "git branch -a --list */{gitRef}" with the same CancellationToken.
    /// </summary>
    [TestCase("repo", "main", "  remotes/origin/main  ", TestName = "GetRefType_RemoteBranchExists_NormalRef_ReturnsRemoteRef")]
    [TestCase("/r/😃", "weird-😃", "remotes/upstream/weird-😃", TestName = "GetRefType_RemoteBranchExists_UnicodeRef_ReturnsRemoteRef")]
    [TestCase("C:\\repo path", "", "remotes/origin/", TestName = "GetRefType_RemoteBranchExists_EmptyRef_ReturnsRemoteRef")]
    [TestCase("   ", "   ", "remotes/origin/   ", TestName = "GetRefType_RemoteBranchExists_WhitespaceRef_ReturnsRemoteRef")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetRefType_CatFileUnknown_RemoteBranchContainsRef_ReturnsRemoteRef(string repoPath, string gitRef, string branchListOutput)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var tokenSource = new CancellationTokenSource();
        var token = tokenSource.Token;

        // cat-file -t => Unknown
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 3 && a[0] == "cat-file" && a[1] == "-t" && a[2] == gitRef)))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = string.Empty });

        // branch -a --list "*/{gitRef}" => contains ref
        var expectedArgs = new[] { "branch", "-a", "--list", "*/" + gitRef };
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.SequenceEqual(expectedArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(token))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = branchListOutput });

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        // Act
        var actual = await sut.GetRefType(repoPath, gitRef, token);

        // Assert
        Assert.That(actual, Is.EqualTo(GitObjectType.RemoteRef));

        processManager.VerifyAll();
    }

    /// <summary>
    /// Verifies that when cat-file does not recognize the ref and the remote branch list does not contain the ref,
    /// GetRefType returns Unknown.
    /// Inputs:
    ///  - repoPath: "repo"
    ///  - gitRef with special characters and long values.
    ///  - branch list output that does NOT contain gitRef.
    /// Expected:
    ///  - Returns GitObjectType.Unknown.
    ///  - Both cat-file and branch-list commands are executed with expected arguments.
    /// </summary>
    [TestCase("repo", "feature/long-branch-name-1234567890", "remotes/origin/other-branch", TestName = "GetRefType_RemoteBranchMissing_LongRef_ReturnsUnknown")]
    [TestCase("repo", "refs/heads/dev", "  remotes/upstream/main  ", TestName = "GetRefType_RemoteBranchMissing_PathLikeRef_ReturnsUnknown")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetRefType_CatFileUnknown_RemoteBranchDoesNotContainRef_ReturnsUnknown(string repoPath, string gitRef, string branchListOutput)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        // cat-file -t => Unknown
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 3 && a[0] == "cat-file" && a[1] == "-t" && a[2] == gitRef)))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "" });

        // branch -a --list => output not containing gitRef
        var expectedBranchArgs = new[] { "branch", "-a", "--list", "*/" + gitRef };
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.SequenceEqual(expectedBranchArgs)),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = branchListOutput });

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        // Act
        var actual = await sut.GetRefType(repoPath, gitRef);

        // Assert
        Assert.That(actual, Is.EqualTo(GitObjectType.Unknown));

        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.Length == 3 && a[0] == "cat-file" && a[1] == "-t" && a[2] == gitRef)), Times.Once);

        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.SequenceEqual(expectedBranchArgs)),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Ensures that when the remote branch listing command fails (non-zero exit code or timeout),
    /// GetRefType throws a ProcessFailedException with a message indicating the repoPath and gitRef.
    /// Inputs:
    ///  - timedOut: true/false
    ///  - exitCode: 0/non-zero
    /// Expected:
    ///  - Throws ProcessFailedException.
    ///  - Exception message contains "Failed to determine git ref type for '{gitRef}' in {repoPath}".
    /// </summary>
    [TestCase(false, 1, TestName = "GetRefType_RemoteLookupFails_NonZeroExit_ThrowsProcessFailedException")]
    [TestCase(true, 0, TestName = "GetRefType_RemoteLookupFails_TimedOut_ThrowsProcessFailedException")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetRefType_RemoteLookupFailure_ThrowsWithRepoPathAndRefInMessage(bool timedOut, int exitCode)
    {
        // Arrange
        var repoPath = "repo/failure";
        var gitRef = "topic/failure";

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        // First: cat-file => Unknown
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 3 && a[0] == "cat-file" && a[1] == "-t" && a[2] == gitRef)))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = "" });

        // Second: branch -a --list => failure
        var args = new[] { "branch", "-a", "--list", "*/" + gitRef };
        var token = new CancellationTokenSource().Token;
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.SequenceEqual(args)),
                It.IsAny<Dictionary<string, string>>(),
                It.Is<CancellationToken>(ct => ct.Equals(token))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = exitCode, TimedOut = timedOut, StandardOutput = "", StandardError = "err" });

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        // Act + Assert
        var ex = Assert.ThrowsAsync<ProcessFailedException>(async () => await sut.GetRefType(repoPath, gitRef, token));
        Assert.That(ex.Message, Does.Contain($"Failed to determine git ref type for '{gitRef}' in {repoPath}"));

        processManager.VerifyAll();
    }

    /// <summary>
    /// Verifies that HasWorkingTreeChangesAsync forwards the provided repoPath unchanged,
    /// and calls git with the exact expected arguments: ["diff", "--exit-code"].
    /// Inputs:
    ///  - Various repoPath strings including empty, whitespace, long, paths with spaces and unicode.
    /// Expected:
    ///  - IProcessManager.ExecuteGit is invoked once with the same repoPath and exact arguments.
    ///  - The method returns false when the process succeeds (ExitCode = 0, TimedOut = false).
    /// </summary>
    [TestCaseSource(nameof(RepoPaths))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task HasWorkingTreeChangesAsync_ExecutesDiffExitCode_WithExactArgsAndRepoPath_ReturnsFalseOnSuccess(string repoPath)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var expectedArgs = new[] { "diff", "--exit-code" };

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.SequenceEqual(expectedArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetry.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        var hasChanges = await sut.HasWorkingTreeChangesAsync(repoPath);

        // Assert
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.SequenceEqual(expectedArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);
        processManager.VerifyNoOtherCalls();

        if (hasChanges != false)
        {
            throw new Exception($"Expected no working tree changes (false) when git diff succeeds. Actual: {hasChanges}");
        }
    }

    /// <summary>
    /// Ensures the boolean result maps correctly from ProcessExecutionResult.Succeeded:
    ///  - Returns false when Succeeded is true (ExitCode == 0 and !TimedOut).
    ///  - Returns true when Succeeded is false (non-zero ExitCode or TimedOut).
    /// Inputs:
    ///  - timedOut and exitCode combinations covering success, normal failure, and timeout.
    /// Expected:
    ///  - The returned boolean equals !Succeeded for the provided ProcessExecutionResult.
    /// </summary>
    [TestCase(false, 0, false, TestName = "NoTimeout_ExitCode0_ReturnsFalse")]
    [TestCase(false, 1, true, TestName = "NoTimeout_NonZeroExit_ReturnsTrue")]
    [TestCase(true, -2, true, TestName = "TimedOut_ExitCodeMinus2_ReturnsTrue")]
    [TestCase(true, 0, true, TestName = "TimedOut_ExitCode0_ReturnsTrue")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task HasWorkingTreeChangesAsync_ResultMappingFromProcessExecution_Scenarios(bool timedOut, int exitCode, bool expected)
    {
        // Arrange
        var repoPath = "/repo/path";
        var expectedArgs = new[] { "diff", "--exit-code" };

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.SequenceEqual(expectedArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut,
                StandardOutput = string.Empty,
                StandardError = string.Empty
            });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetry.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        var actual = await sut.HasWorkingTreeChangesAsync(repoPath);

        // Assert
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.SequenceEqual(expectedArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);
        processManager.VerifyNoOtherCalls();

        if (actual != expected)
        {
            throw new Exception($"Unexpected HasWorkingTreeChangesAsync result. Expected: {expected}, Actual: {actual} (timedOut={timedOut}, exitCode={exitCode})");
        }
    }

    /// <summary>
    /// Validates the return value mapping based on git exit outcomes.
    /// Inputs:
    ///  - exitCode values including 0 (no changes), 1, -1, int.MaxValue, int.MinValue
    ///  - timedOut values true/false
    /// Expected:
    ///  - When TimedOut is true OR ExitCode != 0 => method returns true (indicating staged changes or failure).
    ///  - When TimedOut is false AND ExitCode == 0 => method returns false (no staged changes).
    /// </summary>
    [TestCaseSource(nameof(HasStagedChanges_ReturnCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task HasStagedChangesAsync_GitExitOutcomes_ReturnsExpectedBoolean(int exitCode, bool timedOut, bool expected)
    {
        // Arrange
        var remoteConfigurationMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorderMock = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        processManagerMock
            .Setup(m => m.ExecuteGit(It.IsAny<string>(), It.IsAny<string[]>()))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut
            });

        var sut = new LocalGitClient(
            remoteConfigurationMock.Object,
            telemetryRecorderMock.Object,
            processManagerMock.Object,
            fileSystemMock.Object,
            loggerMock.Object);

        // Act
        var result = await sut.HasStagedChangesAsync("repo");

        // Assert
        result.Should().Be(expected);
    }

    private static IEnumerable<TestCaseData> HasStagedChanges_ReturnCases()
    {
        yield return new TestCaseData(0, false, false).SetName("Exit0_NotTimedOut_ReturnsFalse");
        yield return new TestCaseData(1, false, true).SetName("ExitNonZero_NotTimedOut_ReturnsTrue");
        yield return new TestCaseData(-1, false, true).SetName("ExitNegative_NotTimedOut_ReturnsTrue");
        yield return new TestCaseData(int.MaxValue, false, true).SetName("ExitMax_NotTimedOut_ReturnsTrue");
        yield return new TestCaseData(int.MinValue, false, true).SetName("ExitMin_NotTimedOut_ReturnsTrue");
        yield return new TestCaseData(0, true, true).SetName("Exit0_TimedOut_ReturnsTrue");
        yield return new TestCaseData(1, true, true).SetName("ExitNonZero_TimedOut_ReturnsTrue");
    }

    /// <summary>
    /// Verifies that when the token provider returns null, the method returns early:
    ///  - No header args are inserted.
    ///  - No environment variables are set.
    /// Inputs:
    ///  - repoUri pointing to a valid GitHub URL.
    ///  - args list containing a single "fetch" entry.
    ///  - token provider returns null.
    /// Expected:
    ///  - args remain unchanged.
    ///  - envVars remains empty.
    ///  - Token provider is called exactly once with the provided repoUri.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddGitAuthHeader_TokenNull_DoesNotModifyArgsOrEnvVars()
    {
        // Arrange
        var repoUri = "https://github.com/dotnet/arcade";
        var args = new List<string> { "fetch" };
        var envVars = new Dictionary<string, string>();

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        remoteTokenProvider
            .Setup(p => p.GetTokenForRepositoryAsync(repoUri))
            .ReturnsAsync((string)null);

        var sut = CreateSut(remoteTokenProvider.Object);

        // Act
        await sut.AddGitAuthHeader(args, envVars, repoUri);

        // Assert
        Assert.That(args.Count, Is.EqualTo(1));
        Assert.That(args[0], Is.EqualTo("fetch"));
        Assert.That(envVars.Count, Is.EqualTo(0));
        remoteTokenProvider.Verify(p => p.GetTokenForRepositoryAsync(repoUri), Times.Once);
    }

    /// <summary>
    /// Ensures that when the repository URI resolves to GitRepoType.None, the method returns without changes:
    ///  - No header args are inserted.
    ///  - No environment variables are set.
    /// Inputs:
    ///  - repoUri with an unknown host that maps to GitRepoType.None.
    ///  - A non-null token from the provider.
    /// Expected:
    ///  - args and envVars remain unchanged.
    ///  - Token provider is called exactly once.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddGitAuthHeader_RepoTypeNone_NoChangesToArgsOrEnvVars()
    {
        // Arrange
        var repoUri = "https://example.com/unknown/repo";
        var token = "tok";
        var args = new List<string> { "fetch" };
        var envVars = new Dictionary<string, string>();

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        remoteTokenProvider
            .Setup(p => p.GetTokenForRepositoryAsync(repoUri))
            .ReturnsAsync(token);

        var sut = CreateSut(remoteTokenProvider.Object);

        // Act
        await sut.AddGitAuthHeader(args, envVars, repoUri);

        // Assert
        Assert.That(args.Count, Is.EqualTo(1));
        Assert.That(args[0], Is.EqualTo("fetch"));
        Assert.That(envVars.Count, Is.EqualTo(0));
        remoteTokenProvider.Verify(p => p.GetTokenForRepositoryAsync(repoUri), Times.Once);
    }

    /// <summary>
    /// Validates that for known repository types (GitHub, AzureDevOps, Local), the method:
    ///  - Inserts the required config arg at index 0.
    ///  - Populates the GIT_REMOTE_PAT env var correctly based on repo type.
    ///  - Sets GIT_TERMINAL_PROMPT to "0".
    /// Inputs:
    ///  - repoUri representing GitHub, AzureDevOps, or Local.
    ///  - token string to be used for authorization.
    ///  - expectedKind to indicate which auth scheme/format to validate.
    /// Expected:
    ///  - args[0] equals "--config-env=http.extraheader=GIT_REMOTE_PAT" and args[1] preserves the original "fetch".
    ///  - envVars["GIT_REMOTE_PAT"] matches the expected scheme value.
    ///  - envVars["GIT_TERMINAL_PROMPT"] equals "0".
    ///  - Token provider called exactly once.
    /// </summary>
    [TestCaseSource(nameof(AuthHeaderCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddGitAuthHeader_KnownRepoTypes_SetsExpectedArgsAndEnv(string repoUri, string token, string expectedKind)
    {
        // Arrange
        var args = new List<string> { "fetch" };
        var envVars = new Dictionary<string, string>();
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        remoteTokenProvider
            .Setup(p => p.GetTokenForRepositoryAsync(repoUri))
            .ReturnsAsync(token);

        var sut = CreateSut(remoteTokenProvider.Object);

        // Act
        await sut.AddGitAuthHeader(args, envVars, repoUri);

        // Assert
        Assert.That(args.Count, Is.EqualTo(2));
        Assert.That(args[0], Is.EqualTo("--config-env=http.extraheader=GIT_REMOTE_PAT"));
        Assert.That(args[1], Is.EqualTo("fetch"));

        Assert.That(envVars.ContainsKey("GIT_REMOTE_PAT"), Is.True);
        var actualHeader = envVars["GIT_REMOTE_PAT"];

        if (string.Equals(expectedKind, "github", StringComparison.OrdinalIgnoreCase))
        {
            var expectedBasic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Constants.GitHubBotUserName}:{token}"));
            Assert.That(actualHeader, Is.EqualTo($"Authorization: Basic {expectedBasic}"));
        }
        else if (string.Equals(expectedKind, "ado", StringComparison.OrdinalIgnoreCase))
        {
            Assert.That(actualHeader, Is.EqualTo($"Authorization: Bearer {token}"));
        }
        else if (string.Equals(expectedKind, "local", StringComparison.OrdinalIgnoreCase))
        {
            Assert.That(actualHeader, Is.EqualTo(token));
        }
        else
        {
            Assert.Fail("Unexpected expectedKind provided to test.");
        }

        Assert.That(envVars.ContainsKey("GIT_TERMINAL_PROMPT"), Is.True);
        Assert.That(envVars["GIT_TERMINAL_PROMPT"], Is.EqualTo("0"));
        Assert.That(envVars.Count, Is.EqualTo(2));

        remoteTokenProvider.Verify(p => p.GetTokenForRepositoryAsync(repoUri), Times.Once);
    }

    private static IEnumerable AuthHeaderCases()
    {
        // repoUri, token, expectedKind
        yield return new TestCaseData("https://github.com/org/repo", "p@ss:word", "github")
            .SetName("AddGitAuthHeader_GitHub_SetsBasicAuthHeaderAndArg");
        yield return new TestCaseData("https://dev.azure.com/org/project/_git/repo", "abc123", "ado")
            .SetName("AddGitAuthHeader_AzureDevOps_SetsBearerAuthHeaderAndArg");
        yield return new TestCaseData("repo", "plain_token", "local")
            .SetName("AddGitAuthHeader_Local_SetsRawTokenHeaderAndArg");
    }

    private static LocalGitClient CreateSut(IRemoteTokenProvider tokenProvider)
    {
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        return new LocalGitClient(tokenProvider, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);
    }

    /// <summary>
    /// Verifies that RunGitCommandAsync forwards repoPath, arguments array, and the provided CancellationToken
    /// to IProcessManager.ExecuteGit, and returns the same ProcessExecutionResult instance.
    /// Inputs:
    ///  - Diverse repoPath strings (including empty, whitespace, unicode).
    ///  - Argument arrays (empty, single, multiple with special characters/whitespace).
    ///  - CancellationToken states (canceled and not canceled).
    /// Expected:
    ///  - IProcessManager.ExecuteGit is called exactly once with the same repoPath, identical args, null env vars, and the same token.
    ///  - The returned ProcessExecutionResult is the exact instance provided by the mock.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(RunGitCommandAsync_ForwardingCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task RunGitCommandAsync_ForwardsParameters_ExecutesGitAndReturnsResult(string repoPath, string[] args, bool cancelled)
    {
        // Arrange
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        var expected = new ProcessExecutionResult
        {
            ExitCode = 0,
            TimedOut = false,
            StandardOutput = "ok",
            StandardError = string.Empty
        };

        var cts = new CancellationTokenSource();
        if (cancelled) cts.Cancel();
        var token = cts.Token;

        processManagerMock
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.SequenceEqual(args)),
                It.Is<Dictionary<string, string>>(env => env == null),
                token))
            .ReturnsAsync(expected);

        var sut = CreateSut(processManagerMock.Object);

        // Act
        var actual = await sut.RunGitCommandAsync(repoPath, args, token);

        // Assert
        processManagerMock.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.SequenceEqual(args)),
            It.Is<Dictionary<string, string>>(env => env == null),
            token), Times.Once);

        Assert.That(actual, Is.SameAs(expected));
    }

    /// <summary>
    /// Ensures that SetConfigValue throws ProcessFailedException with a message that includes the repo path,
    /// setting and value when the underlying git command fails due to timeout or non-zero exit code.
    /// Inputs:
    ///  - timedOut: true/false
    ///  - exitCode: 0/non-zero
    /// Expected:
    ///  - ProcessFailedException is thrown.
    ///  - Exception message contains "Failed to set {setting} value to {value} for {repoPath}" and "Exit code: {exitCode}".
    /// </summary>
    [Test]
    [TestCase(true, 0)]
    [TestCase(false, 1)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task SetConfigValue_CommandFailure_ThrowsProcessFailedExceptionWithExpectedMessage(bool timedOut, int exitCode)
    {
        // Arrange
        const string repoPath = "C:\\repo path";
        const string setting = "core.longpaths";
        const string value = "true";

        var remoteProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Loose);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Loose);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var failureResult = new ProcessExecutionResult
        {
            ExitCode = exitCode,
            TimedOut = timedOut,
            StandardOutput = string.Empty,
            StandardError = "error"
        };

        processManager
            .Setup(m => m.ExecuteGit(repoPath, "config", setting, value))
            .ReturnsAsync(failureResult);

        var sut = new LocalGitClient(
            remoteProvider.Object,
            telemetry.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        ProcessFailedException ex = null;
        try
        {
            await sut.SetConfigValue(repoPath, setting, value);
        }
        catch (ProcessFailedException e)
        {
            ex = e;
        }

        // Assert
        Assert.IsNotNull(ex, "Expected ProcessFailedException to be thrown.");
        StringAssert.Contains($"Failed to set {setting} value to {value} for {repoPath}", ex.Message);
        StringAssert.Contains($"Exit code: {exitCode}", ex.Message);
        processManager.Verify(m => m.ExecuteGit(repoPath, "config", setting, value), Times.Once);
    }

    /// <summary>
    /// Verifies that IsAncestorCommit invokes 'git merge-base --is-ancestor ancestor descendant' and
    /// returns true for exit code 0, false for exit code 1, without throwing.
    /// Inputs:
    ///  - repoPath, ancestor, descendant with varied edge cases (empty, whitespace, unicode).
    ///  - exitCode set to 0 or 1; TimedOut varied including true/false and negative exit code.
    /// Expected:
    ///  - For exit code 0 => true; for 1 or negative => false.
    ///  - IProcessManager.ExecuteGit is invoked once with exact arguments.
    ///  - No exception is thrown.
    /// </summary>
    [TestCaseSource(nameof(IsAncestorCommit_NonThrowingCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task IsAncestorCommit_ExitCodeZeroOrOne_ReturnsExpectedAndCallsGit(string repoPath, string ancestor, string descendant, int exitCode, bool timedOut, bool expected)
    {
        // Arrange
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var result = new ProcessExecutionResult
        {
            ExitCode = exitCode,
            TimedOut = timedOut,
            StandardOutput = string.Empty,
            StandardError = string.Empty
        };

        var expectedArgs = new[] { "merge-base", "--is-ancestor", ancestor, descendant };
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))))
            .ReturnsAsync(result);

        var sut = CreateSut(processManager.Object);

        // Act
        var actual = await sut.IsAncestorCommit(repoPath, ancestor, descendant);

        // Assert
        Assert.That(actual, Is.EqualTo(expected));
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))), Times.Once);
    }

    /// <summary>
    /// Ensures that IsAncestorCommit throws ProcessFailedException when the git command
    /// returns an exit code greater than 1, indicating invalid objects or other errors.
    /// Inputs:
    ///  - repoPath, ancestor, descendant including whitespace and unicode variations.
    ///  - exitCode > 1 (e.g., 2), TimedOut may be true/false.
    /// Expected:
    ///  - ProcessFailedException is thrown.
    ///  - Exception message contains the repoPath, ancestor, and descendant in the failure text.
    ///  - IProcessManager.ExecuteGit is invoked once with exact arguments.
    /// </summary>
    [TestCaseSource(nameof(IsAncestorCommit_ThrowingCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task IsAncestorCommit_ExitCodeGreaterThanOne_ThrowsProcessFailedException(string repoPath, string ancestor, string descendant, int exitCode, bool timedOut)
    {
        // Arrange
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var result = new ProcessExecutionResult
        {
            ExitCode = exitCode,
            TimedOut = timedOut,
            StandardOutput = "",
            StandardError = "fatal: bad object"
        };

        var expectedArgs = new[] { "merge-base", "--is-ancestor", ancestor, descendant };
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))))
            .ReturnsAsync(result);

        var sut = CreateSut(processManager.Object);

        // Act
        var ex = Assert.ThrowsAsync<ProcessFailedException>(async () => await sut.IsAncestorCommit(repoPath, ancestor, descendant));

        // Assert
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex.Message, Does.Contain("Failed to determine which commit of"));
        Assert.That(ex.Message, Does.Contain(repoPath));
        Assert.That(ex.Message, Does.Contain(ancestor));
        Assert.That(ex.Message, Does.Contain(descendant));
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))), Times.Once);
    }

    private static IEnumerable IsAncestorCommit_NonThrowingCases()
    {
        yield return new TestCaseData("repo", "a1", "d1", 0, false, true)
            .SetName("IsAncestorCommit_Exit0_ReturnsTrue");
        yield return new TestCaseData("repo", "a2", "d2", 1, false, false)
            .SetName("IsAncestorCommit_Exit1_ReturnsFalse");
        yield return new TestCaseData("", "", "", 0, false, true)
            .SetName("IsAncestorCommit_EmptyStrings_Exit0_ReturnsTrue");
        yield return new TestCaseData("   ", "   ", "   ", 1, true, false)
            .SetName("IsAncestorCommit_Whitespace_Exit1TimedOutTrue_ReturnsFalse");
        yield return new TestCaseData("C:\\rép🚀", "äbc123", "Ωdef456", -2, true, false)
            .SetName("IsAncestorCommit_TimeoutNegativeExit_ReturnsFalse");
    }

    private static IEnumerable IsAncestorCommit_ThrowingCases()
    {
        yield return new TestCaseData("/repo/path", "abc", "def", 2, false)
            .SetName("IsAncestorCommit_Exit2_Throws");
        yield return new TestCaseData("C:\\repo path", "old", "new", 3, true)
            .SetName("IsAncestorCommit_Exit3TimedOutTrue_Throws");
    }

    /// <summary>
    /// Verifies that ResolveConflict calls 'git checkout' with the correct side (--ours/--theirs)
    /// followed by 'git add' in order, and completes without throwing when both commands succeed.
    /// Inputs:
    ///  - repoPath and file variations (empty, whitespace, unicode/space-containing).
    ///  - ours boolean toggling checkout side.
    /// Expected:
    ///  - IProcessManager.ExecuteGit invoked first with ("checkout", "--ours"/"--theirs", file), then with ("add", file).
    ///  - No exception is thrown.
    /// </summary>
    [TestCaseSource(nameof(ResolveConflictSuccessCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ResolveConflict_Success_ExecutesCheckoutThenAddWithCorrectSide(bool ours, string repoPath, string file)
    {
        // Arrange
        var expectedSide = ours ? "--ours" : "--theirs";
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        var seq = new MockSequence();
        processManagerMock
            .InSequence(seq)
            .Setup(m => m.ExecuteGit(repoPath, "checkout", expectedSide, file))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });
        processManagerMock
            .InSequence(seq)
            .Setup(m => m.ExecuteGit(repoPath, "add", file))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });

        var sut = CreateSut(processManagerMock.Object);

        // Act
        await sut.ResolveConflict(repoPath, file, ours);

        // Assert
        processManagerMock.Verify(m => m.ExecuteGit(repoPath, "checkout", expectedSide, file), Times.Once);
        processManagerMock.Verify(m => m.ExecuteGit(repoPath, "add", file), Times.Once);
        processManagerMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures that when the initial 'git checkout --ours/--theirs <file>' fails (by timeout or non-zero exit),
    /// ResolveConflict throws and does not attempt to stage the file.
    /// Inputs:
    ///  - timedOut/exitCode combinations representing failure.
    ///  - repoPath and file strings (including spaces/unicode to validate message formatting).
    /// Expected:
    ///  - ProcessFailedException is thrown with a message containing "Failed to resolve conflict in {file} in {repoPath}".
    ///  - No subsequent 'git add' call is made.
    /// </summary>
    [TestCase(true, 0, "repo/path", "conflicted.txt")]
    [TestCase(false, 1, "C:\\repo path", "file with space.txt")]
    [TestCase(true, 42, "/r/ユニコード", "файл.txt")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ResolveConflict_CheckoutFailure_ThrowsAndDoesNotStage(bool timedOut, int exitCode, string repoPath, string file)
    {
        // Arrange
        var ours = true;
        var expectedSide = ours ? "--ours" : "--theirs";
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        processManagerMock
            .Setup(m => m.ExecuteGit(repoPath, "checkout", expectedSide, file))
            .ReturnsAsync(new ProcessExecutionResult { TimedOut = timedOut, ExitCode = exitCode });
        // No setup for "add" to ensure it is not called

        var sut = CreateSut(processManagerMock.Object);

        // Act
        var ex = Assert.ThrowsAsync<ProcessFailedException>(async () => await sut.ResolveConflict(repoPath, file, ours));

        // Assert
        Assert.That(ex.Message, Does.Contain($"Failed to resolve conflict in {file} in {repoPath}"));
        processManagerMock.Verify(m => m.ExecuteGit(repoPath, "checkout", expectedSide, file), Times.Once);
        processManagerMock.Verify(m => m.ExecuteGit(repoPath, "add", file), Times.Never);
        processManagerMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures that when checkout succeeds but staging via 'git add <file>' fails (by timeout or non-zero exit),
    /// ResolveConflict throws with the proper staging failure message.
    /// Inputs:
    ///  - timedOut/exitCode combinations representing failure.
    ///  - repoPath and file variations.
    /// Expected:
    ///  - ProcessFailedException is thrown with a message containing "Failed to stage resolved conflict in {file} in {repoPath}".
    ///  - Both checkout and add are invoked, in that order.
    /// </summary>
    [TestCase(false, 2, "repo", "a.txt")]
    [TestCase(true, 0, "/tmp/repo", "b.txt")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ResolveConflict_AddFailure_ThrowsWithStagingMessage(bool timedOut, int exitCode, string repoPath, string file)
    {
        // Arrange
        var ours = false;
        var expectedSide = ours ? "--ours" : "--theirs";
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        var seq = new MockSequence();
        processManagerMock
            .InSequence(seq)
            .Setup(m => m.ExecuteGit(repoPath, "checkout", expectedSide, file))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });
        processManagerMock
            .InSequence(seq)
            .Setup(m => m.ExecuteGit(repoPath, "add", file))
            .ReturnsAsync(new ProcessExecutionResult { TimedOut = timedOut, ExitCode = exitCode });

        var sut = CreateSut(processManagerMock.Object);

        // Act
        var ex = Assert.ThrowsAsync<ProcessFailedException>(async () => await sut.ResolveConflict(repoPath, file, ours));

        // Assert
        Assert.That(ex.Message, Does.Contain($"Failed to stage resolved conflict in {file} in {repoPath}"));
        processManagerMock.Verify(m => m.ExecuteGit(repoPath, "checkout", expectedSide, file), Times.Once);
        processManagerMock.Verify(m => m.ExecuteGit(repoPath, "add", file), Times.Once);
        processManagerMock.VerifyNoOtherCalls();
    }

    private static IEnumerable<TestCaseData> ResolveConflictSuccessCases()
    {
        yield return new TestCaseData(true, "repo/path", "conflicted.txt")
            .SetName("ResolveConflict_Success_Ours_DefaultPaths");
        yield return new TestCaseData(false, "C:\\repo path", "file with spaces.txt")
            .SetName("ResolveConflict_Success_Theirs_SpacesInPathAndFile");
        yield return new TestCaseData(true, "", "")
            .SetName("ResolveConflict_Success_EmptyStrings");
        yield return new TestCaseData(false, "/r/ユニコード", "файл.txt")
            .SetName("ResolveConflict_Success_Unicode");
    }

}



public class LocalGitClient_GetFileContentsAsync_Tests
{
    /// <summary>
    /// Verifies that when branch is null or empty, the method reads the file directly from disk
    /// and returns its content.
    /// Inputs:
    ///  - repoPath: a temporary directory containing the target file.
    ///  - relativeFilePath: nested relative path.
    ///  - branch: null or empty string.
    /// Expected:
    ///  - The method returns exactly the file content.
    /// </summary>
    [TestCase(null, TestName = "GetFileContentsAsync_BranchNull_FileExists_ReturnsContent")]
    [TestCase("", TestName = "GetFileContentsAsync_BranchEmpty_FileExists_ReturnsContent")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetFileContentsAsync_BranchNullOrEmpty_FileExists_ReturnsContent(string branch)
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), "darc-tests", Guid.NewGuid().ToString("N"));
        string repoPath = Path.Combine(tempRoot, "repo");
        string relativeFilePath = Path.Combine("sub", "dir", "file.txt");
        string fullDir = Path.Combine(repoPath, "sub", "dir");
        string fullPath = Path.Combine(repoPath, relativeFilePath);
        string expectedContent = "line1\nline2 🚀\r\nline3";

        Directory.CreateDirectory(fullDir);
        File.WriteAllText(fullPath, expectedContent, Encoding.UTF8);

        var sut = CreateSut();

        try
        {
            // Act
            string actual = await sut.GetFileContentsAsync(relativeFilePath, repoPath, branch);

            // Assert
            Assert.That(actual, Is.EqualTo(expectedContent));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Ensures that when the directory containing the file does not exist,
    /// but its parent directory (two levels up from the file) exists,
    /// DependencyFileNotFoundException is thrown with the expected message.
    /// Inputs:
    ///  - repoPath: a temporary directory with only "a" created (but not "a/b").
    ///  - relativeFilePath: "a/b/file.txt".
    ///  - branch: null (to force filesystem path).
    /// Expected:
    ///  - Throws DependencyFileNotFoundException with message:
    ///    "Found parent-directory path ('{parentTwoDirectoriesUp}') but unable to find specified file ('{relativeFilePath}')".
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetFileContentsAsync_DirectoryMissing_ParentTwoDirectoriesUpExists_ThrowsDependencyFileNotFound()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), "darc-tests", Guid.NewGuid().ToString("N"));
        string repoPath = Path.Combine(tempRoot, "repo");
        string relativeFilePath = Path.Combine("a", "b", "file.txt");

        Directory.CreateDirectory(repoPath);
        Directory.CreateDirectory(Path.Combine(repoPath, "a")); // create only "a", not "a/b"

        var sut = CreateSut();

        try
        {
            // Act
            var ex = Assert.ThrowsAsync<DependencyFileNotFoundException>(async () =>
                await sut.GetFileContentsAsync(relativeFilePath, repoPath, null));

            // Assert
            string parentTwoDirectoriesUp = Path.Combine(repoPath, "a");
            string expectedMessage = $"Found parent-directory path ('{parentTwoDirectoriesUp}') but unable to find specified file ('{relativeFilePath}')";
            Assert.That(ex.Message, Is.EqualTo(expectedMessage));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Ensures that when neither the file's directory nor the parent two levels up exists,
    /// DependencyFileNotFoundException is thrown with the expected message indicating both missing.
    /// Inputs:
    ///  - repoPath: a temporary directory without "a" or "a/b".
    ///  - relativeFilePath: "a/b/c/file.txt".
    ///  - branch: null (to force filesystem path).
    /// Expected:
    ///  - Throws DependencyFileNotFoundException with message:
    ///    "Neither parent-directory path ('{parentTwoDirectoriesUp}') nor specified file ('{relativeFilePath}') found."
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetFileContentsAsync_DirectoryAndParentMissing_ThrowsDependencyFileNotFound_WithBothMissingMessage()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), "darc-tests", Guid.NewGuid().ToString("N"));
        string repoPath = Path.Combine(tempRoot, "repo");
        string relativeFilePath = Path.Combine("a", "b", "c", "file.txt");
        Directory.CreateDirectory(repoPath);

        var sut = CreateSut();

        try
        {
            // Act
            var ex = Assert.ThrowsAsync<DependencyFileNotFoundException>(async () =>
                await sut.GetFileContentsAsync(relativeFilePath, repoPath, null));

            // Assert
            string parentTwoDirectoriesUp = Path.Combine(repoPath, "a", "b");
            string expectedMessage = $"Neither parent-directory path ('{parentTwoDirectoriesUp}') nor specified file ('{relativeFilePath}') found.";
            Assert.That(ex.Message, Is.EqualTo(expectedMessage));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Ensures that when the containing directory exists but the file does not,
    /// DependencyFileNotFoundException is thrown with the expected message including the full path.
    /// Inputs:
    ///  - repoPath: a temporary directory with "a/b" created.
    ///  - relativeFilePath: "a/b/file.txt" (file not created).
    ///  - branch: empty string (to force filesystem path).
    /// Expected:
    ///  - Throws DependencyFileNotFoundException with message: "Could not find {fullPath}".
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetFileContentsAsync_DirectoryExistsButFileMissing_ThrowsDependencyFileNotFound_WithFullPath()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), "darc-tests", Guid.NewGuid().ToString("N"));
        string repoPath = Path.Combine(tempRoot, "repo");
        string relativeFilePath = Path.Combine("a", "b", "file.txt");
        string fullDir = Path.Combine(repoPath, "a", "b");
        Directory.CreateDirectory(fullDir);

        var sut = CreateSut();

        try
        {
            // Act
            var ex = Assert.ThrowsAsync<DependencyFileNotFoundException>(async () =>
                await sut.GetFileContentsAsync(relativeFilePath, repoPath, ""));

            // Assert
            string expectedFullPath = Path.Combine(repoPath, relativeFilePath);
            string expectedMessage = $"Could not find {expectedFullPath}";
            Assert.That(ex.Message, Is.EqualTo(expectedMessage));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Partial test documenting the branch-provided code path that calls GetFileFromGitAsync.
    /// Inputs:
    ///  - Any repoPath and relativeFilePath.
    ///  - branch: non-empty (e.g., "main" or whitespace).
    /// Expected:
    ///  - This path depends on calling out to git via GetFileFromGitAsync, which cannot be intercepted
    ///    without altering the implementation (the method is non-virtual and the class is concrete).
    ///  - Marking this test as Inconclusive with guidance.
    /// </summary>
    [TestCase("main")]
    [TestCase("   ")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetFileContentsAsync_BranchProvided_Partial_Inconclusive(string branch)
    {
        Assert.Inconclusive("Cannot isolate GetFileFromGitAsync call path for branch-provided scenario without changing implementation. If GetFileFromGitAsync becomes mockable or exposed via an interface/virtual method, replace this inconclusive test with a concrete verification.");
    }

    private static LocalGitClient CreateSut()
    {
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Loose);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Loose);
        var processManager = new Mock<IProcessManager>(MockBehavior.Loose);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        return new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);
    }
}



public class LocalGitClient_ResetWorkingTree_Tests
{
    /// <summary>
    /// Verifies that when relativePath is null (defaults to "."), the method:
    ///  - Executes "git checkout ." and then "git clean -xdf ."
    ///  - Does not attempt to delete any directory
    ///  - Completes without throwing when both commands succeed.
    /// Inputs:
    ///  - repoPath: native "repo"
    ///  - relativePath: null (implicitly UnixPath.CurrentDir)
    /// Expected:
    ///  - IProcessManager.ExecuteGit is called twice with exact arguments for checkout and clean.
    ///  - IFileSystem.DeleteDirectory is never called.
    ///  - No exception is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ResetWorkingTree_NullRelativePath_CheckoutAndCleanExecuted_NoDeletion()
    {
        // Arrange
        var repoNative = new NativePath("repo");
        UnixPath relative = UnixPath.CurrentDir;

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var expectedCheckoutArgs = new[] { "checkout", (string)relative };
        var expectedCleanArgs = new[] { "clean", "-xdf", (string)relative };

        processManager
            .Setup(m => m.ExecuteGit(
                (string)repoNative,
                It.Is<string[]>(a => a.SequenceEqual(expectedCheckoutArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });

        processManager
            .Setup(m => m.ExecuteGit(
                (string)repoNative,
                It.Is<string[]>(a => a.SequenceEqual(expectedCleanArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        // Act
        await sut.ResetWorkingTree(repoNative, null);

        // Assert
        processManager.Verify(m => m.ExecuteGit((string)repoNative,
            It.Is<string[]>(a => a.SequenceEqual(expectedCheckoutArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);

        processManager.Verify(m => m.ExecuteGit((string)repoNative,
            It.Is<string[]>(a => a.SequenceEqual(expectedCleanArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);

        fileSystem.Verify(fs => fs.DeleteDirectory(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that when "git checkout <relativePath>" fails with the specific pathspec error for a subdirectory,
    /// the method deletes the combined path "repoPath/relativePath" and still runs the clean step.
    /// Inputs:
    ///  - repoPath: "repo"
    ///  - relativePath: "sub/module"
    ///  - checkout result: failed; StandardError contains the pathspec error for 'sub/module'
    ///  - clean result: success
    /// Expected:
    ///  - IFileSystem.DeleteDirectory is called once with "repo/sub/module" and recursive true.
    ///  - Both git commands are executed with exact args.
    ///  - No exception is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ResetWorkingTree_CheckoutFailsWithPathspecForSubdir_DeletesDirectoryAndCleans()
    {
        // Arrange
        var repoNative = new NativePath("repo");
        var relative = new UnixPath("sub/module");
        var expectedDeletionPath = (string)(repoNative / relative);

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var expectedCheckoutArgs = new[] { "checkout", (string)relative };
        var expectedCleanArgs = new[] { "clean", "-xdf", (string)relative };

        processManager
            .Setup(m => m.ExecuteGit(
                (string)repoNative,
                It.Is<string[]>(a => a.SequenceEqual(expectedCheckoutArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 1,
                TimedOut = false,
                StandardError = $"pathspec '{relative}' did not match any file(s) known to git"
            });

        processManager
            .Setup(m => m.ExecuteGit(
                (string)repoNative,
                It.Is<string[]>(a => a.SequenceEqual(expectedCleanArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });

        fileSystem
            .Setup(fs => fs.DeleteDirectory(expectedDeletionPath, true));

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        // Act
        await sut.ResetWorkingTree(repoNative, relative);

        // Assert
        processManager.Verify(m => m.ExecuteGit((string)repoNative,
            It.Is<string[]>(a => a.SequenceEqual(expectedCheckoutArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);

        fileSystem.Verify(fs => fs.DeleteDirectory(expectedDeletionPath, true), Times.Once);

        processManager.Verify(m => m.ExecuteGit((string)repoNative,
            It.Is<string[]>(a => a.SequenceEqual(expectedCleanArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);
    }

    /// <summary>
    /// Verifies that when "git checkout ." fails with the specific pathspec error for current directory,
    /// the method does not delete any working directory and still runs the clean step.
    /// Inputs:
    ///  - repoPath: "repo"
    ///  - relativePath: null (uses ".")
    ///  - checkout result: failed with "pathspec '.' did not match any file(s) known to git"
    ///  - clean result: success
    /// Expected:
    ///  - No call to IFileSystem.DeleteDirectory.
    ///  - Both git commands are executed with exact args.
    ///  - No exception is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ResetWorkingTree_CheckoutFailsWithPathspecForCurrentDir_DoesNotDeleteDirectoryAndCleans()
    {
        // Arrange
        var repoNative = new NativePath("repo");
        UnixPath relative = UnixPath.CurrentDir;

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var expectedCheckoutArgs = new[] { "checkout", (string)relative };
        var expectedCleanArgs = new[] { "clean", "-xdf", (string)relative };

        processManager
            .Setup(m => m.ExecuteGit(
                (string)repoNative,
                It.Is<string[]>(a => a.SequenceEqual(expectedCheckoutArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 1,
                TimedOut = false,
                StandardError = $"pathspec '{relative}' did not match any file(s) known to git"
            });

        processManager
            .Setup(m => m.ExecuteGit(
                (string)repoNative,
                It.Is<string[]>(a => a.SequenceEqual(expectedCleanArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        // Act
        await sut.ResetWorkingTree(repoNative, null);

        // Assert
        processManager.Verify(m => m.ExecuteGit((string)repoNative,
            It.Is<string[]>(a => a.SequenceEqual(expectedCheckoutArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);

        fileSystem.Verify(fs => fs.DeleteDirectory(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);

        processManager.Verify(m => m.ExecuteGit((string)repoNative,
            It.Is<string[]>(a => a.SequenceEqual(expectedCleanArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);
    }

    /// <summary>
    /// Ensures that a failure of the "git clean -xdf <path>" step throws a ProcessFailedException
    /// with the expected failure message, regardless of the checkout step outcome.
    /// Inputs:
    ///  - repoPath: "repo"
    ///  - relativePath: null (uses ".")
    ///  - checkout result: success
    ///  - clean result: failure (non-zero exit code)
    /// Expected:
    ///  - ProcessFailedException is thrown with message containing "Failed to clean the working tree!".
    ///  - Both git commands are invoked with exact arguments.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ResetWorkingTree_CleanFailure_ThrowsProcessFailedException()
    {
        // Arrange
        var repoNative = new NativePath("repo");
        UnixPath relative = UnixPath.CurrentDir;

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var expectedCheckoutArgs = new[] { "checkout", (string)relative };
        var expectedCleanArgs = new[] { "clean", "-xdf", (string)relative };

        processManager
            .Setup(m => m.ExecuteGit(
                (string)repoNative,
                It.Is<string[]>(a => a.SequenceEqual(expectedCheckoutArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });

        processManager
            .Setup(m => m.ExecuteGit(
                (string)repoNative,
                It.Is<string[]>(a => a.SequenceEqual(expectedCleanArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 2,
                TimedOut = false,
                StandardError = "failed cleaning"
            });

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        // Act + Assert
        var ex = Assert.ThrowsAsync<ProcessFailedException>(() => sut.ResetWorkingTree(repoNative, null));
        Assert.That(ex.Message, Does.Contain("Failed to clean the working tree!"));

        processManager.Verify(m => m.ExecuteGit((string)repoNative,
            It.Is<string[]>(a => a.SequenceEqual(expectedCheckoutArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);

        processManager.Verify(m => m.ExecuteGit((string)repoNative,
            It.Is<string[]>(a => a.SequenceEqual(expectedCleanArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);

        fileSystem.Verify(fs => fs.DeleteDirectory(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }
}



public class LocalGitClientCreateBranchAsyncTests
{
    /// <summary>
    /// Verifies that CreateBranchAsync passes the correct repo path and arguments to IProcessManager.ExecuteGit
    /// and completes without throwing when the process succeeds.
    /// Inputs:
    ///  - repoPath: varied strings (normal, empty, whitespace, unicode, very long).
    ///  - branchName: varied strings (normal, empty, whitespace, unicode, very long).
    ///  - overwriteExistingBranch: true/false toggling between "-B" and "-b".
    /// Expected:
    ///  - IProcessManager.ExecuteGit is invoked once with ["checkout", "-B"/"-b", branchName], null env vars, and default CancellationToken.
    ///  - No exception is thrown by CreateBranchAsync.
    /// </summary>
    [TestCaseSource(nameof(CreateBranch_Success_Cases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task CreateBranchAsync_ArgsDependOnOverwriteFlag_ExecutesGitWithExpectedArgumentsAndSucceeds(
        string repoPath,
        string branchName,
        bool overwriteExistingBranch)
    {
        // Arrange
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var expectedFlag = overwriteExistingBranch ? "-B" : "-b";
        var expectedArgs = new[] { "checkout", expectedFlag, branchName };

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 3 && a[0] == "checkout" && a[1] == expectedFlag && a[2] == branchName),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false });

        var sut = CreateSut(processManager.Object);

        // Act
        await sut.CreateBranchAsync(repoPath, branchName, overwriteExistingBranch);

        // Assert
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.SequenceEqual(expectedArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);
        processManager.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures that CreateBranchAsync throws when the underlying git command fails
    /// (either due to timeout or non-zero exit code), and that the exception message
    /// contains the branch name and repository path.
    /// Inputs:
    ///  - timedOut: true/false
    ///  - exitCode: 0 or non-zero
    /// Expected:
    ///  - An exception is thrown.
    ///  - Exception message contains "Failed to create {branchName} in {repoPath}".
    /// </summary>
    [TestCase(true, 0, TestName = "Failure_TimedOut_ZeroExitCode")]
    [TestCase(false, 2, TestName = "Failure_NotTimedOut_NonZeroExitCode")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task CreateBranchAsync_CommandFailure_ThrowsWithBranchAndRepoInMessage(bool timedOut, int exitCode)
    {
        // Arrange
        var repoPath = "/tmp/repo-🚀";
        var branchName = "feature/ünicode";
        var overwriteExistingBranch = true;
        var expectedFlag = overwriteExistingBranch ? "-B" : "-b";
        var expectedArgs = new[] { "checkout", expectedFlag, branchName };

        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.SequenceEqual(expectedArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut,
                StandardOutput = string.Empty,
                StandardError = "error"
            });

        var sut = CreateSut(processManager.Object);

        // Act
        Exception thrown = null;
        try
        {
            await sut.CreateBranchAsync(repoPath, branchName, overwriteExistingBranch);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        // Assert
        if (thrown == null)
        {
            throw new Exception("Expected an exception to be thrown when git checkout -B fails, but none was thrown.");
        }

        var expectedMessagePart = $"Failed to create {branchName} in {repoPath}";
        if (!thrown.Message.Contains(expectedMessagePart, StringComparison.Ordinal))
        {
            throw new Exception($"Exception message did not contain the expected text. Expected to find: '{expectedMessagePart}'. Actual: {thrown.Message}");
        }

        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.SequenceEqual(expectedArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))), Times.Once);
        processManager.VerifyNoOtherCalls();
    }

    private static IEnumerable CreateBranch_Success_Cases()
    {
        yield return new TestCaseData("/repo/path", "feature/x", false).SetName("Repo_Normal_Branch_Normal_CreateNew_b");
        yield return new TestCaseData("C:\\repo path", "branch with spaces", true).SetName("Repo_WithSpaces_Branch_WithSpaces_Overwrite_B");
        yield return new TestCaseData("", "", false).SetName("Repo_Empty_Branch_Empty_CreateNew_b");
        yield return new TestCaseData("   ", " \t\n", true).SetName("Repo_Whitespace_Branch_Whitespace_Overwrite_B");
        yield return new TestCaseData("/r/😃", "weird-😃", false).SetName("Repo_Unicode_Branch_Unicode_CreateNew_b");
        yield return new TestCaseData(new string('a', 512), new string('b', 512), true).SetName("Repo_Long_Branch_Long_Overwrite_B");
    }

    private static LocalGitClient CreateSut(IProcessManager processManager)
    {
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Loose).Object;
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Loose).Object;
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose).Object;
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;

        return new LocalGitClient(remoteTokenProvider, telemetryRecorder, processManager, fileSystem, logger);
    }
}



public class LocalGitClient_GetShaForRefAsync_Tests
{
    /// <summary>
    /// Verifies that when gitRef is a prefix of Constants.EmptyGitObject, the method short-circuits:
    /// Inputs:
    ///  - repoPath: arbitrary path.
    ///  - gitRef: prefixes of the known empty git object (including full value).
    /// Expected:
    ///  - _processManager.ExecuteGit is NOT called.
    ///  - The returned value is exactly the provided gitRef.
    /// </summary>
    [Test]
    [TestCase("4", TestName = "GetShaForRefAsync_EmptyObjectPrefix_SingleChar")]
    [TestCase("4b", TestName = "GetShaForRefAsync_EmptyObjectPrefix_TwoChars")]
    [TestCase("4b82", TestName = "GetShaForRefAsync_EmptyObjectPrefix_FourChars")]
    [TestCase("4b825dc642", TestName = "GetShaForRefAsync_EmptyObjectPrefix_TenChars")]
    [TestCase(Constants.EmptyGitObject, TestName = "GetShaForRefAsync_EmptyObjectPrefix_FullConstant")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetShaForRefAsync_GitRefIsEmptyObjectPrefix_ShortCircuitsAndReturnsInput(string gitRef)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);
        var repoPath = "/repo/path";

        // Act
        var result = await sut.GetShaForRefAsync(repoPath, gitRef);

        // Assert
        processManager.Verify(m => m.ExecuteGit(It.IsAny<string>(), It.IsAny<string[]>()), Times.Never);
        Assert.That(result, Is.EqualTo(gitRef));
    }

    /// <summary>
    /// Ensures that when gitRef is null, the method runs "git rev-parse HEAD" and returns trimmed output.
    /// Inputs:
    ///  - repoPath: varied cases include typical and whitespace-only.
    ///  - gitRef: null.
    /// Expected:
    ///  - _processManager.ExecuteGit called exactly once with ["rev-parse", Constants.HEAD].
    ///  - Returns StandardOutput.Trim() from the process result.
    /// </summary>
    [Test]
    [TestCase("C:\\repo", "  abc123 \r\n", "abc123", TestName = "GetShaForRefAsync_NullRef_RevParseHead_ReturnsTrimmedSha_WindowsPath")]
    [TestCase("/home/user/repo", "\nsha-out\n", "sha-out", TestName = "GetShaForRefAsync_NullRef_RevParseHead_ReturnsTrimmedSha_UnixPath")]
    [TestCase("   ", "  deadbeef  ", "deadbeef", TestName = "GetShaForRefAsync_NullRef_RevParseHead_ReturnsTrimmedSha_WhitespacePath")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetShaForRefAsync_GitRefNull_ExecutesRevParseHeadAndReturnsTrimmedOutput(string repoPath, string standardOutput, string expected)
    {
        // Arrange
        var processResult = new ProcessExecutionResult { ExitCode = 0, TimedOut = false, StandardOutput = standardOutput };

        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.Length == 2 && a[0] == "rev-parse" && a[1] == Constants.HEAD)))
            .ReturnsAsync(processResult);

        var sut = CreateSut(processManager.Object);

        // Act
        var actual = await sut.GetShaForRefAsync(repoPath, null);

        // Assert
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.Length == 2 && a[0] == "rev-parse" && a[1] == Constants.HEAD)), Times.Once);
        Assert.That(actual, Is.EqualTo(expected));
    }

    /// <summary>
    /// Validates that for a non-empty-object-prefix gitRef, the method executes "git rev-parse {gitRef}"
    /// and returns trimmed output.
    /// Inputs:
    ///  - repoPath: various string forms.
    ///  - gitRef: refs that are NOT prefixes of Constants.EmptyGitObject, including unicode and long strings.
    /// Expected:
    ///  - _processManager.ExecuteGit called once with ["rev-parse", gitRef].
    ///  - Returns StandardOutput.Trim().
    /// </summary>
    [Test]
    [TestCaseSource(nameof(NonEmptyObjectPrefixRefs))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetShaForRefAsync_GitRefNotEmptyObjectPrefix_ExecutesRevParseWithRefAndReturnsTrimmedOutput(string repoPath, string gitRef, string stdOut, string expected)
    {
        // Arrange
        var processResult = new ProcessExecutionResult { ExitCode = 0, TimedOut = false, StandardOutput = stdOut };

        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.Length == 2 && a[0] == "rev-parse" && a[1] == gitRef)))
            .ReturnsAsync(processResult);

        var sut = CreateSut(processManager.Object);

        // Act
        var actual = await sut.GetShaForRefAsync(repoPath, gitRef);

        // Assert
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.Length == 2 && a[0] == "rev-parse" && a[1] == gitRef)), Times.Once);
        Assert.That(actual, Is.EqualTo(expected));
    }

    /// <summary>
    /// Ensures that failure conditions from the git command (timeout or non-zero exit code) cause
    /// GetShaForRefAsync to throw ProcessFailedException with a message that includes the repo path.
    /// Inputs:
    ///  - (timedOut, exitCode) combinations that indicate failure.
    ///  - repoPath (varied) and gitRef (specific or null).
    /// Expected:
    ///  - ProcessFailedException is thrown.
    ///  - Exception message contains "Failed to find commit" and the repoPath.
    ///  - When gitRef is non-null, message contains the gitRef as well.
    /// </summary>
    [Test]
    [TestCase(false, 1, "/r/p", "feature/branch", TestName = "GetShaForRefAsync_CommandFailed_NonZeroExit_ThrowsWithRepoAndRef")]
    [TestCase(true, 0, "C:\\repo path", null, TestName = "GetShaForRefAsync_CommandFailed_TimedOut_ThrowsWithRepoPath")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetShaForRefAsync_CommandFailure_ThrowsProcessFailedException(bool timedOut, int exitCode, string repoPath, string gitRef)
    {
        // Arrange
        var processResult = new ProcessExecutionResult
        {
            TimedOut = timedOut,
            ExitCode = exitCode,
            StandardOutput = string.Empty,
            StandardError = "simulated error"
        };

        var expectedArgs = new[] { "rev-parse", gitRef ?? Constants.HEAD };

        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))))
            .ReturnsAsync(processResult);

        var sut = CreateSut(processManager.Object);

        // Act
        var ex = Assert.ThrowsAsync<ProcessFailedException>(async () => await sut.GetShaForRefAsync(repoPath, gitRef));

        // Assert
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))), Times.Once);
        Assert.That(ex.Message, Does.Contain("Failed to find commit"));
        Assert.That(ex.Message, Does.Contain(repoPath));
        if (gitRef != null)
        {
            Assert.That(ex.Message, Does.Contain(gitRef));
        }
    }

    /// <summary>
    /// Verifies that when gitRef is longer than the EmptyGitObject constant (thus not a prefix),
    /// the method treats it as a normal ref and calls git rev-parse with that value.
    /// Inputs:
    ///  - repoPath: "repo"
    ///  - gitRef: Constants.EmptyGitObject + "0" (length 41, not a prefix)
    /// Expected:
    ///  - _processManager.ExecuteGit called with ["rev-parse", gitRef]
    ///  - Returned value equals trimmed StandardOutput.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetShaForRefAsync_GitRefLongerThanEmptyConstant_TreatedAsNormalRef_ExecutesGit()
    {
        // Arrange
        var repoPath = "repo";
        var gitRef = Constants.EmptyGitObject + "0"; // 41 chars, cannot be a prefix of the 40-char constant
        var output = "  out-sha  \n";
        var expected = "out-sha";

        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.Length == 2 && a[0] == "rev-parse" && a[1] == gitRef)))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false, StandardOutput = output });

        var sut = CreateSut(processManager.Object);

        // Act
        var actual = await sut.GetShaForRefAsync(repoPath, gitRef);

        // Assert
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.Length == 2 && a[0] == "rev-parse" && a[1] == gitRef)), Times.Once);
        Assert.That(actual, Is.EqualTo(expected));
    }

    private static LocalGitClient CreateSut(IProcessManager processManager)
    {
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        return new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager, fileSystem.Object, logger.Object);
    }

    private static IEnumerable<TestCaseData> NonEmptyObjectPrefixRefs()
    {
        yield return new TestCaseData("/repo/path", "main", " main \r\n", "main").SetName("NonPrefix_MainBranch");
        yield return new TestCaseData("C:\\r", "refs/heads/feature/x", "\nsha123\n", "sha123").SetName("NonPrefix_RefsHeadsFeature");
        yield return new TestCaseData("/x/🚀", "🔥-branch", "🔥\n", "🔥").SetName("NonPrefix_Unicode");
        yield return new TestCaseData("", "4a", " out \n", "out").SetName("NonPrefix_StartsWith4ButNotEmptyObjectPrefix");
        yield return new TestCaseData("   ", "4b82x", " z \n", "z").SetName("NonPrefix_PartialButMismatchAtFifthChar");
        yield return new TestCaseData("/long", new string('x', 512), " sha \n", "sha").SetName("NonPrefix_VeryLongRef");
    }
}



public class LocalGitClient_GetObjectTypeAsync_Tests
{
    /// <summary>
    /// Verifies that GetObjectTypeAsync maps git 'cat-file -t' output (after trimming) to the expected GitObjectType
    /// and that it invokes IProcessManager.ExecuteGit with the exact arguments ["cat-file", "-t", objectSha].
    /// Inputs:
    ///  - Diverse repoPath and objectSha strings.
    ///  - StandardOutput variations including exact keywords, unexpected text, and whitespace-padded values.
    ///  - Different process success states (ExitCode/TimedOut) to confirm no exception is thrown and mapping uses only StandardOutput.
    /// Expected:
    ///  - Returns the correct GitObjectType for known outputs and Unknown for any non-matching output.
    ///  - IProcessManager.ExecuteGit is called once with repoPath and the exact expected arguments, null env vars, and default cancellation token.
    /// </summary>
    [TestCaseSource(nameof(GetObjectTypeAsync_Cases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetObjectTypeAsync_StandardOutputMapsToEnum_ExecutesWithExpectedArgs(
        string repoPath,
        string objectSha,
        string standardOutput,
        GitObjectType expectedType,
        int exitCode,
        bool timedOut)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var expectedArgs = new[] { "cat-file", "-t", objectSha };
        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.SequenceEqual(expectedArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut,
                StandardOutput = standardOutput
            });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        var result = await sut.GetObjectTypeAsync(repoPath, objectSha);

        // Assert
        Assert.AreEqual(expectedType, result, "Git object type should map from trimmed StandardOutput.");
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.SequenceEqual(expectedArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))),
            Times.Once);
        processManager.VerifyNoOtherCalls();
    }

    private static IEnumerable<TestCaseData> GetObjectTypeAsync_Cases()
    {
        // Known types - exact lowercase
        yield return new TestCaseData("/repo", "abc123", "commit", GitObjectType.Commit, 0, false)
            .SetName("GetObjectTypeAsync_OutputCommit_ReturnsCommit");
        yield return new TestCaseData("/repo", "abc123", "blob", GitObjectType.Blob, 0, false)
            .SetName("GetObjectTypeAsync_OutputBlob_ReturnsBlob");
        yield return new TestCaseData("/repo", "abc123", "tree", GitObjectType.Tree, 0, false)
            .SetName("GetObjectTypeAsync_OutputTree_ReturnsTree");
        yield return new TestCaseData("/repo", "abc123", "tag", GitObjectType.Tag, 0, false)
            .SetName("GetObjectTypeAsync_OutputTag_ReturnsTag");

        // Whitespace around output is trimmed
        yield return new TestCaseData("C:\\repo path", "deadbeef", " blob \r\n", GitObjectType.Blob, 0, false)
            .SetName("GetObjectTypeAsync_WhitespaceAroundBlob_ReturnsBlob");

        // Unknown outputs
        yield return new TestCaseData("/repo", "sha", "", GitObjectType.Unknown, 0, false)
            .SetName("GetObjectTypeAsync_EmptyOutput_ReturnsUnknown");
        yield return new TestCaseData("/repo", "sha", "BLOB", GitObjectType.Unknown, 0, false)
            .SetName("GetObjectTypeAsync_UppercaseBlob_ReturnsUnknown");
        yield return new TestCaseData("/repo", "sha", "remote-ref", GitObjectType.Unknown, 0, false)
            .SetName("GetObjectTypeAsync_UnexpectedText_ReturnsUnknown");

        // Non-zero exit code still maps using StandardOutput (no exception thrown)
        yield return new TestCaseData("/repo", "abc123", "commit", GitObjectType.Commit, 1, false)
            .SetName("GetObjectTypeAsync_FailedProcess_MapsFromOutputAndReturnsCommit");
    }
}



public class LocalGitClient_GetStagedFiles_Tests
{
    /// <summary>
    /// Verifies that GetStagedFiles executes "git diff --name-only --cached",
    /// and returns the trimmed, non-empty lines from StandardOutput preserving order and duplicates.
    /// Inputs:
    ///  - Various repoPath strings and StandardOutput contents (including whitespace, empty, and duplicates).
    /// Expected:
    ///  - IProcessManager.ExecuteGit is called once with ["diff", "--name-only", "--cached"].
    ///  - The returned collection equals the expected lines.
    /// </summary>
    [TestCaseSource(nameof(GetStagedFilesSuccessCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetStagedFiles_Success_ParsesStandardOutputAndReturnsLines(string repoPath, string standardOutput, string[] expected)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var expectedArgs = new[] { "diff", "--name-only", "--cached" };
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                TimedOut = false,
                StandardOutput = standardOutput
            });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        var result = await sut.GetStagedFiles(repoPath);

        // Assert
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))), Times.Once);
        Assert.That(result, Is.EqualTo(expected));
    }

    /// <summary>
    /// Ensures that GetStagedFiles throws when the underlying git command fails (non-zero exit or timeout),
    /// and that the exception message contains the repo path for diagnostics.
    /// Inputs:
    ///  - timedOut: whether the process timed out.
    ///  - exitCode: the process exit code.
    /// Expected:
    ///  - ProcessFailedException is thrown.
    ///  - Exception message contains "Failed to get staged files in {repoPath}" and the repo path.
    ///  - ExecuteGit is invoked once with the correct arguments.
    /// </summary>
    [TestCase(false, 1)]
    [TestCase(true, 0)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetStagedFiles_CommandFailure_ThrowsWithRepoPathInMessage(bool timedOut, int exitCode)
    {
        // Arrange
        var repoPath = "/repo/with/failure";
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var expectedArgs = new[] { "diff", "--name-only", "--cached" };
        processManager
            .Setup(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = exitCode,
                TimedOut = timedOut,
                StandardOutput = "",
                StandardError = "simulated error"
            });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        var ex = Assert.ThrowsAsync<ProcessFailedException>(async () => await sut.GetStagedFiles(repoPath));

        // Assert
        processManager.Verify(m => m.ExecuteGit(repoPath, It.Is<string[]>(a => a.SequenceEqual(expectedArgs))), Times.Once);
        Assert.That(ex!.Message, Does.Contain("Failed to get staged files in"));
        Assert.That(ex!.Message, Does.Contain(repoPath));
    }

    private static IEnumerable GetStagedFilesSuccessCases()
    {
        yield return new TestCaseData(
            "C:\\repo",
            "file1.txt\nfile2.txt\n",
            new[] { "file1.txt", "file2.txt" }
        ).SetName("GetStagedFiles_Success_WindowsPath_TwoLines");

        yield return new TestCaseData(
            "",
            "  file with spaces.txt  \r\n \t dir/file.cs \n",
            new[] { "file with spaces.txt", "dir/file.cs" }
        ).SetName("GetStagedFiles_Success_EmptyRepoPath_TrimsAndParses");

        yield return new TestCaseData(
            "   ",
            "\n\n\r\n",
            Array.Empty<string>()
        ).SetName("GetStagedFiles_Success_WhitespaceRepoPath_EmptyOutput_ReturnsEmpty");

        yield return new TestCaseData(
            "/r/🚀",
            "a\r\n\r\nb\r\n",
            new[] { "a", "b" }
        ).SetName("GetStagedFiles_Success_UnicodePath_IgnoresEmptyLines");

        yield return new TestCaseData(
            "/dup",
            "dup.txt\r\ndup.txt\n",
            new[] { "dup.txt", "dup.txt" }
        ).SetName("GetStagedFiles_Success_PreservesDuplicates_AndOrder");
    }
}



public class LocalGitClient_GetFileFromGitAsync_Tests
{
    /// <summary>
    /// Verifies that GetFileFromGitAsync normalizes Windows-style paths (replacing '\' with '/')
    /// and trims any leading '/' from the relative file path, constructing the exact "git show" argument.
    /// Inputs:
    ///  - Various repoPath values.
    ///  - relativeFilePath with leading slash and/or backslashes.
    ///  - revision strings.
    ///  - outputPath: null (no output redirection).
    /// Expected:
    ///  - IProcessManager.ExecuteGit is invoked once with arguments ["show", $"{revision}:{normalizedPath}"].
    ///  - No exception is thrown.
    /// </summary>
    [TestCaseSource(nameof(ArgsNormalizationCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetFileFromGitAsync_NormalizesPathAndBuildsShowArgs_ExecutesGitWithExpectedArguments(
        string repoPath,
        string relativeFilePath,
        string revision,
        string[] expectedArgs)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(a => a.SequenceEqual(expectedArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false, StandardOutput = "unused" });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        var _ = await sut.GetFileFromGitAsync(repoPath, relativeFilePath, revision);

        // Assert
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<IEnumerable<string>>(a => a.SequenceEqual(expectedArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))),
            Times.Once);
    }

    /// <summary>
    /// Ensures that when an outputPath is provided, GetFileFromGitAsync includes "--output" and the path
    /// in the git arguments and returns the process StandardOutput on success.
    /// Inputs:
    ///  - repoPath: "repo"
    ///  - relativeFilePath: "a\\b\\c.txt" (requires normalization)
    ///  - revision: "abc123"
    ///  - outputPath: "out.bin"
    /// Expected:
    ///  - ExecuteGit called with ["show", "abc123:a/b/c.txt", "--output", "out.bin"]
    ///  - Returned string equals the mocked StandardOutput.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetFileFromGitAsync_WithOutputPath_Success_ReturnsStandardOutputAndIncludesOutputArgs()
    {
        // Arrange
        var repoPath = "repo";
        var relativeFilePath = "a\\b\\c.txt";
        var revision = "abc123";
        var outputPath = "out.bin";
        var expectedArgs = new[] { "show", "abc123:a/b/c.txt", "--output", outputPath };
        var expectedOutput = "file-contents-from-git";

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(a => a.SequenceEqual(expectedArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false, StandardOutput = expectedOutput });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        var result = await sut.GetFileFromGitAsync(repoPath, relativeFilePath, revision, outputPath);

        // Assert
        Assert.That(result, Is.EqualTo(expectedOutput));
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<IEnumerable<string>>(a => a.SequenceEqual(expectedArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when the underlying git command fails (non-zero exit code),
    /// GetFileFromGitAsync returns null.
    /// Inputs:
    ///  - repoPath: "/r"
    ///  - relativeFilePath: "/leading/path.txt" (leading slash trimmed in args)
    ///  - revision: omitted (defaults to "HEAD")
    /// Expected:
    ///  - ExecuteGit called with ["show", "HEAD:leading/path.txt"]
    ///  - Returned value is null.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetFileFromGitAsync_CommandFailure_ReturnsNull()
    {
        // Arrange
        var repoPath = "/r";
        var relativeFilePath = "/leading/path.txt";
        var expectedArgs = new[] { "show", "HEAD:leading/path.txt" };

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<IEnumerable<string>>(a => a.SequenceEqual(expectedArgs)),
                It.Is<Dictionary<string, string>>(env => env == null),
                It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 1, TimedOut = false, StandardOutput = "ignored" });

        var sut = new LocalGitClient(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        var result = await sut.GetFileFromGitAsync(repoPath, relativeFilePath);

        // Assert
        Assert.That(result, Is.Null);
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<IEnumerable<string>>(a => a.SequenceEqual(expectedArgs)),
            It.Is<Dictionary<string, string>>(env => env == null),
            It.Is<CancellationToken>(ct => ct.Equals(default(CancellationToken)))),
            Times.Once);
    }

    private static IEnumerable ArgsNormalizationCases()
    {
        yield return new TestCaseData(
            "C:\\repo",
            "dir\\sub\\file.txt",
            "myrev",
            new[] { "show", "myrev:dir/sub/file.txt" })
            .SetName("Args_NormalizeWindowsSeparators");

        yield return new TestCaseData(
            "/repo",
            "/leading/path.txt",
            "rev2",
            new[] { "show", "rev2:leading/path.txt" })
            .SetName("Args_TrimLeadingSlash");

        yield return new TestCaseData(
            "/r/🙂",
            "\\leading\\win\\🙂\\path",
            "topic/branch",
            new[] { "show", "topic/branch:leading/win/🙂/path" })
            .SetName("Args_UnicodeAndWindowsSeparators");
    }
}



public class LocalGitClient_GetConfigValue_Tests
{
    /// <summary>
    /// Verifies that GetConfigValue calls IProcessManager.ExecuteGit with ["config", setting]
    /// and returns the trimmed StandardOutput on success.
    /// Inputs:
    ///  - repoPath and setting variations (empty, whitespace, typical, long, unicode).
    /// Expected:
    ///  - ExecuteGit invoked once with exact arguments.
    ///  - Returned string equals StandardOutput.Trim().
    /// </summary>
    [TestCaseSource(nameof(GetConfigValue_Success_Cases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetConfigValue_Success_ExecutesGitAndReturnsTrimmedOutput(string repoPath, string setting)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var standardOutput = " \r\n  value-✅ \t\n ";
        var expected = standardOutput.Trim();

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 2 && a[0] == "config" && a[1] == setting)))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, TimedOut = false, StandardOutput = standardOutput });

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        // Act
        var result = await sut.GetConfigValue(repoPath, setting);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.Length == 2 && a[0] == "config" && a[1] == setting)),
            Times.Once);
    }

    /// <summary>
    /// Ensures that GetConfigValue throws a ProcessFailedException when the git command fails
    /// due to timeout or non-zero exit code, and that the exception message contains both
    /// the repo path and setting values.
    /// Inputs:
    ///  - timedOut: true/false
    ///  - exitCode: 0/non-zero (failure when timedOut || exitCode != 0)
    /// Expected:
    ///  - ProcessFailedException is thrown with a message containing "Failed to determine {setting} value for {repoPath}".
    /// </summary>
    [TestCase(false, 1, TestName = "GetConfigValue_Failure_NonZeroExit_ThrowsWithMessage")]
    [TestCase(true, 0, TestName = "GetConfigValue_Failure_TimedOut_ThrowsWithMessage")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetConfigValue_Failure_ThrowsProcessFailedException_WithExpectedMessage(bool timedOut, int exitCode)
    {
        // Arrange
        var repoPath = "/r/ä✅";
        var setting = "core.longpaths";

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        processManager
            .Setup(m => m.ExecuteGit(
                repoPath,
                It.Is<string[]>(a => a.Length == 2 && a[0] == "config" && a[1] == setting)))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = exitCode, TimedOut = timedOut, StandardOutput = "", StandardError = "err" });

        var sut = new LocalGitClient(remoteTokenProvider.Object, telemetry.Object, processManager.Object, fileSystem.Object, logger.Object);

        // Act
        var ex = Assert.ThrowsAsync<ProcessFailedException>(async () => await sut.GetConfigValue(repoPath, setting));

        // Assert
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("Failed to determine"));
        Assert.That(ex.Message, Does.Contain(setting));
        Assert.That(ex.Message, Does.Contain(repoPath));

        processManager.Verify(m => m.ExecuteGit(
            repoPath,
            It.Is<string[]>(a => a.Length == 2 && a[0] == "config" && a[1] == setting)),
            Times.Once);
    }

    private static IEnumerable GetConfigValue_Success_Cases()
    {
        yield return new TestCaseData("repo", "user.name").SetName("GetConfigValue_Success_NormalInputs_ReturnsTrimmedOutput");
        yield return new TestCaseData("", "").SetName("GetConfigValue_Success_EmptyStrings_ReturnsTrimmedOutput");
        yield return new TestCaseData("   ", "   ").SetName("GetConfigValue_Success_WhitespaceStrings_ReturnsTrimmedOutput");
        yield return new TestCaseData("C:\\path with spaces\\r", "http.proxy").SetName("GetConfigValue_Success_WindowsPathWithSpaces_ReturnsTrimmedOutput");
        yield return new TestCaseData("/r/🚀", new string('x', 256)).SetName("GetConfigValue_Success_UnicodePath_LongSetting_ReturnsTrimmedOutput");
    }
}
