// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests;

[TestFixture]
public class DependencyFileManagerPinnedSdkTests
{
    private DependencyFileManager _dependencyFileManager;

    [SetUp]
    public void Setup()
    {
        _dependencyFileManager = new DependencyFileManager((IGitRepo)null, new VersionDetailsParser(), NullLogger.Instance);
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

        var globalJson = JObject.Parse(globalJsonContent);
        var incomingVersion = SemanticVersion.Parse("9.0.100");

        // Create a mock git repo that returns our test files
        var mockGitRepo = new MockGitRepo();
        mockGitRepo.AddFile("eng/Version.Details.xml", CreateVersionDetailsXml());
        mockGitRepo.AddFile("eng/Versions.props", CreateVersionProps());
        mockGitRepo.AddFile("global.json", globalJsonContent);
        mockGitRepo.AddFile("NuGet.config", CreateNuGetConfig());

        var testFileManager = new DependencyFileManager(mockGitRepo, new VersionDetailsParser(), NullLogger.Instance);

        // Act
        var result = await testFileManager.UpdateDependencyFiles(
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

        var globalJson = JObject.Parse(globalJsonContent);
        var incomingVersion = SemanticVersion.Parse("9.0.100");

        // Create a mock git repo that returns our test files
        var mockGitRepo = new MockGitRepo();
        mockGitRepo.AddFile("eng/Version.Details.xml", CreateVersionDetailsXml());
        mockGitRepo.AddFile("eng/Versions.props", CreateVersionProps());
        mockGitRepo.AddFile("global.json", globalJsonContent);
        mockGitRepo.AddFile("NuGet.config", CreateNuGetConfig());

        var testFileManager = new DependencyFileManager(mockGitRepo, new VersionDetailsParser(), NullLogger.Instance);

        // Act
        var result = await testFileManager.UpdateDependencyFiles(
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

        var globalJson = JObject.Parse(globalJsonContent);
        var incomingVersion = SemanticVersion.Parse("9.0.100");

        // Create a mock git repo that returns our test files
        var mockGitRepo = new MockGitRepo();
        mockGitRepo.AddFile("eng/Version.Details.xml", CreateVersionDetailsXml());
        mockGitRepo.AddFile("eng/Versions.props", CreateVersionProps());
        mockGitRepo.AddFile("global.json", globalJsonContent);
        mockGitRepo.AddFile("NuGet.config", CreateNuGetConfig());

        var testFileManager = new DependencyFileManager(mockGitRepo, new VersionDetailsParser(), NullLogger.Instance);

        // Act
        var result = await testFileManager.UpdateDependencyFiles(
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

    [Test]
    public async Task UpdateDependencyFiles_WithPinnedAsString_ShouldNotUpdate()
    {
        // Arrange - test edge case where pinned is a string "true"
        var globalJsonContent = """
            {
              "sdk": {
                "version": "8.0.100",
                "rollForward": "minor"
              },
              "tools": {
                "dotnet": "8.0.100",
                "pinned": "true"
              }
            }
            """;

        var globalJson = JObject.Parse(globalJsonContent);
        var incomingVersion = SemanticVersion.Parse("9.0.100");

        // Create a mock git repo that returns our test files
        var mockGitRepo = new MockGitRepo();
        mockGitRepo.AddFile("eng/Version.Details.xml", CreateVersionDetailsXml());
        mockGitRepo.AddFile("eng/Versions.props", CreateVersionProps());
        mockGitRepo.AddFile("global.json", globalJsonContent);
        mockGitRepo.AddFile("NuGet.config", CreateNuGetConfig());

        var testFileManager = new DependencyFileManager(mockGitRepo, new VersionDetailsParser(), NullLogger.Instance);

        // Act
        var result = await testFileManager.UpdateDependencyFiles(
            new List<DependencyDetail>(),
            null,
            "https://github.com/test/repo",
            "main",
            new List<DependencyDetail>(),
            incomingVersion);

        // Assert
        var updatedGlobalJson = JObject.Parse(result.GlobalJson.Content);
        
        // Version should have changed because "true" string is not a boolean true
        updatedGlobalJson["tools"]["dotnet"].Value<string>().Should().Be("9.0.100");
        updatedGlobalJson["sdk"]["version"].Value<string>().Should().Be("9.0.100");
        
        // Metadata should indicate update
        result.GlobalJson.Metadata.Should().NotBeNull();
        result.GlobalJson.Metadata.Should().ContainKey(GitFileMetadataName.ToolsDotNetUpdate);
        result.GlobalJson.Metadata.Should().ContainKey(GitFileMetadataName.SdkVersionUpdate);
    }

    [Test]
    public async Task UpdateDependencyFiles_WithForceUpdate_ShouldUpdateDespitePinned()
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

        var globalJson = JObject.Parse(globalJsonContent);
        var incomingVersion = SemanticVersion.Parse("9.0.100");

        // Create a mock git repo that returns our test files
        var mockGitRepo = new MockGitRepo();
        mockGitRepo.AddFile("eng/Version.Details.xml", CreateVersionDetailsXml());
        mockGitRepo.AddFile("eng/Versions.props", CreateVersionProps());
        mockGitRepo.AddFile("global.json", globalJsonContent);
        mockGitRepo.AddFile("NuGet.config", CreateNuGetConfig());

        var testFileManager = new DependencyFileManager(mockGitRepo, new VersionDetailsParser(), NullLogger.Instance);

        // Act
        var result = await testFileManager.UpdateDependencyFiles(
            new List<DependencyDetail>(),
            null,
            "https://github.com/test/repo",
            "main",
            new List<DependencyDetail>(),
            incomingVersion,
            forceGlobalJsonUpdate: true);

        // Assert
        var updatedGlobalJson = JObject.Parse(result.GlobalJson.Content);
        
        // Version should not have changed because pinned=true takes precedence over forceUpdate
        updatedGlobalJson["tools"]["dotnet"].Value<string>().Should().Be("8.0.100");
        updatedGlobalJson["sdk"]["version"].Value<string>().Should().Be("8.0.100");
        
        // No metadata should indicate update
        result.GlobalJson.Metadata.Should().BeNull();
    }

    private static string CreateVersionDetailsXml()
    {
        return """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
              </ProductDependencies>
              <ToolsetDependencies>
              </ToolsetDependencies>
            </Dependencies>
            """;
    }

    private static string CreateVersionProps()
    {
        return """
            <Project>
              <PropertyGroup>
              </PropertyGroup>
            </Project>
            """;
    }

    private static string CreateNuGetConfig()
    {
        return """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="dotnet-public" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json" />
              </packageSources>
            </configuration>
            """;
    }
}