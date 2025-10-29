// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
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
          <PropertyGroup>
            <FooVersion>$(FooPackageVersion)</FooVersion>
            <BarVersion>$(BarPackageVersion)</BarVersion>
          </PropertyGroup>
        </Project>
        """;

    private const string VersionPropsWithoutConflictingProperties = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project>
          <Import Project="Version.Details.props" />
          <PropertyGroup>
            <DifferentPackageVersion>2.0.0</DifferentPackageVersion>
            <AnotherPackageVersion>3.0.0</AnotherPackageVersion>
            <BarPackageVersion></BarPackageVersion>
            <FooPackageVersion Condition="some condition">1.0.0</FooPackageVersion>
          </PropertyGroup>
          <PropertyGroup Condition="some condition">
            <FooPackageVersion>1.0.0</FooPackageVersion>
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

    private const string VersionPropsWithConflictingPropertiesAndNoImport = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project>
          <PropertyGroup>
            <FooPackageVersion>2.0.0</FooPackageVersion>
            <DifferentPackageVersion>2.0.0</DifferentPackageVersion>
          </PropertyGroup>
          <PropertyGroup Label="More properties">
            <BarPackageVersion>3.0.0</BarPackageVersion>
          </PropertyGroup>
        </Project>
        """;

    private const string VersionDetailsXmlWithTwoDependencies = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dependencies>
          <ProductDependencies>
            <Dependency Name="Foo" Version="1.0.1">
              <Uri>https://github.com/test/foo</Uri>
              <Sha>abc123</Sha>
            </Dependency>
            <Dependency Name="Missing.Package" Version="2.0.0">
              <Uri>https://github.com/test/missing</Uri>
              <Sha>def456</Sha>
            </Dependency>
          </ProductDependencies>
        </Dependencies>
        """;

    private const string VersionDetailsXmlMatchingProperties = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dependencies>
          <ProductDependencies>
            <Dependency Name="Foo" Version="1.0.1">
              <Uri>https://github.com/test/foo</Uri>
              <Sha>abc123</Sha>
            </Dependency>
            <Dependency Name="Bar" Version="1.0.0">
              <Uri>https://github.com/test/bar</Uri>
              <Sha>def456</Sha>
            </Dependency>
            <Dependency Name="dependency" Version="1.0.0" SkipProperty="True">
              <Uri>https://github.com/test/bar</Uri>
              <Sha>def456</Sha>
            </Dependency>
          </ProductDependencies>
        </Dependencies>
        """;

    private const string VersionDetailsPropsWithOneMissingProperty = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project>
          <PropertyGroup>
            <!-- foo dependencies -->
            <FooPackageVersion>1.0.1</FooPackageVersion>
            <!-- MissingPackagePackageVersion is missing -->
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
                VersionFiles.VersionsProps,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionPropsWithoutConflictingProperties);

        _mockRemote.Setup(r => r.GetFileContentsAsync(
                VersionFiles.VersionDetailsXml,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionDetailsXmlMatchingProperties);

        // Act
        var result = await _policy.EvaluateAsync(_prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveSuccess);
        result.Title.Should().Be("Version.Details.props Validation Merge Policy: All validation checks passed");
        result.MergePolicyName.Should().Be("VersionDetailsProps");
        result.MergePolicyDisplayName.Should().Be("Version.Details.props Validation Merge Policy");
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
                VersionFiles.VersionsProps,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionPropsWithConflictingProperties);

        // Act
        var result = await _policy.EvaluateAsync(_prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveFailure);
        result.Title.Should().Be("#### ❌ Version.Details.props Validation Merge Policy: Validation Failed");
        result.Message.Trim().Should().Be("""
            Validation issues found with `Versions.props` file:

            **Conflicting Properties:** Properties from `Version.Details.props` should not be present in `Versions.props`.
            The following conflicting properties were found:
            - `FooPackageVersion`
            - `BarPackageVersion`

            **Action Required:**
            - Remove the conflicting properties from `Versions.props` to ensure proper separation of concerns between the two files.
            """);
        result.MergePolicyName.Should().Be("VersionDetailsProps");
        result.MergePolicyDisplayName.Should().Be("Version.Details.props Validation Merge Policy");
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
                VersionFiles.VersionsProps,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionPropsWithoutImport);

        // Act
        var result = await _policy.EvaluateAsync(_prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveFailure);
        result.Title.Should().Be("#### ❌ Version.Details.props Validation Merge Policy: Validation Failed");
        result.Message.Trim().Should().Be("""
            Validation issues found with `Versions.props` file:

            **Missing Import:** The `Versions.props` file is missing the required import statement for `Version.Details.props`.

            **Action Required:**
            - Add the following import statement at the beginning of your `Versions.props` file:
              ```xml
              <Import Project="Version.Details.props" Condition="Exists('Version.Details.props')" />
              ```
            """);
        result.MergePolicyName.Should().Be("VersionDetailsProps");
        result.MergePolicyDisplayName.Should().Be("Version.Details.props Validation Merge Policy");
    }

    [Test]
    public async Task EvaluateAsync_WhenDependencyMappingIncomplete_ShouldFail()
    {
        // Arrange
        _mockRemote.Setup(r => r.GetFileContentsAsync(
                VersionFiles.VersionDetailsProps,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionDetailsPropsWithOneMissingProperty);

        _mockRemote.Setup(r => r.GetFileContentsAsync(
                VersionFiles.VersionsProps,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionPropsWithoutConflictingProperties);

        _mockRemote.Setup(r => r.GetFileContentsAsync(
                VersionFiles.VersionDetailsXml,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionDetailsXmlWithTwoDependencies);

        // Act
        var result = await _policy.EvaluateAsync(_prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveFailure);
        result.Title.Should().Be("#### ❌ Version.Details.props Validation Merge Policy: Validation Failed");
        result.Message.Should().Contain("There is a mismatch between dependencies in `Version.Details.xml` and properties in `Version.Details.props`.");
        result.Message.Should().Contain("**Missing Properties:** The following dependencies are missing corresponding properties in `Version.Details.props`:");
        result.Message.Should().Contain("- Add `<MissingPackagePackageVersion>2.0.0</MissingPackagePackageVersion>`");
        result.MergePolicyName.Should().Be("VersionDetailsProps");
        result.MergePolicyDisplayName.Should().Be("Version.Details.props Validation Merge Policy");
    }

    [Test]
    public async Task EvaluateAsync_WhenBothConflictingPropertiesAndMissingImport_ShouldFail()
    {
        // Arrange
        _mockRemote.Setup(r => r.GetFileContentsAsync(
                VersionFiles.VersionDetailsProps,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionDetailsPropsWithProperties);

        _mockRemote.Setup(r => r.GetFileContentsAsync(
                VersionFiles.VersionsProps,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionPropsWithConflictingPropertiesAndNoImport);

        _mockRemote.Setup(r => r.GetFileContentsAsync(
                VersionFiles.VersionDetailsXml,
                _prSummary.TargetRepoUrl,
                _prSummary.HeadBranch))
            .ReturnsAsync(VersionDetailsXmlWithTwoDependencies);

        // Act
        var result = await _policy.EvaluateAsync(_prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveFailure);
        result.Title.Should().Be("#### ❌ Version.Details.props Validation Merge Policy: Validation Failed");
        result.Message.Trim().Should().Be("""
            Validation issues found with `Versions.props` file:

            **Conflicting Properties:** Properties from `Version.Details.props` should not be present in `Versions.props`.
            The following conflicting properties were found:
            - `FooPackageVersion`
            - `BarPackageVersion`

            **Missing Import:** The `Versions.props` file is missing the required import statement for `Version.Details.props`.

            **Action Required:**
            - Remove the conflicting properties from `Versions.props` to ensure proper separation of concerns between the two files.
            - Add the following import statement at the beginning of your `Versions.props` file:
              ```xml
              <Import Project="Version.Details.props" Condition="Exists('Version.Details.props')" />
              ```
            """);
        result.MergePolicyName.Should().Be("VersionDetailsProps");
        result.MergePolicyDisplayName.Should().Be("Version.Details.props Validation Merge Policy");
    }
}
