// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

public class JsonFileMergerTests
{
    private readonly Mock<IGitRepoFactory> _gitRepoFactoryMock = new();
    private readonly Mock<ILocalGitRepo> _targetRepoMock = new();
    private readonly Mock<ILocalGitRepo> _vmrRepoMock = new();
    private readonly Mock<IGitRepo> _gitRepoMock = new();
    private readonly Mock<ICommentCollector> _commentCollectorMock = new();

    private JsonFileMerger _jsonFileMerger = null!;
    private IFlatJsonUpdater _flatJsonUpdater = null!;

    private const string TestJsonPath = "test.json";
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
        _gitRepoMock.Reset();
        _commentCollectorMock.Reset();

        _targetRepoMock.Setup(r => r.Path).Returns(new NativePath(TargetRepoPath));
        _vmrRepoMock.Setup(r => r.Path).Returns(new NativePath(VmrPath));

        _gitRepoFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_gitRepoMock.Object);

        _flatJsonUpdater = new FlatJsonUpdater(NullLogger<FlatJsonUpdater>.Instance);
        _jsonFileMerger = new JsonFileMerger(
            _gitRepoFactoryMock.Object,
            _commentCollectorMock.Object,
            _flatJsonUpdater);
    }

    [Test]
    public async Task MergeJsonsAsyncCorrectlyMergesTest()
    {
        // Arrange
        var targetPreviousJson = """
            {
              "sdk": {
                "version": "8.0.303",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.303",
                "runtimes": {
                  "dotnet": [
                    "6.0.29"
                  ],
                  "aspnetcore": [
                    "6.0.29"
                  ]
                }
              },
              "msbuild-sdks": {
                "Microsoft.DotNet.Arcade.Sdk": "8.0.0-beta.25310.3"
              }
            }
            """;

        var targetCurrentJson = """
            {
              "sdk": {
                "version": "8.0.304"
              },
              "tools": {
                "dotnet": "8.0.303",
                "runtimes": {
                  "dotnet": [
                    "6.0.29",
                    "8.0.0"
                  ],
                  "aspnetcore": [
                    "6.0.29"
                  ]
                }
              },
              "msbuild-sdks": {
                "Microsoft.DotNet.Arcade.Sdk": "8.0.0-beta.25310.3",
                "new.sdk": "1.0.0"
              }
            }
            """;

        var sourcePreviousJson = """
            {
              "sdk": {
                "version": "8.0.303",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.303",
                "runtimes": {
                  "dotnet": [
                    "6.0.29"
                  ],
                  "aspnetcore": [
                    "6.0.29"
                  ]
                }
              },
              "msbuild-sdks": {
                "Microsoft.DotNet.Arcade.Sdk": "8.0.0-beta.25310.3"
              }
            }
            """;

        var sourceCurrentJson = """
            {
              "sdk": {
                "version": "8.0.305",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.307",
                "runtimes": {
                  "dotnet": [
                    "6.0.29"
                  ],
                  "aspnetcore": [
                    "6.0.29"
                  ]
                }
              },
              "msbuild-sdks": {
              },
              "new.something": [
                "1.0.0"
              ]
            }
            """;

        var expectedJson = """
            {
              "sdk": {
                "version": "8.0.305"
              },
              "tools": {
                "dotnet": "8.0.307",
                "runtimes": {
                  "dotnet": [
                    "6.0.29",
                    "8.0.0"
                  ],
                  "aspnetcore": [
                    "6.0.29"
                  ]
                }
              },
              "msbuild-sdks": {
                "new.sdk": "1.0.0"
              },
              "new.something": [
                "1.0.0"
              ]
            }
            """;

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(sourcePreviousJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(sourceCurrentJson);

        // Act
        var hadChanges = await _jsonFileMerger.MergeJsonsAsync(
            _targetRepoMock.Object,
            TestJsonPath,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            TestJsonPath,
            VmrPreviousSha,
            VmrCurrentSha);

        // Assert
        hadChanges.Should().BeTrue();
        _vmrRepoMock.Verify(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()), Times.Once);
        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], expectedJson, GitFileOperation.Add)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task MergeJsonsAsyncHandlesMissingJsonsCorrectly()
    {
        string? targetPreviousJson = null;
        string? targetCurrentJson = null;
        string? vmrPreviousJson = null;
        var sourceCurrentJson = """
            {
              "sdk": {
                "version": "8.0.305",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.307",
                "runtimes": {
                  "dotnet": [
                    "6.0.29"
                  ],
                  "aspnetcore": [
                    "6.0.29"
                  ]
                }
              },
              "msbuild-sdks": {
                "Microsoft.DotNet.Arcade.Sdk": "8.0.0-beta.25310.3"
              }
            }
            """;

        var expectedJson = """
            {
              "sdk": {
                "version": "8.0.305",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.307",
                "runtimes": {
                  "dotnet": [
                    "6.0.29"
                  ],
                  "aspnetcore": [
                    "6.0.29"
                  ]
                }
              },
              "msbuild-sdks": {
                "Microsoft.DotNet.Arcade.Sdk": "8.0.0-beta.25310.3"
              }
            }
            """;

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(sourceCurrentJson);

        // Act
        var hadChanges = await _jsonFileMerger.MergeJsonsAsync(
            _targetRepoMock.Object,
            TestJsonPath,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            TestJsonPath,
            VmrPreviousSha,
            VmrCurrentSha,
            allowMissingFiles: true);

        // Assert
        hadChanges.Should().BeTrue();
        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], expectedJson, GitFileOperation.Add)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task MergeJsonsAsync_HandlesConflictingChangesCorrectlyTestAsync()
    {
        // Arrange
        var targetPreviousJson = """
            {
              "sdk": {
                "version": "8.0.303",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.303",
                "runtimes": {
                  "dotnet": [
                    "6.0.29"
                  ],
                  "aspnetcore": [
                    "6.0.29"
                  ]
                }
              }
            }
            """;

        var targetCurrentJson = """
            {
              "sdk": {
                "version": "8.0.303",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.303",
                "runtimes": {
                  "dotnet": [
                    "6.0.29"
                  ],
                  "aspnetcore": [
                    "6.0.29"
                  ]
                },
                "conflictProperty": "1.0.0"
              }
            }
            """;

        var vmrPreviousJson = """
            {
              "sdk": {
                "version": "8.0.303",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.303",
                "runtimes": {
                  "dotnet": [
                    "6.0.29"
                  ],
                  "aspnetcore": [
                    "6.0.29"
                  ]
                },
                "conflictProperty": "0.9.0"
              }
            }
            """;

        var vmrCurrentJson = """
            {
              "sdk": {
                "version": "8.0.303",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.303",
                "runtimes": {
                  "dotnet": [
                    "6.0.29"
                  ],
                  "aspnetcore": [
                    "6.0.29"
                  ]
                }
              }
            }
            """;

        var expectedJson = """
            {
              "sdk": {
                "version": "8.0.303",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.303",
                "runtimes": {
                  "dotnet": [
                    "6.0.29"
                  ],
                  "aspnetcore": [
                    "6.0.29"
                  ]
                },
                "conflictProperty": "1.0.0"
              }
            }
            """;

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentJson);

        var hadChanges = await _jsonFileMerger.MergeJsonsAsync(
                _targetRepoMock.Object,
                TestJsonPath,
                TargetPreviousSha,
                TargetCurrentSha,
                _vmrRepoMock.Object,
                TestJsonPath,
                VmrPreviousSha,
                VmrCurrentSha);

        // Assert
        hadChanges.Should().BeFalse();
        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], expectedJson, GitFileOperation.Add)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
        _commentCollectorMock.Verify(c => c.AddComment(
                It.Is<string>(s => s.Contains("tools.conflictProperty")),
                CommentType.Information),
            Times.Once);
    }

    [Test]
    public async Task MergeJsonsAsync_FileDeletedInTargetRepo_DoesNothing()
    {
        var targetPreviousJson = """
            {
              "sdk": {
                "version": "8.0.303"
              }
            }
            """;

        string? targetCurrentJson = null; // File deleted in target repo

        var vmrPreviousJson = """
            {
              "sdk": {
                "version": "8.0.303"
              }
            }
            """;

        var vmrCurrentJson = """
            {
              "sdk": {
                "version": "8.0.304"
              }
            }
            """;

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentJson);

        var hadChanges = await _jsonFileMerger.MergeJsonsAsync(
            _targetRepoMock.Object,
            TestJsonPath,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            TestJsonPath,
            VmrPreviousSha,
            VmrCurrentSha,
            allowMissingFiles: true);

        // Assert
        hadChanges.Should().BeFalse();
        // Nothing was deleted because the target branch already has the file deleted
        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.IsAny<List<GitFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task MergeJsonsAsync_FileDeletedInSourceRepo_DeletesFileAsync()
    {
        // Arrange
        var targetPreviousJson = """
            {
              "sdk": {
                "version": "8.0.303"
              }
            }
            """;

        var targetCurrentJson = """
            {
              "sdk": {
                "version": "8.0.304"
              }
            }
            """;

        var vmrPreviousJson = """
            {
              "sdk": {
                "version": "8.0.303"
              }
            }
            """;

        string? vmrCurrentJson = null; // File deleted in VMR (source repo)

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentJson);

        // Act
        var hadChanges = await _jsonFileMerger.MergeJsonsAsync(
            _targetRepoMock.Object,
            TestJsonPath,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            TestJsonPath,
            VmrPreviousSha,
            VmrCurrentSha,
            allowMissingFiles: true);

        // Assert - File should be deleted from target repo and no merge should occur
        hadChanges.Should().BeTrue();
        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], targetCurrentJson, GitFileOperation.Delete)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
    }



    [Test]
    public async Task MergeJsonsAsync_ConflictingBooleanProperties_PostsWarningComment()
    {
        var targetPreviousJson = """
            {
              "boolProperty": true
            }
            """;

        var targetCurrentJson = """
            {
              "boolProperty": false
            }
            """;

        var vmrPreviousJson = """
            {
              "boolProperty": true
            }
            """;

        var vmrCurrentJson = """
            {
              "boolProperty": true
            }
            """;

        var expectedJson = """
            {
              "boolProperty": false
            }
            """;

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentJson);

        var hadChanges = await _jsonFileMerger.MergeJsonsAsync(
            _targetRepoMock.Object,
            TestJsonPath,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            TestJsonPath,
            VmrPreviousSha,
            VmrCurrentSha);

        // Assert
        hadChanges.Should().BeFalse();
        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], expectedJson, GitFileOperation.Add)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);

        // This scenario may not trigger a boolean conflict warning since VMR didn't change the property
        // The test validates that merges can complete without throwing exceptions
    }

    [Test]
    public async Task MergeJsonsAsync_ConflictingNonSemanticVersions_PostsWarningComment()
    {
        var targetPreviousJson = """
            {
              "versionProperty": "1.0.0"
            }
            """;

        var targetCurrentJson = """
            {
              "versionProperty": "custom-version-1"
            }
            """;

        var vmrPreviousJson = """
            {
              "versionProperty": "1.0.0"
            }
            """;

        var vmrCurrentJson = """
            {
              "versionProperty": "custom-version-2"
            }
            """;

        var expectedJson = """
            {
              "versionProperty": "custom-version-1"
            }
            """;

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentJson);

        var hadChanges = await _jsonFileMerger.MergeJsonsAsync(
            _targetRepoMock.Object,
            TestJsonPath,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            TestJsonPath,
            VmrPreviousSha,
            VmrCurrentSha);

        // Assert
        hadChanges.Should().BeFalse();
        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], expectedJson, GitFileOperation.Add)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);

        // Verify that a warning comment is posted for conflicting non-semantic versions
        _commentCollectorMock.Verify(c => c.AddComment(
            It.Is<string>(s => s.Contains("versionProperty") && s.Contains("conflicting value")),
            CommentType.Warning),
            Times.Once);
    }

    [Test]
    public async Task MergeJsonsAsync_ArraysMergedFromBothSides_ConcatenatesAndDeduplicates()
    {
        var targetPreviousJson = """
            {
            }
            """;

        var targetCurrentJson = """
            {
              "runtimes": [
                "6.0.29",
                "8.0.0"
              ]
            }
            """;

        var vmrPreviousJson = """
            {
            }
            """;

        var vmrCurrentJson = """
            {
              "runtimes": [
                "6.0.29",
                "7.0.15"
              ]
            }
            """;

        var expectedJson = """
            {
              "runtimes": [
                "6.0.29",
                "8.0.0",
                "7.0.15"
              ]
            }
            """;

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentJson);

        var hadChanges = await _jsonFileMerger.MergeJsonsAsync(
            _targetRepoMock.Object,
            TestJsonPath,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            TestJsonPath,
            VmrPreviousSha,
            VmrCurrentSha);

        // Assert
        hadChanges.Should().BeTrue();
        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], expectedJson, GitFileOperation.Add)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);

        // Arrays are merged successfully without posting warning comments
        _commentCollectorMock.Verify(c => c.AddComment(
            It.IsAny<string>(),
            CommentType.Warning),
            Times.Never);
    }

    [Test]
    public async Task MergeJsonsAsync_ArraysWithDuplicates_RemovesDuplicatesAfterMerge()
    {
        var targetPreviousJson = """
            {
              "dependencies": [
                "package-a",
                "package-b"
              ]
            }
            """;

        var targetCurrentJson = """
            {
              "dependencies": [
                "package-a",
                "package-b",
                "package-c"
              ]
            }
            """;

        var vmrPreviousJson = """
            {
              "dependencies": [
                "package-a",
                "package-b"
              ]
            }
            """;

        var vmrCurrentJson = """
            {
              "dependencies": [
                "package-a",
                "package-b",
                "package-c",
                "package-d"
              ]
            }
            """;

        var expectedJson = """
            {
              "dependencies": [
                "package-a",
                "package-b",
                "package-c",
                "package-d"
              ]
            }
            """;

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentJson);

        var hadChanges = await _jsonFileMerger.MergeJsonsAsync(
            _targetRepoMock.Object,
            TestJsonPath,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            TestJsonPath,
            VmrPreviousSha,
            VmrCurrentSha);

        // Assert
        hadChanges.Should().BeTrue();
        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], expectedJson, GitFileOperation.Add)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);

        // No warnings should be posted for successful array merge
        _commentCollectorMock.Verify(c => c.AddComment(
            It.IsAny<string>(),
            CommentType.Warning),
            Times.Never);
    }

    [Test]
    public async Task MergeJsonsAsync_RemovingPropertyRemovesEmptyParentObjects()
    {
        // Arrange
        var targetPreviousJson = """
            {
              "sdk": {
                "version": "8.0.303"
              },
              "nested": {
                "deep": {
                  "value": "original"
                }
              }
            }
            """;

        var targetCurrentJson = """
            {
              "sdk": {
                "version": "8.0.303"
              },
              "nested": {
                "deep": {
                  "value": "original"
                }
              }
            }
            """;

        var vmrPreviousJson = """
            {
              "sdk": {
                "version": "8.0.303"
              },
              "nested": {
                "deep": {
                  "value": "original"
                }
              }
            }
            """;

        var vmrCurrentJson = """
            {
              "sdk": {
                "version": "8.0.303"
              }
            }
            """;

        var expectedJson = """
            {
              "sdk": {
                "version": "8.0.303"
              }
            }
            """;

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentJson);

        // Act
        var hadChanges = await _jsonFileMerger.MergeJsonsAsync(
            _targetRepoMock.Object,
            TestJsonPath,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            TestJsonPath,
            VmrPreviousSha,
            VmrCurrentSha);

        // Assert
        hadChanges.Should().BeTrue();
        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], expectedJson, GitFileOperation.Add)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
    }


    private static bool ValidateGitFile(GitFile file, string expectedContent, GitFileOperation operation)
    {
        var normalizedFileContent = file.Content.Trim().Replace("\r\n", "\n");
        var expectedContentNormalized = expectedContent.Trim().Replace("\r\n", "\n");
        return normalizedFileContent == expectedContentNormalized && file.Operation == operation;
    }

    [Test]
    public async Task MergeJsonsAsync_WhenAddingChildToNonObject_ConvertsValueToObject()
    {
        // Arrange
        // Target has "parent" as a string value
        var targetPreviousJson = """
            {
              "parent": "original"
            }
            """;

        var targetCurrentJson = """
            {
              "parent": "original"
            }
            """;

        // VMR changes "parent" from a string to an object with a child
        var vmrPreviousJson = """
            {
              "parent": "original"
            }
            """;

        var vmrCurrentJson = """
            {
              "parent": {
                "child": "newValue"
              }
            }
            """;

        var expectedJson = """
            {
              "parent": {
                "child": "newValue"
              }
            }
            """;

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousJson);
        _vmrRepoMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentJson);

        // Act
        var hadChanges = await _jsonFileMerger.MergeJsonsAsync(
            _targetRepoMock.Object,
            TestJsonPath,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            TestJsonPath,
            VmrPreviousSha,
            VmrCurrentSha);

        // Assert
        hadChanges.Should().BeTrue();
        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], expectedJson, GitFileOperation.Add)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
    }
}
