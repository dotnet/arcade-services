// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Moq;

namespace Maestro.MergePolicies.Tests;

[TestFixture]
public class VersionDetailsPropsMergePolicyTests
{
    private const string VersionDetailsPropsWithProperties = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project>
          <PropertyGroup>
            <!-- arcade dependencies -->
            <FooPackageVersion>1.0.1</FooPackageVersion>
            <!-- bar dependencies -->
            <BarPackageVersion>1.0.0</BarPackageVersion>
          </PropertyGroup>
        </Project>
        """;

    private const string VersionPropsWithoutConflictingProperties = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project>
          <Import Project="Version.Details.props" Condition="Exists('Version.Details.props')" />
          <PropertyGroup>
            <DifferentPackageVersion>2.0.0</DifferentPackageVersion>
            <AnotherPackageVersion>3.0.0</AnotherPackageVersion>
          </PropertyGroup>
        </Project>
        """;

    private const string VersionPropsWithConflictingProperties = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project>
          <Import Project="Version.Details.props" Condition="Exists('Version.Details.props')" />
          <PropertyGroup>
            <FooPackageVersion>2.0.0</FooPackageVersion>
            <DifferentPackageVersion>2.0.0</DifferentPackageVersion>
          </PropertyGroup>
          <PropertyGroup Label="More properties">
            <BarPackageVersion>3.0.0</BarPackageVersion>
          </PropertyGroup>
        </Project>
        """;

    private const string VersionPropsWithoutImport = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project>
          <PropertyGroup>
            <DifferentPackageVersion>2.0.0</DifferentPackageVersion>
            <AnotherPackageVersion>3.0.0</AnotherPackageVersion>
          </PropertyGroup>
        </Project>
        """;

    private Mock<IRemote> _mockRemote = null!;
    private VersionDetailsPropsMergePolicy _policy = null!;
    private PullRequestUpdateSummary _prSummary = null!;

    [SetUp]
    public void Setup()
    {
        _mockRemote = new Mock<IRemote>(MockBehavior.Strict);
        _policy = new VersionDetailsPropsMergePolicy();
        _prSummary = new PullRequestUpdateSummary(
            url: "https://github.com/test/repo/pull/123",
            coherencyCheckSuccessful: null,
            coherencyErrors: [],
            requiredUpdates: [],
            containedUpdates: [],
            headBranch: "test-branch",
            repoUrl: "https://github.com/test/repo",
            codeFlowDirection: CodeFlowDirection.BackFlow);
    }

    [Test]
    public async Task EvaluateAsync_WhenNoConflictingProperties_ShouldSucceed()
    {
        // Arrange
        _mockRemote.Setup(r => r.GetFileContentsAsync(
                VersionFiles.VersionDetailsProps,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionDetailsPropsWithProperties);

        _mockRemote.Setup(r => r.GetFileContentsAsync(
                VersionFiles.VersionProps,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionPropsWithoutConflictingProperties);

        // Act
        var result = await _policy.EvaluateAsync(_prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveSuccess);
        result.Title.Should().Be("No properties from VersionDetailsProps are present in VersionProps and required import statement is present");
        result.MergePolicyName.Should().Be("VersionDetailsProps");
        result.MergePolicyDisplayName.Should().Be("Version Details Properties Merge Policy");
    }

    [Test]
    public async Task EvaluateAsync_WhenConflictingPropertiesExist_ShouldFail()
    {
        // Arrange
        _mockRemote.Setup(r => r.GetFileContentsAsync(
                VersionFiles.VersionDetailsProps,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionDetailsPropsWithProperties);

        _mockRemote.Setup(r => r.GetFileContentsAsync(
                VersionFiles.VersionProps,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionPropsWithConflictingProperties);

        // Act
        var result = await _policy.EvaluateAsync(_prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveFailure);
        result.Title.Should().Be("### ❌ Version Details Properties Validation Failed\r\n\r\nProperties from `VersionDetailsProps` should not be present in `VersionProps`. The following conflicting properties were found:\r\n\r\n- `FooPackageVersion`\r\n- `BarPackageVersion`\r\n\r\n**Action Required:** Please remove these properties from `VersionProps` to ensure proper separation of concerns between the two files.\r\n");
        result.MergePolicyName.Should().Be("VersionDetailsProps");
        result.MergePolicyDisplayName.Should().Be("Version Details Properties Merge Policy");
    }

    [Test]
    public async Task EvaluateAsync_WhenImportStatementMissing_ShouldFail()
    {
        // Arrange
        _mockRemote.Setup(r => r.GetFileContentsAsync(
                VersionFiles.VersionDetailsProps,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionDetailsPropsWithProperties);

        _mockRemote.Setup(r => r.GetFileContentsAsync(
                VersionFiles.VersionProps,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionPropsWithoutImport);

        // Act
        var result = await _policy.EvaluateAsync(_prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveFailure);
        result.Title.Should().Contain("The `VersionProps` file is missing the required import statement for `Version.Details.props`");
        result.Title.Should().Contain("<Import Project=\"Version.Details.props\" Condition=\"Exists('Version.Details.props')\" />");
        result.MergePolicyName.Should().Be("VersionDetailsProps");
        result.MergePolicyDisplayName.Should().Be("Version Details Properties Merge Policy");
    }

    [Test]
    public async Task EvaluateAsync_WhenNotBackFlow_ShouldSucceed()
    {
        // Arrange
        var forwardFlowPr = new PullRequestUpdateSummary(
            url: "https://github.com/test/repo/pull/123",
            coherencyCheckSuccessful: null,
            coherencyErrors: [],
            requiredUpdates: [],
            containedUpdates: [],
            headBranch: "test-branch",
            repoUrl: "https://github.com/test/repo",
            codeFlowDirection: CodeFlowDirection.ForwardFlow);

        // Act
        var result = await _policy.EvaluateAsync(forwardFlowPr, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveSuccess);
        result.Title.Should().Be("Version Details Properties Merge Policy: Not a backflow PR");
        result.MergePolicyName.Should().Be("VersionDetailsProps");
        result.MergePolicyDisplayName.Should().Be("Version Details Properties Merge Policy");

        // Verify no file content calls were made
        _mockRemote.Verify(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
