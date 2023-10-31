// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
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
    private readonly Mock<ILocalGitClient> _localGitRepo = new();
    private readonly Mock<IFileSystem> _fileSystem = new();
    private readonly Mock<IGitRepoCloner> _repoCloner = new();
    private RepositoryCloneManager _manager = null!;

    private readonly NativePath _tmpDir;
    private readonly LocalPath _clonePath;

    public RepositoryCloneManagerTests()
    {
        _tmpDir = new NativePath("/data/tmp");
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

        _repoCloner.Reset();

        _manager = new RepositoryCloneManager(
            _vmrInfo.Object,
            _repoCloner.Object,
            _localGitRepo.Object,
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

        _repoCloner.Verify(x => x.CloneNoCheckoutAsync(RepoUri, _clonePath, null), Times.Once);
        _localGitRepo.Verify(x => x.CheckoutAsync(_clonePath, Ref), Times.Once);
        _localGitRepo.Verify(x => x.CheckoutAsync(_clonePath, "main"), Times.Exactly(2));
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

        _repoCloner.Verify(x => x.CloneNoCheckoutAsync(RepoUri, _clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.CheckoutAsync(_clonePath, Ref), Times.Once);
        _localGitRepo.Verify(x => x.CheckoutAsync(_clonePath, "main"), Times.Once);
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
            .Setup(x => x.AddRemoteIfMissingAsync(clonePath, mapping.DefaultRemote, It.IsAny<CancellationToken>()))
            .ReturnsAsync("default");

        _localGitRepo
            .Setup(x => x.AddRemoteIfMissingAsync(clonePath, newRemote, It.IsAny<CancellationToken>()))
            .ReturnsAsync("new");

        void ResetCalls()
        {
            _repoCloner.ResetCalls();
            _localGitRepo.ResetCalls();
        }

        // Clone for the first time
        var path = await _manager.PrepareClone(mapping, new[] { mapping.DefaultRemote }, "main", default);
        path.Should().Be(clonePath);

        _repoCloner.Verify(x => x.CloneNoCheckoutAsync(mapping.DefaultRemote, clonePath, null), Times.Once);
        _localGitRepo.Verify(x => x.CheckoutAsync(clonePath, "main"), Times.Once);

        // A second clone of the same
        ResetCalls();
        path = await _manager.PrepareClone(mapping, new[] { mapping.DefaultRemote }, Ref, default);
        
        path.Should().Be(clonePath);
        _repoCloner.Verify(x => x.CloneNoCheckoutAsync(mapping.DefaultRemote, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.UpdateRemoteAsync(clonePath, "default", default), Times.Never);
        _localGitRepo.Verify(x => x.CheckoutAsync(clonePath, Ref), Times.Once);

        // A third clone with a new remote
        ResetCalls();
        path = await _manager.PrepareClone(mapping, new[] { mapping.DefaultRemote, newRemote }, Ref, default);
        
        path.Should().Be(clonePath);
        _repoCloner.Verify(x => x.CloneNoCheckoutAsync(RepoUri, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.AddRemoteIfMissingAsync(clonePath, newRemote, It.IsAny<CancellationToken>()), Times.Once);
        _localGitRepo.Verify(x => x.CheckoutAsync(clonePath, Ref), Times.Once);
        _localGitRepo.Verify(x => x.UpdateRemoteAsync(clonePath, "new", default), Times.Once);

        // Same again, should be cached
        ResetCalls();
        path = await _manager.PrepareClone(mapping, new[] { mapping.DefaultRemote, newRemote }, Ref + "3", default);
        
        path.Should().Be(clonePath);
        _repoCloner.Verify(x => x.CloneNoCheckoutAsync(RepoUri, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.AddRemoteIfMissingAsync(clonePath, newRemote, It.IsAny<CancellationToken>()), Times.Never);
        _localGitRepo.Verify(x => x.CheckoutAsync(clonePath, Ref + "3"), Times.Once);
        _localGitRepo.Verify(x => x.UpdateRemoteAsync(clonePath, "new", default), Times.Never);

        // Call with URI directly
        ResetCalls();
        path = await _manager.PrepareClone(RepoUri, Ref + "4", default);

        path.Should().Be(clonePath);
        _repoCloner.Verify(x => x.CloneNoCheckoutAsync(RepoUri, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.AddRemoteIfMissingAsync(clonePath, RepoUri, It.IsAny<CancellationToken>()), Times.Never);
        _localGitRepo.Verify(x => x.CheckoutAsync(clonePath, Ref + "4"), Times.Once);
        _localGitRepo.Verify(x => x.UpdateRemoteAsync(clonePath, "new", default), Times.Never);

        // Call with the second URI directly
        ResetCalls();
        path = await _manager.PrepareClone(newRemote, Ref + "5", default);

        path.Should().Be(clonePath);
        _repoCloner.Verify(x => x.CloneNoCheckoutAsync(RepoUri, clonePath, null), Times.Never);
        _localGitRepo.Verify(x => x.AddRemoteIfMissingAsync(clonePath, newRemote, It.IsAny<CancellationToken>()), Times.Never);
        _localGitRepo.Verify(x => x.CheckoutAsync(clonePath, Ref + "5"), Times.Once);
        _localGitRepo.Verify(x => x.UpdateRemoteAsync(clonePath, "new", default), Times.Never);
    }
}
