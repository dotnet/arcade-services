// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

public class VmrVersionFileMergerTests
{
    private readonly Mock<IGitRepoFactory> _gitRepoFactoryMock = new();
    private readonly Mock<ILogger<VmrVersionFileMerger>> _loggerMock = new();
    private readonly Mock<IVmrInfo> _vmrInfoMock = new();
    private readonly Mock<ILocalGitRepoFactory> _localGitRepoFactoryMock = new();
    private readonly Mock<IVersionDetailsParser> _versionDetailsParserMock = new();
    private readonly Mock<IDependencyFileManager> _dependencyFileManagerMock = new();
    private readonly Mock<ILocalGitRepo> _targetRepoMock = new();
    private readonly Mock<ILocalGitRepo> _vmrMock = new();
    private readonly Mock<IGitRepo> _gitRepoMock = new();
    
    private VmrVersionFileMerger _vmrVersionFileMerger = null!;
    
    private const string TestMappingName = "test-repo";
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
        _targetRepoMock.Setup(r => r.Path).Returns(new NativePath(TargetRepoPath));
        _vmrMock.Setup(r => r.Path).Returns(new NativePath(VmrPath));
        _vmrInfoMock.Setup(v => v.VmrPath).Returns(new NativePath(VmrPath));

        _gitRepoFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_gitRepoMock.Object);
        _localGitRepoFactoryMock.Setup(f => f.Create(It.IsAny<NativePath>())).Returns(_vmrMock.Object);

        _vmrVersionFileMerger = new VmrVersionFileMerger(
            _gitRepoFactoryMock.Object,
            _loggerMock.Object,
            _vmrInfoMock.Object,
            _localGitRepoFactoryMock.Object,
            _versionDetailsParserMock.Object,
            _dependencyFileManagerMock.Object);
    }

    [Test]
    public async Task MergeJsonAsyncCorrectlyMergesTest()
    {
        // Arrange
        var lastFlow = new Backflow("previous-vmr-sha", "previous-repo-sha");
        
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
                }
              },
              "msbuild-sdks": {
                "Microsoft.DotNet.Arcade.Sdk": "8.0.0-beta.25310.3"
              }
            }
            """;
        
        var vmrCurrentJson = """
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

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()    ))
            .ReturnsAsync(vmrPreviousJson);
        _vmrMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentJson);

        // Act
        await _vmrVersionFileMerger.MergeJsonAsync(
            lastFlow,
            _targetRepoMock.Object,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrMock.Object,
            VmrPreviousSha,
            VmrCurrentSha,
            TestMappingName,
            TestJsonPath);

        // Assert
        _vmrMock.Verify(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()), Times.Once);
        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], expectedJson)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);   
    }

    [Test]
    public async Task MergeJsonAsyncHandlesMissingJsonsCorrectly()
    {
        var lastFlow = new Backflow("previous-vmr-sha", "previous-repo-sha");
        
        string? targetPreviousJson = null;
        string? targetCurrentJson = null;
        string? vmrPreviousJson = null;
        var vmrCurrentJson = """
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

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, "HEAD", It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousJson);
        _vmrMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentJson);

        // Act
        await _vmrVersionFileMerger.MergeJsonAsync(
            lastFlow,
            _targetRepoMock.Object,
            TargetPreviousSha,
            TargetCurrentSha,
            _vmrMock.Object,
            VmrPreviousSha,
            VmrCurrentSha,
            TestMappingName,
            TestJsonPath,
            allowMissingFiles: true);

        _gitRepoMock.Verify(g => g.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Count == 1 && ValidateGitFile(files[0], expectedJson)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
    }

    private bool ValidateGitFile(GitFile file, string expectedContent)
    {
        var normalizedFileContent = file.Content.Trim().Replace("\r\n", "\n");
        var expectedContentNormalized = expectedContent.Trim().Replace("\r\n", "\n");
        return normalizedFileContent == expectedContentNormalized;
    }

    [Test]
    public async Task MergeVersionDetails_BackFlow_UsesPreviousVmrDependencies()
    {
        var targetPreviousKey = "targetPrevious";
        var targetCurrentKey = "targetCurrent";
        var vmrPreviousKey = "vmrPrevious";
        var vmrCurrentKey = "vmrCurrent";
        var targetBranch = "main";

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
                CreateDependency("Package.Removed.In.VMR", "1.0.0", VmrPreviousSha)
            ],
            sourceDependency);
        var vmrPreviousDependencies = targetPreviousDependencies; // we forward flew before so they're the same
        var vmrCurrentDependencies = new VersionDetails(
            [
                CreateDependency("Package.From.Build", "1.0.0", VmrPreviousSha),
                CreateDependency("Package.Removed.In.Repo", "1.0.0", VmrPreviousSha),
                CreateDependency("Package.Updated.In.Both", "3.0.0", VmrPreviousSha), // Updated (repo updated to 1.0.3)
                CreateDependency("Package.Added.In.VMR", "2.0.0", VmrPreviousSha), // Added
                CreateDependency("Package.Added.In.Both", "1.1.1", VmrPreviousSha), // Added in both
                // Package.Removed.In.VMR removed
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
        _vmrMock.Setup(v => v.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousKey);
        _vmrMock.Setup(v => v.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentKey);

        _versionDetailsParserMock.Setup(p => p.ParseVersionDetailsXml(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string key, bool _) => versionDetailsDictionary[key]);

        _dependencyFileManagerMock.Setup(d => d.RemoveDependencyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback((string name, string repo, string commit, bool _) =>
            {
                var versionDetails = versionDetailsDictionary[targetCurrentKey];
                versionDetailsDictionary[targetCurrentKey] = new VersionDetails(
                    versionDetails.Dependencies.Where(d => d.Name != name).ToList(),
                    versionDetails.Source);
            })
            .Returns(Task.CompletedTask);

        _dependencyFileManagerMock.Setup(d => d.AddDependencyAsync(It.IsAny<DependencyDetail>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback((DependencyDetail dependency, string repo, string commit) =>
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
            .Returns(Task.CompletedTask);

        var result = await _vmrVersionFileMerger.MergeVersionDetails(
            new ForwardFlow(TargetPreviousSha, VmrPreviousSha),
            new Backflow(VmrCurrentSha, TargetCurrentSha),
            TestMappingName,
            _targetRepoMock.Object,
            targetBranch);

        versionDetailsDictionary[targetCurrentKey].Dependencies
            .Select(d => (d.Name, d.Version))
            .Should()
            .BeEquivalentTo(expectedVersions, options => options.WithStrictOrdering());
        result.Additions.Should().HaveCount(3);
        result.Removals.Should().HaveCount(1);
        result.Updates.Should().HaveCount(2);
        List<(string, string)> expectedAdditions = [
            ("Package.Added.In.Repo", "1.0.0"),
            ("Package.Added.In.Both", "2.2.2"),
            ("Package.Added.In.VMR", "2.0.0")];
        result.Additions.Values
            .Select(a => (DependencyDetail)a.Value!)
            .Select(d => (d.Name, d.Version))
            .Should()
            .BeEquivalentTo(expectedAdditions, options => options.WithStrictOrdering());
        List<(string, string)> expectedUpdates = [
            ("Package.From.Build", "1.0.1"),
            ("Package.Updated.In.Both", "3.0.0")];
        result.Updates.Values
            .Select(u => (DependencyDetail)u.Value!)
            .Select(d => (d.Name, d.Version))
            .Should()
            .BeEquivalentTo(expectedUpdates, options => options.WithStrictOrdering());
    }

    [Test]
    public async Task VmrVersionFileMergesHandlesConflictingChangesCorrectlyTestAsync()
    {
        // Arrange
        var lastFlow = new Backflow("previous-vmr-sha", "previous-repo-sha");
        
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

        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, TargetPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(targetPreviousJson);
        _targetRepoMock.Setup(r => r.GetFileFromGitAsync(TestJsonPath, TargetCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(targetCurrentJson);
        _vmrMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrPreviousSha, It.IsAny<string>()))
            .ReturnsAsync(vmrPreviousJson);
        _vmrMock.Setup(r => r.GetFileFromGitAsync(It.IsAny<string>(), VmrCurrentSha, It.IsAny<string>()))
            .ReturnsAsync(vmrCurrentJson);

        var action = async () => await _vmrVersionFileMerger.MergeJsonAsync(
                lastFlow,
                _targetRepoMock.Object,
                TargetPreviousSha,
                TargetCurrentSha,
                _vmrMock.Object,
                VmrPreviousSha,
                VmrCurrentSha,
                TestMappingName,
                TestJsonPath);

        await action.Should().ThrowAsync<ConflictingDependencyUpdateException>();
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
