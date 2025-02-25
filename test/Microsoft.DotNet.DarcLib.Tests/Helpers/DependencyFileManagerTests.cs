// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.Services.Profile;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.Helpers;

[TestFixture]
public class DependencyFileManagerTests
{
    private const string VersionDetails = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dependencies>
          <!-- Elements contains all product dependencies -->
          <ProductDependencies>
            <Dependency Name="Foo" Version="1.0.0">
              <Uri>https://github.com/dotnet/foo</Uri>
              <Sha>sha1</Sha>
            </Dependency>
            <Dependency Name="Bar" Version="1.0.0">
              <Uri>https://github.com/dotnet/bar</Uri>
              <Sha>sha1</Sha>
            </Dependency>
          </ProductDependencies>
          <ToolsetDependencies>
          </ToolsetDependencies>
        </Dependencies>
        """;

    private const string VersionProps = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project>
          <PropertyGroup>
          </PropertyGroup>
          <!--Package versions-->
          <PropertyGroup>
            <FooPackageVersion>1.0.0</FooPackageVersion>
            <BarPackageVersion>1.0.0</BarPackageVersion>
          </PropertyGroup>
        </Project>
        """;

    private const string DotnetTools = """
        {
          "version": 1,
          "isRoot": true,
          "tools": {
            "microsoft.dnceng.secretmanager": {
              "version": "1.1.0-beta.25071.2",
              "commands": [
                "secret-manager"
              ]
            },
            "foo": {
              "version": "8.0.0",
              "commands": [
                "foo"
              ]
            },
            "microsoft.dnceng.configuration.bootstrap": {
              "version": "1.1.0-beta.25071.2",
              "commands": [
                "bootstrap-dnceng-configuration"
              ]
            }
          }
        }
        """;

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public async Task RemoveDependencyShouldRemoveDependency(bool dotnetToolsExists)
    {
        var expectedVersionDetails = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dependencies>
          <!-- Elements contains all product dependencies -->
          <ProductDependencies>
            <Dependency Name="Bar" Version="1.0.0">
              <Uri>https://github.com/dotnet/bar</Uri>
              <Sha>sha1</Sha>
            </Dependency>
          </ProductDependencies>
          <ToolsetDependencies>
          </ToolsetDependencies>
        </Dependencies>
        """;
        var expectedVersionProps = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project>
              <PropertyGroup>
              </PropertyGroup>
              <!--Package versions-->
              <PropertyGroup>
                <BarPackageVersion>1.0.0</BarPackageVersion>
              </PropertyGroup>
            </Project>
            """;
        var expectedDotNetTools = """
            {
              "version": 1,
              "isRoot": true,
              "tools": {
                "microsoft.dnceng.secretmanager": {
                  "version": "1.1.0-beta.25071.2",
                  "commands": [
                    "secret-manager"
                  ]
                },
                "microsoft.dnceng.configuration.bootstrap": {
                  "version": "1.1.0-beta.25071.2",
                  "commands": [
                    "bootstrap-dnceng-configuration"
                  ]
                }
              }
            }
            """;

        var tmpVersionDetailsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tmpVersionPropsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tmpDotnetToolsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        DependencyDetail dependency = new()
        {
            Name = "Foo"
        };

        Mock<IGitRepo> repo = new();
        Mock<IGitRepoFactory> repoFactory = new();

        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionDetails);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionProps, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionProps);
        if (!dotnetToolsExists)
        {
            repo.Setup(r => r.GetFileContentsAsync(VersionFiles.DotnetToolsConfigJson, It.IsAny<string>(), It.IsAny<string>()))
                .Throws<DependencyFileNotFoundException>();
        }
        else
        {
            repo.Setup(r => r.GetFileContentsAsync(VersionFiles.DotnetToolsConfigJson, It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(DotnetTools);
        }

        repo.Setup(r => r.CommitFilesAsync(
            It.Is<List<GitFile>>(files =>
                files.Count == (dotnetToolsExists ? 3 : 2) &&
                files.Any(f => f.FilePath == VersionFiles.VersionDetailsXml) && files.Any(f => f.FilePath == VersionFiles.VersionProps)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()))
            .Callback<List<GitFile>, string, string, string>((files, repoUri, branch, commitMessage) =>
            {
                File.WriteAllText(tmpVersionDetailsPath, files[0].Content);
                File.WriteAllText(tmpVersionPropsPath, files[1].Content);
                if (dotnetToolsExists)
                {
                    File.WriteAllText(tmpDotnetToolsPath, files[2].Content);
                }
            });

        repoFactory.Setup(repoFactory => repoFactory.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        DependencyFileManager manager = new(
            repoFactory.Object,
            new VersionDetailsParser(),
            NullLogger.Instance);

        try
        {
            await manager.RemoveDependencyAsync(dependency.Name, string.Empty, string.Empty);

            File.ReadAllText(tmpVersionDetailsPath).Replace("\r\n", "\n").TrimEnd().Should()
                .Be(expectedVersionDetails.Replace("\r\n", "\n").TrimEnd());
            File.ReadAllText(tmpVersionPropsPath).Replace("\r\n", "\n").TrimEnd().Should()
                .Be(expectedVersionProps.Replace("\r\n", "\n").TrimEnd());
            if (dotnetToolsExists)
            {
                File.ReadAllText(tmpDotnetToolsPath).Replace("\r\n", "\n").TrimEnd().Should()
                    .Be(expectedDotNetTools.Replace("\r\n", "\n").TrimEnd());
            }
        }
        finally
        {
            if (File.Exists(tmpVersionDetailsPath))
            {
                File.Delete(tmpVersionDetailsPath);
            }
            if (File.Exists(tmpVersionPropsPath))
            {
                File.Delete(tmpVersionPropsPath);
            }
            if (File.Exists(tmpDotnetToolsPath))
            {
                File.Delete(tmpDotnetToolsPath);
            }
        }
    }

    [Test]
    public async Task RemoveDependencyShouldNotThrowWhenDependencyDoesNotExist()
    {
        DependencyDetail dependency = new()
        {
            Name = "gaa"
        };

        Mock<IGitRepo> repo = new();
        Mock<IGitRepoFactory> repoFactory = new();

        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionDetails);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionProps, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionProps);
        repoFactory.Setup(repoFactory => repoFactory.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        DependencyFileManager manager = new(
            repoFactory.Object,
            new VersionDetailsParser(),
            NullLogger.Instance);

        Func<Task> act = async () => await manager.RemoveDependencyAsync(dependency.Name, string.Empty, string.Empty);
        await act.Should().NotThrowAsync<DependencyException>();
    }
}
