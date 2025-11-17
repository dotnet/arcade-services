// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

public class VersionDetailsFileMergerTests
{
    private readonly Mock<IGitRepoFactory> _gitRepoFactoryMock = new();
    private readonly Mock<ILogger<VmrVersionFileMerger>> _loggerMock = new();
    private readonly Mock<ILocalGitRepoFactory> _localGitRepoFactoryMock = new();
    private readonly Mock<IVersionDetailsParser> _versionDetailsParserMock = new();
    private readonly Mock<IDependencyFileManager> _dependencyFileManagerMock = new();
    private readonly Mock<ILocalGitRepo> _targetRepoMock = new();
    private readonly Mock<ILocalGitRepo> _vmrRepoMock = new();
    private readonly Mock<ICommentCollector> _commentCollectorMock = new();

    private VersionDetailsFileMerger _versionDetailsFileMerger = null!;

    private const string TargetPreviousSha = "target-previous-sha";
    private const string TargetCurrentSha = "target-current-sha";
    private const string VmrPreviousSha = "vmr-previous-sha";
    private const string VmrCurrentSha = "vmr-current-sha";
    private const string TargetRepoPath = "/target-repo";
    private const string VmrPath = "/vmr";

    [SetUp]
    public void SetUp()
    {
        _targetRepoMock.Reset();
        _vmrRepoMock.Reset();
        _commentCollectorMock.Reset();

        _targetRepoMock.Setup(r => r.Path).Returns(new NativePath(TargetRepoPath));
        _vmrRepoMock.Setup(r => r.Path).Returns(new NativePath(VmrPath));

        _localGitRepoFactoryMock.Setup(f => f.Create(It.IsAny<NativePath>())).Returns(_vmrRepoMock.Object);

        _versionDetailsFileMerger = new VersionDetailsFileMerger(
            _gitRepoFactoryMock.Object,
            _loggerMock.Object,
            _localGitRepoFactoryMock.Object,
            _versionDetailsParserMock.Object,
            _dependencyFileManagerMock.Object,
            _commentCollectorMock.Object);
    }

    [Test]
    public async Task MergeVersionDetails_BackFlow_UsesPreviousVmrDependencies()
    {
        var targetPreviousKey = "targetPrevious";
        var targetCurrentKey = "targetCurrent";
        var vmrPreviousKey = "vmrPrevious";
        var vmrCurrentKey = "vmrCurrent";
        var targetBranch = "main";
        var packageAlreadyRemovedInRepo = "Package.That.Is.Already.Removed.In.Repo";

        var sourceDependency = new SourceDependency("uri", "mapping", VmrPreviousSha, 1);
        var targetPreviousDependencies = new VersionDetails(
            [
                CreateDependency("Package.From.Build", "1.0.0", VmrPreviousSha),
                CreateDependency("Package.Removed.In.Repo", "1.0.0", VmrPreviousSha),
                CreateDependency("Package.Updated.In.Both", "1.0.0", VmrPreviousSha),
                CreateDependency("Package.Removed.In.VMR", "1.0.0", VmrPreviousSha), // Will be removed in VMR
            ],
            sourceDependency);
        var targetCurrentDependencies = new VersionDetails(
            [
                CreateDependency("Package.From.Build", "1.0.1", VmrPreviousSha), // Updated
                CreateDependency("Package.Updated.In.Both", "1.0.3", VmrPreviousSha), // Updated (vmr updated to 3.0.0)
                CreateDependency("Package.Added.In.Repo", "1.0.0", VmrPreviousSha), // Added
                CreateDependency("Package.Added.In.Both", "2.2.2", VmrPreviousSha), // Added in both
                CreateDependency("Package.Removed.In.VMR", "1.0.0", VmrPreviousSha),
            ],
            sourceDependency);
        var vmrPreviousDependencies = new VersionDetails(
            [
                ..targetPreviousDependencies.Dependencies,
                CreateDependency(packageAlreadyRemovedInRepo, "1.0.0", VmrPreviousSha),
            ],
            sourceDependency);
        var vmrCurrentDependencies = new VersionDetails(
            [
                CreateDependency("Package.From.Build", "1.0.0", VmrPreviousSha),
                CreateDependency("Package.Removed.In.Repo", "1.0.0", VmrPreviousSha),
                CreateDependency("Package.Updated.In.Both", "3.0.0", VmrPreviousSha), // Updated (repo updated to 1.0.3)
                CreateDependency("Package.Added.In.VMR", "2.0.0", VmrPreviousSha), // Added
                CreateDependency("Package.Added.In.Both", "1.1.1", VmrPreviousSha), // Added in both
                // Package.Removed.In.VMR removed
                // Package.That.Is.Already.Removed.In.Repo is removed
            ],
            sourceDependency);
        List<(string, string)> expectedVersions =
            [
                ("Package.From.Build", "1.0.1"),
                ("Package.Updated.In.Both", "3.0.0"),
                ("Package.Added.In.Repo", "1.0.0"),
                ("Package.Added.In.Both", "2.2.2"),
                ("Package.Added.In.VMR", "2.0.0")
            ];
        Dictionary<string, VersionDetails> versionDetailsDictionary = new()
        {
            { targetPreviousKey, targetPreviousDependencies },
            { targetCurrentKey, targetCurrentDependencies },
            { vmrPreviousKey, vmrPreviousDependencies },
            { vmrCurrentKey, vmrCurrentDependencies }
        };

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousKey);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentKey);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), targetBranch, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentKey);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentKey);
        _vmrRepoMock.Setup(v => v.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousKey);
        _vmrRepoMock.Setup(v => v.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentKey);

        _versionDetailsParserMock.Setup(p => p.ParseVersionDetailsXml(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string key, bool _) => versionDetailsDictionary[key]);

        _dependencyFileManagerMock.Setup(d => d.TryRemoveDependencyAsync(packageAlreadyRemovedInRepo, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UnixPath>(), It.IsAny<bool>()))
            .ReturnsAsync(false);
        _dependencyFileManagerMock.Setup(d => d.TryRemoveDependencyAsync(It.Is<string>(name => name != packageAlreadyRemovedInRepo), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UnixPath>(), It.IsAny<bool>()))
            .Callback((string name, string repo, string commit, UnixPath? _, bool? _) =>
            {
                var versionDetails = versionDetailsDictionary[targetCurrentKey];
                versionDetailsDictionary[targetCurrentKey] = new VersionDetails(
                    versionDetails.Dependencies.Where(d => d.Name != name).ToList(),
                    versionDetails.Source);
            })
            .ReturnsAsync(true);

        _dependencyFileManagerMock.Setup(d => d.TryAddOrUpdateDependency(It.IsAny<DependencyDetail>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UnixPath>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Callback((DependencyDetail dependency, string repo, string commit, UnixPath? _, bool _, bool? _, bool _) =>
            {
                var versionDetails = versionDetailsDictionary[targetCurrentKey];
                var dep = versionDetails.Dependencies.FirstOrDefault(d => d.Name == dependency.Name);
                if (dep == null)
                {
                    versionDetailsDictionary[targetCurrentKey] = new VersionDetails(
                        [
                            ..versionDetails.Dependencies,
                            dependency
                        ],
                        versionDetails.Source);
                }
                else
                {
                    dep.Version = dependency.Version;
                }
            })
            .ReturnsAsync(true);

        var result = await _versionDetailsFileMerger.MergeVersionDetails(
            _targetRepoMock.Object,
            "TARGET VERSION.DETAILS PATH",
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            "VMR VERSION.DETAILS PATH",
            VmrPreviousSha,
            VmrCurrentSha,
            mappingToApplyChanges: null);

        versionDetailsDictionary[targetCurrentKey].Dependencies
            .Select(d => (d.Name, d.Version))
            .Should()
            .BeEquivalentTo(expectedVersions, options => options.WithStrictOrdering());
        result.Additions.Should().HaveCount(2);
        // there should only be one removal because Package.That.Is.Already.Removed.In.Repo was already removed in the target repo
        result.Removals.Should().HaveCount(1);
        result.Updates.Should().HaveCount(1);
        List<(string, string)> expectedAdditions = [
            ("Package.Added.In.Both", "2.2.2"),
            ("Package.Added.In.VMR", "2.0.0")];
        result.Additions.Values
            .Select(a => (DependencyDetail)a.Value!)
            .Select(d => (d.Name, d.Version))
            .Should()
            .BeEquivalentTo(expectedAdditions, options => options.WithStrictOrdering());
        List<(string, string)> expectedUpdates = [
            ("Package.Updated.In.Both", "3.0.0")];
        result.Updates.Values
            .Select(u => (DependencyDetail)u.Value!)
            .Select(d => (d.Name, d.Version))
            .Should()
            .BeEquivalentTo(expectedUpdates, options => options.WithStrictOrdering());

    }

    [Test]
    public async Task MergeVersionDetails_ConflictingIncomparableVersions_PostsWarningComment()
    {
        var targetPreviousDependencies = new VersionDetails(
            [
                CreateDependency("TestPackage", "1.0.0", "sha1"),
            ],
            null);
        var targetCurrentDependencies = new VersionDetails(
            [
                CreateDependency("TestPackage", "custom-version-1", "sha2"), // Non-semantic version
            ],
            null);
        var vmrPreviousDependencies = new VersionDetails(
            [
                CreateDependency("TestPackage", "1.0.0", "sha1"),
            ],
            null);
        var vmrCurrentDependencies = new VersionDetails(
            [
                CreateDependency("TestPackage", "custom-version-2", "sha3"), // Different non-semantic version
            ],
            null);

        var targetPreviousKey = "targetPrevious";
        var targetCurrentKey = "targetCurrent";
        var vmrPreviousKey = "vmrPrevious";
        var vmrCurrentKey = "vmrCurrent";

        Dictionary<string, VersionDetails> versionDetailsDictionary = new()
        {
            { targetPreviousKey, targetPreviousDependencies },
            { targetCurrentKey, targetCurrentDependencies },
            { vmrPreviousKey, vmrPreviousDependencies },
            { vmrCurrentKey, vmrCurrentDependencies }
        };

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousKey);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentKey);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentKey);
        _vmrRepoMock.Setup(v => v.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousKey);
        _vmrRepoMock.Setup(v => v.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentKey);

        _versionDetailsParserMock.Setup(p => p.ParseVersionDetailsXml(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string key, bool _) => versionDetailsDictionary[key]);

        _dependencyFileManagerMock.Setup(d => d.TryAddOrUpdateDependency(It.IsAny<DependencyDetail>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UnixPath>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(true);

        var result = await _versionDetailsFileMerger.MergeVersionDetails(
            _targetRepoMock.Object,
            "TARGET VERSION.DETAILS PATH",
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            "VMR VERSION.DETAILS PATH",
            VmrPreviousSha,
            VmrCurrentSha,
            mappingToApplyChanges: null);

        // Verify that a warning comment is posted for conflicting incomparable versions
        _commentCollectorMock.Verify(c => c.AddComment(
            It.Is<string>(s => s.Contains("conflict") && s.Contains("TestPackage") && s.Contains("incomparable")),
            CommentType.Warning),
            Times.Once);
    }

    private static DependencyDetail CreateDependency(string name, string version, string commit, DependencyType type = DependencyType.Product)
        => new()
        {
            Name = name,
            Version = version,
            Commit = commit,
            RepoUri = "uri",
            Type = type,
        };
}
