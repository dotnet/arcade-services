// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Moq;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.Tests;

public class SourceDependencyTests
{
    /// <summary>
    /// Ensures GitHubRepository takes precedence when present.
    /// Inputs:
    ///  - Build with GitHubRepository and AzureDevOpsRepository set, a Commit, and an Id.
    ///  - Non-empty mapping value.
    /// Expected:
    ///  - SourceDependency.Uri equals GitHubRepository.
    ///  - SourceDependency.Mapping equals input mapping.
    ///  - SourceDependency.Sha equals Build.Commit.
    ///  - SourceDependency.BarId equals Build.Id.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_BuildWithGitHubRepo_UsesGitHubRepositoryForUri()
    {
        // Arrange
        var build = CreateBuild(id: 123, commit: "sha-gh");
        build.GitHubRepository = "https://github.com/org/repo";
        build.AzureDevOpsRepository = "https://dev.azure.com/org/project/_git/repo";
        var mapping = "src/* => eng/*";

        // Act
        var result = new SourceDependency(build, mapping);

        // Assert
        result.Uri.Should().Be("https://github.com/org/repo");
        result.Mapping.Should().Be(mapping);
        result.Sha.Should().Be("sha-gh");
        result.BarId.Should().Be(123);
    }

    /// <summary>
    /// Validates that when GitHubRepository is not provided, AzureDevOpsRepository is used.
    /// Inputs:
    ///  - Build with only AzureDevOpsRepository set, a Commit, and an Id.
    ///  - Non-empty mapping value.
    /// Expected:
    ///  - SourceDependency.Uri equals AzureDevOpsRepository.
    ///  - SourceDependency.Mapping equals input mapping.
    ///  - SourceDependency.Sha equals Build.Commit.
    ///  - SourceDependency.BarId equals Build.Id.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_BuildWithAzureDevOpsRepoWhenGitHubMissing_UsesAzureDevOpsRepositoryForUri()
    {
        // Arrange
        var build = CreateBuild(id: 456, commit: "sha-azdo");
        build.AzureDevOpsRepository = "https://dev.azure.com/org/project/_git/repo";
        var mapping = "a => b";

        // Act
        var result = new SourceDependency(build, mapping);

        // Assert
        result.Uri.Should().Be("https://dev.azure.com/org/project/_git/repo");
        result.Mapping.Should().Be(mapping);
        result.Sha.Should().Be("sha-azdo");
        result.BarId.Should().Be(456);
    }

    /// <summary>
    /// Confirms that when neither GitHubRepository nor AzureDevOpsRepository is provided,
    /// the resulting Uri can be null due to the underlying GetRepository() behavior.
    /// Inputs:
    ///  - Build with no repository properties set, a Commit, and an Id.
    ///  - Non-empty mapping value.
    /// Expected:
    ///  - SourceDependency.Uri is null.
    ///  - SourceDependency.Mapping equals input mapping.
    ///  - SourceDependency.Sha equals Build.Commit.
    ///  - SourceDependency.BarId equals Build.Id.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_NoRepository_AllowsNullUri()
    {
        // Arrange
        var build = CreateBuild(id: 789, commit: "sha-no-repo");
        var mapping = "m => n";

        // Act
        var result = new SourceDependency(build, mapping);

        // Assert
        result.Uri.Should().BeNull();
        result.Mapping.Should().Be(mapping);
        result.Sha.Should().Be("sha-no-repo");
        result.BarId.Should().Be(789);
    }

    /// <summary>
    /// Ensures that an empty GitHubRepository string is returned verbatim (no fallback),
    /// even if AzureDevOpsRepository is set.
    /// Inputs:
    ///  - Build with GitHubRepository set to empty string and AzureDevOpsRepository set to a value.
    ///  - Non-empty mapping value, a Commit, and an Id.
    /// Expected:
    ///  - SourceDependency.Uri equals string.Empty.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_GitHubRepositoryEmptyString_ReturnsEmptyUri()
    {
        // Arrange
        var build = CreateBuild(id: 99, commit: "sha-empty");
        build.GitHubRepository = string.Empty;
        build.AzureDevOpsRepository = "https://dev.azure.com/org/project/_git/repo";
        var mapping = "x => y";

        // Act
        var result = new SourceDependency(build, mapping);

        // Assert
        result.Uri.Should().Be(string.Empty);
        result.Mapping.Should().Be(mapping);
        result.Sha.Should().Be("sha-empty");
        result.BarId.Should().Be(99);
    }

    /// <summary>
    /// Verifies that extreme and boundary Build.Id values are passed through and assigned to BarId without modification.
    /// Inputs:
    ///  - Build.Id values: int.MinValue, -1, 0, 1, int.MaxValue.
    ///  - AzureDevOpsRepository present to provide a non-null Uri; a simple mapping and commit.
    /// Expected:
    ///  - SourceDependency.BarId equals the provided Build.Id.
    /// </summary>
    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(int.MaxValue)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_IdExtremes_MappedToBarId(int id)
    {
        // Arrange
        var build = CreateBuild(id: id, commit: "sha");
        build.AzureDevOpsRepository = "https://repo";
        var mapping = "map";

        // Act
        var result = new SourceDependency(build, mapping);

        // Assert
        result.BarId.Should().Be(id);
        result.Uri.Should().Be("https://repo");
        result.Mapping.Should().Be("map");
        result.Sha.Should().Be("sha");
    }

    /// <summary>
    /// Ensures that Mapping and Sha fields are assigned verbatim for varied string inputs, including empty,
    /// whitespace-only, long strings, and strings with special/control characters.
    /// Inputs:
    ///  - Tuple(mapping, commit) with representative edge cases.
    /// Expected:
    ///  - SourceDependency.Mapping equals mapping.
    ///  - SourceDependency.Sha equals commit.
    /// </summary>
    [TestCaseSource(nameof(MappingAndCommitCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_MappingAndCommitVariants_AssignedVerbatim(string mapping, string commit)
    {
        // Arrange
        var build = CreateBuild(id: 42, commit: commit);
        build.AzureDevOpsRepository = "https://repo";

        // Act
        var result = new SourceDependency(build, mapping);

        // Assert
        result.Mapping.Should().Be(mapping);
        result.Sha.Should().Be(commit);
        result.Uri.Should().Be("https://repo");
        result.BarId.Should().Be(42);
    }

    private static IEnumerable MappingAndCommitCases()
    {
        yield return new TestCaseData(string.Empty, string.Empty);
        yield return new TestCaseData("   ", "   ");
        yield return new TestCaseData(new string('a', 1000), new string('b', 256));
        yield return new TestCaseData(@"path/with\slashes?and&symbols=<>%$#@!" + "\u0000", "sha:*?<>|\"'\\\u0000");
    }

    private static Build CreateBuild(int id, string commit)
    {
        return new Build(
            id: id,
            dateProduced: DateTimeOffset.UtcNow,
            staleness: 0,
            released: false,
            stable: false,
            commit: commit,
            channels: new List<Channel>(),
            assets: new List<Asset>(),
            dependencies: new List<BuildRef>(),
            incoherencies: new List<BuildIncoherence>());
    }
}
