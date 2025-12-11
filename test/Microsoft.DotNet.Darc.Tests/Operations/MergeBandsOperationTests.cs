// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using AwesomeAssertions;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class MergeBandsOperationTests
{
    private Mock<IProcessManager> _processManagerMock = null!;
    private Mock<ILogger<MergeBandsOperation>> _loggerMock = null!;

    [SetUp]
    public void Setup()
    {
        _processManagerMock = new Mock<IProcessManager>();
        _loggerMock = new Mock<ILogger<MergeBandsOperation>>();
    }

    [Test]
    public async Task MergeBandsOperationShouldReturnErrorWhenSourceBranchIsEmpty()
    {
        MergeBandsCommandLineOptions options = new()
        {
            SourceBranch = string.Empty
        };

        MergeBandsOperation operation = new(options, _processManagerMock.Object, _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);
    }

    [Test]
    public async Task MergeBandsOperationShouldExecuteGitCommands()
    {
        string sourceBranch = "release/10.0.1xx";
        string targetBranch = "release/10.0.2xx";
        string vmrPath = "/path/to/vmr";

        _processManagerMock.Setup(pm => pm.FindGitRoot(It.IsAny<string>()))
            .Returns(vmrPath);

        _processManagerMock.Setup(pm => pm.ExecuteGit(vmrPath, "branch", "--show-current"))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = targetBranch });

        _processManagerMock.Setup(pm => pm.ExecuteGit(vmrPath, "merge", sourceBranch))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        _processManagerMock.Setup(pm => pm.ExecuteGit(vmrPath, "reset", "--", "src"))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        _processManagerMock.Setup(pm => pm.ExecuteGit(vmrPath, "checkout", targetBranch, "--", It.IsAny<string>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        _processManagerMock.Setup(pm => pm.ExecuteGit(vmrPath, "clean", "-fdx", "--", "src"))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        MergeBandsCommandLineOptions options = new()
        {
            SourceBranch = sourceBranch
        };

        MergeBandsOperation operation = new(options, _processManagerMock.Object, _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.SuccessCode);

        // Verify all the git commands were called
        _processManagerMock.Verify(pm => pm.ExecuteGit(vmrPath, "branch", "--show-current"), Times.Once);
        _processManagerMock.Verify(pm => pm.ExecuteGit(vmrPath, "merge", sourceBranch), Times.Once);
        _processManagerMock.Verify(pm => pm.ExecuteGit(vmrPath, "reset", "--", "src"), Times.Once);
        _processManagerMock.Verify(pm => pm.ExecuteGit(vmrPath, "clean", "-fdx", "--", "src"), Times.Once);
    }

    [Test]
    public async Task MergeBandsOperationShouldReturnErrorWhenMergeFails()
    {
        string sourceBranch = "release/10.0.1xx";
        string targetBranch = "release/10.0.2xx";
        string vmrPath = "/path/to/vmr";

        _processManagerMock.Setup(pm => pm.FindGitRoot(It.IsAny<string>()))
            .Returns(vmrPath);

        _processManagerMock.Setup(pm => pm.ExecuteGit(vmrPath, "branch", "--show-current"))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0, StandardOutput = targetBranch });

        _processManagerMock.Setup(pm => pm.ExecuteGit(vmrPath, "merge", sourceBranch))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 1, StandardError = "Merge conflict" });

        MergeBandsCommandLineOptions options = new()
        {
            SourceBranch = sourceBranch
        };

        MergeBandsOperation operation = new(options, _processManagerMock.Object, _loggerMock.Object);

        int result = await operation.ExecuteAsync();

        result.Should().Be(Constants.ErrorCode);
    }
}
