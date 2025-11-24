// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging.Abstractions;
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

    private const string VersionDetailsProps = """
        <Project>
        </Project>
        """;

    private const string VersionProps = """
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
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionsProps, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionProps);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsProps, It.IsAny<string>(), It.IsAny<string>()))
            .Throws<DependencyFileNotFoundException>();
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
                files.Any(f => f.FilePath == VersionFiles.VersionDetailsXml) && files.Any(f => f.FilePath == VersionFiles.VersionsProps)),
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

            NormalizeLineEndings(File.ReadAllText(tmpVersionDetailsPath)).Should()
                .Be(NormalizeLineEndings(expectedVersionDetails ));
            NormalizeLineEndings(File.ReadAllText(tmpVersionPropsPath)).Should()
                .Be(NormalizeLineEndings(expectedVersionProps));
            if (dotnetToolsExists)
            {
                NormalizeLineEndings(File.ReadAllText(tmpDotnetToolsPath)).Should()
                    .Be(NormalizeLineEndings(expectedDotNetTools));
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
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionsProps, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionProps);
        repoFactory.Setup(repoFactory => repoFactory.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        DependencyFileManager manager = new(
            repoFactory.Object,
            new VersionDetailsParser(),
            NullLogger.Instance);

        Func<Task> act = async () => await manager.RemoveDependencyAsync(dependency.Name, string.Empty, string.Empty);
        await act.Should().NotThrowAsync<DependencyException>();
    }

    [Test]
    public async Task AddDependencyShouldMatchAlternatePropNames()
    {
        string versionDetails =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
              </ProductDependencies>
              <ToolsetDependencies>
                <Dependency Name="Microsoft.DotNet.Build.Tasks.Packaging" Version="10.0.0-beta.25217.1">
                  <Uri>https://github.com/dotnet/arcade</Uri>
                  <Sha>76dd1b4eb3b15881da350de93805ea6ab936364c</Sha>
                </Dependency>
                <Dependency Name="Microsoft.DotNet.Build.Tasks.Installers" Version="10.0.0-beta.25217.1">
                  <Uri>https://github.com/dotnet/arcade</Uri>
                  <Sha>76dd1b4eb3b15881da350de93805ea6ab936364c</Sha>
                </Dependency>
              </ToolsetDependencies>
            </Dependencies>
            """;

        string versionProps =
            """
            <Project ToolsVersion="4.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <MicrosoftDotNetBuildTasksPackagingVersion>10.0.0-beta.25217.1</MicrosoftDotNetBuildTasksPackagingVersion>
                <MicrosoftDotNetBuildTasksInstallersPackageVersion>10.0.0-beta.25217.1</MicrosoftDotNetBuildTasksInstallersPackageVersion>
              </PropertyGroup>
            </Project>
            """;

        Mock<IGitRepo> repo = new();
        Mock<IGitRepoFactory> repoFactory = new();

        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => versionDetails);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionsProps, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => versionProps);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsProps, It.IsAny<string>(), It.IsAny<string>()))
            .Throws<DependencyFileNotFoundException>();
        repoFactory
            .Setup(repoFactory => repoFactory.CreateClient(It.IsAny<string>()))
            .Returns(repo.Object);

        repo.Setup(r => r.CommitFilesAsync(
            It.IsAny<List<GitFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()))
            .Callback<List<GitFile>, string, string, string>((files, repoUri, branch, commitMessage) =>
            {
                foreach(var file in files)
                {
                    if (file.FilePath == VersionFiles.VersionDetailsXml)
                    {
                        versionDetails = file.Content;
                    }
                    else if (file.FilePath == VersionFiles.VersionsProps)
                    {
                        versionProps = file.Content;
                    }
                }
            });

        DependencyFileManager manager = new(
            repoFactory.Object,
            new VersionDetailsParser(),
            NullLogger.Instance);

        await manager.AddDependencyAsync(
            new DependencyDetail()
            {
                Name = "Microsoft.DotNet.Build.Tasks.Packaging",
                Version = "10.0.0-beta.22222.3",
                Commit = "abc",
                Type = DependencyType.Toolset,
                RepoUri = "https://github.com/dotnet/arcade",
            },
            string.Empty,
            string.Empty);

        await manager.AddDependencyAsync(
            new DependencyDetail()
            {
                Name = "Microsoft.DotNet.Build.Tasks.Installers",
                Version = "10.0.0-beta.33333.1",
                Commit = "def",
                Type = DependencyType.Toolset,
                RepoUri = "https://github.com/dotnet/arcade",
            },
            string.Empty,
            string.Empty);

        string expectedVersionDetails =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
              </ProductDependencies>
              <ToolsetDependencies>
                <Dependency Name="Microsoft.DotNet.Build.Tasks.Packaging" Version="10.0.0-beta.22222.3">
                  <Uri>https://github.com/dotnet/arcade</Uri>
                  <Sha>abc</Sha>
                </Dependency>
                <Dependency Name="Microsoft.DotNet.Build.Tasks.Installers" Version="10.0.0-beta.33333.1">
                  <Uri>https://github.com/dotnet/arcade</Uri>
                  <Sha>def</Sha>
                </Dependency>
              </ToolsetDependencies>
            </Dependencies>
            """;

        string expectedVersionProps =
            """
            <Project ToolsVersion="4.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <MicrosoftDotNetBuildTasksPackagingVersion>10.0.0-beta.22222.3</MicrosoftDotNetBuildTasksPackagingVersion>
                <MicrosoftDotNetBuildTasksInstallersPackageVersion>10.0.0-beta.33333.1</MicrosoftDotNetBuildTasksInstallersPackageVersion>
              </PropertyGroup>
            </Project>
            """;

        NormalizeLineEndings(versionDetails).Should().Be(NormalizeLineEndings(expectedVersionDetails));
        NormalizeLineEndings(versionProps).Should().Be(NormalizeLineEndings(expectedVersionProps));
    }

    [Test]
    public void GetXmlDocumentHandlesBomCharacters()
    {
        // Create XML content without BOM
        const string xmlWithoutBom =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <Dependency Name="TestPackage" Version="1.0.0">
                  <Uri>https://github.com/test/test</Uri>
                  <Sha>abc123</Sha>
                </Dependency>
              </ProductDependencies>
            </Dependencies>
            """;

        string xmlWithBom = "∩╗┐" + xmlWithoutBom;
        var f = () => DependencyFileManager.GetXmlDocument(xmlWithBom);
        f.Should().NotThrow<Exception>();
    }

    [Test]
    public async Task AddDependencyShouldAddToVersionDetailsPropsWhenItExists()
    {
        Mock<IGitRepo> repo = new();
        Mock<IGitRepoFactory> repoFactory = new();

        var versionDetails = VersionDetails;
        var versionProps = VersionProps;
        var versionDetailsProps = VersionDetailsProps;

        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => versionDetails);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionsProps, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => versionProps);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsProps, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => versionDetailsProps);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.DotnetToolsConfigJson, It.IsAny<string>(), It.IsAny<string>()))
                .Throws<DependencyFileNotFoundException>();
        repoFactory.Setup(repoFactory => repoFactory.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        repo.Setup(r => r.CommitFilesAsync(
            It.IsAny<List<GitFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()))
            .Callback<List<GitFile>, string, string, string>((files, repoUri, branch, commitMessage) =>
            {
                foreach (var file in files)
                {
                    if (file.FilePath == VersionFiles.VersionDetailsXml)
                    {
                        versionDetails = file.Content;
                    }
                    else if (file.FilePath == VersionFiles.VersionsProps)
                    {
                        versionProps = file.Content;
                    }
                    else if (file.FilePath == VersionFiles.VersionDetailsProps)
                    {
                        versionDetailsProps = file.Content;
                    }
                }
            });

        DependencyFileManager manager = new(
            repoFactory.Object,
            new VersionDetailsParser(),
            NullLogger.Instance);

        await manager.AddDependencyAsync(
            new DependencyDetail()
            {
                Name = "Foo",
                Version = "1.0.1",
                Commit = "abc123",
                Type = DependencyType.Product,
                RepoUri = "https://github.com/dotnet/arcade"
            },
            string.Empty,
            string.Empty
        );

        var expectedVersionDetails = """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <!-- Elements contains all product dependencies -->
              <ProductDependencies>
                <Dependency Name="Foo" Version="1.0.1">
                  <Uri>https://github.com/dotnet/arcade</Uri>
                  <Sha>abc123</Sha>
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

        var expectedVersionDetailsProps = """
            <!--
            This file is auto-generated by the Maestro dependency flow system.
            Do not edit it manually, as it will get overwritten by automation.
            This file should be imported by eng/Versions.props
            -->
            <Project>
              <PropertyGroup>
                <!-- dotnet/arcade dependencies -->
                <FooPackageVersion>1.0.1</FooPackageVersion>
                <!-- dotnet/bar dependencies -->
                <BarPackageVersion>1.0.0</BarPackageVersion>
              </PropertyGroup>
              <!--Property group for alternate package version names-->
              <PropertyGroup>
                <!-- dotnet/arcade dependencies -->
                <FooVersion>$(FooPackageVersion)</FooVersion>
                <!-- dotnet/bar dependencies -->
                <BarVersion>$(BarPackageVersion)</BarVersion>
              </PropertyGroup>
            </Project>
            """;

        NormalizeLineEndings(expectedVersionDetails).Should()
            .Be(NormalizeLineEndings(versionDetails));
        NormalizeLineEndings(expectedVersionDetailsProps).Should()
            .Be(NormalizeLineEndings(versionDetailsProps));
        // VersionProps should not change
        NormalizeLineEndings(versionProps).Should()
            .Be(NormalizeLineEndings(VersionProps));

        await manager.RemoveDependencyAsync("Bar", string.Empty, string.Empty);

        expectedVersionDetails = """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <!-- Elements contains all product dependencies -->
              <ProductDependencies>
                <Dependency Name="Foo" Version="1.0.1">
                  <Uri>https://github.com/dotnet/arcade</Uri>
                  <Sha>abc123</Sha>
                </Dependency>
              </ProductDependencies>
              <ToolsetDependencies>
              </ToolsetDependencies>
            </Dependencies>
            """;

        expectedVersionDetailsProps = """
            <!--
            This file is auto-generated by the Maestro dependency flow system.
            Do not edit it manually, as it will get overwritten by automation.
            This file should be imported by eng/Versions.props
            -->
            <Project>
              <PropertyGroup>
                <!-- dotnet/arcade dependencies -->
                <FooPackageVersion>1.0.1</FooPackageVersion>
              </PropertyGroup>
              <!--Property group for alternate package version names-->
              <PropertyGroup>
                <!-- dotnet/arcade dependencies -->
                <FooVersion>$(FooPackageVersion)</FooVersion>
              </PropertyGroup>
            </Project>
            """;

        NormalizeLineEndings(expectedVersionDetails).Should()
            .Be(NormalizeLineEndings(versionDetails));
        NormalizeLineEndings(expectedVersionDetailsProps).Should()
            .Be(NormalizeLineEndings(versionDetailsProps));
        // VersionProps should not change
        NormalizeLineEndings(versionProps).Should()
            .Be(NormalizeLineEndings(VersionProps));

        // now add a dependency with `SkipProperty = true`
        await manager.AddDependencyAsync(
            new DependencyDetail()
            {
                Name = "Bar",
                Version = "1.0.1",
                Commit = "abc123",
                Type = DependencyType.Product,
                RepoUri = "https://github.com/dotnet/other",
                SkipProperty = true
            },
            string.Empty,
            string.Empty);

        expectedVersionDetails = """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <!-- Elements contains all product dependencies -->
              <ProductDependencies>
                <Dependency Name="Foo" Version="1.0.1">
                  <Uri>https://github.com/dotnet/arcade</Uri>
                  <Sha>abc123</Sha>
                </Dependency>
                <Dependency Name="Bar" Version="1.0.1" SkipProperty="True">
                  <Uri>https://github.com/dotnet/other</Uri>
                  <Sha>abc123</Sha>
                </Dependency>
              </ProductDependencies>
              <ToolsetDependencies>
              </ToolsetDependencies>
            </Dependencies>
            """;

        NormalizeLineEndings(expectedVersionDetails).Should()
            .Be(NormalizeLineEndings(versionDetails));
        NormalizeLineEndings(expectedVersionDetailsProps).Should()
            .Be(NormalizeLineEndings(versionDetailsProps));
        // VersionProps should not change
        NormalizeLineEndings(versionProps).Should()
            .Be(NormalizeLineEndings(VersionProps));
    }

    [Test]
    public async Task AddDependencyAddsOnlyVersionDetailsProps()
    {
        Mock<IGitRepo> repo = new();
        Mock<IGitRepoFactory> repoFactory = new();

        var versionDetails = VersionDetails;

        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => versionDetails);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsProps, It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new DependencyFileNotFoundException());
        repoFactory.Setup(repoFactory => repoFactory.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        DependencyFileManager manager = new(
            repoFactory.Object,
            new VersionDetailsParser(),
            NullLogger.Instance);

        await manager.AddDependencyAsync(
            new DependencyDetail()
            {
                Name = "Foo",
                Version = "1.0.1",
                Commit = "abc123",
                Type = DependencyType.Product,
                RepoUri = "https://github.com/dotnet/arcade"
            },
            string.Empty,
            string.Empty,
            versionDetailsOnly: true
        );

        repo.Verify(r => r.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Any(f => f.FilePath == VersionFiles.VersionDetailsXml)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()),
            Times.Once);
        repo.Verify(r => r.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Any(f => f.FilePath == VersionFiles.VersionsProps)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()),
            Times.Never);
        repo.Verify(r => r.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Any(f => f.FilePath == VersionFiles.GlobalJson)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()),
            Times.Never);
        repo.Verify(r => r.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Any(f => f.FilePath == VersionFiles.DotnetToolsConfigJson)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task AddDependencyShouldAddToVmrRepo()
    {
        Mock<IGitRepo> repo = new();
        Mock<IGitRepoFactory> repoFactory = new();

        UnixPath relativeBasePath = VmrInfo.GetRelativeRepoSourcesPath("path");

        repo.Setup(r => r.GetFileContentsAsync(relativeBasePath / VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => VersionDetails);
        repoFactory.Setup(repoFactory => repoFactory.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        DependencyFileManager manager = new(
            repoFactory.Object,
            new VersionDetailsParser(),
            NullLogger.Instance);

        await manager.AddDependencyAsync(
            new DependencyDetail()
            {
                Name = "Foo",
                Version = "1.0.1",
                Commit = "abc123",
                Type = DependencyType.Product,
                RepoUri = "uri"
            },
            "uri",
            "branch",
            versionDetailsOnly: true,
            relativeBasePath: relativeBasePath,
            repoHasVersionDetailsProps: true);

        repo.Verify(r => r.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Any(f => f.FilePath == relativeBasePath / VersionFiles.VersionDetailsXml)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()),
            Times.Once);
        repo.Verify(r => r.CommitFilesAsync(
            It.Is<List<GitFile>>(files => files.Any(f => f.FilePath == relativeBasePath / VersionFiles.VersionDetailsProps)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()),
            Times.Once);
    }

    private static string NormalizeLineEndings(string input) => input.Replace("\r\n", "\n").TrimEnd();
}
