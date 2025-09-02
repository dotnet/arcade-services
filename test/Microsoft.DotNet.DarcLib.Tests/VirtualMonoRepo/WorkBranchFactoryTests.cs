// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class WorkBranchFactoryTests
{
    /// <summary>
    /// Ensures that when baseBranch is null, the factory resolves the current branch from "git rev-parse --abbrev-ref HEAD",
    /// trims any whitespace/newlines, creates the work branch with overwrite enabled, and returns a work branch whose OriginalBranch
    /// equals the resolved base branch.
    /// Inputs:
    ///  - baseBranch: null.
    ///  - repo.ExecuteGitCommand returns StandardOutput with various whitespace and unicode forms.
    /// Expected:
    ///  - ExecuteGitCommand is called with ["rev-parse", "--abbrev-ref", "HEAD"].
    ///  - CreateBranchAsync(branchName, true) is called exactly once.
    ///  - Returned IWorkBranch is not null and OriginalBranch equals the trimmed output.
    /// </summary>
    [Test]
    [TestCase("main", "main")]
    [TestCase(" main ", "main")]
    [TestCase("main\n", "main")]
    [TestCase("feature/üñîçødê", "feature/üñîçødê")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task CreateWorkBranchAsync_BaseBranchNull_UsesCurrentHeadAndCreatesBranch(string headOutput, string expectedBaseBranch)
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<WorkBranch>>(MockBehavior.Loose);

        var revParseResult = new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = headOutput
        };

        repoMock
            .Setup(r => r.ExecuteGitCommand("rev-parse", "--abbrev-ref", "HEAD"))
            .ReturnsAsync(revParseResult);

        var branchName = "my-feature-1";

        repoMock
            .Setup(r => r.CreateBranchAsync(branchName, true))
            .Returns(Task.CompletedTask);

        var sut = new WorkBranchFactory(loggerMock.Object);

        // Act
        var workBranch = await sut.CreateWorkBranchAsync(repoMock.Object, branchName, null);

        // Assert
        workBranch.Should().NotBeNull();
        workBranch.OriginalBranch.Should().Be(expectedBaseBranch);

        repoMock.Verify(r => r.ExecuteGitCommand("rev-parse", "--abbrev-ref", "HEAD"), Times.Once);
        repoMock.Verify(r => r.CreateBranchAsync(branchName, true), Times.Once);
    }

    /// <summary>
    /// Validates that when resolving the base branch fails (non-zero exit code),
    /// the factory throws a ProcessFailedException with the appropriate message and does not attempt to create a branch.
    /// Inputs:
    ///  - baseBranch: null.
    ///  - repo.ExecuteGitCommand returns ExitCode != 0.
    /// Expected:
    ///  - ProcessFailedException is thrown with a message containing "Failed to determine the current branch".
    ///  - CreateBranchAsync is never called.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task CreateWorkBranchAsync_BaseBranchResolutionFails_ThrowsProcessFailedException()
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<WorkBranch>>(MockBehavior.Loose);

        var revParseResult = new ProcessExecutionResult
        {
            ExitCode = 1,
            StandardOutput = string.Empty,
            StandardError = "fatal: not a git repository"
        };

        repoMock
            .Setup(r => r.ExecuteGitCommand("rev-parse", "--abbrev-ref", "HEAD"))
            .ReturnsAsync(revParseResult);

        var branchName = "any-branch";
        var sut = new WorkBranchFactory(loggerMock.Object);

        // Act
        Func<Task> action = () => sut.CreateWorkBranchAsync(repoMock.Object, branchName, null);

        // Assert
        await action.Should().ThrowAsync<ProcessFailedException>()
            .WithMessage("*Failed to determine the current branch*");

        repoMock.Verify(r => r.CreateBranchAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Ensures that if the provided baseBranch equals branchName, the factory throws an Exception with a helpful message,
    /// and does not attempt to resolve HEAD or create a branch.
    /// Inputs:
    ///  - baseBranch: equal to branchName, including edge cases such as empty, whitespace-only, unicode, and very long names.
    /// Expected:
    ///  - Exception is thrown with the exact expected message.
    ///  - ExecuteGitCommand and CreateBranchAsync are never called.
    /// </summary>
    [Test]
    [TestCase("main")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("topic/branch/β")]
    [TestCase("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task CreateWorkBranchAsync_BaseBranchEqualsBranchName_ThrowsHelpfulException(string sameName)
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<WorkBranch>>(MockBehavior.Loose);

        var sut = new WorkBranchFactory(loggerMock.Object);

        var expectedMessage =
            "You are already on branch " + sameName + ". " +
            "Previous sync probably failed and left the branch unmerged. " +
            "To complete the sync checkout the original branch and try again.";

        // Act
        Func<Task> action = () => sut.CreateWorkBranchAsync(repoMock.Object, sameName, sameName);

        // Assert
        await action.Should().ThrowAsync<Exception>()
            .WithMessage(expectedMessage);

        repoMock.Verify(r => r.ExecuteGitCommand(It.IsAny<string[]>()), Times.Never);
        repoMock.Verify(r => r.ExecuteGitCommand(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
        repoMock.Verify(r => r.CreateBranchAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that when a valid baseBranch is provided (different from branchName), the factory does not query HEAD,
    /// creates the branch with overwrite enabled, and returns a work branch with OriginalBranch equal to the provided baseBranch.
    /// Inputs:
    ///  - baseBranch: "base-line".
    ///  - branchName: varied values including empty, whitespace-only, and special characters.
    /// Expected:
    ///  - ExecuteGitCommand is never called to resolve HEAD.
    ///  - CreateBranchAsync(branchName, true) is called once with provided branchName.
    ///  - Returned IWorkBranch has OriginalBranch == "base-line".
    /// </summary>
    [Test]
    [TestCase("short")]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("feature#?$")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task CreateWorkBranchAsync_BaseBranchProvided_DoesNotQueryHeadAndCreatesWithOverwrite(string branchName)
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<WorkBranch>>(MockBehavior.Loose);

        repoMock
            .Setup(r => r.CreateBranchAsync(It.IsAny<string>(), true))
            .Returns(Task.CompletedTask);

        var baseBranch = "base-line";
        var sut = new WorkBranchFactory(loggerMock.Object);

        // Act
        var workBranch = await sut.CreateWorkBranchAsync(repoMock.Object, branchName, baseBranch);

        // Assert
        workBranch.Should().NotBeNull();
        workBranch.OriginalBranch.Should().Be(baseBranch);

        repoMock.Verify(r => r.ExecuteGitCommand(It.IsAny<string[]>()), Times.Never);
        repoMock.Verify(r => r.ExecuteGitCommand(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
        repoMock.Verify(r => r.CreateBranchAsync(branchName, true), Times.Once);
    }

    /// <summary>
    /// Verifies that when baseBranch is null, the factory resolves the current branch via 'git rev-parse --abbrev-ref HEAD',
    /// trims the output, logs the creation, creates the new work branch with overwrite flag, and returns a work branch
    /// whose OriginalBranch equals the resolved branch.
    /// Inputs:
    ///  - repo mock returning a successful ProcessExecutionResult with StandardOutput " main \r\n".
    ///  - branchName "feature/new".
    ///  - baseBranch null.
    /// Expected:
    ///  - repo.ExecuteGitCommand("rev-parse", "--abbrev-ref", "HEAD") is called once and succeeds.
    ///  - repo.CreateBranchAsync("feature/new", true) is called once.
    ///  - ILogger logs with Information once.
    ///  - Returned IWorkBranch has OriginalBranch == "main".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CreateWorkBranchAsync_BaseBranchNull_ResolvesCurrentBranchAndCreatesBranch()
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<WorkBranch>>(MockBehavior.Loose);

        var gitResult = new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = " main \r\n",
            StandardError = string.Empty
        };

        repoMock
            .Setup(r => r.ExecuteGitCommand("rev-parse", "--abbrev-ref", "HEAD"))
            .ReturnsAsync(gitResult);
        repoMock
            .Setup(r => r.CreateBranchAsync("feature/new", true))
            .Returns(Task.CompletedTask);

        var factory = new WorkBranchFactory(loggerMock.Object);

        // Act
        var workBranch = await factory.CreateWorkBranchAsync(repoMock.Object, "feature/new", null);

        // Assert
        workBranch.Should().NotBeNull();
        workBranch.OriginalBranch.Should().Be("main");

        repoMock.Verify(r => r.ExecuteGitCommand("rev-parse", "--abbrev-ref", "HEAD"), Times.Once);
        repoMock.Verify(r => r.CreateBranchAsync("feature/new", true), Times.Once);

        loggerMock.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == LogLevel.Information),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                {
                    var s = v.ToString();
                    return s != null && s.Contains("Creating a branch");
                }),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
            Times.Once);
    }

    /// <summary>
    /// Ensures that when the base branch equals the target branch, the factory throws an Exception
    /// preventing branch creation. Covers both cases: explicit baseBranch equals branchName, and
    /// baseBranch resolved from HEAD equals branchName.
    /// Inputs (parameterized by baseBranchIsNull):
    ///  - branchName "same".
    ///  - If baseBranchIsNull==false: baseBranch = "same".
    ///  - If baseBranchIsNull==true: repo returns HEAD as "same".
    /// Expected:
    ///  - Exception is thrown with a message containing "You are already on branch same".
    ///  - repo.CreateBranchAsync is never called.
    ///  - HEAD resolution is skipped when baseBranch provided; otherwise executed once.
    /// </summary>
    [TestCase(false, TestName = "CreateWorkBranchAsync_BaseBranchProvidedEqualToTarget_Throws")]
    [TestCase(true, TestName = "CreateWorkBranchAsync_BaseBranchResolvedEqualToTarget_Throws")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CreateWorkBranchAsync_BaseEqualsTarget_Throws(bool baseBranchIsNull)
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<WorkBranch>>(MockBehavior.Loose);

        var branchName = "same";
        string baseBranch = baseBranchIsNull ? null : "same";

        if (baseBranchIsNull)
        {
            var headResult = new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "same",
                StandardError = string.Empty
            };

            repoMock
                .Setup(r => r.ExecuteGitCommand("rev-parse", "--abbrev-ref", "HEAD"))
                .ReturnsAsync(headResult);
        }

        var factory = new WorkBranchFactory(loggerMock.Object);

        // Act
        Func<Task> act = () => factory.CreateWorkBranchAsync(repoMock.Object, branchName, baseBranch);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*You are already on branch same*");

        repoMock.Verify(r => r.CreateBranchAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);

        if (baseBranchIsNull)
        {
            repoMock.Verify(r => r.ExecuteGitCommand("rev-parse", "--abbrev-ref", "HEAD"), Times.Once);
        }
        else
        {
            repoMock.Verify(r => r.ExecuteGitCommand("rev-parse", "--abbrev-ref", "HEAD"), Times.Never);
        }
    }

    /// <summary>
    /// Verifies that when baseBranch is provided, the factory does not attempt to resolve HEAD
    /// and proceeds to create the work branch with overwriteExistingBranch set to true.
    /// Inputs:
    ///  - baseBranch = "main".
    ///  - branchName = "topic".
    /// Expected:
    ///  - repo.ExecuteGitCommand for HEAD is never called.
    ///  - repo.CreateBranchAsync("topic", true) is called once.
    ///  - Returned work branch has OriginalBranch == "main".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CreateWorkBranchAsync_BaseBranchProvided_SkipsHeadLookupAndCreatesBranch()
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<WorkBranch>>(MockBehavior.Loose);

        repoMock
            .Setup(r => r.CreateBranchAsync("topic", true))
            .Returns(Task.CompletedTask);

        var factory = new WorkBranchFactory(loggerMock.Object);

        // Act
        var workBranch = await factory.CreateWorkBranchAsync(repoMock.Object, "topic", "main");

        // Assert
        workBranch.Should().NotBeNull();
        workBranch.OriginalBranch.Should().Be("main");

        repoMock.Verify(r => r.ExecuteGitCommand("rev-parse", "--abbrev-ref", "HEAD"), Times.Never);
        repoMock.Verify(r => r.CreateBranchAsync("topic", true), Times.Once);
    }

    /// <summary>
    /// Ensures that if resolving the current branch fails (non-zero exit code or timeout),
    /// the factory propagates a ProcessFailedException with a message starting with the provided failure text.
    /// Inputs:
    ///  - baseBranch null.
    ///  - repo.ExecuteGitCommand returns ProcessExecutionResult with ExitCode = 1.
    /// Expected:
    ///  - ProcessFailedException is thrown.
    ///  - Exception message contains "Failed to determine the current branch".
    ///  - repo.CreateBranchAsync is never called.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CreateWorkBranchAsync_HeadLookupFails_ThrowsProcessFailedException()
    {
        // Arrange
        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<WorkBranch>>(MockBehavior.Loose);

        var failing = new ProcessExecutionResult
        {
            ExitCode = 1,
            StandardOutput = "",
            StandardError = "fatal: not a git repository"
        };

        repoMock
            .Setup(r => r.ExecuteGitCommand("rev-parse", "--abbrev-ref", "HEAD"))
            .ReturnsAsync(failing);

        var factory = new WorkBranchFactory(loggerMock.Object);

        // Act
        Func<Task> act = () => factory.CreateWorkBranchAsync(repoMock.Object, "branch-x", null);

        // Assert
        await act.Should().ThrowAsync<ProcessFailedException>()
            .WithMessage("*Failed to determine the current branch*");

        repoMock.Verify(r => r.CreateBranchAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }
}
