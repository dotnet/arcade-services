// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class CherryPickOperationTests
{
    private Mock<IProcessManager> _processManager;
    private Mock<IFileSystem> _fileSystem;
    private Mock<IVmrPatchHandler> _patchHandler;
    private Mock<IVersionDetailsParser> _versionDetailsParser;
    private Mock<ILocalGitRepoFactory> _localGitRepoFactory;
    private Mock<IVmrInfo> _vmrInfo;
    private Mock<ILogger<CherryPickOperation>> _logger;
    private CherryPickCommandLineOptions _options;

    [SetUp]
    public void Setup()
    {
        _processManager = new Mock<IProcessManager>();
        _fileSystem = new Mock<IFileSystem>();
        _patchHandler = new Mock<IVmrPatchHandler>();
        _versionDetailsParser = new Mock<IVersionDetailsParser>();
        _localGitRepoFactory = new Mock<ILocalGitRepoFactory>();
        _vmrInfo = new Mock<IVmrInfo>();
        _logger = new Mock<ILogger<CherryPickOperation>>();

        _options = new CherryPickCommandLineOptions
        {
            SourceRepo = "/test/repo",
            Commit = "abcd1234"
        };
    }

    [Test]
    public void Constructor_ShouldInitializeAllDependencies()
    {
        // Act
        var operation = new CherryPickOperation(
            _options,
            _processManager.Object,
            _fileSystem.Object,
            _patchHandler.Object,
            _versionDetailsParser.Object,
            _localGitRepoFactory.Object,
            _vmrInfo.Object,
            _logger.Object);

        // Assert
        operation.Should().NotBeNull();
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnErrorCode_WhenVersionDetailsXmlNotFound()
    {
        // Arrange
        var gitRoot = "/test/current";
        
        _processManager.Setup(x => x.FindGitRoot(It.IsAny<string>()))
            .Returns(gitRoot);
        
        _fileSystem.Setup(x => x.FileExists(It.IsAny<string>()))
            .Returns((string path) => path.Contains("source-manifest.json") ? false : false);

        var operation = new CherryPickOperation(
            _options,
            _processManager.Object,
            _fileSystem.Object,
            _patchHandler.Object,
            _versionDetailsParser.Object,
            _localGitRepoFactory.Object,
            _vmrInfo.Object,
            _logger.Object);

        // Act
        var result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnErrorCode_WhenExceptionOccurs()
    {
        // Arrange
        _processManager.Setup(x => x.FindGitRoot(It.IsAny<string>()))
            .Throws(new Exception("Test exception"));

        var operation = new CherryPickOperation(
            _options,
            _processManager.Object,
            _fileSystem.Object,
            _patchHandler.Object,
            _versionDetailsParser.Object,
            _localGitRepoFactory.Object,
            _vmrInfo.Object,
            _logger.Object);

        // Act
        var result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);
    }

    [Test]
    public void CherryPickCommandLineOptions_ShouldHaveRequiredProperties()
    {
        // Arrange & Act
        var options = new CherryPickCommandLineOptions
        {
            SourceRepo = "test-repo",
            Commit = "test-commit"
        };

        // Assert
        options.SourceRepo.Should().Be("test-repo");
        options.Commit.Should().Be("test-commit");
    }
}