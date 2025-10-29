// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
public class BuildBranchLinkTests
{
    [Test]
    public void GetBranchLink_ShouldReturnGitHubLink_ForGitHubRepository()
    {
        // Arrange
        var build = new Build(1, DateTimeOffset.Now, 0, false, false, "abc123", [], [], [], [])
        {
            GitHubRepository = "https://github.com/dotnet/aspnetcore",
            GitHubBranch = "release/10.0-preview7"
        };

        // Act
        var result = build.GetBranchLink();

        // Assert
        result.Should().Be("https://github.com/dotnet/aspnetcore/tree/release/10.0-preview7");
    }

    [Test]
    public void GetBranchLink_ShouldReturnAzureDevOpsLink_ForAzureDevOpsRepository()
    {
        // Arrange
        var build = new Build(2, DateTimeOffset.Now, 0, false, false, "def456", [], [], [], [])
        {
            AzureDevOpsRepository = "https://dev.azure.com/dnceng/internal/_git/dotnet-aspnetcore",
            AzureDevOpsBranch = "release/10.0-preview7"
        };

        // Act
        var result = build.GetBranchLink();

        // Assert
        result.Should().Be("https://dev.azure.com/dnceng/internal/_git/dotnet-aspnetcore?version=GBrelease/10.0-preview7");
    }

    [Test]
    public void GetBranchLink_ShouldReturnBranchLink_WhenNoBranchIsSet()
    {
        // Arrange
        var build = new Build(3, DateTimeOffset.Now, 0, false, false, "ghi789", [], [], [], [])
        {
            GitHubRepository = "https://github.com/dotnet/aspnetcore"
            // No branch set - will use null
        };

        // Act
        var result = build.GetBranchLink();

        // Assert - GitRepoUrlUtils handles null branch gracefully
        result.Should().Be("https://github.com/dotnet/aspnetcore/tree/");
    }

    [Test]
    public void GetBranchLink_ShouldThrowException_WhenNoRepositoryIsSet()
    {
        // Arrange
        var build = new Build(4, DateTimeOffset.Now, 0, false, false, "jkl012", [], [], [], [])
        {
            GitHubBranch = "main"
            // No repository set
        };

        // Act & Assert
        build.Invoking(b => b.GetBranchLink())
            .Should().Throw<ArgumentException>()
            .WithMessage("Unknown git repository type (Parameter 'repoUri')");
    }
}
