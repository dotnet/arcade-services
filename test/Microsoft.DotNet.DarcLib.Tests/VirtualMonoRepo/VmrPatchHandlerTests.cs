// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

public class VmrPatchHandlerTests
{
    private const string IndividualRepoName = "test-repo";
    private const string Sha1 = "e7f4f5f758f08b1c5abb1e51ea735ca20e7f83a4";
    private const string Sha2 = "605fdaa751bd5b76f9846801cebf5814e700f9ef";
    private const string SubmoduleSha1 = "839e1e3b415fc2747dde68f47d940faa414020ec";
    private const string SubmoduleSha2 = "bd5b76f98468017131aabe68f47d758f08b1c5ab";

    private static readonly UnixPath SRC = new("src");
    private static readonly UnixPath RepoVmrPath = SRC / IndividualRepoName;
    private static readonly NativePath TmpDir = new("/tmp");

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
    private readonly Mock<ILocalGitClient> _localGitRepo = new();
    private readonly Mock<IRepositoryCloneManager> _cloneManager = new();
    private readonly Mock<IProcessManager> _processManager = new();
    private readonly Mock<IFileSystem> _fileSystem = new();
    private VmrPatchHandler _patchHandler = null!;

    private readonly NativePath _vmrPath;
    private readonly NativePath _patchDir;
    private readonly LocalGitRepo _clone;

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
        },
        DisableSynchronization: false);

    public VmrPatchHandlerTests()
    {
        _vmrPath = new NativePath("/data/vmr");
        _patchDir = TmpDir / "patch";
        _clone = new LocalGitRepo(TmpDir / IndividualRepoName, _localGitRepo.Object, _processManager.Object);
    }
    
    [SetUp]
    public void SetUp()
    {
        _vmrInfo.Reset();
        _vmrInfo
            .SetupGet(x => x.VmrPath)
            .Returns(_vmrPath);
        _vmrInfo
            .Setup(x => x.GetRepoSourcesPath(It.IsAny<SourceMapping>()))
            .Returns((SourceMapping mapping) => _vmrPath / VmrInfo.SourcesDir / mapping.Name);

        _dependencyTracker.Reset();

        _localGitRepo.Reset();
        _localGitRepo.SetReturnsDefault(Task.FromResult(new List<GitSubmoduleInfo>()));

        _cloneManager.Reset();
        _cloneManager
            .Setup(x => x.PrepareCloneAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string uri, string _, bool _, CancellationToken _) => new LocalGitRepo(TmpDir / uri.Split("/").Last(), _localGitRepo.Object, _processManager.Object));

        _processManager.Reset();
        _processManager
            .SetupGet(x => x.GitExecutable)
            .Returns("git");
        _processManager.SetReturnsDefault(Task.FromResult(new ProcessExecutionResult
        {
            ExitCode = 0,
        }));

        _fileSystem.Reset();
        _fileSystem.SetReturnsDefault(Mock.Of<IFileInfo>(x => x.Exists && x.Length == 895));
        _fileSystem
            .SetupGet(x => x.DirectorySeparatorChar)
            .Returns('/');
        _fileSystem
            .Setup(x => x.PathCombine(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string first, string second) => (first + "/" + second).Replace("//", null));
        _fileSystem
            .Setup(x => x.GetFiles($"{_vmrPath}/patches/{IndividualRepoName}"))
            .Returns([.. _vmrPatches]);
        _fileSystem
            .Setup(x => x.DirectoryExists($"{_vmrPath}/patches/{IndividualRepoName}"))
            .Returns(true);

        _patchHandler = new VmrPatchHandler(
            _vmrInfo.Object,
            _dependencyTracker.Object,
            _localGitRepo.Object,
            _cloneManager.Object,
            _processManager.Object,
            _fileSystem.Object,
            new NullLogger<VmrPatchHandler>());
    }

    [Test]
    public async Task ApplyPatchesTest()
    {
        // Setup
        var patch = new VmrIngestionPatch(_patchDir / "test-repo.patch", RepoVmrPath);
        _fileSystem.SetReturnsDefault(Mock.Of<IFileInfo>(x => x.Exists == true && x.Length == 1243));

        // Act
        await _patchHandler.ApplyPatch(patch, _vmrInfo.Object.VmrPath, true, false, new CancellationToken());

        // Verify
        VerifyGitCall(new List<string>
        {
            "apply",
            "--cached",
            "--ignore-space-change",
            "--directory",
            RepoVmrPath,
            patch.Path,
        });

        _localGitRepo.Verify(x =>
            x.ResetWorkingTree(
                It.Is<NativePath>(p => p == _vmrInfo.Object.VmrPath),
                It.Is<UnixPath?>(p => p == patch.ApplicationPath)),
            Times.AtLeastOnce);

        _fileSystem.Verify(x => x.DeleteFile(patch.Path), Times.Once);
    }

    [Test]
    public async Task CreatePatchesWithNoSubmodulesTest()
    {
        // Setup
        NativePath expectedPatchName = _patchDir / $"{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            _clone,
            Sha1,
            Sha2,
            _patchDir,
            TmpDir,
            CancellationToken.None);

        var expectedArgs = GetExpectedGitDiffArguments(expectedPatchName, Sha1, Sha2, null);

        // Verify
        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                _clone.Path,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);
        
        _dependencyTracker.Verify(x => x.UpdateSubmodules(It.Is<List<SubmoduleRecord>>(l => l.Count == 0)), Times.Once);

        patches.Should().ContainSingle();
        patches.Single().Should().Be(new VmrIngestionPatch(expectedPatchName, RepoVmrPath));
    }

    [Test]
    public async Task CreatePatchesWithSubmoduleWithoutChangesTest()
    {
        // Setup
        NativePath expectedPatchName = _patchDir / $"{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";

        // Return the same info for both
        _localGitRepo
            .Setup(x => x.GetGitSubmodulesAsync(_clone.Path, Sha1))
            .ReturnsAsync([_submoduleInfo]);

        _localGitRepo
            .Setup(x => x.GetGitSubmodulesAsync(_clone.Path, Sha2))
            .ReturnsAsync([_submoduleInfo]);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            _clone,
            Sha1,
            Sha2,
            _patchDir,
            TmpDir,
            CancellationToken.None);

        var expectedArgs = GetExpectedGitDiffArguments(expectedPatchName, Sha1, Sha2, new[] { _submoduleInfo.Path });

        // Verify
        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                _clone.Path,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        _cloneManager
            .Verify(x => x.PrepareCloneAsync(_submoduleInfo.Url, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

        patches.Should().ContainSingle();
        patches.Single().Should().Be(new VmrIngestionPatch(expectedPatchName, RepoVmrPath));

        _dependencyTracker.Verify(x => x.UpdateSubmodules(It.IsAny<List<SubmoduleRecord>>()), Times.Exactly(1));

        _dependencyTracker.Verify(
            x => x.UpdateSubmodules(
                It.Is<List<SubmoduleRecord>>(
                    l => l[0].CommitSha == _submoduleInfo.Commit 
                        && l[0].RemoteUri == _submoduleInfo.Url
                        && l[0].Path == IndividualRepoName + '/' + _submoduleInfo.Path)), Times.Once);
    }

    [Test]
    public async Task CreatePatchesWithSubmoduleAddedTest()
    {
        // Setup
        NativePath expectedPatchName = _patchDir / $"{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";
        NativePath expectedSubmodulePatchName = _patchDir / $"{_submoduleInfo.Name}-{Commit.GetShortSha(Constants.EmptyGitObject)}-{Commit.GetShortSha(SubmoduleSha1)}.patch";

        // Return no submodule for first SHA, one for second
        _localGitRepo
            .Setup(x => x.GetGitSubmodulesAsync(_clone.Path, Sha1))
            .ReturnsAsync([]);

        _localGitRepo
            .Setup(x => x.GetGitSubmodulesAsync(_clone.Path, Sha2))
            .ReturnsAsync([_submoduleInfo]);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            _clone,
            Sha1,
            Sha2,
            _patchDir,
            TmpDir,
            CancellationToken.None);

        // Verify diff for the individual repo
        var expectedArgs = GetExpectedGitDiffArguments(
            expectedPatchName, Sha1, Sha2, new[] { _submoduleInfo.Path });

        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                _clone.Path,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        // Verify diff for the submodule
        expectedArgs = GetExpectedGitDiffArguments(
            expectedSubmodulePatchName, Constants.EmptyGitObject, SubmoduleSha1, null)
            .Take(8)
            .Append(VmrPatchHandler.GetInclusionRule("**/*"))
            .Append(VmrPatchHandler.GetExclusionRule("LICENSE.md"));

        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                TmpDir / "external-1",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        _cloneManager
            .Verify(x => x.PrepareCloneAsync(_submoduleInfo.Url, SubmoduleSha1, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        _dependencyTracker.Verify(x => x.UpdateSubmodules(It.IsAny<List<SubmoduleRecord>>()), Times.Exactly(2));

        _dependencyTracker.Verify(
            x => x.UpdateSubmodules(
                It.Is<List<SubmoduleRecord>>(
                    l => l.Count == 1
                        && l[0].CommitSha == _submoduleInfo.Commit
                        && l[0].RemoteUri == _submoduleInfo.Url
                        && l[0].Path == IndividualRepoName + '/' + _submoduleInfo.Path)),
            Times.Once);

        _dependencyTracker.Verify(x => x.UpdateSubmodules(It.Is<List<SubmoduleRecord>>(l => l.Count == 0)), Times.Once);

        patches.Should().BeEquivalentTo(new List<VmrIngestionPatch>
        {
            new(expectedPatchName, RepoVmrPath),
            new(expectedSubmodulePatchName, RepoVmrPath / _submoduleInfo.Path),
        });
    }

    [Test]
    public async Task CreatePatchesWithSubmoduleAndNestedSubmoduleAddedTest()
    {
        // Setup
        var nestedSubmoduleSha1 = "839e1e3b415fc2747dde68f47d940faa414020eb";

        GitSubmoduleInfo nestedSubmoduleInfo = new(
            "external-2",
            "external-2",
            "https://github.com/dotnet/external-2",
            nestedSubmoduleSha1);

        NativePath expectedPatchName = _patchDir / $"{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";
        NativePath expectedSubmodulePatchName = _patchDir / $"{_submoduleInfo.Name}-{Commit.GetShortSha(Constants.EmptyGitObject)}-{Commit.GetShortSha(SubmoduleSha1)}.patch";
        NativePath expectedNestedSubmodulePatchName = _patchDir / $"{nestedSubmoduleInfo.Name}-{Commit.GetShortSha(Constants.EmptyGitObject)}-{Commit.GetShortSha(nestedSubmoduleSha1)}.patch";
        
        // Return no submodule for first SHA, one for second
        _localGitRepo
            .Setup(x => x.GetGitSubmodulesAsync(_clone.Path, Sha1))
            .ReturnsAsync([]);

        _localGitRepo
            .Setup(x => x.GetGitSubmodulesAsync(_clone.Path, Sha2))
            .ReturnsAsync([_submoduleInfo]);

        _localGitRepo
            .Setup(x => x.GetGitSubmodulesAsync(TmpDir / "external-1", SubmoduleSha1))
            .ReturnsAsync([nestedSubmoduleInfo]);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            _clone,
            Sha1,
            Sha2,
            _patchDir,
            TmpDir,
            CancellationToken.None);

        // Verify diff for the individual repo
        var expectedArgs = GetExpectedGitDiffArguments(
            expectedPatchName, Sha1, Sha2, new[] { _submoduleInfo.Path });

        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                _clone.Path,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        // Verify diff for the submodule
        expectedArgs = GetExpectedGitDiffArguments(
            expectedSubmodulePatchName, Constants.EmptyGitObject, SubmoduleSha1, new[] { nestedSubmoduleInfo.Path })
            .Take(8)
            .Append(VmrPatchHandler.GetInclusionRule("**/*"))
            .Append(VmrPatchHandler.GetExclusionRule("LICENSE.md"))
            .Append(":(exclude)external-2");

        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                TmpDir / "external-1",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        _cloneManager
            .Verify(x => x.PrepareCloneAsync(_submoduleInfo.Url, SubmoduleSha1, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // Verify diff for the nested submodule
        expectedArgs = GetExpectedGitDiffArguments(
            expectedNestedSubmodulePatchName, Constants.EmptyGitObject, nestedSubmoduleSha1, null)
            .Take(8)
            .Append(VmrPatchHandler.GetInclusionRule("**/*"));

        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                TmpDir / "external-2",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        _cloneManager
            .Verify(x => x.PrepareCloneAsync(nestedSubmoduleInfo.Url, nestedSubmoduleSha1, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);

        _dependencyTracker.Verify(x => x.UpdateSubmodules(It.IsAny<List<SubmoduleRecord>>()), Times.Exactly(3));

        _dependencyTracker.Verify(
            x => x.UpdateSubmodules(
                It.Is<List<SubmoduleRecord>>(
                    l => l.Count == 1
                        && l[0].CommitSha == nestedSubmoduleInfo.Commit
                        && l[0].RemoteUri == nestedSubmoduleInfo.Url
                        && l[0].Path == IndividualRepoName + "/" + _submoduleInfo.Path + "/" + nestedSubmoduleInfo.Path)),
            Times.Once);

        _dependencyTracker.Verify(
            x => x.UpdateSubmodules(
                It.Is<List<SubmoduleRecord>>(
                    l => l.Count == 1
                        && l[0].CommitSha == _submoduleInfo.Commit
                        && l[0].RemoteUri == _submoduleInfo.Url
                        && l[0].Path == IndividualRepoName + '/' + _submoduleInfo.Path)),
            Times.Once);

        _dependencyTracker.Verify(x => x.UpdateSubmodules(It.Is<List<SubmoduleRecord>>(l => l.Count == 0)), Times.Once);

        patches.Should().BeEquivalentTo(new List<VmrIngestionPatch>
        {
            new(expectedPatchName, RepoVmrPath),
            new(expectedSubmodulePatchName, RepoVmrPath / _submoduleInfo.Path),
            new(expectedNestedSubmodulePatchName, RepoVmrPath / _submoduleInfo.Path / nestedSubmoduleInfo.Path),
        });
    }

    [Test]
    public async Task CreatePatchesWithSubmoduleRemovedTest()
    {
        // Setup
        NativePath expectedPatchName = _patchDir / $"{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";
        NativePath expectedSubmodulePatchName = _patchDir / $"{_submoduleInfo.Name}-{Commit.GetShortSha(SubmoduleSha1)}-{Commit.GetShortSha(Constants.EmptyGitObject)}.patch";

        // Return no submodule for first SHA, one for second
        _localGitRepo
            .Setup(x => x.GetGitSubmodulesAsync(_clone.Path, Sha1))
            .ReturnsAsync([_submoduleInfo]);

        _localGitRepo
            .Setup(x => x.GetGitSubmodulesAsync(_clone.Path, Sha2))
            .ReturnsAsync([]);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            _clone,
            Sha1,
            Sha2,
            _patchDir,
            TmpDir,
            CancellationToken.None);

        // Verify diff for the individual repo
        var expectedArgs = GetExpectedGitDiffArguments(
            expectedPatchName, Sha1, Sha2, new[] { _submoduleInfo.Path });

        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                _clone.Path,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        // Verify diff for the submodule
        expectedArgs = GetExpectedGitDiffArguments(
            expectedSubmodulePatchName, SubmoduleSha1, Constants.EmptyGitObject, null)
            .Take(8)
            .Append(VmrPatchHandler.GetInclusionRule("**/*"))
            .Append(VmrPatchHandler.GetExclusionRule("LICENSE.md"));

        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                TmpDir / "external-1",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        _cloneManager
            .Verify(x => x.PrepareCloneAsync(_submoduleInfo.Url, SubmoduleSha1, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        _dependencyTracker.Verify(x => x.UpdateSubmodules(It.IsAny<List<SubmoduleRecord>>()), Times.Exactly(2));

        _dependencyTracker.Verify(
            x => x.UpdateSubmodules(
                It.Is<List<SubmoduleRecord>>(
                    l => l.Count == 1
                        && l[0].CommitSha == Constants.EmptyGitObject
                        && l[0].RemoteUri == _submoduleInfo.Url
                        && l[0].Path == IndividualRepoName + '/' + _submoduleInfo.Path)),
            Times.Once);

        _dependencyTracker.Verify(x => x.UpdateSubmodules(It.Is<List<SubmoduleRecord>>(l => l.Count == 0)), Times.Once);

        patches.Should().BeEquivalentTo(new List<VmrIngestionPatch>
        {
            new(expectedPatchName, RepoVmrPath),
            new(expectedSubmodulePatchName, RepoVmrPath / _submoduleInfo.Path),
        });
    }

    [Test]
    public async Task CreatePatchesWithSubmoduleCommitChangedTest()
    {
        // Setup
        NativePath expectedPatchName = _patchDir / $"{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";
        NativePath expectedSubmodulePatchName = _patchDir / $"{_submoduleInfo.Name}-{Commit.GetShortSha(SubmoduleSha1)}-{Commit.GetShortSha(SubmoduleSha2)}.patch";

        _localGitRepo
            .Setup(x => x.GetGitSubmodulesAsync(_clone.Path, Sha1))
            .ReturnsAsync([_submoduleInfo]);

        _localGitRepo
            .Setup(x => x.GetGitSubmodulesAsync(_clone.Path, Sha2))
            .ReturnsAsync([_submoduleInfo with { Commit = SubmoduleSha2 }]);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            _clone,
            Sha1,
            Sha2,
            _patchDir,
            TmpDir,
            CancellationToken.None);

        // Verify diff for the individual repo
        var expectedArgs = GetExpectedGitDiffArguments(
            expectedPatchName, Sha1, Sha2, new[] { _submoduleInfo.Path });

        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                _clone.Path,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        // Verify diff for the submodule
        expectedArgs = GetExpectedGitDiffArguments(
            expectedSubmodulePatchName, SubmoduleSha1, SubmoduleSha2, null)
            .Take(8)
            .Append(VmrPatchHandler.GetInclusionRule("**/*"))
            .Append(VmrPatchHandler.GetExclusionRule("LICENSE.md"));

        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                TmpDir / "external-1",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        _cloneManager
            .Verify(x => x.PrepareCloneAsync(_submoduleInfo.Url, SubmoduleSha1, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        _dependencyTracker.Verify(x => x.UpdateSubmodules(It.IsAny<List<SubmoduleRecord>>()), Times.Exactly(2));

        _dependencyTracker.Verify(
            x => x.UpdateSubmodules(
                It.Is<List<SubmoduleRecord>>(
                    l => l.Count == 1
                        && l[0].CommitSha == SubmoduleSha2
                        && l[0].RemoteUri == _submoduleInfo.Url
                        && l[0].Path == IndividualRepoName + '/' + _submoduleInfo.Path)),
            Times.Once);

        _dependencyTracker.Verify(x => x.UpdateSubmodules(It.Is<List<SubmoduleRecord>>(l => l.Count == 0)), Times.Once);

        patches.Should().BeEquivalentTo(new List<VmrIngestionPatch>
        {
            new(expectedPatchName, RepoVmrPath),
            new(expectedSubmodulePatchName, RepoVmrPath / _submoduleInfo.Path),
        });
    }

    [Test]
    public async Task CreatePatchesWithSubmoduleUrlChangedTest()
    {
        // Setup
        NativePath expectedPatchName = _patchDir / $"{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";
        NativePath expectedSubmodulePatchName1 = _patchDir / $"{_submoduleInfo.Name}-{Commit.GetShortSha(SubmoduleSha1)}-{Commit.GetShortSha(Constants.EmptyGitObject)}.patch";
        NativePath expectedSubmodulePatchName2 = _patchDir / $"{_submoduleInfo.Name}-{Commit.GetShortSha(Constants.EmptyGitObject)}-{Commit.GetShortSha(SubmoduleSha2)}.patch";

        _localGitRepo
            .Setup(x => x.GetGitSubmodulesAsync(_clone.Path, Sha1))
            .ReturnsAsync([_submoduleInfo]);

        _localGitRepo
            .Setup(x => x.GetGitSubmodulesAsync(_clone.Path, Sha2))
            .ReturnsAsync([_submoduleInfo with { Commit = SubmoduleSha2, Url = "https://github.com/dotnet/external-2" }]);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            _clone,
            Sha1,
            Sha2,
            _patchDir,
            TmpDir,
            CancellationToken.None);

        // Verify diff for the individual repo
        var expectedArgs = GetExpectedGitDiffArguments(
            expectedPatchName, Sha1, Sha2, new[] { _submoduleInfo.Path });

        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                _clone.Path,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        // Verify diff for the submodule
        expectedArgs = GetExpectedGitDiffArguments(
            expectedSubmodulePatchName1, SubmoduleSha1, Constants.EmptyGitObject, null)
            .Take(8)
            .Append(VmrPatchHandler.GetInclusionRule("**/*"))
            .Append(VmrPatchHandler.GetExclusionRule("LICENSE.md"));

        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                TmpDir / "external-1",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        expectedArgs = GetExpectedGitDiffArguments(
            expectedSubmodulePatchName2, Constants.EmptyGitObject, SubmoduleSha2, null)
            .Take(8)
            .Append(VmrPatchHandler.GetInclusionRule("**/*"))
            .Append(VmrPatchHandler.GetExclusionRule("LICENSE.md"));

        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                TmpDir / "external-2",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        _cloneManager
            .Verify(x => x.PrepareCloneAsync(_submoduleInfo.Url, SubmoduleSha1, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        _cloneManager
            .Verify(x => x.PrepareCloneAsync("https://github.com/dotnet/external-2", SubmoduleSha2, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        _dependencyTracker.Verify(x => x.UpdateSubmodules(It.IsAny<List<SubmoduleRecord>>()), Times.Exactly(3));

        _dependencyTracker.Verify(
            x => x.UpdateSubmodules(
                It.Is<List<SubmoduleRecord>>(
                    l => l.Count == 2 
                    && l.Any(
                        r => r.CommitSha == Constants.EmptyGitObject 
                        && r.RemoteUri == _submoduleInfo.Url 
                        && r.Path == IndividualRepoName + '/' + _submoduleInfo.Path)
                    && l.Any(
                        r => r.CommitSha == SubmoduleSha2 
                        && r.RemoteUri == "https://github.com/dotnet/external-2" 
                        && r.Path == IndividualRepoName + '/' + _submoduleInfo.Path))),
            Times.Once);

        _dependencyTracker.Verify(x => x.UpdateSubmodules(It.Is<List<SubmoduleRecord>>(l => l.Count == 0)), Times.Exactly(2));

        patches.Should().BeEquivalentTo(new List<VmrIngestionPatch>
        {
            new(expectedPatchName, RepoVmrPath),
            new(expectedSubmodulePatchName1, RepoVmrPath / _submoduleInfo.Path),
            new(expectedSubmodulePatchName2, RepoVmrPath / _submoduleInfo.Path),
        });
    }

    [Test]
    public async Task PatchIsAppliedOnRepoWithTrailingSlashTest()
    {
        // Setup
        _vmrInfo.Reset();
        _vmrInfo
            .SetupGet(x => x.VmrPath)
            .Returns(new NativePath("/data/vmr/"));
        _vmrInfo
            .Setup(x => x.GetRepoSourcesPath(It.IsAny<SourceMapping>()))
            .Returns((SourceMapping mapping) => _vmrPath / VmrInfo.SourcesDir / mapping.Name);

        _patchHandler = new VmrPatchHandler(
            _vmrInfo.Object,
            _dependencyTracker.Object,
            _localGitRepo.Object,
            _cloneManager.Object,
            _processManager.Object,
            _fileSystem.Object,
            new NullLogger<VmrPatchHandler>());

        var patch = new VmrIngestionPatch(_patchDir / $"test-repo.patch", RepoVmrPath);
        _fileSystem.SetReturnsDefault(Mock.Of<IFileInfo>(x => x.Exists == true && x.Length == 1243));

        // Act
        await _patchHandler.ApplyPatch(patch, _vmrInfo.Object.VmrPath, false, false, new CancellationToken());

        // Verify
        VerifyGitCall(new List<string>
        {
            "apply",
            "--cached",
            "--ignore-space-change",
            "--directory",
            RepoVmrPath,
            patch.Path,
        },
        _vmrPath / "/");

        _localGitRepo.Verify(x =>
            x.ResetWorkingTree(
                It.Is<NativePath>(p => p == _vmrInfo.Object.VmrPath),
                It.Is<UnixPath?>(p => p == patch.ApplicationPath)),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task CreatePatchesWithSplittingWhenOverSizeLimitTest()
    {
        /*
         * This test verifies that if a patch is larger than the maximum allowed size, it is split into multiple patches.
         * The fake repo layout is as follows:
         *  ├── large-dir-1       >1GB
         *  │   ├── large-dir-2   >1GB
         *  │   │   ├── a.txt
         *  │   │   └── b.txt
         *  │   └── small-dir
         *  │       └── c.txt
         *  └── root-file
         */
        NativePath expectedPatchName = _patchDir / $"{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";

        var largeDir1 = _clone.Path / "large-dir-1";
        var largeDir2 = largeDir1 / "large-dir-2";
        var smallDir = _clone.Path / "large-dir-1" / "small-dir";

        // Patch for the whole repo
        _fileSystem
            .Setup(x => x.GetFileInfo(expectedPatchName))
            .Returns(Mock.Of<IFileInfo>(x => x.Length == 1_500_000_000));

        // Patch for large-dir-1
        _fileSystem
            .Setup(x => x.GetFileInfo(expectedPatchName + ".1"))
            .Returns(Mock.Of<IFileInfo>(x => x.Length == 1_500_000_000));

        _fileSystem
            .Setup(x => x.GetDirectories(_clone.Path))
            .Returns([largeDir1]);

        _fileSystem
            .Setup(x => x.GetDirectories(largeDir1))
            .Returns([ largeDir2, smallDir
            ]);

        _fileSystem
            .Setup(x => x.GetFiles(_clone.Path))
            .Returns([_clone.Path / "root-file"]);

        _fileSystem
            .Setup(x => x.GetFiles(largeDir2))
            .Returns([largeDir2 / "a.txt", largeDir2 / "b.txt"]);

        _fileSystem
            .Setup(x => x.GetFiles(smallDir))
            .Returns([smallDir / "c.txt"]);

        // Act
        var patches = await _patchHandler.CreatePatches(
            _testRepoMapping,
            _clone,
            Sha1,
            Sha2,
            _patchDir,
            TmpDir,
            CancellationToken.None);

        var expectedArgs = GetExpectedGitDiffArguments(expectedPatchName, Sha1, Sha2, null);

        // Verify
        _processManager
            .Verify(x => x.Execute("git",
                expectedArgs,
                It.IsAny<TimeSpan?>(),
                _clone.Path,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

        _dependencyTracker.Verify(x => x.UpdateSubmodules(It.Is<List<SubmoduleRecord>>(l => l.Count == 0)), Times.Once);

        patches.Should().BeEquivalentTo(new List<VmrIngestionPatch>
        {
            new(expectedPatchName + ".2", RepoVmrPath),
            new(expectedPatchName + ".1.1", RepoVmrPath / "large-dir-1" / "large-dir-2"),
            new(expectedPatchName + ".1.2", RepoVmrPath / "large-dir-1" / "small-dir"),
        });

        _fileSystem.Verify(x => x.DeleteFile(expectedPatchName), Times.Once);
        _fileSystem.Verify(x => x.DeleteFile(expectedPatchName + ".1"), Times.Once);
    }

    [Test]
    public async Task CreatePatchesWithAFileTooLargeTest()
    {
        // This test verifies that we cannot ingest files over 1GB in size with a reasonable error
        NativePath expectedPatchName = _patchDir / $"{IndividualRepoName}-{Commit.GetShortSha(Sha1)}-{Commit.GetShortSha(Sha2)}.patch";

        // Patch for the whole repo
        _fileSystem
            .Setup(x => x.GetFileInfo(expectedPatchName))
            .Returns(Mock.Of<IFileInfo>(x => x.Length == 1_500_000_000));

        // Patch for big-file
        _fileSystem
            .Setup(x => x.GetFileInfo(expectedPatchName + ".2"))
            .Returns(Mock.Of<IFileInfo>(x => x.Length == 1_500_000_000));

        _fileSystem
            .Setup(x => x.GetDirectories(_clone.Path))
            .Returns([]);

        _fileSystem
            .Setup(x => x.GetFiles(_clone.Path))
            .Returns([_clone.Path / "small-file", _clone.Path / "big-file"]);

        // Act
        var action = async () => await _patchHandler.CreatePatches(
            _testRepoMapping,
            _clone,
            Sha1,
            Sha2,
            _patchDir,
            TmpDir,
            CancellationToken.None);

        // Verify
        await action.Should().ThrowAsync<Exception>().WithMessage($"File {_clone.Path / "big-file"} is too big (>1GB) to be ingested into VMR*");
    }

    private void VerifyGitCall(IEnumerable<string> expectedArguments, Times? times = null) => VerifyGitCall(expectedArguments, _vmrPath.Path, times);

    private void VerifyGitCall(IEnumerable<string> expectedArguments, string repoDir, Times? times = null)
    {
        _processManager
            .Verify(x => x.ExecuteGit(repoDir, expectedArguments, It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()), times ?? Times.Once());
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
            "--no-color",
            "--output",
            new NativePath(patchPath),
            $"{Sha1}..{Sha2}",
            "--",
            ":(glob,attr:!vmr-ignore)*.*",
            ":(glob,attr:!vmr-ignore)src/*",
            VmrPatchHandler.GetExclusionRule("*.dll"),
            VmrPatchHandler.GetExclusionRule("*.exe"),
            VmrPatchHandler.GetExclusionRule("src/**/tests/**/*.*"),
            VmrPatchHandler.GetExclusionRule("submodules/external-1/LICENSE.md"),
        };

        if (submodules != null)
        {
            args.AddRange(submodules.Select(s => $":(exclude){s}"));
        }
        
        return args;
    }
}
