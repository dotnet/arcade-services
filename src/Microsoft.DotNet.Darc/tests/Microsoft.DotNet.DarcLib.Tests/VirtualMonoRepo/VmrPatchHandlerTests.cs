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
    private const string ClonePath = "/tmp/" + IndividualRepoName;
    private const string Sha1 = "e7f4f5f758f08b1c5abb1e51ea735ca20e7f83a4";
    private const string Sha2 = "605fdaa751bd5b76f9846801cebf5814e700f9ef";
    private const string SubmoduleSha1 = "839e1e3b415fc2747dde68f47d940faa414020ec";
    private const string SubmoduleSha2 = "bd5b76f98468017131aabe68f47d758f08b1c5ab";
    private const string PatchDir = "/tmp/patch";

    private readonly GitSubmoduleInfo _submoduleInfo = new(
        "external-1",
        "submodules/external-1",
        "https://github.com/dotnet/external-1",
        SubmoduleSha1);

    private readonly IReadOnlyCollection<string> _vmrPatches = new[]
    {
        "test-repo-patch1.patch",
        "test-repo-patch2.patch",
        "submodule.patch",
    };

    private readonly Mock<IVmrInfo> _vmrInfo = new();
    private readonly Mock<IVmrDependencyTracker> _dependencyTracker = new();
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
        Exclude: new[]
        { 
            "*.dll", 
            "*.exe", 
            "src/**/tests/**/*.*", 
            "submodules/external-1/LICENSE.md",
        });
    
    [SetUp]
    public void SetUp()
    {
        _vmrInfo.Reset();
        _vmrInfo
            .SetupGet(x => x.VmrPath)
            .Returns(VmrPath);
        _vmrInfo
            .Setup(x => x.PatchesPath)
            .Returns(VmrPath + "/patches");
        _vmrInfo
            .Setup(x => x.GetRepoSourcesPath(It.IsAny<SourceMapping>()))
            .Returns((SourceMapping mapping) => VmrPath + "/src/" + mapping.Name);

        _dependencyTracker.Reset();

        _localGitRepo.Reset();
        _localGitRepo.SetReturnsDefault<List<GitSubmoduleInfo>>(new());

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
        _fileSystem
            .Setup(x => x.GetFiles($"{VmrPath}/patches/{IndividualRepoName}"))
            .Returns(_vmrPatches.ToArray());
        _fileSystem
            .Setup(x => x.DirectoryExists($"{VmrPath}/patches/{IndividualRepoName}"))
            .Returns(true);

        _patchHandler = new VmrPatchHandler(
            _vmrInfo.Object,
            _dependencyTracker.Object,
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
        var patch = new VmrIngestionPatch($"{PatchDir}/test-repo.patch", IndividualRepoName);
        _fileSystem.SetReturnsDefault(Mock.Of<IFileInfo>(x => x.Exists == true && x.Length == 1243));

        // Act
        await _patchHandler.ApplyPatch(_testRepoMapping, patch, new CancellationToken());

        // Verify
        VerifyGitCall(new[]
        {
            "apply",
            "--cached",
            "--ignore-space-change",
            "--directory",
            $"src/{IndividualRepoName}",
            patch.Path,
        });
        
        VerifyGitCall(new[]
        {
            "checkout",
            $"src/{IndividualRepoName}",
        });
    }

    [Test]
    public async Task VmrPatchesAreAppliedTest()
    {
        // Setup
        _fileSystem.SetReturnsDefault(Mock.Of<IFileInfo>(x => x.Exists == true && x.Length == 1243));

        // Act
        await _patchHandler.ApplyVmrPatches(_testRepoMapping, new CancellationToken());

        foreach (var patch in _vmrPatches)
        {
            // Verify
            VerifyGitCall(new[]
            {
                "apply",
                "--cached",
                "--ignore-space-change",
                "--directory",
                $"src/{IndividualRepoName}",
                patch,
            });
        }

        VerifyGitCall(new[]
        {
            "checkout",
            $"src/{IndividualRepoName}",
        },
        times: Times.Exactly(_vmrPatches.Count));
    }

    [Test]
    public async Task PatchedFilesAreRestoredTest()
    {
        // Setup
        var patch = new VmrIngestionPatch($"{PatchDir}/test-repo.patch", string.Empty);

        const string originalRevision = "e7f4f5f758f08b1c5abb1e51ea735ca20e7f83a4";

        var patchedFiles = new[]
        {
            "src/roslyn-analyzers/eng/Versions.props",
            "src/foo/bar.xml",
            "src/xyz.cs",
        };

        _fileSystem
            .Setup(x => x.FileExists($"{ClonePath}/{patchedFiles[0]}"))
            .Returns(true);

        _fileSystem
            .Setup(x => x.FileExists($"{ClonePath}/{patchedFiles[1]}"))
            .Returns(true);

        _fileSystem
            .Setup(x => x.FileExists($"{ClonePath}/{patchedFiles[2]}"))
            .Returns(false);

        SetupGitCall(
            new[] { "apply", "--numstat", _vmrPatches.First() },
            new ProcessExecutionResult()
            {
                ExitCode = 0,
                StandardOutput = $"""
                    0       14      {patchedFiles[0]}
                    """,
            },
            ClonePath);

        SetupGitCall(
            new[] { "apply", "--numstat", _vmrPatches.Last() },
            new ProcessExecutionResult()
            {
                ExitCode = 0,
                StandardOutput = $"""
                    0       8       {patchedFiles[1]}
                    -       -       {patchedFiles[2]}
                    """,
            },
            ClonePath);

        // Act
        await _patchHandler.RestorePatchedFilesFromRepo(
            _testRepoMapping,
            ClonePath,
            originalRevision,
            CancellationToken.None);

        // Verify
        _localGitRepo.Verify(x => x.Checkout(ClonePath, originalRevision, false), Times.Once);
        _localGitRepo.Verify(x => x.Stage(VmrPath, VmrPath + "/src/" + IndividualRepoName), Times.Once);

        _fileSystem
            .Verify(x => x.CopyFile(
                ClonePath + '/' + patchedFiles[0],
                VmrPath + "/src/test-repo/" + patchedFiles[0],
                true),
              Times.Once);

        _fileSystem
            .Verify(x => x.CopyFile(
                ClonePath + '/' + patchedFiles[1],
                VmrPath + "/src/test-repo/" + patchedFiles[1],
                true),
              Times.Once);

        _fileSystem
            .Verify(x => x.DeleteFile(VmrPath + "/src/test-repo/" + patchedFiles[2]), Times.Once);
    }
    
    [Test]
    public async Task CreatePatchesWithNoSubmodulesTest()
    {
        // Setup
        string expectedPatchName = $"{PatchDir}/{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            ClonePath,
            Sha1,
            Sha2,
            PatchDir,
            "/tmp",
            CancellationToken.None);

        var expectedArgs = GetExpectedGitDiffArguments(expectedPatchName, Sha1, Sha2, null);

        // Verify
        _processManager
            .Verify(x => x.ExecuteGit(
                ClonePath,
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);
        
        patches.Should().ContainSingle();
        patches.Single().Should().Be(new VmrIngestionPatch(expectedPatchName, IndividualRepoName));
    }

    [Test]
    public async Task CreatePatchesWithSubmoduleWithoutChangesTest()
    {
        // Setup
        string expectedPatchName = $"{PatchDir}/{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";

        // Return the same info for both
        _localGitRepo
            .Setup(x => x.GetGitSubmodules(ClonePath, Sha1))
            .Returns(new List<GitSubmoduleInfo> { _submoduleInfo });

        _localGitRepo
            .Setup(x => x.GetGitSubmodules(ClonePath, Sha2))
            .Returns(new List<GitSubmoduleInfo> { _submoduleInfo });

        var remote = new Mock<IRemote>();

        _remoteFactory
            .Setup(x => x.GetRemoteAsync(_submoduleInfo.Url, It.IsAny<ILogger>()))
            .ReturnsAsync(remote.Object);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            ClonePath,
            Sha1,
            Sha2,
            PatchDir,
            "/tmp",
            CancellationToken.None);

        var expectedArgs = GetExpectedGitDiffArguments(expectedPatchName, Sha1, Sha2, new[] { _submoduleInfo.Path });

        // Verify
        _processManager
            .Verify(x => x.ExecuteGit(
                ClonePath,
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);

        remote.Verify(
            x => x.Clone(_submoduleInfo.Url, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()),
            Times.Never);

        patches.Should().ContainSingle();
        patches.Single().Should().Be(new VmrIngestionPatch(expectedPatchName, IndividualRepoName));
    }

    [Test]
    public async Task CreatePatchesWithSubmoduleAddedTest()
    {
        // Setup
        string expectedPatchName = $"{PatchDir}/{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";
        string expectedSubmodulePatchName = $"{PatchDir}/{_submoduleInfo.Name}-{Commit.GetShortSha(Constants.EmptyGitObject)}-{Commit.GetShortSha(SubmoduleSha1)}.patch";

        // Return no submodule for first SHA, one for second
        _localGitRepo
            .Setup(x => x.GetGitSubmodules(ClonePath, Sha1))
            .Returns(new List<GitSubmoduleInfo>());

        _localGitRepo
            .Setup(x => x.GetGitSubmodules(ClonePath, Sha2))
            .Returns(new List<GitSubmoduleInfo> { _submoduleInfo });

        var remote = new Mock<IRemote>();

        _remoteFactory
            .Setup(x => x.GetRemoteAsync(_submoduleInfo.Url, It.IsAny<ILogger>()))
            .ReturnsAsync(remote.Object);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            ClonePath,
            Sha1,
            Sha2,
            PatchDir,
            "/tmp",
            CancellationToken.None);

        // Verify diff for the individual repo
        var expectedArgs = GetExpectedGitDiffArguments(
            expectedPatchName, Sha1, Sha2, new[] { _submoduleInfo.Path });

        _processManager
            .Verify(x => x.ExecuteGit(
                ClonePath,
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);

        // Verify diff for the submodule
        expectedArgs = GetExpectedGitDiffArguments(
            expectedSubmodulePatchName, Constants.EmptyGitObject, SubmoduleSha1, null)
            .Take(7)
            .Append(":(glob,attr:!vmr-ignore)**/*")
            .Append(":(exclude,glob,attr:!vmr-preserve)LICENSE.md");

        _processManager
            .Verify(x => x.ExecuteGit(
                "/tmp/D8FC6934CE892A82EE79D572E24A7512",
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);

        remote.Verify(
            x => x.Clone(_submoduleInfo.Url, SubmoduleSha1, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()),
            Times.Once);

        patches.Should().BeEquivalentTo(new List<VmrIngestionPatch>
        {
            new VmrIngestionPatch(expectedPatchName, IndividualRepoName),
            new VmrIngestionPatch(expectedSubmodulePatchName, IndividualRepoName + '/' + _submoduleInfo.Path),
        });
    }

    [Test]
    public async Task CreatePatchesWithSubmoduleAndNestedSubmoduleAddedTest()
    {
        // Setup
        string nestedSubmoduleSha1 = "839e1e3b415fc2747dde68f47d940faa414020eb";

        GitSubmoduleInfo nestedSubmoduleInfo = new(
            "external-2",
            "external-2",
            "https://github.com/dotnet/external-2",
            nestedSubmoduleSha1);

        string expectedPatchName = $"{PatchDir}/{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";
        string expectedSubmodulePatchName = $"{PatchDir}/{_submoduleInfo.Name}-{Commit.GetShortSha(Constants.EmptyGitObject)}-{Commit.GetShortSha(SubmoduleSha1)}.patch";
        string expectedNestedSubmodulePatchName = $"{PatchDir}/{nestedSubmoduleInfo.Name}-{Commit.GetShortSha(Constants.EmptyGitObject)}-{Commit.GetShortSha(nestedSubmoduleSha1)}.patch";
        
        // Return no submodule for first SHA, one for second
        _localGitRepo
            .Setup(x => x.GetGitSubmodules(ClonePath, Sha1))
            .Returns(new List<GitSubmoduleInfo>());

        _localGitRepo
            .Setup(x => x.GetGitSubmodules(ClonePath, Sha2))
            .Returns(new List<GitSubmoduleInfo> { _submoduleInfo });

        var submoduleClonePath = "/tmp/D8FC6934CE892A82EE79D572E24A7512";

        _localGitRepo
            .Setup(x => x.GetGitSubmodules(submoduleClonePath, SubmoduleSha1))
            .Returns(new List<GitSubmoduleInfo> { nestedSubmoduleInfo });

        var remote = new Mock<IRemote>();
        var nestedSubmoduleRemote = new Mock<IRemote>();

        _remoteFactory
            .Setup(x => x.GetRemoteAsync(_submoduleInfo.Url, It.IsAny<ILogger>()))
            .ReturnsAsync(remote.Object);

        _remoteFactory
            .Setup(x => x.GetRemoteAsync(nestedSubmoduleInfo.Url, It.IsAny<ILogger>()))
            .ReturnsAsync(nestedSubmoduleRemote.Object);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            ClonePath,
            Sha1,
            Sha2,
            PatchDir,
            "/tmp",
            CancellationToken.None);

        // Verify diff for the individual repo
        var expectedArgs = GetExpectedGitDiffArguments(
            expectedPatchName, Sha1, Sha2, new[] { _submoduleInfo.Path });

        _processManager
            .Verify(x => x.ExecuteGit(
                ClonePath,
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);

        // Verify diff for the submodule
        expectedArgs = GetExpectedGitDiffArguments(
            expectedSubmodulePatchName, Constants.EmptyGitObject, SubmoduleSha1, new[] { nestedSubmoduleInfo.Path })
            .Take(7)
            .Append(":(glob,attr:!vmr-ignore)**/*")
            .Append(":(exclude,glob,attr:!vmr-preserve)LICENSE.md")
            .Append(":(exclude)external-2");

        _processManager
            .Verify(x => x.ExecuteGit(
                "/tmp/D8FC6934CE892A82EE79D572E24A7512",
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);

        remote.Verify(
            x => x.Clone(_submoduleInfo.Url, SubmoduleSha1, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()),
            Times.Once);

        // Verify diff for the nested submodule
        expectedArgs = GetExpectedGitDiffArguments(
            expectedNestedSubmodulePatchName, Constants.EmptyGitObject, nestedSubmoduleSha1, null)
            .Take(7)
            .Append(":(glob,attr:!vmr-ignore)**/*");

        _processManager
            .Verify(x => x.ExecuteGit(
                "/tmp/44B69A1449124AD460A08091672021B6",
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);

        nestedSubmoduleRemote.Verify(
            x => x.Clone(nestedSubmoduleInfo.Url, nestedSubmoduleSha1, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()),
            Times.Once);

        patches.Should().BeEquivalentTo(new List<VmrIngestionPatch>
        {
            new VmrIngestionPatch(expectedPatchName, IndividualRepoName),
            new VmrIngestionPatch(expectedSubmodulePatchName, IndividualRepoName + "/" + _submoduleInfo.Path),
            new VmrIngestionPatch(expectedNestedSubmodulePatchName, IndividualRepoName + "/" + _submoduleInfo.Path + "/" + nestedSubmoduleInfo.Path),
        });
    }

    [Test]
    public async Task CreatePatchesWithSubmoduleRemovedTest()
    {
        // Setup
        string expectedPatchName = $"{PatchDir}/{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";
        string expectedSubmodulePatchName = $"{PatchDir}/{_submoduleInfo.Name}-{Commit.GetShortSha(SubmoduleSha1)}-{Commit.GetShortSha(Constants.EmptyGitObject)}.patch";

        // Return no submodule for first SHA, one for second
        _localGitRepo
            .Setup(x => x.GetGitSubmodules(ClonePath, Sha1))
            .Returns(new List<GitSubmoduleInfo> { _submoduleInfo });

        _localGitRepo
            .Setup(x => x.GetGitSubmodules(ClonePath, Sha2))
            .Returns(new List<GitSubmoduleInfo>());

        var remote = new Mock<IRemote>();

        _remoteFactory
            .Setup(x => x.GetRemoteAsync(_submoduleInfo.Url, It.IsAny<ILogger>()))
            .ReturnsAsync(remote.Object);

        // Pretend the submodule was already cloned - no clone should happen then
        _fileSystem
            .Setup(x => x.DirectoryExists("/tmp/D8FC6934CE892A82EE79D572E24A7512"))
            .Returns(true);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            ClonePath,
            Sha1,
            Sha2,
            PatchDir,
            "/tmp",
            CancellationToken.None);

        // Verify diff for the individual repo
        var expectedArgs = GetExpectedGitDiffArguments(
            expectedPatchName, Sha1, Sha2, new[] { _submoduleInfo.Path });

        _processManager
            .Verify(x => x.ExecuteGit(
                ClonePath,
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);

        // Verify diff for the submodule
        expectedArgs = GetExpectedGitDiffArguments(
            expectedSubmodulePatchName, SubmoduleSha1, Constants.EmptyGitObject, null)
            .Take(7)
            .Append(":(glob,attr:!vmr-ignore)**/*")
            .Append(":(exclude,glob,attr:!vmr-preserve)LICENSE.md");

        _processManager
            .Verify(x => x.ExecuteGit(
                "/tmp/D8FC6934CE892A82EE79D572E24A7512",
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);

        remote.Verify(
            x => x.Clone(_submoduleInfo.Url, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()),
            Times.Never);

        _processManager
            .Verify(x => x.ExecuteGit(
                "/tmp/D8FC6934CE892A82EE79D572E24A7512",
                new[] { "fetch", "--all" }),
                Times.AtLeastOnce);

        patches.Should().BeEquivalentTo(new List<VmrIngestionPatch>
        {
            new VmrIngestionPatch(expectedPatchName, IndividualRepoName),
            new VmrIngestionPatch(expectedSubmodulePatchName, IndividualRepoName + "/" + _submoduleInfo.Path),
        });
    }

    [Test]
    public async Task CreatePatchesWithSubmoduleCommitChangedTest()
    {
        // Setup
        string expectedPatchName = $"{PatchDir}/{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";
        string expectedSubmodulePatchName = $"{PatchDir}/{_submoduleInfo.Name}-{Commit.GetShortSha(SubmoduleSha1)}-{Commit.GetShortSha(SubmoduleSha2)}.patch";

        _localGitRepo
            .Setup(x => x.GetGitSubmodules(ClonePath, Sha1))
            .Returns(new List<GitSubmoduleInfo> { _submoduleInfo });

        _localGitRepo
            .Setup(x => x.GetGitSubmodules(ClonePath, Sha2))
            .Returns(new List<GitSubmoduleInfo> { _submoduleInfo with { Commit = SubmoduleSha2 } });

        var remote = new Mock<IRemote>();

        _remoteFactory
            .Setup(x => x.GetRemoteAsync(_submoduleInfo.Url, It.IsAny<ILogger>()))
            .ReturnsAsync(remote.Object);

        // Pretend the submodule was already cloned - no clone should happen then
        _fileSystem
            .Setup(x => x.DirectoryExists("/tmp/D8FC6934CE892A82EE79D572E24A7512"))
            .Returns(true);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            ClonePath,
            Sha1,
            Sha2,
            PatchDir,
            "/tmp",
            CancellationToken.None);

        // Verify diff for the individual repo
        var expectedArgs = GetExpectedGitDiffArguments(
            expectedPatchName, Sha1, Sha2, new[] { _submoduleInfo.Path });

        _processManager
            .Verify(x => x.ExecuteGit(
                ClonePath,
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);

        // Verify diff for the submodule
        expectedArgs = GetExpectedGitDiffArguments(
            expectedSubmodulePatchName, SubmoduleSha1, SubmoduleSha2, null)
            .Take(7)
            .Append(":(glob,attr:!vmr-ignore)**/*")
            .Append(":(exclude,glob,attr:!vmr-preserve)LICENSE.md");

        _processManager
            .Verify(x => x.ExecuteGit(
                "/tmp/D8FC6934CE892A82EE79D572E24A7512",
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);

        remote.Verify(
            x => x.Clone(_submoduleInfo.Url, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()),
            Times.Never);
        
        _processManager
            .Verify(x => x.ExecuteGit(
                "/tmp/D8FC6934CE892A82EE79D572E24A7512",
                new[] { "fetch", "--all" }),
                Times.AtLeastOnce);

        patches.Should().BeEquivalentTo(new List<VmrIngestionPatch>
        {
            new VmrIngestionPatch(expectedPatchName, IndividualRepoName),
            new VmrIngestionPatch(expectedSubmodulePatchName, IndividualRepoName + "/" + _submoduleInfo.Path),
        });
    }

    [Test]
    public async Task CreatePatchesWithSubmoduleUrlChangedTest()
    {
        // Setup
        string expectedPatchName = $"{PatchDir}/{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";
        string expectedSubmodulePatchName1 = $"{PatchDir}/{_submoduleInfo.Name}-{Commit.GetShortSha(SubmoduleSha1)}-{Commit.GetShortSha(Constants.EmptyGitObject)}.patch";
        string expectedSubmodulePatchName2 = $"{PatchDir}/{_submoduleInfo.Name}-{Commit.GetShortSha(Constants.EmptyGitObject)}-{Commit.GetShortSha(SubmoduleSha2)}.patch";

        _localGitRepo
            .Setup(x => x.GetGitSubmodules(ClonePath, Sha1))
            .Returns(new List<GitSubmoduleInfo> { _submoduleInfo });

        _localGitRepo
            .Setup(x => x.GetGitSubmodules(ClonePath, Sha2))
            .Returns(new List<GitSubmoduleInfo> { _submoduleInfo with { Commit = SubmoduleSha2, Url = "https://github.com/dotnet/external-2" } });

        var remote1 = new Mock<IRemote>();
        var remote2 = new Mock<IRemote>();

        _remoteFactory
            .Setup(x => x.GetRemoteAsync(_submoduleInfo.Url, It.IsAny<ILogger>()))
            .ReturnsAsync(remote1.Object);

        _remoteFactory
            .Setup(x => x.GetRemoteAsync("https://github.com/dotnet/external-2", It.IsAny<ILogger>()))
            .ReturnsAsync(remote2.Object);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            ClonePath,
            Sha1,
            Sha2,
            PatchDir,
            "/tmp",
            CancellationToken.None);

        // Verify diff for the individual repo
        var expectedArgs = GetExpectedGitDiffArguments(
            expectedPatchName, Sha1, Sha2, new[] { _submoduleInfo.Path });

        _processManager
            .Verify(x => x.ExecuteGit(
                ClonePath,
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);

        // Verify diff for the submodule
        expectedArgs = GetExpectedGitDiffArguments(
            expectedSubmodulePatchName1, SubmoduleSha1, Constants.EmptyGitObject, null)
            .Take(7)
            .Append(":(glob,attr:!vmr-ignore)**/*")
            .Append(":(exclude,glob,attr:!vmr-preserve)LICENSE.md");

        _processManager
            .Verify(x => x.ExecuteGit(
                "/tmp/D8FC6934CE892A82EE79D572E24A7512",
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);

        expectedArgs = GetExpectedGitDiffArguments(
            expectedSubmodulePatchName2, Constants.EmptyGitObject, SubmoduleSha2, null)
            .Take(7)
            .Append(":(glob,attr:!vmr-ignore)**/*")
            .Append(":(exclude,glob,attr:!vmr-preserve)LICENSE.md");

        _processManager
            .Verify(x => x.ExecuteGit(
                "/tmp/44B69A1449124AD460A08091672021B6",
                expectedArgs,
                It.IsAny<CancellationToken>()),
                Times.Once);

        remote1.Verify(
            x => x.Clone(_submoduleInfo.Url, SubmoduleSha1, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()),
            Times.Once);

        remote2.Verify(
            x => x.Clone("https://github.com/dotnet/external-2", SubmoduleSha2, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()),
            Times.Once);

        patches.Should().BeEquivalentTo(new List<VmrIngestionPatch>
        {
            new VmrIngestionPatch(expectedPatchName, IndividualRepoName),
            new VmrIngestionPatch(expectedSubmodulePatchName1, IndividualRepoName + "/" + _submoduleInfo.Path),
            new VmrIngestionPatch(expectedSubmodulePatchName2, IndividualRepoName + "/" + _submoduleInfo.Path),
        });
    }

    [Test]
    public async Task PatchIsAppliedOnRepoWithTrailingSlashTest()
    {
        // Setup
        _vmrInfo.Reset();
        _vmrInfo
            .SetupGet(x => x.VmrPath)
            .Returns(VmrPath + "/");
        _vmrInfo
            .Setup(x => x.GetRepoSourcesPath(It.IsAny<SourceMapping>()))
            .Returns((SourceMapping mapping) => VmrPath + "/src/" + mapping.Name);

        _patchHandler = new VmrPatchHandler(
            _vmrInfo.Object,
            _dependencyTracker.Object,
            _localGitRepo.Object,
            _remoteFactory.Object,
            _processManager.Object,
            _fileSystem.Object,
            new NullLogger<VmrPatchHandler>());

        var patch = new VmrIngestionPatch($"{PatchDir}/test-repo.patch", IndividualRepoName);
        _fileSystem.SetReturnsDefault(Mock.Of<IFileInfo>(x => x.Exists == true && x.Length == 1243));

        // Act
        await _patchHandler.ApplyPatch(_testRepoMapping, patch, new CancellationToken());

        // Verify
        VerifyGitCall(new[]
        {
            "apply",
            "--cached",
            "--ignore-space-change",
            "--directory",
            $"src/{IndividualRepoName}",
            patch.Path,
        },
        VmrPath + "/");

        VerifyGitCall(new[]
        {
            "checkout",
            $"src/{IndividualRepoName}",
        },
        VmrPath + "/");
    }

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

    private static IEnumerable<string> GetExpectedGitDiffArguments(
        string patchPath,
        string Sha1,
        string Sha2,
        IEnumerable<string>? submodules)
    {
        var args = new List<string>()
        {
            "diff",
            "--patch",
            "--binary",
            "--output",
            patchPath,
            $"{Sha1}..{Sha2}",
            "--",
            ":(glob,attr:!vmr-ignore)*.*",
            ":(glob,attr:!vmr-ignore)src/*",
            ":(exclude,glob,attr:!vmr-preserve)*.dll",
            ":(exclude,glob,attr:!vmr-preserve)*.exe",
            ":(exclude,glob,attr:!vmr-preserve)src/**/tests/**/*.*",
            ":(exclude,glob,attr:!vmr-preserve)submodules/external-1/LICENSE.md",
        };

        if (submodules != null)
        {
            args.AddRange(submodules.Select(s => $":(exclude){s}"));
        }
        
        return args;
    }
}
