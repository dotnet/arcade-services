// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.Helpers;

[TestFixture]
public class DependencyFileManagerPinnedSdkTests
{
    private const string VersionDetails = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dependencies>
          <ProductDependencies>
          </ProductDependencies>
          <ToolsetDependencies>
          </ToolsetDependencies>
        </Dependencies>
        """;

    private const string VersionProps = """
        <Project>
          <PropertyGroup>
          </PropertyGroup>
        </Project>
        """;

    private const string NuGetConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <add key="dotnet-public" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json" />
          </packageSources>
        </configuration>
        """;

    private static void SetupCommonMocks(Mock<IGitRepo> repo, string globalJsonContent)
    {
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionDetails);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionsProps, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionProps);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.GlobalJson, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(globalJsonContent);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.DotnetToolsConfigJson, It.IsAny<string>(), It.IsAny<string>()))
            .Throws<DependencyFileNotFoundException>();
        
        // Mock NuGet.config files - provide the first one and let others throw exceptions
        bool firstConfig = true;
        foreach (var nugetConfigName in VersionFiles.NugetConfigNames)
        {
            if (firstConfig)
            {
                repo.Setup(r => r.GetFileContentsAsync(nugetConfigName, It.IsAny<string>(), It.IsAny<string>()))
                    .ReturnsAsync(NuGetConfig);
                firstConfig = false;
            }
            else
            {
                repo.Setup(r => r.GetFileContentsAsync(nugetConfigName, It.IsAny<string>(), It.IsAny<string>()))
                    .Throws<DependencyFileNotFoundException>();
            }
        }
    }

    [Test]
    public async Task UpdateDependencyFiles_WithPinnedSdk_ShouldSkipUpdate()
    {
        // Arrange
        var globalJsonContent = """
            {
              "sdk": {
                "version": "8.0.100",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.100",
                "pinned": true
              }
            }
            """;

        var incomingVersion = SemanticVersion.Parse("9.0.100");

        Mock<IGitRepo> repo = new();
        Mock<IGitRepoFactory> repoFactory = new();

        SetupCommonMocks(repo, globalJsonContent);
        repoFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        var manager = new DependencyFileManager(repoFactory.Object, new VersionDetailsParser(), NullLogger.Instance);

        // Act
        var result = await manager.UpdateDependencyFiles(
            new List<DependencyDetail>(),
            null,
            "https://github.com/test/repo",
            "main",
            new List<DependencyDetail>(),
            incomingVersion);

        // Assert
        var updatedGlobalJson = JObject.Parse(result.GlobalJson.Content);
        
        // Version should not have changed
        updatedGlobalJson["tools"]["dotnet"].Value<string>().Should().Be("8.0.100");
        updatedGlobalJson["sdk"]["version"].Value<string>().Should().Be("8.0.100");
        
        // Pinned should still be there
        updatedGlobalJson["tools"]["pinned"].Value<bool>().Should().BeTrue();
        
        // No metadata should indicate update
        result.GlobalJson.Metadata.Should().BeNull();
    }

    [Test]
    public async Task UpdateDependencyFiles_WithPinnedFalse_ShouldUpdate()
    {
        // Arrange
        var globalJsonContent = """
            {
              "sdk": {
                "version": "8.0.100",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.100",
                "pinned": false
              }
            }
            """;

        var incomingVersion = SemanticVersion.Parse("9.0.100");

        Mock<IGitRepo> repo = new();
        Mock<IGitRepoFactory> repoFactory = new();

        SetupCommonMocks(repo, globalJsonContent);
        repoFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        var manager = new DependencyFileManager(repoFactory.Object, new VersionDetailsParser(), NullLogger.Instance);

        // Act
        var result = await manager.UpdateDependencyFiles(
            new List<DependencyDetail>(),
            null,
            "https://github.com/test/repo",
            "main",
            new List<DependencyDetail>(),
            incomingVersion);

        // Assert
        var updatedGlobalJson = JObject.Parse(result.GlobalJson.Content);
        
        // Version should have changed
        updatedGlobalJson["tools"]["dotnet"].Value<string>().Should().Be("9.0.100");
        updatedGlobalJson["sdk"]["version"].Value<string>().Should().Be("9.0.100");
        
        // Pinned should still be there
        updatedGlobalJson["tools"]["pinned"].Value<bool>().Should().BeFalse();
        
        // Metadata should indicate update
        result.GlobalJson.Metadata.Should().NotBeNull();
        result.GlobalJson.Metadata.Should().ContainKey(GitFileMetadataName.ToolsDotNetUpdate);
        result.GlobalJson.Metadata.Should().ContainKey(GitFileMetadataName.SdkVersionUpdate);
    }

    [Test]
    public async Task UpdateDependencyFiles_WithoutPinnedProperty_ShouldUpdate()
    {
        // Arrange
        var globalJsonContent = """
            {
              "sdk": {
                "version": "8.0.100",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.100"
              }
            }
            """;

        var incomingVersion = SemanticVersion.Parse("9.0.100");

        Mock<IGitRepo> repo = new();
        Mock<IGitRepoFactory> repoFactory = new();

        SetupCommonMocks(repo, globalJsonContent);
        repoFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        var manager = new DependencyFileManager(repoFactory.Object, new VersionDetailsParser(), NullLogger.Instance);

        // Act
        var result = await manager.UpdateDependencyFiles(
            new List<DependencyDetail>(),
            null,
            "https://github.com/test/repo",
            "main",
            new List<DependencyDetail>(),
            incomingVersion);

        // Assert
        var updatedGlobalJson = JObject.Parse(result.GlobalJson.Content);
        
        // Version should have changed
        updatedGlobalJson["tools"]["dotnet"].Value<string>().Should().Be("9.0.100");
        updatedGlobalJson["sdk"]["version"].Value<string>().Should().Be("9.0.100");
        
        // Metadata should indicate update
        result.GlobalJson.Metadata.Should().NotBeNull();
        result.GlobalJson.Metadata.Should().ContainKey(GitFileMetadataName.ToolsDotNetUpdate);
        result.GlobalJson.Metadata.Should().ContainKey(GitFileMetadataName.SdkVersionUpdate);
    }
}
