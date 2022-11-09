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
    private const string TmpDir = "/data/tmp";
    private const string RepoUri = "https://github.com/dotnet/test-repo";
    private const string Ref = "e7f4f5f758f08b1c5abb1e51ea735ca20e7f83a4";
    private const string ClonePath = $"{TmpDir}/62B2F7243B6B94DA";

    private readonly Mock<IVmrInfo> _vmrInfo = new();
    private readonly Mock<ILocalGitRepo> _localGitRepo = new();
    private readonly Mock<IProcessManager> _processManager = new();
    private readonly Mock<IFileSystem> _fileSystem = new();
    private readonly Mock<IRemoteFactory> _remoteFactory = new();
    private readonly Mock<IRemote> _remote = new();

    private RepositoryCloneManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _vmrInfo.Reset();
        _vmrInfo
            .SetupGet(x => x.TmpPath)
            .Returns(TmpDir);

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
        _remoteFactory.SetReturnsDefault(Task.FromResult(_remote.Object));

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
        path.Should().Be(ClonePath);
        path = await _manager.PrepareClone(RepoUri, "main", default);
        path.Should().Be(ClonePath);
        path = await _manager.PrepareClone(RepoUri, "main", default);
        path.Should().Be(ClonePath);

        _remoteFactory.Verify(x => x.GetRemoteAsync(RepoUri, It.IsAny<ILogger>()), Times.Once);
        _remote.Verify(x => x.Clone(RepoUri, Ref, ClonePath, false, null), Times.Once);
        _localGitRepo.Verify(x => x.Checkout(ClonePath, "main", false), Times.Exactly(2));
    }

    [Test]
    public async Task CloneIsReusedTest()
    {
        _fileSystem
            .Setup(x => x.DirectoryExists(ClonePath))
            .Returns(true);
        
        var path = await _manager.PrepareClone(RepoUri, Ref, default);
        path.Should().Be(ClonePath);
        path = await _manager.PrepareClone(RepoUri, "main", default);
        path.Should().Be(ClonePath);

        _remoteFactory.Verify(x => x.GetRemoteAsync(RepoUri, It.IsAny<ILogger>()), Times.Never);
        _remote.Verify(x => x.Clone(RepoUri, Ref, ClonePath, false, null), Times.Never);
        _processManager.Verify(x => x.ExecuteGit(ClonePath, "fetch", "--all"), Times.Once);
        _localGitRepo.Verify(x => x.Checkout(ClonePath, Ref, false), Times.Once);
        _localGitRepo.Verify(x => x.Checkout(ClonePath, "main", false), Times.Once);
    }
}
