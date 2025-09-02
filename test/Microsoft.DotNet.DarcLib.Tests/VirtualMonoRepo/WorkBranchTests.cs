// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;


namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class WorkBranchTests
{
    /// <summary>
    /// Verifies that a successful merge results in a commit with the provided commit message
    /// and that the expected merge git arguments are used.
    /// Inputs:
    ///  - Various commit message values (empty, whitespace, long, special characters).
    /// Expected:
    ///  - CheckoutAsync called with OriginalBranch.
    ///  - ExecuteGitCommand called with ["merge", workBranch, "--no-commit", "--no-edit", "--squash", "-q"].
    ///  - CommitAsync invoked with the same commit message and allowEmpty = true.
    ///  - No exception is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(ValidCommitMessages))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task MergeBackAsync_SuccessfulMerge_CommitsAndUsesExpectedGitArgs(string commitMessage)
    {
        // Arrange
        var originalBranch = "main";
        var workBranch = "feature/xyz";

        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        repoMock
            .Setup(r => r.CheckoutAsync(originalBranch))
            .Returns(Task.CompletedTask);

        // Initial merge succeeds
        repoMock
            .Setup(r => r.ExecuteGitCommand(
                It.Is<string[]>(a => IsExpectedMergeArgs(a, workBranch)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(success: true));

        // Commit expectation
        repoMock
            .Setup(r => r.CommitAsync(
                commitMessage,
                true,
                It.Is<(string Name, string Email)?>(a => a == null),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new WorkBranch(repoMock.Object, loggerMock.Object, originalBranch, workBranch);

        // Act
        Func<Task> act = () => sut.MergeBackAsync(commitMessage);

        // Assert
        await act.Should().NotThrowAsync();

        repoMock.Verify(r => r.CheckoutAsync(originalBranch), Times.Once);
        repoMock.Verify(r => r.ExecuteGitCommand(
                It.Is<string[]>(a => IsExpectedMergeArgs(a, workBranch)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        repoMock.Verify(r => r.CommitAsync(
                commitMessage,
                true,
                It.Is<(string Name, string Email)?>(a => a == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Ensures that when an initial merge fails due to whitespace-only/EOL differences,
    /// the method performs a whitespace diff, stages all changes, retries the merge, and then commits.
    /// Inputs:
    ///  - Initial merge: fails (non-conflict).
    ///  - "git diff -w": succeeds.
    ///  - "git add -A": succeeds.
    ///  - Second merge attempt: succeeds.
    /// Expected:
    ///  - ExecuteGitCommand with merge args is called twice.
    ///  - "git diff -w" and "git add -A" are executed.
    ///  - CommitAsync is called once.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task MergeBackAsync_WhitespaceOnlyChanges_StagesAndRetriesMergeAndCommits()
    {
        // Arrange
        var originalBranch = "main";
        var workBranch = "feature/eol-fix";
        var commitMessage = "Fix EOL whitespace";

        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        repoMock.Setup(r => r.CheckoutAsync(originalBranch)).Returns(Task.CompletedTask);

        // First merge fails, second succeeds
        repoMock
            .SetupSequence(r => r.ExecuteGitCommand(
                It.Is<string[]>(a => IsExpectedMergeArgs(a, workBranch)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(success: false, stdErr: "some non-conflict error"))
            .ReturnsAsync(CreateResult(success: true));

        // diff -w succeeds
        repoMock
            .Setup(r => r.ExecuteGitCommand(
                It.Is<string[]>(a => a.Length == 2 && a[0] == "diff" && a[1] == "-w")))
            .ReturnsAsync(CreateResult(success: true));

        // add -A succeeds
        repoMock
            .Setup(r => r.ExecuteGitCommand(
                It.Is<string[]>(a => a.Length == 2 && a[0] == "add" && a[1] == "-A")))
            .ReturnsAsync(CreateResult(success: true));

        // Commit expectation
        repoMock
            .Setup(r => r.CommitAsync(
                commitMessage,
                true,
                It.Is<(string Name, string Email)?>(a => a == null),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new WorkBranch(repoMock.Object, loggerMock.Object, originalBranch, workBranch);

        // Act
        await sut.MergeBackAsync(commitMessage);

        // Assert
        repoMock.Verify(r => r.ExecuteGitCommand(
                It.Is<string[]>(a => IsExpectedMergeArgs(a, workBranch)),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        repoMock.Verify(r => r.ExecuteGitCommand(
                It.Is<string[]>(a => a.Length == 2 && a[0] == "diff" && a[1] == "-w")),
            Times.Once);
        repoMock.Verify(r => r.ExecuteGitCommand(
                It.Is<string[]>(a => a.Length == 2 && a[0] == "add" && a[1] == "-A")),
            Times.Once);
        repoMock.Verify(r => r.CommitAsync(
                commitMessage,
                true,
                It.Is<(string Name, string Email)?>(a => a == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Validates that a merge conflict triggers a WorkBranchInConflictException and prevents committing.
    /// Inputs:
    ///  - Initial merge: fails with "CONFLICT (content): Merge conflict" in stderr.
    ///  - "git diff -w": fails (so no staging/retry occurs).
    /// Expected:
    ///  - WorkBranchInConflictException is thrown.
    ///  - CommitAsync is not called.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task MergeBackAsync_MergeConflict_ThrowsWorkBranchInConflictExceptionAndDoesNotCommit()
    {
        // Arrange
        var originalBranch = "main";
        var workBranch = "feature/conflict";
        var commitMessage = "Will conflict";

        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        repoMock.Setup(r => r.CheckoutAsync(originalBranch)).Returns(Task.CompletedTask);

        // First merge fails with conflict
        repoMock
            .Setup(r => r.ExecuteGitCommand(
                It.Is<string[]>(a => IsExpectedMergeArgs(a, workBranch)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(success: false, stdErr: "CONFLICT (content): Merge conflict in file"));

        // diff -w fails
        repoMock
            .Setup(r => r.ExecuteGitCommand(
                It.Is<string[]>(a => a.Length == 2 && a[0] == "diff" && a[1] == "-w")))
            .ReturnsAsync(CreateResult(success: false, stdErr: "diff failed"));

        var sut = new WorkBranch(repoMock.Object, loggerMock.Object, originalBranch, workBranch);

        // Act
        Func<Task> act = () => sut.MergeBackAsync(commitMessage);

        // Assert
        await act.Should().ThrowAsync<WorkBranchInConflictException>();
        repoMock.Verify(r => r.CommitAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<(string Name, string Email)?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Ensures that when staging whitespace-only changes fails, the operation throws a ProcessFailedException
    /// and does not attempt to commit.
    /// Inputs:
    ///  - Initial merge: fails (non-conflict).
    ///  - "git diff -w": succeeds.
    ///  - "git add -A": fails, causing ThrowIfFailed to raise ProcessFailedException.
    /// Expected:
    ///  - ProcessFailedException is thrown with a message indicating staging failure.
    ///  - CommitAsync is not called.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task MergeBackAsync_StagingWhitespaceChangesFails_ThrowsProcessFailedException()
    {
        // Arrange
        var originalBranch = "main";
        var workBranch = "feature/eol-stage-failure";
        var commitMessage = "stage failure";

        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        repoMock.Setup(r => r.CheckoutAsync(originalBranch)).Returns(Task.CompletedTask);

        // First merge fails
        repoMock
            .Setup(r => r.ExecuteGitCommand(
                It.Is<string[]>(a => IsExpectedMergeArgs(a, workBranch)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResult(success: false, stdErr: "non-conflict failure"));

        // diff -w succeeds
        repoMock
            .Setup(r => r.ExecuteGitCommand(
                It.Is<string[]>(a => a.Length == 2 && a[0] == "diff" && a[1] == "-w")))
            .ReturnsAsync(CreateResult(success: true));

        // add -A fails -> ThrowIfFailed should throw
        repoMock
            .Setup(r => r.ExecuteGitCommand(
                It.Is<string[]>(a => a.Length == 2 && a[0] == "add" && a[1] == "-A")))
            .ReturnsAsync(CreateResult(success: false, stdErr: "index lock"));

        var sut = new WorkBranch(repoMock.Object, loggerMock.Object, originalBranch, workBranch);

        // Act
        Func<Task> act = () => sut.MergeBackAsync(commitMessage);

        // Assert
        var assertion = await act.Should().ThrowAsync<ProcessFailedException>();
        assertion.Which.Message.Should().Contain("Failed to stage whitespace-only EOL changes");
        repoMock.Verify(r => r.CommitAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<(string Name, string Email)?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static bool IsExpectedMergeArgs(string[] args, string workBranch)
    {
        return args.Length == 6
            && args[0] == "merge"
            && args[1] == workBranch
            && args[2] == "--no-commit"
            && args[3] == "--no-edit"
            && args[4] == "--squash"
            && args[5] == "-q";
    }

    private static ProcessExecutionResult CreateResult(bool success, string stdErr = "")
    {
        return new ProcessExecutionResult
        {
            ExitCode = success ? 0 : 1,
            StandardError = stdErr,
            TimedOut = false
        };
    }

    private static readonly string LongCommitMessage = new string('x', 2048);

    private static readonly object[] ValidCommitMessages =
    {
            "",                       // empty
            "   ",                    // whitespace only
            LongCommitMessage,        // very long
            "🚀✨\r\n\t!@#$%^&*()[]{};:'\",.<>/?\\|`~" // special/control characters
        };
}
