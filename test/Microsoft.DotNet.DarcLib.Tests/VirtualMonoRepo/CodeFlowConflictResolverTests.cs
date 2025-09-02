// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
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

[TestFixture]
public class CodeFlowConflictResolverTests
{
    /// <summary>
    /// Verifies that the constructor accepts valid dependency instances without throwing
    /// and does not interact with any of the provided dependencies during construction.
    /// Inputs:
    ///  - Mocks for IVmrInfo, IVmrPatchHandler, IFileSystem, ILogger with the specified MockBehavior.
    /// Expected:
    ///  - Instance is created successfully (not null).
    ///  - No calls are made to any of the dependency mocks during construction.
    /// </summary>
    /// <param name="behavior">The Moq behavior to use for all mocks to validate both Strict and Loose scenarios.</param>
    [TestCase(MockBehavior.Strict)]
    [TestCase(MockBehavior.Loose)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithValidDependencies_DoesNotThrow(MockBehavior behavior)
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(behavior);
        var patchHandlerMock = new Mock<IVmrPatchHandler>(behavior);
        var fileSystemMock = new Mock<IFileSystem>(behavior);
        var loggerMock = new Mock<ILogger>(behavior);

        // Act
        var resolver = new TestableCodeFlowConflictResolver(
            vmrInfoMock.Object,
            patchHandlerMock.Object,
            fileSystemMock.Object,
            loggerMock.Object);

        // Assert
        resolver.Should().NotBeNull();
        vmrInfoMock.VerifyNoOtherCalls();
        patchHandlerMock.VerifyNoOtherCalls();
        fileSystemMock.VerifyNoOtherCalls();
        loggerMock.VerifyNoOtherCalls();
    }

    private sealed class TestableCodeFlowConflictResolver : CodeFlowConflictResolver
    {
        public TestableCodeFlowConflictResolver(
            IVmrInfo vmrInfo,
            IVmrPatchHandler patchHandler,
            IFileSystem fileSystem,
            ILogger logger)
            : base(vmrInfo, patchHandler, fileSystem, logger)
        {
        }
    }

    /// <summary>
    /// Verifies that when 'git merge --no-commit --no-ff' succeeds:
    ///  - The repository is checked out to the specified head branch.
    ///  - A commit is performed with the expected message and options (allowEmpty: false).
    ///  - The method returns an empty collection, indicating no conflicted files.
    /// Inputs:
    ///  - Various head/merge branch names (including empty and unicode names).
    /// Expected:
    ///  - No exceptions.
    ///  - Empty result.
    ///  - Correct git command invocations.
    /// </summary>
    [TestCase("main", "feature/x")]
    [TestCase("", "bugfix/123")]
    [TestCase("feature/🔥-测试", "release/1.0")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryMergingBranch_MergeSucceeds_CommitsAndReturnsEmpty(string headBranch, string branchToMerge)
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;
        var sut = new TestableCodeFlowConflictResolver(
            Mock.Of<IVmrInfo>(),
            Mock.Of<IVmrPatchHandler>(),
            Mock.Of<IFileSystem>(),
            logger);

        var token = new CancellationTokenSource().Token;

        repoMock.Setup(r => r.CheckoutAsync(headBranch)).Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.RunGitCommandAsync(
                It.Is<string[]>(a => ArgsEqual(a, "merge", "--no-commit", "--no-ff", branchToMerge)),
                It.Is<CancellationToken>(ct => ct == token)))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        repoMock
            .Setup(r => r.CommitAsync(
                $"Merge {branchToMerge} into {headBranch}",
                false,
                It.IsAny<(string Name, string Email)?>(),
                CancellationToken.None))
            .Returns(Task.CompletedTask);

        // Act
        var result = await sut.PublicTryMergingBranch(repoMock.Object, headBranch, branchToMerge, token);

        // Assert
        result.Should().BeEmpty();
        repoMock.Verify(r => r.CheckoutAsync(headBranch), Times.Once);
        repoMock.Verify(r => r.RunGitCommandAsync(
            It.Is<string[]>(a => ArgsEqual(a, "merge", "--no-commit", "--no-ff", branchToMerge)),
            It.Is<CancellationToken>(ct => ct == token)), Times.Once);
        repoMock.Verify(r => r.RunGitCommandAsync(
            It.Is<string[]>(a => ArgsEqual(a, "diff", "--name-only", "--diff-filter=U", "--relative")),
            It.IsAny<CancellationToken>()), Times.Never);
        repoMock.Verify(r => r.CommitAsync(
            $"Merge {branchToMerge} into {headBranch}",
            false,
            It.IsAny<(string Name, string Email)?>(),
            CancellationToken.None), Times.Once);
    }

    /// <summary>
    /// Ensures that when 'git merge' succeeds but committing throws an exception with
    /// message containing "nothing to commit", the exception is swallowed (fast-forward case)
    /// and the method still returns an empty collection.
    /// Inputs:
    ///  - Valid repo, head branch, and branch to merge.
    /// Expected:
    ///  - Empty result.
    ///  - CommitAsync is attempted once and throws, but the method does not throw.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryMergingBranch_MergeSucceedsCommitThrowsNothingToCommit_ReturnsEmpty()
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;
        var sut = new TestableCodeFlowConflictResolver(
            Mock.Of<IVmrInfo>(),
            Mock.Of<IVmrPatchHandler>(),
            Mock.Of<IFileSystem>(),
            logger);

        var headBranch = "main";
        var branchToMerge = "feature/fast-forward";
        var token = CancellationToken.None;

        repoMock.Setup(r => r.CheckoutAsync(headBranch)).Returns(Task.CompletedTask);
        repoMock
            .Setup(r => r.RunGitCommandAsync(
                It.Is<string[]>(a => ArgsEqual(a, "merge", "--no-commit", "--no-ff", branchToMerge)),
                It.Is<CancellationToken>(ct => ct == token)))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        repoMock
            .Setup(r => r.CommitAsync(
                $"Merge {branchToMerge} into {headBranch}",
                false,
                It.IsAny<(string Name, string Email)?>(),
                CancellationToken.None))
            .ThrowsAsync(new Exception("nothing to commit - fast-forward"));

        // Act
        var result = await sut.PublicTryMergingBranch(repoMock.Object, headBranch, branchToMerge, token);

        // Assert
        result.Should().BeEmpty();
        repoMock.Verify(r => r.CommitAsync(
            $"Merge {branchToMerge} into {headBranch}",
            false,
            It.IsAny<(string Name, string Email)?>(),
            CancellationToken.None), Times.Once);
    }

    /// <summary>
    /// Validates that when 'git merge' fails and fetching conflicted files via 'git diff --name-only --diff-filter=U --relative'
    /// also fails, the method first attempts to abort the merge and then throws a ProcessFailedException.
    /// Inputs:
    ///  - Merge result: failure.
    ///  - Diff result: failure.
    /// Expected:
    ///  - A ProcessFailedException is thrown.
    ///  - 'git merge --abort' is executed with CancellationToken.None.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryMergingBranch_MergeFails_DiffFails_AbortsAndThrows()
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;
        var sut = new TestableCodeFlowConflictResolver(
            Mock.Of<IVmrInfo>(),
            Mock.Of<IVmrPatchHandler>(),
            Mock.Of<IFileSystem>(),
            logger);

        var headBranch = "main";
        var branchToMerge = "feature/conflict";
        var token = new CancellationTokenSource().Token;

        repoMock.Setup(r => r.CheckoutAsync(headBranch)).Returns(Task.CompletedTask);

        var mergeFailed = new ProcessExecutionResult { ExitCode = 1, StandardError = "merge failed" };
        var diffFailed = new ProcessExecutionResult { ExitCode = 2, StandardError = "diff failed" };
        var abortSucceeded = new ProcessExecutionResult { ExitCode = 0 };

        var sequence = repoMock
            .SetupSequence(r => r.RunGitCommandAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mergeFailed) // merge
            .ReturnsAsync(diffFailed)  // diff
            .ReturnsAsync(abortSucceeded); // merge --abort

        // Act
        ProcessFailedException thrown = null;
        try
        {
            await sut.PublicTryMergingBranch(repoMock.Object, headBranch, branchToMerge, token);
        }
        catch (ProcessFailedException ex)
        {
            thrown = ex;
        }

        // Assert
        (thrown != null).Should().BeTrue();
        repoMock.Verify(r => r.RunGitCommandAsync(
            It.Is<string[]>(a => ArgsEqual(a, "merge", "--no-commit", "--no-ff", branchToMerge)),
            It.Is<CancellationToken>(ct => ct == token)), Times.Once);
        repoMock.Verify(r => r.RunGitCommandAsync(
            It.Is<string[]>(a => ArgsEqual(a, "diff", "--name-only", "--diff-filter=U", "--relative")),
            It.Is<CancellationToken>(ct => ct == token)), Times.Once);
        repoMock.Verify(r => r.RunGitCommandAsync(
            It.Is<string[]>(a => ArgsEqual(a, "merge", "--abort")),
            It.Is<CancellationToken>(ct => ct == CancellationToken.None)), Times.Once);
    }

    /// <summary>
    /// Ensures that when 'git merge' fails but retrieving conflicted files via diff succeeds,
    /// the method returns a collection of UnixPath objects corresponding to the conflicted files.
    /// Inputs:
    ///  - Merge result: failure.
    ///  - Diff result: success with multiple lines including whitespace and CRLF.
    /// Expected:
    ///  - Returned paths match the diff output (trimmed and excluding empty lines).
    ///  - No attempt to abort the merge is made.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryMergingBranch_MergeFails_DiffSucceeds_ReturnsConflictedPaths()
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;
        var sut = new TestableCodeFlowConflictResolver(
            Mock.Of<IVmrInfo>(),
            Mock.Of<IVmrPatchHandler>(),
            Mock.Of<IFileSystem>(),
            logger);

        var headBranch = "main";
        var branchToMerge = "feature/conflict-list";
        var token = CancellationToken.None;

        repoMock.Setup(r => r.CheckoutAsync(headBranch)).Returns(Task.CompletedTask);

        var mergeFailed = new ProcessExecutionResult { ExitCode = 1, StandardError = "merge failed" };
        var diffSucceeded = new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "  a.txt  \r\nb/c.txt\n\r\n  d.txt  \n"
        };

        repoMock
            .SetupSequence(r => r.RunGitCommandAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mergeFailed)   // merge
            .ReturnsAsync(diffSucceeded); // diff

        // Act
        var result = await sut.PublicTryMergingBranch(repoMock.Object, headBranch, branchToMerge, token);

        // Assert
        var expected = new[] { new UnixPath("a.txt"), new UnixPath("b/c.txt"), new UnixPath("d.txt") };
        result.Count.Should().Be(expected.Length);
        result.Should().BeEquivalentTo(expected);
        repoMock.Verify(r => r.RunGitCommandAsync(
            It.Is<string[]>(a => ArgsEqual(a, "merge", "--no-commit", "--no-ff", branchToMerge)),
            It.Is<CancellationToken>(ct => ct == token)), Times.Once);
        repoMock.Verify(r => r.RunGitCommandAsync(
            It.Is<string[]>(a => ArgsEqual(a, "diff", "--name-only", "--diff-filter=U", "--relative")),
            It.Is<CancellationToken>(ct => ct == token)), Times.Once);
        repoMock.Verify(r => r.RunGitCommandAsync(
            It.Is<string[]>(a => ArgsEqual(a, "merge", "--abort")),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static bool ArgsEqual(string[] actual, params string[] expected)
        => actual != null && expected != null && actual.SequenceEqual(expected);

    /// <summary>
    /// A test-only derived type exposing the protected static AbortMerge method.
    /// </summary>
    private sealed class ExposedResolver : CodeFlowConflictResolver
    {
        public ExposedResolver(IVmrInfo vmrInfo, IVmrPatchHandler patchHandler, IFileSystem fileSystem, ILogger logger)
            : base(vmrInfo, patchHandler, fileSystem, logger)
        {
        }

        public static Task InvokeAbortMerge(ILocalGitRepo repo) => AbortMerge(repo);
    }

    /// <summary>
    /// Ensures that AbortMerge calls 'git merge --abort' with CancellationToken.None and completes successfully.
    /// Inputs:
    ///  - ILocalGitRepo returns a successful ProcessExecutionResult (ExitCode == 0, TimedOut == false).
    /// Expected:
    ///  - No exception is thrown.
    ///  - RunGitCommandAsync is invoked exactly once with ["merge", "--abort"] and CancellationToken.None.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task AbortMerge_GitSucceeds_DoesNotThrowAndInvokesCorrectCommand()
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);

        string[] capturedArgs = Array.Empty<string>();
        CancellationToken capturedToken = default;

        var successResult = new ProcessExecutionResult
        {
            ExitCode = 0,
            TimedOut = false,
            StandardError = string.Empty,
            StandardOutput = string.Empty
        };

        repoMock
            .Setup(r => r.RunGitCommandAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback<string[], CancellationToken>((args, token) =>
            {
                capturedArgs = args;
                capturedToken = token;
            })
            .ReturnsAsync(successResult);

        // Act
        await ExposedResolver.InvokeAbortMerge(repoMock.Object);

        // Assert
        repoMock.Verify(r => r.RunGitCommandAsync(It.Is<string[]>(a => a.SequenceEqual(new[] { "merge", "--abort" })), It.Is<CancellationToken>(t => t == CancellationToken.None)), Times.Once);
        capturedArgs.Should().Equal(new[] { "merge", "--abort" });
        capturedToken.Should().Be(CancellationToken.None);
    }

    /// <summary>
    /// Verifies that AbortMerge throws ProcessFailedException when the git invocation fails.
    /// Inputs:
    ///  - ILocalGitRepo returns a failing ProcessExecutionResult (timed out or non-zero exit code).
    /// Expected:
    ///  - ProcessFailedException is thrown with a message indicating merge abort failure.
    /// </summary>
    [TestCase(true, 0, TestName = "AbortMerge_GitTimedOut_ThrowsProcessFailedException")]
    [TestCase(false, 1, TestName = "AbortMerge_GitNonZeroExit_ThrowsProcessFailedException")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task AbortMerge_GitFails_ThrowsProcessFailedException(bool timedOut, int exitCode)
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);

        var failResult = new ProcessExecutionResult
        {
            ExitCode = exitCode,
            TimedOut = timedOut,
            StandardError = "error",
            StandardOutput = string.Empty
        };

        repoMock
            .Setup(r => r.RunGitCommandAsync(It.Is<string[]>(a => a.SequenceEqual(new[] { "merge", "--abort" })), It.Is<CancellationToken>(t => t == CancellationToken.None)))
            .ReturnsAsync(failResult);

        // Act
        Func<Task> act = () => ExposedResolver.InvokeAbortMerge(repoMock.Object);

        // Assert
        await act.Should().ThrowAsync<ProcessFailedException>()
            .WithMessage("*Failed to abort a merge when resolving version file conflicts*");
        repoMock.Verify(r => r.RunGitCommandAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
