// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

public class VmrPatchHandlerTests
{
    private const string VmrPath = "/data/vmr";
    private const string IndividualRepoName = "test-repo";

    private readonly Mock<IVmrDependencyTracker> _vmrInfo = new();
    private readonly Mock<ILocalGitRepo> _localGitRepo = new();
    private readonly Mock<IRemoteFactory> _remoteFactory = new();
    private readonly Mock<IProcessManager> _processManager = new();
    private readonly Mock<IFileSystem> _fileSystem = new();
    private VmrPatchHandler _patchHandler = null!;

    private readonly SourceMapping _testRepoMapping = new(
        Name: IndividualRepoName,
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
        _fileSystem
            .SetupGet(x => x.DirectorySeparatorChar)
            .Returns('/');
        _fileSystem
            .Setup(x => x.PathCombine(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string first, string second) => (first + "/" + second).Replace("//", null));

        _patchHandler = new VmrPatchHandler(
            _vmrInfo.Object,
            _localGitRepo.Object,
            _remoteFactory.Object,
            _processManager.Object,
            _fileSystem.Object,
            new NullLogger<VmrPatchHandler>());
    }

    [Test]
    public async Task PatchIsAppliedTest()
    {
        // Setup
        var patch = new VmrIngestionPatch("/tmp/patches/test-repo.patch", string.Empty);

        // Act
        await _patchHandler.ApplyPatch(_testRepoMapping, patch, new CancellationToken());

        // Verify
        VerifyGitCall(new[]
        {
            "apply",
            "--cached",
            "--ignore-space-change",
            "--directory",
            $"src/{IndividualRepoName}/",
            patch.Path,
        });
        
        VerifyGitCall(new[]
        {
            "checkout",
            $"src/{IndividualRepoName}/",
        });
    }

    [Test]
    public async Task VmrPatchesAreAppliedTest()
    {
        // Act
        await _patchHandler.ApplyVmrPatches(_testRepoMapping, new CancellationToken());

        foreach (var patch in _testRepoMapping.VmrPatches)
        {
            // Verify
            VerifyGitCall(new[]
            {
                "apply",
                "--cached",
                "--ignore-space-change",
                "--directory",
                $"src/{IndividualRepoName}/",
                patch,
            });
        }

        VerifyGitCall(new[]
        {
            "checkout",
            $"src/{IndividualRepoName}/",
        },
        times: Times.Exactly(_testRepoMapping.VmrPatches.Count));
    }

    [Test]
    public async Task PatchedFilesAreRestoredTest()
    {
        // Setup
        var patch = new VmrIngestionPatch("/tmp/patches/test-repo.patch", string.Empty);

        const string clonePath = "/tmp/test-repo";
        const string originalRevision = "e7f4f5f758f08b1c5abb1e51ea735ca20e7f83a4";

        var patchedFiles = new[]
        {
            "src/roslyn-analyzers/eng/Versions.props",
            "src/foo/bar.xml",
            "src/xyz.cs",
        };

        SetupGitCall(
            new[] { "apply", "--numstat", _testRepoMapping.VmrPatches.First() },
            new ProcessExecutionResult()
            {
                ExitCode = 0,
                StandardOutput = $"""
                    0       14      {patchedFiles[0]}
                    """,
            },
            clonePath);

        SetupGitCall(
            new[] { "apply", "--numstat", _testRepoMapping.VmrPatches.Last() },
            new ProcessExecutionResult()
            {
                ExitCode = 0,
                StandardOutput = $"""
                    0       8       {patchedFiles[1]}
                    -       -       {patchedFiles[2]}
                    """,
            },
            clonePath);

        // Act
        await _patchHandler.RestorePatchedFilesFromRepo(
            _testRepoMapping,
            clonePath,
            originalRevision,
            CancellationToken.None);

        // Verify
        _localGitRepo.Verify(x => x.Checkout(clonePath, originalRevision, false), Times.Once);
        _localGitRepo.Verify(x => x.Stage(VmrPath, VmrPath + "/src/" + IndividualRepoName), Times.Once);

        foreach (var file in patchedFiles)
        {
            _fileSystem.Verify(x => x.CopyFile(
                clonePath + '/' + file,
                VmrPath + "/src/test-repo/" + file,
                true));
        }
    }
    
    [Test]
    public async Task CreatePatchesWithNoSubmodulesTest()
    {
        // Setup
        const string clonePath = "/tmp/test-repo";
        const string sha1 = "e7f4f5f758f08b1c5abb1e51ea735ca20e7f83a4";
        const string sha2 = "605fdaa751bd5b76f9846801cebf5814e700f9ef";
        string expectedPatchName = $"/tmp/patches/{IndividualRepoName}-{Commit.GetShortSha(sha1)}-{Commit.GetShortSha(sha2)}.patch";

        _localGitRepo.SetReturnsDefault<List<GitSubmoduleInfo>>(new());

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            clonePath,
            sha1,
            sha2,
            "/tmp/patches",
            "/tmp",
            CancellationToken.None);

        // Verify
        _processManager
            .Verify(x => x.ExecuteGit(
                clonePath,
                new List<string>()
                {
                    "diff",
                    "--patch",
                    "--binary",
                    "--output",
                    expectedPatchName,
                    $"{sha1}..{sha2}",
                    "--",
                    ":(glob,attr:!vmr-ignore)*.*",
                    ":(glob,attr:!vmr-ignore)src/*",
                    ":(exclude,glob,attr:!vmr-preserve)*.dll",
                    ":(exclude,glob,attr:!vmr-preserve)*.exe",
                    ":(exclude,glob,attr:!vmr-preserve)src/**/tests/**/*.*",
                },
                It.IsAny<CancellationToken>()),
                Times.Once);
        
        patches.Should().ContainSingle();
        patches.Single().Should().Be(new VmrIngestionPatch(expectedPatchName, string.Empty));
    }

    [Test]
    public async Task CreatePatchesWithSubmoduleWithoutChangesTest()
    {
        // Setup
        const string clonePath = "/tmp/test-repo";
        const string sha1 = "e7f4f5f758f08b1c5abb1e51ea735ca20e7f83a4";
        const string sha2 = "605fdaa751bd5b76f9846801cebf5814e700f9ef";
        const string submoduleSha = "839e1e3b415fc2747dde68f47d940faa414020ec";
        string expectedPatchName = $"/tmp/patches/{IndividualRepoName}-{Commit.GetShortSha(sha1)}-{Commit.GetShortSha(sha2)}.patch";

        var submoduleInfo = new GitSubmoduleInfo(
            "external-1",
            "submodules/external-1",
            "https://github.com/dotnet/external-1",
            submoduleSha);

        // Return the same info for both
        _localGitRepo
            .Setup(x => x.GetGitSubmodules(clonePath, sha1))
            .Returns(new List<GitSubmoduleInfo> { submoduleInfo });

        _localGitRepo
            .Setup(x => x.GetGitSubmodules(clonePath, sha2))
            .Returns(new List<GitSubmoduleInfo> { submoduleInfo });

        var remote = new Mock<IRemote>();

        _remoteFactory
            .Setup(x => x.GetRemoteAsync("https://github.com/dotnet/external-1", It.IsAny<ILogger>()))
            .ReturnsAsync(remote.Object);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            clonePath,
            sha1,
            sha2,
            "/tmp/patches",
            "/tmp",
            CancellationToken.None);

        // Verify
        _processManager
            .Verify(x => x.ExecuteGit(
                clonePath,
                new List<string>()
                {
                    "diff",
                    "--patch",
                    "--binary",
                    "--output",
                    expectedPatchName,
                    $"{sha1}..{sha2}",
                    "--",
                    ":(glob,attr:!vmr-ignore)*.*",
                    ":(glob,attr:!vmr-ignore)src/*",
                    ":(exclude,glob,attr:!vmr-preserve)*.dll",
                    ":(exclude,glob,attr:!vmr-preserve)*.exe",
                    ":(exclude,glob,attr:!vmr-preserve)src/**/tests/**/*.*",
                    ":(exclude)submodules/external-1",
                },
                It.IsAny<CancellationToken>()),
                Times.Once);

        remote.Verify(
            x => x.Clone("https://github.com/dotnet/external-1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()),
            Times.Never);

        patches.Should().ContainSingle();
        patches.Single().Should().Be(new VmrIngestionPatch(expectedPatchName, string.Empty));
    }
        //remote
        //    .Verify(x => x.Clone("https://github.com/dotnet/external-1", submoduleSha, "/tmp/external-1", false, null), Times.Never);


    private void SetupGitCall(string[] expectedArguments, ProcessExecutionResult result, string repoDir = VmrPath)
    {
        _processManager
            .Setup(x => x.ExecuteGit(repoDir, expectedArguments, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private void VerifyGitCall(string[] expectedArguments, string repoDir = VmrPath, Times? times = null)
    {
        _processManager
            .Verify(x => x.ExecuteGit(repoDir, expectedArguments, It.IsAny<CancellationToken>()), times ?? Times.Once());
    }
}
