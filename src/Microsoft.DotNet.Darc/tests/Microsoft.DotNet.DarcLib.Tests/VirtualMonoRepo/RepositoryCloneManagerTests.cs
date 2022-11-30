// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

public class RepositoryCloneManagerTests
{
    private const string RepoUri = "https://github.com/dotnet/test-repo";
    private const string Ref = "e7f4f5f758f08b1c5abb1e51ea735ca20e7f83a4";

    private readonly Mock<IVmrInfo> _vmrInfo = new();
    private readonly Mock<ILocalGitRepo> _localGitRepo = new();
    private readonly Mock<IProcessManager> _processManager = new();
    private readonly Mock<IFileSystem> _fileSystem = new();
    private readonly Mock<IGitRepoClonerFactory> _remoteFactory = new();
    private readonly Mock<IGitRepoCloner> _remote = new();
    private RepositoryCloneManager _manager = null!;

    private readonly LocalPath _tmpDir;
    private readonly LocalPath _clonePath;

    public RepositoryCloneManagerTests()
    {
        _tmpDir = new UnixPath("/data/tmp");
        _clonePath = _tmpDir / "62B2F7243B6B94DA";
    }

    [SetUp]
    public void SetUp()
    {
        _vmrInfo.Reset();
        _vmrInfo
            .SetupGet(x => x.TmpPath)
            .Returns(_tmpDir);

        _localGitRepo.Reset();

        _processManager.Reset();
        _processManager.SetReturnsDefault(Task.FromResult(new ProcessExecutionResult
        {
            ExitCode = 0,
        }));

        _fileSystem.Reset();
        _fileSystem
            .Setup(x => x.PathCombine(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string first, string second) => (first + "/" + second).Replace("//", null));

        _remote.Reset();
        _remoteFactory.Reset();
        _remoteFactory.SetReturnsDefault(_remote.Object);

        _manager = new RepositoryCloneManager(
            _vmrInfo.Object,
            _localGitRepo.Object,
            _remoteFactory.Object,
            _processManager.Object,
            _fileSystem.Object,
            new NullLogger<VmrPatchHandler>());
    }

    [Test]
    public async Task RepoIsClonedOnceTest()
    {
        var path = await _manager.PrepareClone(RepoUri, Ref, default);
        path.Should().Be(_clonePath);
        path = await _manager.PrepareClone(RepoUri, "main", default);
        path.Should().Be(_clonePath);
        path = await _manager.PrepareClone(RepoUri, "main", default);
        path.Should().Be(_clonePath);

        _remoteFactory.Verify(x => x.GetCloner(RepoUri, It.IsAny<ILogger>()), Times.Once);
        _remote.Verify(x => x.Clone(RepoUri, Ref, _clonePath, false, null), Times.Once);
        _localGitRepo.Verify(x => x.Checkout(_clonePath, "main", false), Times.Exactly(2));
    }

    [Test]
    public async Task CloneIsReusedTest()
    {
        _fileSystem
            .Setup(x => x.DirectoryExists(_clonePath))
            .Returns(true);

        var path = await _manager.PrepareClone(RepoUri, Ref, default);
        path.Should().Be(_clonePath);
        path = await _manager.PrepareClone(RepoUri, "main", default);
        path.Should().Be(_clonePath);

        _remoteFactory.Verify(x => x.GetCloner(RepoUri, It.IsAny<ILogger>()), Times.Never);
        _remote.Verify(x => x.Clone(RepoUri, Ref, _clonePath, false, null), Times.Never);
        _processManager.Verify(x => x.ExecuteGit(_clonePath, "fetch", "--all"), Times.Once);
        _localGitRepo.Verify(x => x.Checkout(_clonePath, Ref, false), Times.Once);
        _localGitRepo.Verify(x => x.Checkout(_clonePath, "main", false), Times.Once);
    }
}
