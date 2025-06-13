// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

[TestFixture]
public class CodeflowChangeAnalyzerTests
{
    private const string TestMappingName = "test-repo";
    private const string TestHeadBranch = "feature-branch";
    private const string TestTargetBranch = "main";
    private const string TestAncestorCommit = "ee69bb149b4824b93abb3d6b029aeacfa30d6207";
    private static readonly NativePath TestVmrPath = new("/path/to/vmr");

    private Mock<ILocalGitRepoFactory> _localGitRepoFactory = null!;
    private Mock<ILocalGitRepo> _localGitRepo = null!;
    private Mock<IVersionDetailsParser> _versionDetailsParser = null!;
    private Mock<IBasicBarClient> _barClient = null!;
    private IVmrInfo _vmrInfo = null!;
    private CodeflowChangeAnalyzer _analyzer = null!;

    [SetUp]
    public void SetUp()
    {
        _localGitRepoFactory = new Mock<ILocalGitRepoFactory>();
        _localGitRepo = new Mock<ILocalGitRepo>();
        _versionDetailsParser = new Mock<IVersionDetailsParser>();
        _barClient = new Mock<IBasicBarClient>();
        _vmrInfo = Mock.Of<IVmrInfo>(x => x.VmrPath == TestVmrPath);

        _localGitRepoFactory
            .Setup(x => x.Create(TestVmrPath))
            .Returns(_localGitRepo.Object);

        _localGitRepo
            .Setup(x => x.ExecuteGitCommand("merge-base", TestTargetBranch, TestHeadBranch))
            .ReturnsAsync(new ProcessExecutionResult()
            {
                ExitCode = 0,
                StandardOutput = TestAncestorCommit,
            });

        _analyzer = new CodeflowChangeAnalyzer(
            _localGitRepoFactory.Object,
            _versionDetailsParser.Object,
            _barClient.Object,
            _vmrInfo,
            NullLogger<CodeflowChangeAnalyzer>.Instance);
    }

    [Test]
    public async Task ForwardFlowHasMeaningfulChangesAsync_WithUnexpectedChangedFiles_ShouldReturnTrue()
    {
        // Arrange
        var gitDiffOutput =
            """
            eng/common/build.ps1
            src/test-repo/Program.cs
            src/test-repo/Library.cs
            src/source-manifest.json
            src/test-repo/global.json
            src/test-repo/eng/Version.Details.xml
            src/test-repo/eng/Versions.props
            """;

        SetChangedFiles(gitDiffOutput);

        // Act
        var result = await _analyzer.ForwardFlowHasMeaningfulChangesAsync(TestMappingName, TestHeadBranch, TestTargetBranch);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public async Task ForwardFlowHasMeaningfulChangesAsync_WithExpectedChangedFiles_ShouldReturnFalse()
    {
        // Arrange
        var gitDiffOutput =
            """
            eng/common/build.ps1
            src/source-manifest.json
            """;

        SetChangedFiles(gitDiffOutput);

        // Act
        var result = await _analyzer.ForwardFlowHasMeaningfulChangesAsync(TestMappingName, TestHeadBranch, TestTargetBranch);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public async Task ForwardFlowHasMeaningfulChangesAsync_WithNoUnexpectedChanges_ShouldReturnFalse()
    {
        // Arrange
        var gitDiffOutput =
            """
            eng/common/build.ps1
            src/source-manifest.json
            src/test-repo/global.json
            src/test-repo/eng/Version.Details.xml
            src/test-repo/eng/Versions.props
            """;

        var gitDiffWithIgnoredOutput =
            """
            diff --git a/src/roslyn-analyzers/eng/Version.Details.xml b/src/roslyn-analyzers/eng/Version.Details.xml
            index de02854b9db..05f32ddf7a6 100644
            --- a/src/roslyn-analyzers/eng/Version.Details.xml
            +++ b/src/roslyn-analyzers/eng/Version.Details.xml
            @@ -3 +3 @@
            -  <Source Uri="https://github.com/dotnet/dotnet" Mapping="roslyn-analyzers" Sha="7e27ec4c314eb774eae2c54ce4682c98973c7c60" BarId="270662" />
            +  <Source Uri="https://github.com/dotnet/dotnet" Mapping="roslyn-analyzers" Sha="9a90ec1b43070dc3ee0f0b869a78a175c1d33b68" BarId="271018" />
            @@ -11 +11 @@
            -    <Dependency Name="Microsoft.DotNet.Arcade.Sdk" Version="10.0.0-beta.25304.106">
            +    <Dependency Name="Microsoft.DotNet.Arcade.Sdk" Version="10.0.0-beta.25306.103">
            @@ -13 +13 @@
            -      <Sha>7e27ec4c314eb774eae2c54ce4682c98973c7c60</Sha>
            +      <Sha>9a90ec1b43070dc3ee0f0b869a78a175c1d33b68</Sha>
            @@ -15 +15 @@
            -    <Dependency Name="Microsoft.DotNet.XliffTasks" Version="10.0.0-beta.25304.106">
            +    <Dependency Name="Microsoft.DotNet.XliffTasks" Version="10.0.0-beta.25306.103">
            @@ -17 +17 @@
            -      <Sha>7e27ec4c314eb774eae2c54ce4682c98973c7c60</Sha>
            +      <Sha>9a90ec1b43070dc3ee0f0b869a78a175c1d33b68</Sha>
            diff --git a/src/roslyn-analyzers/eng/Versions.props b/src/roslyn-analyzers/eng/Versions.props
            index 3d1c4193028..d6ba1d51912 100644
            --- a/src/roslyn-analyzers/eng/Versions.props
            +++ b/src/roslyn-analyzers/eng/Versions.props
            @@ -78 +78 @@
            -    <MicrosoftDotNetXliffTasksVersion>10.0.0-beta.25304.106</MicrosoftDotNetXliffTasksVersion>
            +    <MicrosoftDotNetXliffTasksVersion>10.0.0-beta.25306.103</MicrosoftDotNetXliffTasksVersion>
            diff --git a/src/roslyn-analyzers/global.json b/src/roslyn-analyzers/global.json
            index 903c014977c..d8c8302c44b 100644
            --- a/src/roslyn-analyzers/global.json
            +++ b/src/roslyn-analyzers/global.json
            @@ -21 +21 @@
            -    "Microsoft.DotNet.Arcade.Sdk": "10.0.0-beta.25304.106"
            +    "Microsoft.DotNet.Arcade.Sdk": "10.0.0-beta.25306.103"
            diff --git a/src/source-manifest.json b/src/source-manifest.json
            index 6cb979ccc3a..90ec6500113 100644
            --- a/src/source-manifest.json
            +++ b/src/source-manifest.json
            @@ -102,2 +102,2 @@
            -      "packageVersion": "10.0.0-preview.25305.2",
            -      "barId": 270822,
            +      "packageVersion": "10.0.0-preview.25309.1",
            +      "barId": 271087,
            @@ -106 +106 @@
            -      "commitSha": "b5c183add7824375fb00c6f4a1614098555b3e62"
            +      "commitSha": "d72f60b9124ae5a25538dbceab7b3590ce309641"
            """;

        SetChangedFiles(gitDiffOutput);
        SetGitDiff(gitDiffWithIgnoredOutput);
        SetupVersionDetailsAndBuilds();

        // Act
        var result = await _analyzer.ForwardFlowHasMeaningfulChangesAsync(TestMappingName, TestHeadBranch, TestTargetBranch);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public async Task ForwardFlowHasMeaningfulChangesAsync_WithUnexpectedChanges_ShouldReturnTrue()
    {
        // Arrange
        var gitDiffOutput =
            """
            eng/common/build.ps1
            src/source-manifest.json
            src/test-repo/global.json
            src/test-repo/eng/Version.Details.xml
            src/test-repo/eng/Versions.props
            """;

        var gitDiffWithIgnoredOutput =
            """
            diff --git a/src/test-repo/eng/Version.Details.xml b/src/test-repo/eng/Version.Details.xml
            index bfc5f65e016..05f32ddf7a6 100644
            --- a/src/test-repo/eng/Version.Details.xml
            +++ b/src/test-repo/eng/Version.Details.xml
            @@ -3,3 +3,3 @@
            -    <SomeUnexpectedElement>OldValue</SomeUnexpectedElement>
            +    <SomeUnexpectedElement>NewValue</SomeUnexpectedElement>";
            """;

        SetChangedFiles(gitDiffOutput);
        SetGitDiff(gitDiffWithIgnoredOutput);
        SetupVersionDetailsAndBuilds();

        // Act
        var result = await _analyzer.ForwardFlowHasMeaningfulChangesAsync(TestMappingName, TestHeadBranch, TestTargetBranch);

        // Assert
        result.Should().BeTrue();
    }

    private void SetChangedFiles(string output)
    {
        var result = new ProcessExecutionResult()
        {
            StandardOutput = output,
            ExitCode = 0,
        };

        _localGitRepo
            .Setup(x => x.ExecuteGitCommand("diff", "--name-only", $"{TestAncestorCommit}..{TestHeadBranch}"))
            .ReturnsAsync(result);
    }

    private void SetGitDiff(string output)
    {
        var result = new ProcessExecutionResult()
        {
            StandardOutput = output,
            ExitCode = 0,
        };

        _localGitRepo
            .Setup(x => x.ExecuteGitCommand(It.Is<string[]>(args =>
                args.Length > 2 &&
                args[0] == "diff" &&
                args[1] == "-U0" &&
                args[2] == $"{TestAncestorCommit}..{TestHeadBranch}")))
            .ReturnsAsync(result);
    }

    private void SetupVersionDetailsAndBuilds()
    {
        var versionDetails1 = new VersionDetails(
            Dependencies: [],
            Source: new SourceDependency("https://github.com/dotnet/dotnet", "test-repo", "abc123", 270662));

        var versionDetails2 = new VersionDetails(
            Dependencies: [],
            Source: new SourceDependency("https://github.com/dotnet/dotnet", "test-repo", "def456", 271018));

        _localGitRepo
            .Setup(x => x.GetFileFromGitAsync("src/test-repo/eng/Version.Details.xml", TestAncestorCommit, null))
            .ReturnsAsync("<Dependencies><Source BarId=\"270662\" /></Dependencies>");

        _localGitRepo
            .Setup(x => x.GetFileFromGitAsync("src/test-repo/eng/Version.Details.xml", TestHeadBranch, null))
            .ReturnsAsync("<Dependencies><Source BarId=\"271018\" /></Dependencies>");

        _versionDetailsParser
            .Setup(x => x.ParseVersionDetailsXml("<Dependencies><Source BarId=\"270662\" /></Dependencies>", true))
            .Returns(versionDetails1);

        _versionDetailsParser
            .Setup(x => x.ParseVersionDetailsXml("<Dependencies><Source BarId=\"271018\" /></Dependencies>", true))
            .Returns(versionDetails2);

        var build1 = new Build(
            id: 270662,
            dateProduced: DateTimeOffset.Now.AddDays(-1),
            staleness: 0,
            released: false,
            stable: true,
            commit: "abc123",
            channels: [],
            assets:
            [
                new Asset(1, 270662, true, "Microsoft.DotNet.Arcade.Sdk", "10.0.0-beta.25304.106", null),
            ],
            dependencies: [],
            incoherencies: [])
        {
            GitHubRepository = "https://github.com/dotnet/dotnet"
        };

        var build2 = new Build(
            id: 271018,
            dateProduced: DateTimeOffset.Now,
            staleness: 0,
            released: false,
            stable: true,
            commit: "def456",
            channels: [],
            assets:
            [
                new Asset(2, 271018, true, "Microsoft.DotNet.Arcade.Sdk", "10.0.0-beta.25306.103", null),
            ],
            dependencies: [],
            incoherencies: [])
        {
            GitHubRepository = "https://github.com/dotnet/dotnet"
        };

        _barClient.Setup(x => x.GetBuildAsync(270662)).ReturnsAsync(build1);
        _barClient.Setup(x => x.GetBuildAsync(271018)).ReturnsAsync(build2);
    }
}
