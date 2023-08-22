// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
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
        _processManager.SetReturnsDefault(Task.FromResult(new ProcessExecutionResult() { ExitCode = 0 }));

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

        _remoteFactory.Verify(x => x.GetCloner(RepoUri, It.IsAny<GitClonerType>()), Times.Once);
        _remote.Verify(x => x.CloneAsync(RepoUri, _clonePath, null), Times.Once);
        _localGitRepo.Verify(x => x.CheckoutNativeAsync(_clonePath, Ref), Times.Once);
        _localGitRepo.Verify(x => x.CheckoutNativeAsync(_clonePath, "main"), Times.Exactly(2));
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

        _remoteFactory.Verify(x => x.GetCloner(RepoUri, It.IsAny<GitClonerType>()), Times.Never);
        _remote.Verify(x => x.CloneAsync(RepoUri, _clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.CheckoutNativeAsync(_clonePath, Ref), Times.Once);
        _localGitRepo.Verify(x => x.CheckoutNativeAsync(_clonePath, "main"), Times.Once);
    }

    [Test]
    public async Task MultipleRemotesAreConfiguredTest()
    {
        var mapping = new SourceMapping(
            "test-repo",
            RepoUri,
            "main",
            Array.Empty<string>(),
            Array.Empty<string>());

        var newRemote = "https://dev.azure.com/dnceng/test-repo";

        var clonePath = _tmpDir / mapping.Name;

        _fileSystem
            .SetupSequence(x => x.DirectoryExists(clonePath))
            .Returns(false)
            .Returns(true)
            .Returns(true)
            .Returns(true)
            .Returns(true);

        _localGitRepo
            .Setup(x => x.AddRemoteIfMissing(clonePath, mapping.DefaultRemote, true))
            .Returns("default");

        _localGitRepo
            .Setup(x => x.AddRemoteIfMissing(clonePath, newRemote, true))
            .Returns("new");

        void ResetCalls()
        {
            _processManager.ResetCalls();
            _remoteFactory.ResetCalls();
            _localGitRepo.ResetCalls();
            _remote.ResetCalls();
        }

        // Clone for the first time
        var path = await _manager.PrepareClone(mapping, new[] { mapping.DefaultRemote }, "main", default);
        path.Should().Be(clonePath);

        _remoteFactory.Verify(x => x.GetCloner(mapping.DefaultRemote, It.IsAny<GitClonerType>()), Times.Once);
        _remote.Verify(x => x.CloneAsync(mapping.DefaultRemote, clonePath, null), Times.Once);
        _localGitRepo.Verify(x => x.CheckoutNativeAsync(clonePath, "main"), Times.Once);

        // A second clone of the same
        ResetCalls();
        path = await _manager.PrepareClone(mapping, new[] { mapping.DefaultRemote }, Ref, default);
        
        path.Should().Be(clonePath);
        _remote.Verify(x => x.CloneAsync(mapping.DefaultRemote, clonePath, null), Times.Never);
        _processManager.Verify(x => x.ExecuteGit(clonePath, new[] { "fetch", mapping.DefaultRemote }, default), Times.Never);
        _localGitRepo.Verify(x => x.CheckoutNativeAsync(clonePath, Ref), Times.Once);

        // A third clone with a new remote
        ResetCalls();
        path = await _manager.PrepareClone(mapping, new[] { mapping.DefaultRemote, newRemote }, Ref, default);
        
        path.Should().Be(clonePath);
        _remoteFactory.Verify(x => x.GetCloner(newRemote, It.IsAny<GitClonerType>()), Times.Never);
        _remote.Verify(x => x.CloneAsync(RepoUri, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.AddRemoteIfMissing(clonePath, newRemote, true), Times.Once);
        _localGitRepo.Verify(x => x.CheckoutNativeAsync(clonePath, Ref), Times.Once);
        _processManager.Verify(x => x.ExecuteGit(clonePath, new[] { "fetch", newRemote }, default), Times.Once);

        // Same again, should be cached
        ResetCalls();
        path = await _manager.PrepareClone(mapping, new[] { mapping.DefaultRemote, newRemote }, Ref + "3", default);
        
        path.Should().Be(clonePath);
        _remoteFactory.Verify(x => x.GetCloner(newRemote, It.IsAny<GitClonerType>()), Times.Never);
        _remote.Verify(x => x.CloneAsync(RepoUri, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.AddRemoteIfMissing(clonePath, newRemote, true), Times.Never);
        _localGitRepo.Verify(x => x.CheckoutNativeAsync(clonePath, Ref + "3"), Times.Once);
        _processManager.Verify(x => x.ExecuteGit(clonePath, new[] { "fetch", mapping.DefaultRemote }, default), Times.Never);

        // Call with URI directly
        ResetCalls();
        path = await _manager.PrepareClone(RepoUri, Ref + "4", default);

        path.Should().Be(clonePath);
        _remoteFactory.Verify(x => x.GetCloner(RepoUri, It.IsAny<GitClonerType>()), Times.Never);
        _remote.Verify(x => x.CloneAsync(RepoUri, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.AddRemoteIfMissing(clonePath, RepoUri, true), Times.Never);
        _localGitRepo.Verify(x => x.CheckoutNativeAsync(clonePath, Ref + "4"), Times.Once);
        _processManager.Verify(x => x.ExecuteGit(clonePath, new[] { "fetch", mapping.DefaultRemote }, default), Times.Never);

        // Call with the second URI directly
        ResetCalls();
        path = await _manager.PrepareClone(newRemote, Ref + "5", default);

        path.Should().Be(clonePath);
        _remoteFactory.Verify(x => x.GetCloner(newRemote, It.IsAny<GitClonerType>()), Times.Never);
        _remote.Verify(x => x.CloneAsync(RepoUri, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.AddRemoteIfMissing(clonePath, newRemote, true), Times.Never);
        _localGitRepo.Verify(x => x.CheckoutNativeAsync(clonePath, Ref + "5"), Times.Once);
        _processManager.Verify(x => x.ExecuteGit(clonePath, new[] { "fetch", mapping.DefaultRemote }, default), Times.Never);
    }
}
