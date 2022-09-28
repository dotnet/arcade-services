// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

public class VmrPatchHandlerTests
{
    private const string VmrPath = "/data/vmr";

    private readonly Mock<IVmrDependencyTracker> _vmrInfo = new();
    private readonly Mock<ILocalGitRepo> _localGitRepo = new();
    private readonly Mock<IRemoteFactory> _remoteFactory = new();
    private readonly Mock<IProcessManager> _processManager = new();
    private readonly Mock<IFileSystem> _fileSystem = new();
    private VmrPatchHandler _patchHandler = null!;

    private readonly SourceMapping _testRepoMapping = new(
        Name: "test-repo",
        DefaultRemote: "https://github.com/dotnet/test-repo",
        DefaultRef: "main",
        Include: new[] { "*.*", "src/*" },
        Exclude: new[] { "*.dll", "*.exe", "src/**/tests/**/*.*" },
        VmrPatches: new[] { "patches/test-repo-patch1.patch", "patches/test-repo-patch2.patch" });

    [SetUp]
    public void SetUp()
    {
        _vmrInfo.Reset();
        _vmrInfo
            .SetupGet(x => x.VmrPath)
            .Returns(VmrPath);
        _vmrInfo
            .Setup(x => x.GetRepoSourcesPath(It.IsAny<SourceMapping>()))
            .Returns((SourceMapping mapping) => VmrPath + "/src/" + mapping.Name);

        _localGitRepo.Reset();

        _remoteFactory.Reset();

        _processManager.Reset();
        _processManager
            .SetupGet(x => x.GitExecutable)
            .Returns("git");
        _processManager.SetReturnsDefault(Task.FromResult(new ProcessExecutionResult
        {
            ExitCode = 0,
        }));

        _fileSystem.Reset();

        _patchHandler = new VmrPatchHandler(
            _vmrInfo.Object,
            _localGitRepo.Object,
            _remoteFactory.Object,
            _processManager.Object,
            _fileSystem.Object,
            new NullLogger<VmrPatchHandler>());
    }

    [Test]
    public async Task ApplyPatchTest()
    {
        // Setup
        var patch = new VmrIngestionPatch("/tmp/patches/test-repo.patch", string.Empty);

        // Act
        await _patchHandler.ApplyPatch(_testRepoMapping, patch, new CancellationToken());

        // Verify
        var expectedArgs = new[]
        {
            "apply",
            "--cached",
            "--ignore-space-change",
            "--directory",
            "src/test-repo/",
            patch.Path,
        };

        _processManager
            .Verify(x => x.ExecuteGit(VmrPath, expectedArgs, CancellationToken.None), Times.Once);

        expectedArgs = new[]
        {
            "checkout",
            "src/test-repo/"
        };

        _processManager
            .Verify(x => x.ExecuteGit(VmrPath, expectedArgs, CancellationToken.None), Times.Once);
    }
}
