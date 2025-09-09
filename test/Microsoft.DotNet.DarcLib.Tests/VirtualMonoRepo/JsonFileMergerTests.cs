// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
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

        _jsonFileMerger = new JsonFileMerger(
            _gitRepoFactoryMock.Object,
            _commentCollectorMock.Object);
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
        await _jsonFileMerger.MergeJsonsAsync(
            _targetRepoMock.Object,
            TestJsonPath,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            TestJsonPath,
            VmrPreviousSha,
            VmrCurrentSha);

        // Assert
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
        await _jsonFileMerger.MergeJsonsAsync(
            _targetRepoMock.Object,
            TestJsonPath,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            TestJsonPath,
            VmrPreviousSha,
            VmrCurrentSha,
            allowMissingFiles: true);

        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], expectedJson, GitFileOperation.Add)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task VmrVersionFileMergesHandlesConflictingChangesCorrectlyTestAsync()
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

        await _jsonFileMerger.MergeJsonsAsync(
                _targetRepoMock.Object,
                TestJsonPath,
                TargetPreviousSha,
                TargetCurrentSha,
                _vmrRepoMock.Object,
                TestJsonPath,
                VmrPreviousSha,
                VmrCurrentSha);

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

        await _jsonFileMerger.MergeJsonsAsync(
            _targetRepoMock.Object,
            TestJsonPath,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrRepoMock.Object,
            TestJsonPath,
            VmrPreviousSha,
            VmrCurrentSha,
            allowMissingFiles: true);

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
        await _jsonFileMerger.MergeJsonsAsync(
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
        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], targetCurrentJson, GitFileOperation.Delete)),
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
}
