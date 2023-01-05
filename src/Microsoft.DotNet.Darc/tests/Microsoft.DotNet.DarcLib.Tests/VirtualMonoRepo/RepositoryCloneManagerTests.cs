// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using FluentAssertions;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
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
            _fileSystem.Object,
            new NullLogger<VmrPatchHandler>());
    }

    [Test]
    public void RepoIsClonedOnceTest()
    {
        var path = _manager.PrepareClone(RepoUri, Ref, default);
        path.Should().Be(_clonePath);
        path = _manager.PrepareClone(RepoUri, "main", default);
        path.Should().Be(_clonePath);
        path = _manager.PrepareClone(RepoUri, "main", default);
        path.Should().Be(_clonePath);

        _remoteFactory.Verify(x => x.GetCloner(RepoUri, It.IsAny<ILogger>()), Times.Once);
        _remote.Verify(x => x.Clone(RepoUri, _clonePath, null), Times.Once);
        _localGitRepo.Verify(x => x.Checkout(_clonePath, Ref, false), Times.Once);
        _localGitRepo.Verify(x => x.Checkout(_clonePath, "main", false), Times.Exactly(2));
    }

    [Test]
    public void CloneIsReusedTest()
    {
        _fileSystem
            .Setup(x => x.DirectoryExists(_clonePath))
            .Returns(true);

        var path = _manager.PrepareClone(RepoUri, Ref, default);
        path.Should().Be(_clonePath);
        path = _manager.PrepareClone(RepoUri, "main", default);
        path.Should().Be(_clonePath);

        _remoteFactory.Verify(x => x.GetCloner(RepoUri, It.IsAny<ILogger>()), Times.Never);
        _remote.Verify(x => x.Clone(RepoUri, _clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.Checkout(_clonePath, Ref, false), Times.Once);
        _localGitRepo.Verify(x => x.Checkout(_clonePath, "main", false), Times.Once);
    }

    [Test]
    public void MultipleRemotesAreConfiguredTest()
    {
        var mapping = new SourceMapping(
            "test-repo",
            RepoUri,
            "main",
            Array.Empty<string>(),
            Array.Empty<string>());

        var clonePath = _tmpDir / mapping.Name;

        _fileSystem
            .SetupSequence(x => x.DirectoryExists(clonePath))
            .Returns(false)
            .Returns(true)
            .Returns(true)
            .Returns(true)
            .Returns(true);

        void ResetCalls()
        {
            _remoteFactory.ResetCalls();
            _localGitRepo.ResetCalls();
            _remote.ResetCalls();
        }

        // Clone for the first time
        var path = _manager.PrepareClone(mapping, new[] { mapping.DefaultRemote }, "main", default);
        path.Should().Be(clonePath);

        _remoteFactory.Verify(x => x.GetCloner(mapping.DefaultRemote, It.IsAny<ILogger>()), Times.Once);
        _remote.Verify(x => x.Clone(mapping.DefaultRemote, clonePath, null), Times.Once);
        _localGitRepo.Verify(x => x.Checkout(clonePath, "main", false), Times.Once);

        // A second clone of the same
        ResetCalls();
        path = _manager.PrepareClone(mapping, new[] { mapping.DefaultRemote }, Ref, default);
        
        path.Should().Be(clonePath);
        _remote.Verify(x => x.Clone(mapping.DefaultRemote, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.Checkout(clonePath, Ref, false), Times.Once);

        // A third clone with a new remote
        ResetCalls();
        var newRemote = "https://dev.azure.com/dnceng/test-repo";
        path = _manager.PrepareClone(mapping, new[] { mapping.DefaultRemote, newRemote }, Ref, default);
        
        path.Should().Be(clonePath);
        _remoteFactory.Verify(x => x.GetCloner(newRemote, It.IsAny<ILogger>()), Times.Never);
        _remote.Verify(x => x.Clone(RepoUri, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.AddRemoteIfMissing(clonePath, newRemote, true), Times.Once);
        _localGitRepo.Verify(x => x.Checkout(clonePath, Ref, false), Times.Once);

        // Same again, should be cached
        ResetCalls();
        path = _manager.PrepareClone(mapping, new[] { mapping.DefaultRemote, newRemote }, Ref + "3", default);
        
        path.Should().Be(clonePath);
        _remoteFactory.Verify(x => x.GetCloner(newRemote, It.IsAny<ILogger>()), Times.Never);
        _remote.Verify(x => x.Clone(RepoUri, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.AddRemoteIfMissing(clonePath, newRemote, true), Times.Never);
        _localGitRepo.Verify(x => x.Checkout(clonePath, Ref + "3", false), Times.Once);

        // Call with URI directly
        ResetCalls();
        path = _manager.PrepareClone(RepoUri, Ref + "4", default);

        path.Should().Be(clonePath);
        _remoteFactory.Verify(x => x.GetCloner(RepoUri, It.IsAny<ILogger>()), Times.Never);
        _remote.Verify(x => x.Clone(RepoUri, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.AddRemoteIfMissing(clonePath, RepoUri, true), Times.Never);
        _localGitRepo.Verify(x => x.Checkout(clonePath, Ref + "4", false), Times.Once);

        // Call with the second URI directly
        ResetCalls();
        path = _manager.PrepareClone(newRemote, Ref + "5", default);

        path.Should().Be(clonePath);
        _remoteFactory.Verify(x => x.GetCloner(newRemote, It.IsAny<ILogger>()), Times.Never);
        _remote.Verify(x => x.Clone(RepoUri, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.AddRemoteIfMissing(clonePath, newRemote, true), Times.Never);
        _localGitRepo.Verify(x => x.Checkout(clonePath, Ref + "5", false), Times.Once);
    }
}
