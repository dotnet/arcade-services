// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet;
using NuGet.Versioning;
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
        <?xml version="1.0" encoding="utf-8"?>
        <Project>
        </Project>
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
                .Be(NormalizeLineEndings(expectedVersionDetails));
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
            <?xml version="1.0" encoding="utf-8"?>
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
            <?xml version="1.0" encoding="utf-8"?>
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
            <?xml version="1.0" encoding="utf-8"?>
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
            <?xml version="1.0" encoding="utf-8"?>
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
                RepoUri = "https://github.com/dotnet/arcade",
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
                  <Uri>https://github.com/dotnet/arcade</Uri>
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

    private string NormalizeLineEndings(string input) => input.Replace("\r\n", "\n").TrimEnd();

    /// <summary>
    /// Verifies that the constructor overload taking IGitRepo wires _gitClientFactory to always use the provided repo instance,
    /// regardless of the repoUri input used by public methods.
    /// Input: Various repoUri strings including empty, whitespace, typical URL, custom scheme, and long string.
    /// Expected: IGitRepo.GetFileContentsAsync is invoked exactly once with the provided repoUri and branch per test case.
    /// </summary>
    [Test]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("https://github.com/org/repo")]
    [TestCase("weird:uri://?x=1")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task Constructor_UsesProvidedGitRepoRegardlessOfRepoUri(string repoUri)
    {
        // Arrange
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(@"<?xml version=""1.0"" encoding=""utf-8""?><Project />");

        var manager = new DependencyFileManager(
            gitRepo.Object,
            versionDetailsParser.Object,
            logger.Object);

        // Act
        var _ = await manager.ReadVersionDetailsXmlAsync(repoUri, "main");

        // Assert
        gitRepo.Verify(r => r.GetFileContentsAsync(
                It.IsAny<string>(),
                repoUri,
                "main"),
            Times.Once);

        // Also ensure at least the initial debug log was attempted to be written
        logger.Verify(l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Ensures that when reading a valid XML via a manager created with the IGitRepo constructor,
    /// the logger receives both the "start" and "success" debug log entries.
    /// Input: Valid XML content from mocked IGitRepo.
    /// Expected: ILogger receives exactly two Debug log calls; IGitRepo is queried once.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadVersionDetailsXmlAsync_ValidXml_LogsStartAndSuccess()
    {
        // Arrange
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(@"<?xml version=""1.0"" encoding=""utf-8""?><Project />");

        var manager = new DependencyFileManager(
            gitRepo.Object,
            versionDetailsParser.Object,
            logger.Object);

        // Act
        var _ = await manager.ReadVersionDetailsXmlAsync("https://example/repo", "branch");

        // Assert
        gitRepo.Verify(r => r.GetFileContentsAsync(
                It.IsAny<string>(),
                "https://example/repo",
                "branch"),
            Times.Once);

        logger.Verify(l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// Ensures that when the XML content is invalid, the manager (constructed with the IGitRepo overload)
    /// logs an error and rethrows the parsing exception.
    /// Input: Invalid (non-XML) content from mocked IGitRepo.
    /// Expected: One Debug log (start), one Error log (failure), and the thrown exception is observed in the test flow.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadVersionDetailsXmlAsync_InvalidXml_LogsErrorAndRethrows()
    {
        // Arrange
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("<<<not-xml>>>");

        var manager = new DependencyFileManager(
            gitRepo.Object,
            versionDetailsParser.Object,
            logger.Object);

        // Act
        try
        {
            var _ = await manager.ReadVersionDetailsXmlAsync("repo", "branch");
        }
        catch
        {
            // Assert
            gitRepo.Verify(r => r.GetFileContentsAsync(
                    It.IsAny<string>(),
                    "repo",
                    "branch"),
                Times.Once);

            logger.Verify(l => l.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((_, __) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);

            logger.Verify(l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((_, __) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);

            return;
        }

        Assert.Fail("Expected an exception to be thrown due to invalid XML content.");
    }

    /// <summary>
    /// Verifies that the constructor throws when the git client factory parameter is null.
    /// Input: gitClientFactory = null; versionDetailsParser and logger are non-null mocks.
    /// Expected: NullReferenceException thrown by the constructor due to accessing CreateClient on a null factory.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_NullGitRepoFactory_ThrowsNullReferenceException()
    {
        // Arrange
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger>(MockBehavior.Strict).Object;

        // Act
        Action act = () => new DependencyFileManager(
            (IGitRepoFactory)null,
            versionDetailsParser,
            logger);

        // Assert
        act.Should().Throw<NullReferenceException>();
    }

    /// <summary>
    /// Verifies that the constructor does not throw when versionDetailsParser and/or logger are null.
    /// Input cases:
    /// - versionDetailsParser = null, logger != null
    /// - versionDetailsParser != null, logger = null
    /// - versionDetailsParser = null, logger = null
    /// Expected: No exception is thrown in all cases, since the constructor does not validate these parameters.
    /// </summary>
    [Test]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_NullParserOrLogger_DoesNotThrow(bool parserIsNull, bool loggerIsNull)
    {
        // Arrange
        var repoFactory = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var parser = parserIsNull ? null : new Mock<IVersionDetailsParser>(MockBehavior.Strict).Object;
        var logger = loggerIsNull ? null : new Mock<ILogger>(MockBehavior.Strict).Object;

        // Act
        Action act = () => new DependencyFileManager(
            repoFactory.Object,
            parser,
            logger);

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Verifies that the constructor correctly stores the CreateClient delegate from the provided IGitRepoFactory.
    /// Input: Valid mocks for factory, parser, and logger; ReadVersionPropsAsync is invoked to force usage of the stored delegate.
    /// Expected: No exception is thrown; IGitRepoFactory.CreateClient is invoked with the provided repoUri when a public method uses the factory.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task Constructor_StoresFactoryDelegate_UsedByPublicMethodsAsync()
    {
        // Arrange
        const string repoUri = "https://unit.test/repo";
        const string branch = "main";
        const string versionsPropsContent =
            @"<?xml version=""1.0"" encoding=""utf-8""?><Project><PropertyGroup></PropertyGroup></Project>";

        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(versionsPropsContent);

        var repoFactory = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        repoFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(gitRepo.Object);

        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;

        var sut = new DependencyFileManager(
            repoFactory.Object,
            parser,
            logger);

        // Act
        var doc = await sut.ReadVersionPropsAsync(repoUri, branch);

        // Assert
        doc.Should().NotBeNull();
        repoFactory.Verify(f => f.CreateClient(repoUri), Times.Once);
        gitRepo.Verify(r => r.GetFileContentsAsync(It.IsAny<string>(), repoUri, branch), Times.Once);
    }

    /// <summary>
    /// Validates that ReadVersionDetailsXmlAsync:
    /// - Computes the correct file path using GetVersionFilePath with/without a relative base path.
    /// - Passes through repoUri and branch verbatim to IGitRepo.GetFileContentsAsync.
    /// - Parses the returned XML content into an XmlDocument.
    /// </summary>
    /// <param name="repoUri">Repository URI passed to the IGitRepoFactory and IGitRepo calls.</param>
    /// <param name="branch">Branch name forwarded to IGitRepo.GetFileContentsAsync.</param>
    /// <param name="relativeBasePath">Optional UnixPath used to prefix the Version.Details.xml path.</param>
    /// <param name="expectedPath">The exact expected path that should be requested from IGitRepo.</param>
    [Test]
    [TestCaseSource(nameof(ReadVersionDetailsXmlAsync_CallsGitRepoWithExpectedPath_TestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadVersionDetailsXmlAsync_RelativeBasePathVariants_CallsRepoWithCorrectPathAndParsesXml(
        string repoUri,
        string branch,
        UnixPath relativeBasePath,
        string expectedPath)
    {
        // Arrange
        var xmlContent = "<?xml version=\"1.0\" encoding=\"utf-8\"?><Dependencies></Dependencies>";

        var repoMock = new Mock<IGitRepo>(MockBehavior.Strict);
        var factoryMock = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var parserMock = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        repoMock
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(xmlContent);
        factoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(repoMock.Object);

        var sut = new DependencyFileManager(factoryMock.Object, parserMock.Object, loggerMock.Object);

        // Act
        var document = await sut.ReadVersionDetailsXmlAsync(repoUri, branch, relativeBasePath);

        // Assert
        factoryMock.Verify(f => f.CreateClient(repoUri), Times.Once);
        repoMock.Verify(r => r.GetFileContentsAsync(expectedPath, repoUri, branch), Times.Once);

        // Minimal additional validation of the parsed document structure without relying on external assertion frameworks
        if (document == null || document.DocumentElement == null || document.DocumentElement.Name != "Dependencies")
        {
            throw new InvalidOperationException("Parsed XmlDocument does not match expected structure.");
        }
    }

    /// <summary>
    /// Ensures that ReadVersionDetailsXmlAsync propagates XmlException when the underlying content is invalid XML.
    /// Inputs:
    /// - repoUri: "repo"
    /// - branch: "branch"
    /// - relativeBasePath: null (default path)
    /// Expected:
    /// - XmlException is thrown.
    /// - IGitRepo.GetFileContentsAsync is invoked with the default Version.Details.xml path.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadVersionDetailsXmlAsync_InvalidXml_ThrowsXmlException()
    {
        // Arrange
        var invalidXml = "<Dependencies"; // malformed XML
        var repoMock = new Mock<IGitRepo>(MockBehavior.Strict);
        var factoryMock = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var parserMock = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        repoMock
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(invalidXml);
        factoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(repoMock.Object);

        var sut = new DependencyFileManager(factoryMock.Object, parserMock.Object, loggerMock.Object);

        // Act
        bool threw = false;
        try
        {
            await sut.ReadVersionDetailsXmlAsync("repo", "branch", null);
        }
        catch (XmlException)
        {
            threw = true;
        }

        // Assert
        if (!threw)
        {
            throw new InvalidOperationException("Expected XmlException to be thrown for invalid XML.");
        }

        repoMock.Verify(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, "repo", "branch"), Times.Once);
    }

    private static IEnumerable<TestCaseData> ReadVersionDetailsXmlAsync_CallsGitRepoWithExpectedPath_TestCases()
    {
        var case1Base = (UnixPath)null;
        var case1Expected = VersionFiles.VersionDetailsXml;
        yield return new TestCaseData("https://example.com/repo", "main", case1Base, case1Expected)
            .SetName("ReadVersionDetailsXmlAsync_NoBasePath_UsesDefaultPath");

        var case2Base = new UnixPath("src/sub");
        var case2Expected = (string)(case2Base / VersionFiles.VersionDetailsXml);
        yield return new TestCaseData("repo://special", "feature/ä-测试", case2Base, case2Expected)
            .SetName("ReadVersionDetailsXmlAsync_WithRelativeBasePath_AppendsToPath");

        var case3Base = new UnixPath(".");
        var case3Expected = (string)(case3Base / VersionFiles.VersionDetailsXml);
        yield return new TestCaseData(" ", "  ", case3Base, case3Expected)
            .SetName("ReadVersionDetailsXmlAsync_WithWhitespaceInputs_PassesThroughAndCombines");
    }
    private const string MinimalVersionDetailsXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dependencies>
          <ProductDependencies>
          </ProductDependencies>
          <ToolsetDependencies>
          </ToolsetDependencies>
        </Dependencies>
        """;

    /// <summary>
    /// Verifies that ParseVersionDetailsXmlAsync forwards the includePinned flag to the parser,
    /// reads the Version.Details.xml from the expected default path (without relative base path),
    /// and uses the provided repoUri and branch when querying the repository.
    /// Expected behavior: Parser is called exactly once with the same includePinned value as provided.
    /// </summary>
    /// <param name="includePinned">Whether pinned dependencies should be included.</param>
    [Test]
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ParseVersionDetailsXmlAsync_IncludePinnedIsForwardedToParser(bool includePinned)
    {
        // Arrange
        var repoUri = "https://github.com/org/repo";
        var branch = "main";

        var gitRepoMock = new Mock<IGitRepo>(MockBehavior.Strict);
        var gitRepoFactoryMock = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var parserMock = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        gitRepoFactoryMock
            .Setup(f => f.CreateClient(repoUri))
            .Returns(gitRepoMock.Object);

        gitRepoMock
            .Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, repoUri, branch))
            .ReturnsAsync(MinimalVersionDetailsXml);

        var expected = new VersionDetails(Array.Empty<DependencyDetail>(), null);
        parserMock
            .Setup(p => p.ParseVersionDetailsXml(It.IsAny<XmlDocument>(), It.Is<bool>(b => b == includePinned)))
            .Returns(expected);

        var sut = new DependencyFileManager(gitRepoFactoryMock.Object, parserMock.Object, loggerMock.Object);

        // Act
        await sut.ParseVersionDetailsXmlAsync(repoUri, branch, includePinned);

        // Assert
        gitRepoFactoryMock.Verify(f => f.CreateClient(repoUri), Times.Once);
        gitRepoMock.Verify(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, repoUri, branch), Times.Once);
        parserMock.Verify(p => p.ParseVersionDetailsXml(It.IsAny<XmlDocument>(), includePinned), Times.Once);
        gitRepoFactoryMock.VerifyNoOtherCalls();
        gitRepoMock.VerifyNoOtherCalls();
        parserMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that ParseVersionDetailsXmlAsync combines the relativeBasePath with the Version.Details.xml path
    /// when reading file contents from the repository.
    /// Expected behavior: IGitRepo.GetFileContentsAsync is called with the combined path "relative/eng/Version.Details.xml".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ParseVersionDetailsXmlAsync_WithRelativeBasePath_CombinesPathCorrectly()
    {
        // Arrange
        var repoUri = "https://github.com/org/repo";
        var branch = "release";
        var relativeBasePath = new UnixPath("sub/module");
        var expectedPath = (relativeBasePath / VersionFiles.VersionDetailsXml).ToString();

        var gitRepoMock = new Mock<IGitRepo>(MockBehavior.Strict);
        var gitRepoFactoryMock = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var parserMock = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        gitRepoFactoryMock
            .Setup(f => f.CreateClient(repoUri))
            .Returns(gitRepoMock.Object);

        gitRepoMock
            .Setup(r => r.GetFileContentsAsync(expectedPath, repoUri, branch))
            .ReturnsAsync(MinimalVersionDetailsXml);

        parserMock
            .Setup(p => p.ParseVersionDetailsXml(It.IsAny<XmlDocument>(), It.IsAny<bool>()))
            .Returns(new VersionDetails(Array.Empty<DependencyDetail>(), null));

        var sut = new DependencyFileManager(gitRepoFactoryMock.Object, parserMock.Object, loggerMock.Object);

        // Act
        await sut.ParseVersionDetailsXmlAsync(repoUri, branch, includePinned: true, relativeBasePath: relativeBasePath);

        // Assert
        gitRepoFactoryMock.Verify(f => f.CreateClient(repoUri), Times.Once);
        gitRepoMock.Verify(r => r.GetFileContentsAsync(expectedPath, repoUri, branch), Times.Once);
        parserMock.Verify(p => p.ParseVersionDetailsXml(It.IsAny<XmlDocument>(), true), Times.Once);
        gitRepoFactoryMock.VerifyNoOtherCalls();
        gitRepoMock.VerifyNoOtherCalls();
        parserMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that TryRemoveDependencyAsync returns true and commits changes when the dependency exists.
    /// Inputs:
    /// - dependencyName is present in Version.Details.xml (case-varied via TestCase).
    /// Expected:
    /// - Method returns true.
    /// - CommitFilesAsync is called exactly once.
    /// </summary>
    [TestCase("Foo")]
    [TestCase("fOo")]
    [TestCase("FOO")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryRemoveDependencyAsync_DependencyPresent_ReturnsTrueAndCommitsOnce(string dependencyName)
    {
        // Arrange
        var repoUri = "https://unit.test/repo";
        var branch = "refs/heads/main";

        var versionDetailsXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <Dependency Name="Foo" Version="1.0.0">
                  <Uri>https://example/repo</Uri>
                  <Sha>abc</Sha>
                </Dependency>
                <Dependency Name="Bar" Version="1.0.0">
                  <Uri>https://example/repo</Uri>
                  <Sha>def</Sha>
                </Dependency>
              </ProductDependencies>
              <ToolsetDependencies />
            </Dependencies>
            """;

        var versionsPropsXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Project>
              <PropertyGroup>
                <FooPackageVersion>1.0.0</FooPackageVersion>
                <BarPackageVersion>1.0.0</BarPackageVersion>
              </PropertyGroup>
            </Project>
            """;

        var repo = new Mock<IGitRepo>(MockBehavior.Strict);
        var repoFactory = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        // Version.Details.xml is read twice (existence check + removal); returning same content is fine
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(versionDetailsXml);
        // Versions.props is read when repoHasVersionDetailsProps = false
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionsProps, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(versionsPropsXml);
        // .config/dotnet-tools.json does not exist in this scenario
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.DotnetToolsConfigJson, It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new DependencyFileNotFoundException("not found"));
        // Commit of updated files
        repo.Setup(r => r.CommitFilesAsync(
                It.IsAny<List<GitFile>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        repoFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        var dependencies = new List<DependencyDetail>
        {
            new DependencyDetail { Name = "Foo" },
            new DependencyDetail { Name = "Bar" }
        };
        parser.Setup(p => p.ParseVersionDetailsXml(It.IsAny<XmlDocument>(), It.IsAny<bool>()))
              .Returns(new VersionDetails(dependencies, null));

        var sut = new DependencyFileManager(repoFactory.Object, parser.Object, logger.Object);

        // Act
        var result = await sut.TryRemoveDependencyAsync(dependencyName, repoUri, branch, relativeBasePath: null, repoHasVersionDetailsProps: false);

        // Assert
        result.Should().BeTrue();
        repo.Verify(r => r.CommitFilesAsync(
            It.IsAny<List<GitFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()),
            Times.Once);
        repo.VerifyAll();
        parser.VerifyAll();
    }

    /// <summary>
    /// Verifies that TryRemoveDependencyAsync returns false and does not commit when the dependency is absent.
    /// Inputs:
    /// - dependencyName not present in Version.Details.xml (including empty/whitespace or special strings).
    /// Expected:
    /// - Method returns false.
    /// - CommitFilesAsync is never called.
    /// </summary>
    [TestCase("Baz")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("Foo-Bar")]
    [TestCase("F o o")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryRemoveDependencyAsync_DependencyMissingOrInvalid_ReturnsFalseAndDoesNotCommit(string dependencyName)
    {
        // Arrange
        var repoUri = "https://unit.test/repo";
        var branch = "refs/heads/main";

        var versionDetailsXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <Dependency Name="Foo" Version="1.0.0">
                  <Uri>https://example/repo</Uri>
                  <Sha>abc</Sha>
                </Dependency>
                <Dependency Name="Bar" Version="1.0.0">
                  <Uri>https://example/repo</Uri>
                  <Sha>def</Sha>
                </Dependency>
              </ProductDependencies>
              <ToolsetDependencies />
            </Dependencies>
            """;

        var repo = new Mock<IGitRepo>(MockBehavior.Strict);
        var repoFactory = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        // Only Version.Details.xml is read because the method returns before removal when dependency doesn't exist
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(versionDetailsXml);

        repoFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        var dependencies = new List<DependencyDetail>
        {
            new DependencyDetail { Name = "Foo" },
            new DependencyDetail { Name = "Bar" }
        };
        parser.Setup(p => p.ParseVersionDetailsXml(It.IsAny<XmlDocument>(), It.IsAny<bool>()))
              .Returns(new VersionDetails(dependencies, null));

        var sut = new DependencyFileManager(repoFactory.Object, parser.Object, logger.Object);

        // Act
        var result = await sut.TryRemoveDependencyAsync(dependencyName, repoUri, branch);

        // Assert
        result.Should().BeFalse();
        repo.Verify(r => r.CommitFilesAsync(
            It.IsAny<List<GitFile>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()),
            Times.Never);
        repo.VerifyAll();
        parser.VerifyAll();
    }

    /// <summary>
    /// Verifies that GetXmlDocument loads valid XML successfully both with and without the pseudo-BOM prefix,
    /// and that the returned XmlDocument is configured to preserve whitespace.
    /// Inputs:
    ///  - withBom: whether the input XML string is prefixed by the "∩╗┐" sequence.
    /// Expected:
    ///  - No exception is thrown.
    ///  - Returned XmlDocument is not null, PreserveWhitespace is true, and the root node name matches.
    /// </summary>
    [Test]
    [TestCase(false)]
    [TestCase(true)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetXmlDocument_ValidXmlWithOrWithoutBom_LoadsAndPreservesWhitespace(bool withBom)
    {
        // Arrange
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
        var input = withBom ? "∩╗┐" + xmlWithoutBom : xmlWithoutBom;

        // Act
        XmlDocument doc = DependencyFileManager.GetXmlDocument(input);

        // Assert
        Assert.That(doc, Is.Not.Null);
        Assert.That(doc.PreserveWhitespace, Is.True);
        Assert.That(doc.DocumentElement, Is.Not.Null);
        Assert.That(doc.DocumentElement!.Name, Is.EqualTo("Dependencies"));
    }

    /// <summary>
    /// Ensures that GetXmlDocument throws an XmlException for invalid XML inputs.
    /// Inputs:
    ///  - A series of invalid XML strings (empty, whitespace only, random text, malformed structures, or just the prefix).
    /// Expected:
    ///  - XmlException is thrown for each invalid input.
    /// </summary>
    [Test]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\t\n")]
    [TestCase("not xml")]
    [TestCase("<?xml version=\"1.0\" encoding=\"utf-8\"?>")]
    [TestCase("<root>")]
    [TestCase("∩╗┐")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetXmlDocument_InvalidXml_ThrowsXmlException(string invalidXml)
    {
        // Arrange
        // invalidXml provided by [TestCase]

        // Act
        TestDelegate act = () => DependencyFileManager.GetXmlDocument(invalidXml);

        // Assert
        Assert.That(act, Throws.TypeOf<XmlException>());
    }

    /// <summary>
    /// Validates that whitespace within the XML content is preserved by GetXmlDocument,
    /// specifically checking for the presence of XmlWhitespace nodes.
    /// Inputs:
    ///  - XML with indentation and newlines between elements.
    /// Expected:
    ///  - Returned document has PreserveWhitespace = true and contains XmlWhitespace child nodes.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetXmlDocument_WhitespaceIsPreserved_WhitespaceNodesExist()
    {
        // Arrange
        const string xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <child />
            </root>
            """;

        // Act
        XmlDocument doc = DependencyFileManager.GetXmlDocument(xml);

        // Assert
        Assert.That(doc.PreserveWhitespace, Is.True);
        Assert.That(doc.DocumentElement, Is.Not.Null);
        var hasWhitespace = doc.DocumentElement!.ChildNodes.Cast<XmlNode>().Any(n => n.NodeType == XmlNodeType.Whitespace);
        Assert.That(hasWhitespace, Is.True, "Expected at least one XmlWhitespace node among the children of <root>.");
    }

    /// <summary>
    /// Confirms an invalid directory path results in an expected exception.
    /// Input: A non-existent directory path.
    /// Expected: DirectoryNotFoundException is thrown by NormalizeAttributes.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void NormalizeAttributes_NonExistentDirectory_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        string nonExistent = Path.Combine(Path.GetTempPath(), "darc_normalize_" + Guid.NewGuid().ToString("N"));

        // Act + Assert
        Assert.Throws<DirectoryNotFoundException>(() => DependencyFileManager.NormalizeAttributes(nonExistent));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "darc_normalize_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            // Ensure all are writable before deletion in case test failed earlier
            ClearReadOnlyRecursively(path);

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup; ignore failures to not mask test results
        }
    }

    private static void ClearReadOnlyRecursively(string directory)
    {
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
        }

        foreach (var dir in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories).Reverse())
        {
            var attrs = File.GetAttributes(dir);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(dir, attrs & ~FileAttributes.ReadOnly);
            }
        }

        var rootAttrs = File.GetAttributes(directory);
        if (rootAttrs.HasFlag(FileAttributes.ReadOnly))
        {
            File.SetAttributes(directory, rootAttrs & ~FileAttributes.ReadOnly);
        }
    }

    /// <summary>
    /// Validates that GetPackageSources returns all add elements that contain both 'key' and 'value' attributes,
    /// preserving document order, and including entries even when attributes are empty or whitespace-only strings.
    /// Add elements missing either attribute are ignored.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetPackageSources_NoFilter_IncludesValidEntriesAndPreservesOrder()
    {
        // Arrange
        var xml = """
            <configuration>
              <packageSources>
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                <add key="MyFeed" value="https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json" />
                <add key="NoValueOnly" />
                <add value="https://example.com/no-key" />
                <add key="" value="" />
                <add key="Spaces" value="   " />
              </packageSources>
            </configuration>
            """;
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        var manager = new DependencyFileManager(
            Mock.Of<IGitRepo>(),
            Mock.Of<IVersionDetailsParser>(),
            new Mock<ILogger>().Object);

        // Act
        var sources = manager.GetPackageSources(doc);

        // Assert
        Assert.That(sources, Is.Not.Null);
        Assert.That(sources.Count, Is.EqualTo(4), "Expected to include entries with both attributes even if empty/whitespace, and ignore nodes missing an attribute.");
        Assert.Multiple(() =>
        {
            Assert.That(sources[0].key, Is.EqualTo("nuget.org"));
            Assert.That(sources[0].feed, Is.EqualTo("https://api.nuget.org/v3/index.json"));

            Assert.That(sources[1].key, Is.EqualTo("MyFeed"));
            Assert.That(sources[1].feed, Is.EqualTo("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"));

            Assert.That(sources[2].key, Is.EqualTo(""));
            Assert.That(sources[2].feed, Is.EqualTo(""));

            Assert.That(sources[3].key, Is.EqualTo("Spaces"));
            Assert.That(sources[3].feed, Is.EqualTo("   "));
        });
    }

    /// <summary>
    /// Ensures that when a filter is provided, GetPackageSources returns only entries where the filter predicate
    /// evaluates to true for the 'value' (feed) attribute. Non-matching entries are excluded.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetPackageSources_FilterApplied_ReturnsOnlyMatchingFeeds()
    {
        // Arrange
        var xml = """
            <configuration>
              <packageSources>
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                <add key="AzDo" value="https://pkgs.dev.azure.com/contoso/_packaging/feed/nuget/v3/index.json" />
                <add key="Empty" value="" />
                <add key="White" value="   " />
              </packageSources>
            </configuration>
            """;
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        var manager = new DependencyFileManager(
            Mock.Of<IGitRepo>(),
            Mock.Of<IVersionDetailsParser>(),
            new Mock<ILogger>().Object);

        // Only include Azure DevOps feeds
        Func<string, bool> filter = v => v != null && v.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase);

        // Act
        var sources = manager.GetPackageSources(doc, filter);

        // Assert
        Assert.That(sources, Is.Not.Null);
        Assert.That(sources.Count, Is.EqualTo(1), "Only entries with value containing 'dev.azure.com' should be included.");
        Assert.Multiple(() =>
        {
            Assert.That(sources[0].key, Is.EqualTo("AzDo"));
            Assert.That(sources[0].feed, Is.EqualTo("https://pkgs.dev.azure.com/contoso/_packaging/feed/nuget/v3/index.json"));
        });
    }

    /// <summary>
    /// Verifies that when a null IGitRepo is provided to the constructor, using the manager later
    /// to access the repo results in a NullReferenceException due to the stored factory returning null.
    /// Input: gitRepo = null; versionDetailsParser and logger are valid mocks.
    /// Expected: Calling a method that uses the git client throws NullReferenceException.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task Constructor_NullGitRepo_UsingManagerThrowsNullReferenceException()
    {
        // Arrange
        IGitRepo gitRepo = null;
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var manager = new DependencyFileManager(
            gitRepo,
            versionDetailsParser.Object,
            logger.Object);

        // Act + Assert
        try
        {
            var _ = await manager.ReadVersionDetailsXmlAsync("repo", "branch");
            throw new Exception("Expected NullReferenceException was not thrown when using a manager constructed with a null IGitRepo.");
        }
        catch (NullReferenceException)
        {
            // Expected path
        }
    }

    /// <summary>
    /// Verifies that the constructor correctly stores the IGitRepoFactory.CreateClient delegate,
    /// and that public methods use it by invoking CreateClient with the provided repoUri.
    /// Inputs: Various repoUri strings including empty, whitespace, typical URL, custom scheme, and long string.
    /// Expected: IGitRepoFactory.CreateClient is invoked exactly once with the given repoUri; the returned IGitRepo is used to fetch contents once.
    /// </summary>
    [Test]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("https://github.com/org/repo")]
    [TestCase("weird:uri://?x=1")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task Constructor_StoresFactoryDelegate_UsedByPublicMethodsAsync(string repoUri)
    {
        // Arrange
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(@"<?xml version=""1.0"" encoding=""utf-8""?><Project />");

        var factory = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        factory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(gitRepo.Object);

        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var manager = new DependencyFileManager(factory.Object, parser.Object, logger.Object);

        // Act
        var _ = await manager.ReadVersionDetailsXmlAsync(repoUri, "main");

        // Assert
        factory.Verify(f => f.CreateClient(repoUri), Times.Once);
        gitRepo.Verify(r => r.GetFileContentsAsync(It.IsAny<string>(), repoUri, "main"), Times.Once);
    }
    /// <summary>
    /// Verifies that when no matching add elements exist under configuration/packageSources,
    /// the method returns an empty list and does not throw.
    /// This covers cases with no packageSources section and with only non-element nodes.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetPackageSources_NoAddNodes_ReturnsEmptyList()
    {
        // Arrange
        var xml = """
                <configuration>
                  <otherSection>
                    <add key="ignored" value="ignored" />
                  </otherSection>
                  <packageSources>
                    <!-- comment -->
                    <?pi processing-instruction?>
                    Text node
                  </packageSources>
                </configuration>
                """;
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        var manager = new DependencyFileManager(
            Mock.Of<IGitRepo>(),
            Mock.Of<IVersionDetailsParser>(),
            new Mock<ILogger>().Object);

        // Act
        var sources = manager.GetPackageSources(doc);

        // Assert
        Assert.That(sources, Is.Not.Null);
        Assert.That(sources.Count, Is.EqualTo(0));
    }
    private const string XmlWithNs_SingleMatch = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <FooPackageVersion>1.2.3</FooPackageVersion>
  </PropertyGroup>
</Project>";

    private const string Xml_NoPropertyGroupParent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project>
  <FooPackageVersion>1.0.0</FooPackageVersion>
  <PropertyGroup />
</Project>";

    private const string XmlWithNs_MultipleMatches = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <FooPackageVersion>first</FooPackageVersion>
  </PropertyGroup>
  <PropertyGroup>
    <FooPackageVersion>second</FooPackageVersion>
  </PropertyGroup>
</Project>";

    private const string XmlWithNs_NestedUnderInner = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <Inner>
      <FooPackageVersion>nested</FooPackageVersion>
    </Inner>
  </PropertyGroup>
</Project>";

    /// <summary>
    /// Verifies GetVersionPropsNode returns the expected node under various XML structures and inputs.
    /// Inputs:
    /// - xmlContent: XML payload forming the version props document; when null, an empty XmlDocument is used.
    /// - nodeName: Name of the property node to search for.
    /// Expected:
    /// - Returns the node when it exists directly under a PropertyGroup (namespace-agnostic).
    /// - Returns null when the node is not under a PropertyGroup, case mismatched, empty name, or no root element.
    /// - Returns the first matching node in document order when multiple matches exist.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(GetVersionPropsNode_TestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetVersionPropsNode_VariousInputs_BehavesAsExpected(string xmlContent, string nodeName, bool expectedFound, string expectedInnerText)
    {
        // Arrange
        var doc = CreateXmlDocument(xmlContent);

        // Act
        XmlNode result = DependencyFileManager.GetVersionPropsNode(doc, nodeName);

        // Assert
        if (expectedFound)
        {
            Assert.That(result, Is.Not.Null, "Expected a node to be found.");
            Assert.That(result.Name, Is.EqualTo(nodeName), "Node name should match the requested property name.");
            Assert.That(result.InnerText, Is.EqualTo(expectedInnerText), "Node content should match the expected value.");
        }
        else
        {
            Assert.That(result, Is.Null, "Expected no node to be found.");
        }
    }

    /// <summary>
    /// Ensures that when nodeName contains a single quote, the XPath constructed by GetVersionPropsNode
    /// becomes invalid and an XPathException is thrown.
    /// Inputs:
    /// - xmlContent containing a valid matching structure.
    /// - nodeName with a single quote character.
    /// Expected:
    /// - XPathException is thrown due to malformed XPath string.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetVersionPropsNode_NodeNameContainsSingleQuote_ThrowsXPathException()
    {
        // Arrange
        var doc = CreateXmlDocument(XmlWithNs_SingleMatch);
        var nodeName = "Foo'PackageVersion";

        // Act & Assert
        Assert.That(
            () => DependencyFileManager.GetVersionPropsNode(doc, nodeName),
            Throws.TypeOf<XPathException>());
    }

    private static IEnumerable<TestCaseData> GetVersionPropsNode_TestCases()
    {
        yield return new TestCaseData(XmlWithNs_SingleMatch, "FooPackageVersion", true, "1.2.3").SetName("DirectChildOfPropertyGroup_WithNamespace_FindsNode");
        yield return new TestCaseData(XmlWithNs_MultipleMatches, "FooPackageVersion", true, "first").SetName("MultipleMatches_ReturnsFirstInDocumentOrder");
        yield return new TestCaseData(Xml_NoPropertyGroupParent, "FooPackageVersion", false, null).SetName("NotUnderPropertyGroup_ReturnsNull");
        yield return new TestCaseData(XmlWithNs_NestedUnderInner, "FooPackageVersion", false, null).SetName("NestedUnderInner_ReturnsNull");
        yield return new TestCaseData(XmlWithNs_SingleMatch, "foopackageversion", false, null).SetName("CaseMismatch_ReturnsNull");
        yield return new TestCaseData(null, "AnyNode", false, null).SetName("EmptyDocument_NoRoot_ReturnsNull");
        yield return new TestCaseData(XmlWithNs_SingleMatch, "", false, null).SetName("EmptyNodeName_ReturnsNull");
        yield return new TestCaseData(XmlWithNs_SingleMatch, " ", false, null).SetName("WhitespaceNodeName_ReturnsNull");
    }

    private static XmlDocument CreateXmlDocument(string xml)
    {
        var doc = new XmlDocument();
        if (!string.IsNullOrEmpty(xml))
        {
            doc.LoadXml(xml);
        }
        return doc;
    }
}




/// <summary>
/// Unit tests targeting DependencyFileManager.ReadGlobalJsonAsync only.
/// </summary>
[TestFixture]
public class DependencyFileManagerReadGlobalJsonAsyncTests
{
    /// <summary>
    /// Verifies that ReadGlobalJsonAsync computes the correct path using the optional relativeBasePath,
    /// calls IGitRepo.GetFileContentsAsync with the expected arguments, and parses valid JSON content.
    /// </summary>
    /// <param name="relativeBasePathStr">Optional relative base path used to prefix the global.json path.</param>
    /// <param name="expectedPath">The expected full path passed to IGitRepo.GetFileContentsAsync.</param>
    [Test]
    [TestCase(null, "global.json")]
    [TestCase("eng", "eng/global.json")]
    [TestCase("eng/sub", "eng/sub/global.json")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadGlobalJsonAsync_WithRelativeBasePath_ComputesPathAndParsesJson(string relativeBasePathStr, string expectedPath)
    {
        // Arrange
        var repoUri = "https://example.com/repo.git";
        var branch = "main";
        var relativeBasePath = relativeBasePathStr == null ? null : new UnixPath(relativeBasePathStr);

        var jsonContent = """
        {
          "sdk": { "version": "8.0.100" },
          "tools": { "dotnet": "8.0.100" }
        }
        """;

        var gitRepoMock = new Mock<IGitRepo>(MockBehavior.Strict);
        gitRepoMock
            .Setup(r => r.GetFileContentsAsync(expectedPath, repoUri, branch))
            .ReturnsAsync(jsonContent);

        var versionDetailsParserMock = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var manager = new DependencyFileManager(gitRepoMock.Object, versionDetailsParserMock.Object, loggerMock.Object);

        // Act
        var result = await manager.ReadGlobalJsonAsync(repoUri, branch, relativeBasePath);

        // Assert
        result.Should().NotBeNull();
        result["sdk"]?["version"]?.Value<string>().Should().Be("8.0.100");
        gitRepoMock.Verify(r => r.GetFileContentsAsync(expectedPath, repoUri, branch), Times.Once);
    }

    /// <summary>
    /// Ensures that ReadGlobalJsonAsync propagates DependencyFileNotFoundException when global.json
    /// does not exist at the computed path.
    /// </summary>
    /// <param name="relativeBasePathStr">Optional relative base path used to prefix the global.json path.</param>
    /// <param name="expectedPath">The expected full path passed to IGitRepo.GetFileContentsAsync.</param>
    [Test]
    [TestCase(null, "global.json")]
    [TestCase("eng", "eng/global.json")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadGlobalJsonAsync_WhenFileMissing_ThrowsDependencyFileNotFoundException(string relativeBasePathStr, string expectedPath)
    {
        // Arrange
        var repoUri = "https://example.com/repo.git";
        var branch = "feature/test";
        var relativeBasePath = relativeBasePathStr == null ? null : new UnixPath(relativeBasePathStr);

        var gitRepoMock = new Mock<IGitRepo>(MockBehavior.Strict);
        gitRepoMock
            .Setup(r => r.GetFileContentsAsync(expectedPath, repoUri, branch))
            .ThrowsAsync(new DependencyFileNotFoundException());

        var versionDetailsParserMock = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var manager = new DependencyFileManager(gitRepoMock.Object, versionDetailsParserMock.Object, loggerMock.Object);

        // Act
        Func<Task> act = async () => await manager.ReadGlobalJsonAsync(repoUri, branch, relativeBasePath);

        // Assert
        await act.Should().ThrowAsync<DependencyFileNotFoundException>();
        gitRepoMock.Verify(r => r.GetFileContentsAsync(expectedPath, repoUri, branch), Times.Once);
    }

    /// <summary>
    /// Confirms that ReadGlobalJsonAsync throws a JsonReaderException when the file content
    /// is not valid JSON and still forwards the correct path to IGitRepo.
    /// </summary>
    /// <param name="relativeBasePathStr">Optional relative base path used to prefix the global.json path.</param>
    /// <param name="expectedPath">The expected full path passed to IGitRepo.GetFileContentsAsync.</param>
    [Test]
    [TestCase(null, "global.json")]
    [TestCase("eng", "eng/global.json")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadGlobalJsonAsync_WithInvalidJson_ThrowsJsonReaderException(string relativeBasePathStr, string expectedPath)
    {
        // Arrange
        var repoUri = "https://example.com/repo.git";
        var branch = "release/1.0";
        var relativeBasePath = relativeBasePathStr == null ? null : new UnixPath(relativeBasePathStr);

        var invalidJson = "not a valid json";

        var gitRepoMock = new Mock<IGitRepo>(MockBehavior.Strict);
        gitRepoMock
            .Setup(r => r.GetFileContentsAsync(expectedPath, repoUri, branch))
            .ReturnsAsync(invalidJson);

        var versionDetailsParserMock = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var manager = new DependencyFileManager(gitRepoMock.Object, versionDetailsParserMock.Object, loggerMock.Object);

        // Act
        Func<Task> act = async () => await manager.ReadGlobalJsonAsync(repoUri, branch, relativeBasePath);

        // Assert
        await act.Should().ThrowAsync<JsonReaderException>();
        gitRepoMock.Verify(r => r.GetFileContentsAsync(expectedPath, repoUri, branch), Times.Once);
    }

}




[TestFixture]
public class DependencyFileManager_ReadDotNetToolsConfigJsonAsync_Tests
{
    /// <summary>
    /// Ensures that when the .config/dotnet-tools.json file exists:
    /// - The manager requests the correct path (with or without a relative base path)
    /// - The repoUri and branch are forwarded to IGitRepo correctly
    /// - The returned string content is parsed into a JObject
    /// </summary>
    /// <param name="relativeBase">Optional base path to prepend to the tools config location.</param>
    /// <param name="expectedPath">Expected path argument passed to IGitRepo, depending on whether a base path is used.</param>
    [Test]
    [TestCase(null, ".config/dotnet-tools.json")]
    [TestCase("eng", "eng/.config/dotnet-tools.json")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadDotNetToolsConfigJsonAsync_FileExists_ReturnsParsedJsonAndUsesExpectedPath(string relativeBase, string expectedPath)
    {
        // Arrange
        var repoUri = "https://example.com/repo.git";
        var branch = "main";
        var json = """
        {
          "version": 1,
          "isRoot": true,
          "tools": {
            "foo": { "version": "1.2.3", "commands": [ "foo" ] }
          }
        }
        """;

        string capturedPath = null;
        string capturedRepoUri = null;
        string capturedBranch = null;

        Mock<IGitRepo> repo = new();
        repo.Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((p, u, b) =>
            {
                capturedPath = p;
                capturedRepoUri = u;
                capturedBranch = b;
            })
            .ReturnsAsync(json);

        Mock<IGitRepoFactory> repoFactory = new();
        repoFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        var manager = new DependencyFileManager(
            repoFactory.Object,
            new VersionDetailsParser(),
            NullLogger.Instance);

        UnixPath basePath = relativeBase == null ? null : new UnixPath(relativeBase);

        // Act
        var result = await manager.ReadDotNetToolsConfigJsonAsync(repoUri, branch, basePath);

        // Assert
        capturedPath.Should().Be(expectedPath);
        capturedRepoUri.Should().Be(repoUri);
        capturedBranch.Should().Be(branch);

        result.Should().NotBeNull();
        result.Value<int>("version").Should().Be(1);
        result["tools"]?["foo"]?["version"]!.Value<string>().Should().Be("1.2.3");
    }

    /// <summary>
    /// Verifies that when the tools manifest file is missing, the method:
    /// - Catches DependencyFileNotFoundException
    /// - Returns null instead of throwing
    /// - Uses the expected path when no base path is provided
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadDotNetToolsConfigJsonAsync_FileMissing_ReturnsNull()
    {
        // Arrange
        var repoUri = "https://example.com/repo.git";
        var branch = "feature/xyz";

        string capturedPath = null;

        Mock<IGitRepo> repo = new();
        repo.Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((p, _, _) => capturedPath = p)
            .ThrowsAsync(new DependencyFileNotFoundException());

        Mock<IGitRepoFactory> repoFactory = new();
        repoFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        var manager = new DependencyFileManager(
            repoFactory.Object,
            new VersionDetailsParser(),
            NullLogger.Instance);

        // Act
        var result = await manager.ReadDotNetToolsConfigJsonAsync(repoUri, branch, null);

        // Assert
        capturedPath.Should().Be(".config/dotnet-tools.json");
        result.Should().BeNull();
    }

    /// <summary>
    /// Ensures that invalid JSON content causes a parsing exception to bubble up,
    /// confirming that only file-not-found is swallowed and not other errors.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadDotNetToolsConfigJsonAsync_InvalidJson_ThrowsJsonReaderException()
    {
        // Arrange
        var repoUri = "repo";
        var branch = "branch";
        var invalidJson = "{ invalid json ...";

        Mock<IGitRepo> repo = new();
        repo.Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(invalidJson);

        Mock<IGitRepoFactory> repoFactory = new();
        repoFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        var manager = new DependencyFileManager(
            repoFactory.Object,
            new VersionDetailsParser(),
            NullLogger.Instance);

        // Act
        var act = async () => await manager.ReadDotNetToolsConfigJsonAsync(repoUri, branch, null);

        // Assert
        await act.Should().ThrowAsync<JsonReaderException>();
    }
}




/// <summary>
/// Tests for DependencyFileManager.TryAddOrUpdateDependency focusing on:
/// - Returning false and not committing when the dependency is already present with identical Name, Version, RepoUri, and Commit.
/// - Returning true and committing when any identifying field differs.
/// - Throwing when a null dependency is provided.
/// </summary>
[TestFixture]
public class DependencyFileManagerTryAddOrUpdateDependencyTests
{
    private const string BaseVersionDetailsXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dependencies>
          <ProductDependencies>
            <Dependency Name="Foo" Version="1.0.0">
              <Uri>https://github.com/dotnet/foo</Uri>
              <Sha>abc123</Sha>
            </Dependency>
          </ProductDependencies>
          <ToolsetDependencies>
          </ToolsetDependencies>
        </Dependencies>
        """;

    /// <summary>
    /// Ensures that when the target dependency already exists with identical
    /// Name, Version, RepoUri, and Commit, the method returns false and does not commit any changes.
    /// Inputs:
    /// - Existing Version.Details.xml contains Foo 1.0.0 from https://github.com/dotnet/foo with commit abc123.
    /// - Requested dependency is exactly the same.
    /// Expected:
    /// - TryAddOrUpdateDependency returns false.
    /// - No commit occurs.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryAddOrUpdateDependency_DependencyAlreadyPresentWithSameVersionRepoAndCommit_ReturnsFalseAndDoesNotCommitAsync()
    {
        // Arrange
        var repoMock = new Mock<IGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        repoMock
            .Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(BaseVersionDetailsXml);

        var manager = new DependencyFileManager(
            repoMock.Object,
            new VersionDetailsParser(),
            loggerMock.Object);

        var dependency = new DependencyDetail
        {
            Name = "Foo",
            Version = "1.0.0",
            RepoUri = "https://github.com/dotnet/foo",
            Commit = "abc123",
            Type = DependencyType.Product
        };

        // Act
        var result = await manager.TryAddOrUpdateDependency(
            dependency,
            repoUri: string.Empty,
            branch: string.Empty,
            relativeBasePath: null,
            versionDetailsOnly: true,
            repoHasVersionDetailsProps: null);

        // Assert
        Assert.That(result, Is.False, "No update should be performed when the dependency is identical.");
        repoMock.Verify(r => r.CommitFilesAsync(
                It.IsAny<List<GitFile>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>
    /// Ensures that when any of Version, Commit, or RepoUri differs from the existing dependency,
    /// the method returns true and commits changes by invoking the add/update path.
    /// Inputs:
    /// - Existing Version.Details.xml contains Foo 1.0.0 from https://github.com/dotnet/foo with commit abc123.
    /// - Requested dependency differs by one field as specified by the test case.
    /// Expected:
    /// - TryAddOrUpdateDependency returns true.
    /// - A commit occurs updating Version.Details.xml.
    /// </summary>
    [Test]
    [TestCase("Version", "2.0.0")]
    [TestCase("Commit", "def456")]
    [TestCase("RepoUri", "https://github.com/dotnet/other")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryAddOrUpdateDependency_ExistingDependencyDiffers_UpdatesAndCommitsAsync(string changedField, string newValue)
    {
        // Arrange
        var repoMock = new Mock<IGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        // Version.Details.xml exists with a baseline dependency.
        string versionDetailsContent = BaseVersionDetailsXml;

        repoMock
            .Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => versionDetailsContent);

        // Ensure that Version.Details.props, dotnet-tools.json lookups do not interfere when we set versionDetailsOnly = true.
        repoMock
            .Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsProps, It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new DependencyFileNotFoundException());
        repoMock
            .Setup(r => r.GetFileContentsAsync(VersionFiles.DotnetToolsConfigJson, It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new DependencyFileNotFoundException());

        // Capture commits to validate that the update path was executed.
        repoMock
            .Setup(r => r.CommitFilesAsync(
                It.IsAny<List<GitFile>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var manager = new DependencyFileManager(
            repoMock.Object,
            new VersionDetailsParser(),
            loggerMock.Object);

        var dependency = new DependencyDetail
        {
            Name = "Foo",
            Version = "1.0.0",
            RepoUri = "https://github.com/dotnet/foo",
            Commit = "abc123",
            Type = DependencyType.Product
        };

        // Apply the change for the test case
        switch (changedField)
        {
            case "Version":
                dependency.Version = newValue;
                break;
            case "Commit":
                dependency.Commit = newValue;
                break;
            case "RepoUri":
                dependency.RepoUri = newValue;
                break;
            default:
                Assert.Fail("Unsupported changedField test case.");
                break;
        }

        // Act
        var result = await manager.TryAddOrUpdateDependency(
            dependency,
            repoUri: string.Empty,
            branch: string.Empty,
            relativeBasePath: null,
            versionDetailsOnly: true,
            repoHasVersionDetailsProps: null);

        // Assert
        Assert.That(result, Is.True, "Update should be performed when any identifying field differs.");
        repoMock.Verify(r => r.CommitFilesAsync(
                It.Is<List<GitFile>>(files => files.Any(f => f.FilePath == VersionFiles.VersionDetailsXml)),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    /// <summary>
    /// Ensures that providing a null dependency causes the method to throw.
    /// Inputs:
    /// - dependency is null.
    /// Expected:
    /// - A NullReferenceException is thrown due to property access on null in the equality check.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void TryAddOrUpdateDependency_NullDependency_ThrowsNullReferenceException()
    {
        // Arrange
        var repoMock = new Mock<IGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        repoMock
            .Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(BaseVersionDetailsXml);

        var manager = new DependencyFileManager(
            repoMock.Object,
            new VersionDetailsParser(),
            loggerMock.Object);

        // Act + Assert
        Assert.ThrowsAsync<NullReferenceException>(async () =>
        {
            await manager.TryAddOrUpdateDependency(
                dependency: null,
                repoUri: string.Empty,
                branch: string.Empty,
                relativeBasePath: null,
                versionDetailsOnly: true,
                repoHasVersionDetailsProps: null);
        });
    }
}



[TestFixture]
public class DependencyFileManager_GetVersionPropsNode_Tests
{
    private const string XmlWithNs_SingleMatch = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <FooPackageVersion>1.2.3</FooPackageVersion>
  </PropertyGroup>
</Project>";

    private const string Xml_NoPropertyGroupParent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project>
  <FooPackageVersion>1.0.0</FooPackageVersion>
  <PropertyGroup />
</Project>";

    private const string XmlWithNs_MultipleMatches = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <FooPackageVersion>first</FooPackageVersion>
  </PropertyGroup>
  <PropertyGroup>
    <FooPackageVersion>second</FooPackageVersion>
  </PropertyGroup>
</Project>";

    private const string XmlWithNs_NestedUnderInner = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <Inner>
      <FooPackageVersion>nested</FooPackageVersion>
    </Inner>
  </PropertyGroup>
</Project>";

    /// <summary>
    /// Verifies GetVersionPropsNode returns the expected node under various XML structures and inputs.
    /// Inputs:
    /// - xmlContent: XML payload forming the version props document; when null, an empty XmlDocument is used.
    /// - nodeName: Name of the property node to search for.
    /// Expectations:
    /// - When the node exists directly under a PropertyGroup (any namespace), the node is returned.
    /// - When the node is not under a PropertyGroup, or document has no root, or nodeName is empty/mismatched in case, null is returned.
    /// - When multiple matches exist, the first in document order is returned.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(GetVersionPropsNode_TestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetVersionPropsNode_VariousInputs_BehavesAsExpected(string xmlContent, string nodeName, bool expectedFound, string expectedInnerText)
    {
        // Arrange
        var doc = CreateXmlDocument(xmlContent);

        // Act
        XmlNode result = DependencyFileManager.GetVersionPropsNode(doc, nodeName);

        // Assert
        if (expectedFound)
        {
            Assert.That(result, Is.Not.Null, "Expected a node to be found.");
            Assert.That(result.Name, Is.EqualTo(nodeName), "Node name should match the requested property name.");
            Assert.That(result.InnerText, Is.EqualTo(expectedInnerText), "Node content should match the expected value.");
        }
        else
        {
            Assert.That(result, Is.Null, "Expected no node to be found.");
        }
    }

    private static IEnumerable<TestCaseData> GetVersionPropsNode_TestCases()
    {
        yield return new TestCaseData(XmlWithNs_SingleMatch, "FooPackageVersion", true, "1.2.3")
            .SetName("GetVersionPropsNode_NodeUnderPropertyGroupWithDefaultNamespace_ReturnsNode");

        yield return new TestCaseData(Xml_NoPropertyGroupParent, "FooPackageVersion", false, null)
            .SetName("GetVersionPropsNode_NodeNotUnderPropertyGroup_ReturnsNull");

        yield return new TestCaseData(XmlWithNs_MultipleMatches, "FooPackageVersion", true, "first")
            .SetName("GetVersionPropsNode_MultipleMatches_ReturnsFirstInDocumentOrder");

        yield return new TestCaseData(null, "FooPackageVersion", false, null)
            .SetName("GetVersionPropsNode_DocumentWithoutRoot_ReturnsNull");

        yield return new TestCaseData(XmlWithNs_NestedUnderInner, "FooPackageVersion", false, null)
            .SetName("GetVersionPropsNode_NodeNestedNotDirectChildOfPropertyGroup_ReturnsNull");

        yield return new TestCaseData(XmlWithNs_SingleMatch, "", false, null)
            .SetName("GetVersionPropsNode_EmptyNodeName_ReturnsNull");

        yield return new TestCaseData(XmlWithNs_SingleMatch, "foopackageversion", false, null)
            .SetName("GetVersionPropsNode_CaseMismatch_ReturnsNull");
    }

    private static XmlDocument CreateXmlDocument(string xml)
    {
        var doc = new XmlDocument();
        if (!string.IsNullOrEmpty(xml))
        {
            doc.LoadXml(xml);
        }
        return doc;
    }
}

/// <summary>
/// Unit tests targeting DependencyFileManager.ReadVersionDetailsXmlAsync only.
/// </summary>
[TestFixture]
public class DependencyFileManager_ReadVersionDetailsXmlAsync_Tests
{
    private const string MinimalVersionDetailsXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
              </ProductDependencies>
              <ToolsetDependencies>
              </ToolsetDependencies>
            </Dependencies>
            """;

    /// <summary>
    /// Validates that ReadVersionDetailsXmlAsync:
    /// - Computes the correct file path using the optional relativeBasePath.
    /// - Passes repoUri and branch verbatim to IGitRepo.GetFileContentsAsync.
    /// - Successfully parses the returned XML content.
    /// </summary>
    /// <param name="relativeBase">Optional relative base path; when null, the default VersionFiles.VersionDetailsXml is used.</param>
    /// <param name="repoUri">Repository URI to forward unchanged.</param>
    /// <param name="branch">Branch name to forward unchanged.</param>
    [Test]
    [TestCase(null, "https://github.com/org/repo", "main")]
    [TestCase("eng", "weird:uri://?x=1", "feature/áéí")]
    [TestCase("sub/dir", "", "")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadVersionDetailsXmlAsync_RelativeBasePath_ComputesExpectedPathAndParsesXml(string relativeBase, string repoUri, string branch)
    {
        // Arrange
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(MinimalVersionDetailsXml);

        var manager = new DependencyFileManager(gitRepo.Object, versionDetailsParser.Object, logger.Object);

        var basePath = relativeBase == null ? null : new UnixPath(relativeBase);
        var expectedPath = basePath == null
            ? VersionFiles.VersionDetailsXml
            : (string)(basePath / VersionFiles.VersionDetailsXml);

        // Act
        var document = await manager.ReadVersionDetailsXmlAsync(repoUri, branch, basePath);

        // Assert
        gitRepo.Verify(r => r.GetFileContentsAsync(
                expectedPath,
                repoUri,
                branch),
            Times.Once);

        // Basic XML validation via AwesomeAssertions
        Assertions.Assert.That(document, Is.NotNull);
        Assertions.Assert.That(document.DocumentElement, Is.NotNull);
        Assertions.Assert.That(document.DocumentElement.Name, Is.EqualTo("Dependencies"));

        // Log entries: start + success
        logger.Verify(l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// Ensures that when the content is invalid XML:
    /// - ReadVersionDetailsXmlAsync triggers one Debug log (start) and one Error log (failure).
    /// - The XmlException bubbles up to the caller.
    /// - IGitRepo.GetFileContentsAsync is called with the default Version.Details.xml path when no relative base is provided.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadVersionDetailsXmlAsync_InvalidXml_LogsErrorAndRethrows()
    {
        // Arrange
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("<<<not-xml>>>");

        var manager = new DependencyFileManager(gitRepo.Object, versionDetailsParser.Object, logger.Object);

        // Act
        XmlException caught = null;
        try
        {
            var _ = await manager.ReadVersionDetailsXmlAsync("repo", "branch");
        }
        catch (XmlException ex)
        {
            caught = ex;
        }

        // Assert
        gitRepo.Verify(r => r.GetFileContentsAsync(
                VersionFiles.VersionDetailsXml,
                "repo",
                "branch"),
            Times.Once);

        // Exactly 1 Debug (start) and 1 Error (failure)
        logger.Verify(l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);

        logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);

        Assertions.Assert.That(caught, Is.NotNull);
    }

    /// <summary>
    /// Verifies that repoUri and branch strings are forwarded verbatim to IGitRepo.GetFileContentsAsync across edge-case inputs,
    /// and that valid XML content is parsed without throwing.
    /// Inputs include empty, whitespace, very long, and special-character URIs and branch names.
    /// </summary>
    /// <param name="repoUri">Repository URI forwarded without modification.</param>
    /// <param name="branch">Branch name forwarded without modification.</param>
    [Test]
    [TestCase("", "")]
    [TestCase(" ", " ")]
    [TestCase("https://github.com/org/repo", "main")]
    [TestCase("custom+scheme://host/path?x=1&y=2", "release/1.0")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadVersionDetailsXmlAsync_RepoUriAndBranchVariants_ForwardedVerbatim(string repoUri, string branch)
    {
        // Arrange
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(MinimalVersionDetailsXml);

        var manager = new DependencyFileManager(gitRepo.Object, versionDetailsParser.Object, logger.Object);

        // Act
        var _ = await manager.ReadVersionDetailsXmlAsync(repoUri, branch);

        // Assert
        gitRepo.Verify(r => r.GetFileContentsAsync(
                VersionFiles.VersionDetailsXml,
                repoUri,
                branch),
            Times.Once);

        // Successful read => 2 Debug logs
        logger.Verify(l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Exactly(2));
    }
}


/// <summary>
/// Unit tests targeting DependencyFileManager.ReadVersionPropsAsync only.
/// </summary>
[TestFixture]
public class DependencyFileManager_ReadVersionPropsAsync_Tests
{
    private const string MinimalVersionsPropsXml = @"<?xml version=""1.0"" encoding=""utf-8""?><Project><PropertyGroup/></Project>";

    /// <summary>
    /// Verifies that ReadVersionPropsAsync:
    /// - Computes the correct path using GetVersionFilePath(VersionFiles.VersionsProps, relativeBasePath)
    /// - Forwards repoUri and branch verbatim to IGitRepo.GetFileContentsAsync
    /// - Logs start and success debug messages
    /// Inputs cover relativeBasePath being null and various non-null values.
    /// </summary>
    /// <param name="relativeBasePathStr">Optional base path; when null, default path is used.</param>
    /// <param name="expectedPath">Expected combined path passed to GetFileContentsAsync.</param>
    [Test]
    [TestCase(null, "eng/Versions.props")]
    [TestCase("eng", "eng/eng/Versions.props")]
    [TestCase("relative", "relative/eng/Versions.props")]
    [TestCase("base/sub", "base/sub/eng/Versions.props")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadVersionPropsAsync_RelativeBasePathVariants_CallsRepoWithCorrectPathAndLogs(string relativeBasePathStr, string expectedPath)
    {
        // Arrange
        const string repoUri = "https://example/repo";
        const string branch = "main";
        var relativeBasePath = relativeBasePathStr == null ? null : new UnixPath(relativeBasePathStr);

        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(MinimalVersionsPropsXml);

        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Loose).Object;

        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new DependencyFileManager(
            gitRepo.Object,
            parser,
            logger.Object);

        // Act
        var _ = await sut.ReadVersionPropsAsync(repoUri, branch, relativeBasePath);

        // Assert
        gitRepo.Verify(r => r.GetFileContentsAsync(
                expectedPath,
                repoUri,
                branch),
            Times.Once);

        logger.Verify(l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeast(2));
    }

    /// <summary>
    /// Ensures that when the returned file content is invalid XML, ReadVersionPropsAsync:
    /// - Logs an error
    /// - Propagates the XmlException
    /// Inputs: repoUri = "repo", branch = "branch", relativeBasePath = null (default path is used).
    /// Expected: XmlException is thrown; ILogger receives an Error log; IGitRepo is called with "eng/Versions.props".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadVersionPropsAsync_InvalidXml_LogsErrorAndRethrows()
    {
        // Arrange
        const string repoUri = "repo";
        const string branch = "branch";
        const string expectedPath = "eng/Versions.props";

        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("not xml");

        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Loose).Object;

        var sut = new DependencyFileManager(
            gitRepo.Object,
            parser,
            logger.Object);

        // Act
        try
        {
            var _ = await sut.ReadVersionPropsAsync(repoUri, branch);
            // If no exception is thrown, the following verification (expecting an error log) will fail the test.
        }
        catch (XmlException)
        {
            // Assert
            gitRepo.Verify(r => r.GetFileContentsAsync(
                    expectedPath,
                    repoUri,
                    branch),
                Times.Once);

            logger.Verify(l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((_, __) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);

            return;
        }

        // Ensure that if no exception occurred, we still fail by requiring an error log which wouldn't have been written.
        logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }
}



[TestFixture]
public class DependencyFileManager_VersionDetailsPropsExistsAsync_Tests
{
    /// <summary>
    /// Verifies that when the Versions.Details.props file exists at the computed path,
    /// VersionDetailsPropsExistsAsync returns true and calls IGitRepo.GetFileContentsAsync with the expected
    /// file path, repoUri, and branch. The path must reflect the optional relativeBasePath prefix.
    /// Inputs:
    /// - relativeBasePathStr: null or a UNIX-like relative path prefix.
    /// Expected:
    /// - The method returns true.
    /// - IGitRepo.GetFileContentsAsync is invoked exactly once with the computed path,
    ///   repoUri, and branch forwarded unchanged.
    /// </summary>
    [Test]
    [TestCase(null)]
    [TestCase("eng/sub")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task VersionDetailsPropsExistsAsync_FilePresent_ReturnsTrueAndUsesComputedPath(string relativeBasePathStr)
    {
        // Arrange
        var repoUri = "https://example/repo";
        var branch = "main";
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        string? capturedPath = null;

        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((filePath, ru, br) => capturedPath = filePath)
            .ReturnsAsync("<any content>");

        var manager = new DependencyFileManager(gitRepo.Object, parser.Object, logger.Object);

        var relativeBasePath = relativeBasePathStr == null ? null : new UnixPath(relativeBasePathStr);
        var expectedPath = relativeBasePathStr == null
            ? VersionFiles.VersionDetailsProps
            : $"{relativeBasePathStr}/{VersionFiles.VersionDetailsProps}";

        // Act
        var exists = await manager.VersionDetailsPropsExistsAsync(repoUri, branch, relativeBasePath);

        // Assert
        Assert.That(exists, Is.True, "Expected true when file exists.");
        Assert.That(capturedPath, Is.EqualTo(expectedPath), "Computed path should include the optional base path.");
        gitRepo.Verify(r => r.GetFileContentsAsync(
                It.Is<string>(p => p == expectedPath),
                It.Is<string>(ru => ru == repoUri),
                It.Is<string>(br => br == branch)),
            Times.Once);
    }

    /// <summary>
    /// Ensures that when IGitRepo throws DependencyFileNotFoundException for the computed Versions.Details.props path,
    /// the method catches it and returns false.
    /// Inputs:
    /// - repoUri and branch passed through unchanged; relativeBasePath = null to use default path.
    /// Expected:
    /// - The method returns false.
    /// - IGitRepo.GetFileContentsAsync is invoked once with the default VersionDetailsProps path.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task VersionDetailsPropsExistsAsync_FileMissing_ReturnsFalse()
    {
        // Arrange
        var repoUri = "repo";
        var branch = "branch";
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new DependencyFileNotFoundException("not found"));

        var manager = new DependencyFileManager(gitRepo.Object, parser.Object, logger.Object);

        // Act
        var exists = await manager.VersionDetailsPropsExistsAsync(repoUri, branch, null);

        // Assert
        Assert.That(exists, Is.False, "Expected false when file is missing.");
        gitRepo.Verify(r => r.GetFileContentsAsync(
                It.Is<string>(p => p == VersionFiles.VersionDetailsProps),
                It.Is<string>(ru => ru == repoUri),
                It.Is<string>(br => br == branch)),
            Times.Once);
    }

    /// <summary>
    /// Verifies that non-DependencyFileNotFoundException errors thrown by IGitRepo.GetFileContentsAsync
    /// are not swallowed and are rethrown by VersionDetailsPropsExistsAsync.
    /// Inputs:
    /// - repoUri, branch, and a non-null relativeBasePath to exercise path combination logic.
    /// Expected:
    /// - The method rethrows the original exception.
    /// - IGitRepo.GetFileContentsAsync is invoked exactly once with the combined path.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void VersionDetailsPropsExistsAsync_UnexpectedError_Rethrows()
    {
        // Arrange
        var repoUri = "r";
        var branch = "b";
        var basePath = new UnixPath("base");
        var expectedPath = $"base/{VersionFiles.VersionDetailsProps}";

        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var ex = new InvalidOperationException("boom");

        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(ex);

        var manager = new DependencyFileManager(gitRepo.Object, parser.Object, logger.Object);

        // Act & Assert
        var thrown = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.VersionDetailsPropsExistsAsync(repoUri, branch, basePath));

        Assert.That(thrown, Is.SameAs(ex), "Original exception instance should be propagated.");
        gitRepo.Verify(r => r.GetFileContentsAsync(
                It.Is<string>(p => p == expectedPath),
                It.Is<string>(ru => ru == repoUri),
                It.Is<string>(br => br == branch)),
            Times.Once);
    }
}



/// <summary>
/// Unit tests targeting DependencyFileManager.ReadNugetConfigAsync.
/// </summary>
[TestFixture]
public class DependencyFileManagerReadNugetConfigAsyncTests
{
    /// <summary>
    /// Verifies that ReadNugetConfigAsync:
    /// - Attempts each NuGet.config name in the order defined by VersionFiles.NugetConfigNames
    /// - Returns the first successfully read XML with the correct Name in the tuple
    /// - Stops attempting further names after the first success
    /// - Forwards the provided repoUri and branch to the underlying IGitRepo
    /// </summary>
    /// <param name="foundIndex">Index within VersionFiles.NugetConfigNames where the file exists (0-based).</param>
    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadNugetConfigAsync_FirstAvailableNameIsReturned_AndStopsAtFirstSuccess(int foundIndex)
    {
        // Arrange
        var allNames = VersionFiles.NugetConfigNames.ToList();
        Assume.That(foundIndex >= 0 && foundIndex < allNames.Count, "foundIndex must be within the bounds of NugetConfigNames.");

        string expectedName = allNames[foundIndex];
        string repoUri = "https://example/repo";
        string branch = "main";
        string validXml = @"<?xml version=""1.0"" encoding=""utf-8""?><configuration />";

        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string filePath, string repo, string br) =>
            {
                if (filePath == expectedName)
                {
                    // Successful content for the matching expected file name
                    return Task.FromResult(validXml);
                }

                // Simulate "not found" for other names
                throw new DependencyFileNotFoundException($"Not found: {filePath}");
            });

        var manager = new DependencyFileManager(
            gitRepo.Object,
            versionDetailsParser.Object,
            logger.Object);

        // Act
        var result = await manager.ReadNugetConfigAsync(repoUri, branch);

        // Assert
        // Validate tuple content without relying on external assertion frameworks
        if (result.Name != expectedName)
        {
            throw new Exception($"Expected Name '{expectedName}' but got '{result.Name}'.");
        }

        if (result.Content == null || result.Content.DocumentElement == null || result.Content.DocumentElement.Name != "configuration")
        {
            throw new Exception("Returned XmlDocument is null or has an unexpected root element.");
        }

        // Verify call counts and arguments
        for (int i = 0; i < allNames.Count; i++)
        {
            var name = allNames[i];
            var times = i <= foundIndex ? Times.Once() : Times.Never();

            gitRepo.Verify(r => r.GetFileContentsAsync(
                    name,
                    repoUri,
                    branch),
                times);
        }
    }

    /// <summary>
    /// Ensures that when none of the NuGet.config variations exist in the repository,
    /// ReadNugetConfigAsync throws DependencyFileNotFoundException with the expected message, and
    /// attempts each variation exactly once in the defined order.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ReadNugetConfigAsync_NoNamesFound_ThrowsDependencyFileNotFoundExceptionWithExpectedMessage()
    {
        // Arrange
        var allNames = VersionFiles.NugetConfigNames.ToList();
        string repoUri = "repo";
        string branch = "branch";
        string expectedFirst = allNames.First();
        string expectedMessage = $"None of the {expectedFirst} variations were found in the repo '{repoUri}' and branch '{branch}'";

        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        gitRepo
            .Setup(r => r.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string, string>((filePath, repo, br) =>
            {
                // Simulate "not found" for all attempted names
                throw new DependencyFileNotFoundException($"Not found: {filePath}");
            });

        var manager = new DependencyFileManager(
            gitRepo.Object,
            versionDetailsParser.Object,
            logger.Object);

        // Act
        try
        {
            var _ = await manager.ReadNugetConfigAsync(repoUri, branch);
        }
        catch (DependencyFileNotFoundException ex)
        {
            // Assert
            if (!string.Equals(ex.Message, expectedMessage, StringComparison.Ordinal))
            {
                throw new Exception($"Expected message '{expectedMessage}', but got '{ex.Message}'.");
            }

            foreach (var name in allNames)
            {
                gitRepo.Verify(r => r.GetFileContentsAsync(
                        name,
                        repoUri,
                        branch),
                    Times.Once);
            }

            return;
        }

        throw new Exception("Expected DependencyFileNotFoundException to be thrown, but no exception was observed.");
    }
}


[TestFixture]
public class DependencyFileManager_RemoveDependencyAsync_Tests
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

    private static string NormalizeLineEndings(string input) => input.Replace("\r\n", "\n").TrimEnd();

    /// <summary>
    /// Verifies that when repoHasVersionDetailsProps is false, RemoveDependencyAsync:
    /// - Removes the dependency from Version.Details.xml and Versions.props
    /// - Optionally removes the tool entry from .config/dotnet-tools.json if it exists
    /// - Commits exactly the expected files with correct content
    /// - Uses the expected commit message format including a trailing single quote
    /// Inputs:
    /// - dotnetToolsExists: whether .config/dotnet-tools.json exists
    /// Expected:
    /// - Commit includes Version.Details.xml and Versions.props, plus tools file if it existed
    /// - Content matches expected outputs where Foo is removed
    /// - Commit message equals "Remove Foo from Version.Details.xml and Version.props'"
    /// </summary>
    [Test]
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task RemoveDependencyAsync_RepoHasVersionDetailsPropsFalse_RemovesFromXmlProps_OptionallyFromDotnetTools(bool dotnetToolsExists)
    {
        // Arrange
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

        string capturedVersionDetails = null;
        string capturedVersionProps = null;
        string capturedDotnetTools = null;
        string capturedCommitMessage = null;

        var repo = new Mock<IGitRepo>(MockBehavior.Strict);
        var repoFactory = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionDetails);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionsProps, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionProps);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsProps, It.IsAny<string>(), It.IsAny<string>()))
            .Throws<DependencyFileNotFoundException>();

        if (dotnetToolsExists)
        {
            repo.Setup(r => r.GetFileContentsAsync(VersionFiles.DotnetToolsConfigJson, It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(DotnetTools);
        }
        else
        {
            repo.Setup(r => r.GetFileContentsAsync(VersionFiles.DotnetToolsConfigJson, It.IsAny<string>(), It.IsAny<string>()))
                .Throws<DependencyFileNotFoundException>();
        }

        repo.Setup(r => r.CommitFilesAsync(
                It.IsAny<List<GitFile>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<List<GitFile>, string, string, string>((files, repoUri, branch, message) =>
            {
                capturedCommitMessage = message;
                foreach (var f in files)
                {
                    if (f.FilePath == VersionFiles.VersionDetailsXml)
                    {
                        capturedVersionDetails = f.Content;
                    }
                    else if (f.FilePath == VersionFiles.VersionsProps)
                    {
                        capturedVersionProps = f.Content;
                    }
                    else if (f.FilePath == VersionFiles.DotnetToolsConfigJson)
                    {
                        capturedDotnetTools = f.Content;
                    }
                }
            })
            .Returns(Task.CompletedTask);

        repoFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        var manager = new DependencyFileManager(repoFactory.Object, new VersionDetailsParser(), logger.Object);

        // Act
        await manager.RemoveDependencyAsync("Foo", string.Empty, string.Empty, null, false);

        // Assert
        NormalizeLineEndings(capturedVersionDetails).Should().Be(NormalizeLineEndings(expectedVersionDetails));
        NormalizeLineEndings(capturedVersionProps).Should().Be(NormalizeLineEndings(expectedVersionProps));

        if (dotnetToolsExists)
        {
            NormalizeLineEndings(capturedDotnetTools).Should().Be(NormalizeLineEndings(expectedDotNetTools));
        }
        else
        {
            capturedDotnetTools.Should().BeNull();
        }

        capturedCommitMessage.Should().Be("Remove Foo from Version.Details.xml and Version.props'");
        repo.Verify(r => r.CommitFilesAsync(It.IsAny<List<GitFile>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    /// <summary>
    /// Ensures that when repoHasVersionDetailsProps is true, RemoveDependencyAsync:
    /// - Commits Version.Details.xml and Version.Details.props (not Versions.props)
    /// - Optionally commits .config/dotnet-tools.json if tools manifest exists
    /// - Uses exactly one commit
    /// Inputs:
    /// - dotnetToolsExists: whether .config/dotnet-tools.json exists
    /// Expected:
    /// - Files committed include Version.Details.xml and Version.Details.props
    /// - Versions.props is not committed
    /// - Tools file is included only when present
    /// </summary>
    [Test]
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task RemoveDependencyAsync_RepoHasVersionDetailsPropsTrue_CommitsVersionDetailsPropsAndNotVersionsProps(bool dotnetToolsExists)
    {
        // Arrange
        var repo = new Mock<IGitRepo>(MockBehavior.Strict);
        var repoFactory = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionDetails);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionsProps, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionProps);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsProps, It.IsAny<string>(), It.IsAny<string>()))
            .Throws<DependencyFileNotFoundException>();

        if (dotnetToolsExists)
        {
            repo.Setup(r => r.GetFileContentsAsync(VersionFiles.DotnetToolsConfigJson, It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(DotnetTools);
        }
        else
        {
            repo.Setup(r => r.GetFileContentsAsync(VersionFiles.DotnetToolsConfigJson, It.IsAny<string>(), It.IsAny<string>()))
                .Throws<DependencyFileNotFoundException>();
        }

        List<GitFile> committedFiles = null;

        repo.Setup(r => r.CommitFilesAsync(
                It.IsAny<List<GitFile>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<List<GitFile>, string, string, string>((files, repoUri, branch, message) =>
            {
                committedFiles = files;
            })
            .Returns(Task.CompletedTask);

        repoFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(repo.Object);
        var manager = new DependencyFileManager(repoFactory.Object, new VersionDetailsParser(), logger.Object);

        // Act
        await manager.RemoveDependencyAsync("Foo", "repo", "branch", null, true);

        // Assert
        committedFiles.Should().NotBeNull();
        committedFiles.Any(f => f.FilePath == VersionFiles.VersionDetailsXml).Should().BeTrue();
        committedFiles.Any(f => f.FilePath == VersionFiles.VersionDetailsProps).Should().BeTrue();
        committedFiles.Any(f => f.FilePath == VersionFiles.VersionsProps).Should().BeFalse();

        if (dotnetToolsExists)
        {
            committedFiles.Any(f => f.FilePath == VersionFiles.DotnetToolsConfigJson).Should().BeTrue();
        }
        else
        {
            committedFiles.Any(f => f.FilePath == VersionFiles.DotnetToolsConfigJson).Should().BeFalse();
        }

        repo.Verify(r => r.CommitFilesAsync(It.IsAny<List<GitFile>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    /// <summary>
    /// Confirms that when the specified dependency does not exist:
    /// - RemoveDependencyAsync does not throw
    /// - A commit still occurs (method always commits returned XML and props)
    /// Inputs:
    /// - dependencyName: "Baz" (non-existent)
    /// Expected:
    /// - No exception
    /// - CommitFilesAsync called exactly once
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task RemoveDependencyAsync_DependencyDoesNotExist_DoesNotThrowAndCommitsOnce()
    {
        // Arrange
        var repo = new Mock<IGitRepo>(MockBehavior.Strict);
        var repoFactory = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionDetails);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionsProps, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionProps);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsProps, It.IsAny<string>(), It.IsAny<string>()))
            .Throws<DependencyFileNotFoundException>();
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.DotnetToolsConfigJson, It.IsAny<string>(), It.IsAny<string>()))
            .Throws<DependencyFileNotFoundException>();

        repo.Setup(r => r.CommitFilesAsync(
                It.IsAny<List<GitFile>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        repoFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(repo.Object);
        var manager = new DependencyFileManager(repoFactory.Object, new VersionDetailsParser(), logger.Object);

        // Act
        Func<Task> act = async () => await manager.RemoveDependencyAsync("Baz", string.Empty, string.Empty, null, false);

        // Assert
        await act.Should().NotThrowAsync();
        repo.Verify(r => r.CommitFilesAsync(It.IsAny<List<GitFile>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }
}


[TestFixture]
public class DependencyFileManager_UpdateVersionDetailsXmlSourceTag_Tests
{
    private const string MinimalVersionDetailsXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
              </ProductDependencies>
              <ToolsetDependencies>
              </ToolsetDependencies>
            </Dependencies>
            """;

    /// <summary>
    /// Ensures that when the <Source> element is missing:
    /// - A new <Source> element is created and prepended as the first child of <Dependencies>.
    /// - The Uri, Mapping, and Sha attributes are set from the provided SourceDependency.
    /// - The BarId attribute is only set when provided.
    /// Inputs:
    /// - hasBarId: whether BarId should be set in the SourceDependency.
    /// - barId: integer value to use when hasBarId is true (including 0 and int.MaxValue edge cases).
    /// Expected:
    /// - Exactly one <Source> node exists.
    /// - The first child of <Dependencies> is <Source>.
    /// - Attributes match the provided dependency values; BarId exists only when hasBarId is true.
    /// </summary>
    [Test]
    [TestCase(false, 0)]
    [TestCase(true, 0)]
    [TestCase(true, int.MaxValue)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void UpdateVersionDetailsXmlSourceTag_NoExistingSource_AddsSourceAndSetsAttributes(bool hasBarId, int barId)
    {
        // Arrange
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var manager = new DependencyFileManager(gitRepo.Object, parser.Object, logger.Object);

        var doc = CreateXmlDocument(MinimalVersionDetailsXml);
        var uri = " https://example.com/repo?x=1&y=2 ";
        var mapping = " src/sub path ";
        var sha = " abcdef0123456789 ";
        int? nullableBarId = hasBarId ? barId : (int?)null;
        var source = new SourceDependency(uri, mapping, sha, nullableBarId);

        // Act
        manager.UpdateVersionDetailsXmlSourceTag(doc, source);

        // Assert
        var sourceNodes = doc.SelectNodes("//Source");
        NUnit.Framework.Assert.That(sourceNodes, Is.Not.Null);
        NUnit.Framework.Assert.That(sourceNodes.Count, Is.EqualTo(1));

        var sourceNode = sourceNodes[0];
        NUnit.Framework.Assert.That(sourceNode.Attributes["Uri"]?.Value, Is.EqualTo(uri));
        NUnit.Framework.Assert.That(sourceNode.Attributes["Mapping"]?.Value, Is.EqualTo(mapping));
        NUnit.Framework.Assert.That(sourceNode.Attributes["Sha"]?.Value, Is.EqualTo(sha));

        var barIdAttr = sourceNode.Attributes["BarId"];
        if (hasBarId)
        {
            NUnit.Framework.Assert.That(barIdAttr, Is.Not.Null);
            NUnit.Framework.Assert.That(barIdAttr.Value, Is.EqualTo(barId.ToString()));
        }
        else
        {
            NUnit.Framework.Assert.That(barIdAttr, Is.Null);
        }

        var dependenciesNode = doc.SelectSingleNode("//Dependencies");
        NUnit.Framework.Assert.That(dependenciesNode, Is.Not.Null);
        NUnit.Framework.Assert.That(dependenciesNode.ChildNodes[0].Name, Is.EqualTo("Source"));
    }

    /// <summary>
    /// Ensures that when a <Source> element already exists:
    /// - Attributes Uri, Mapping, and Sha are updated to the new values.
    /// - The existing BarId attribute remains unchanged when the incoming dependency has BarId = null.
    /// - The node is not moved (i.e., it remains in its original order; Prepend occurs only when node is missing).
    /// Inputs:
    /// - Existing XML with a <Source BarId="123"> placed as the last child under <Dependencies>.
    /// - Incoming SourceDependency has BarId = null.
    /// Expected:
    /// - Exactly one <Source> node remains.
    /// - Updated Uri, Mapping, and Sha match new values.
    /// - BarId remains "123".
    /// - <Source> stays as the last child of <Dependencies>.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void UpdateVersionDetailsXmlSourceTag_ExistingSource_UpdatesAttributesAndPreservesExistingBarIdWhenNewIsNull()
    {
        // Arrange
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Strict);
        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var manager = new DependencyFileManager(gitRepo.Object, parser.Object, logger.Object);

        var initialXml = """
                <?xml version="1.0" encoding="utf-8"?>
                <Dependencies>
                  <ProductDependencies />
                  <ToolsetDependencies />
                  <Source Uri="old-uri" Mapping="old-map" Sha="old-sha" BarId="123" />
                </Dependencies>
                """;

        var doc = CreateXmlDocument(initialXml);

        var newUri = "https://new/repo";
        var newMapping = "new/mapping";
        var newSha = "newsha";
        int? newBarId = null;
        var source = new SourceDependency(newUri, newMapping, newSha, newBarId);

        // Act
        manager.UpdateVersionDetailsXmlSourceTag(doc, source);

        // Assert
        var sourceNodes = doc.SelectNodes("//Source");
        NUnit.Framework.Assert.That(sourceNodes, Is.Not.Null);
        NUnit.Framework.Assert.That(sourceNodes.Count, Is.EqualTo(1));

        var sourceNode = sourceNodes[0];
        NUnit.Framework.Assert.That(sourceNode.Attributes["Uri"]?.Value, Is.EqualTo(newUri));
        NUnit.Framework.Assert.That(sourceNode.Attributes["Mapping"]?.Value, Is.EqualTo(newMapping));
        NUnit.Framework.Assert.That(sourceNode.Attributes["Sha"]?.Value, Is.EqualTo(newSha));
        NUnit.Framework.Assert.That(sourceNode.Attributes["BarId"]?.Value, Is.EqualTo("123"));

        var dependenciesNode = doc.SelectSingleNode("//Dependencies");
        NUnit.Framework.Assert.That(dependenciesNode, Is.Not.Null);
        NUnit.Framework.Assert.That(dependenciesNode.LastChild?.Name, Is.EqualTo("Source"));
    }

    private static XmlDocument CreateXmlDocument(string xml)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(xml);
        return document;
    }
}


[TestFixture]
public class DependencyFileManager_GetXmlDocument_Tests
{
    /// <summary>
    /// Verifies that GetXmlDocument loads valid XML successfully both with and without the pseudo-BOM prefix,
    /// and that the returned XmlDocument is configured to preserve whitespace.
    /// Inputs:
    ///  - withBom: whether the input XML string is prefixed by the "∩╗┐" sequence.
    /// Expected:
    ///  - No exception is thrown.
    ///  - Returned XmlDocument is not null, PreserveWhitespace is true, and the root node name matches.
    /// </summary>
    [Test]
    [TestCase(false)]
    [TestCase(true)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetXmlDocument_ValidXmlWithOrWithoutBom_LoadsAndPreservesWhitespace(bool withBom)
    {
        // Arrange
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
        string input = withBom ? "∩╗┐" + xmlWithoutBom : xmlWithoutBom;

        // Act
        XmlDocument doc = DependencyFileManager.GetXmlDocument(input);

        // Assert
        Assert.That(doc, Is.Not.Null);
        Assert.That(doc.PreserveWhitespace, Is.True);
        Assert.That(doc.DocumentElement, Is.Not.Null);
        Assert.That(doc.DocumentElement.Name, Is.EqualTo("Dependencies"));
    }

    /// <summary>
    /// Ensures that GetXmlDocument throws an XmlException for invalid XML inputs.
    /// Inputs:
    ///  - A series of invalid XML strings (empty, whitespace only, random text, malformed structures, or just the prefix).
    /// Expected:
    ///  - XmlException is thrown for each invalid input.
    /// </summary>
    [Test]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\t\n")]
    [TestCase("not xml")]
    [TestCase("<?xml version=\"1.0\" encoding=\"utf-8\"?>")]
    [TestCase("<root>")]
    [TestCase("∩╗┐")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetXmlDocument_InvalidXml_ThrowsXmlException(string invalidXml)
    {
        // Arrange
        // invalidXml provided by [TestCase]

        // Act
        TestDelegate act = () => DependencyFileManager.GetXmlDocument(invalidXml);

        // Assert
        Assert.That(act, Throws.TypeOf<XmlException>());
    }

    /// <summary>
    /// Validates that whitespace within the XML content is preserved by GetXmlDocument,
    /// specifically checking for the presence of XmlWhitespace nodes.
    /// Inputs:
    ///  - XML with indentation and newlines between elements.
    /// Expected:
    ///  - Returned document has PreserveWhitespace = true and contains XmlWhitespace child nodes.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetXmlDocument_WhitespaceIsPreserved_WhitespaceNodesExist()
    {
        // Arrange
        const string xml =
            """
                <?xml version="1.0" encoding="utf-8"?>
                <root>
                  <child>value</child>
                </root>
                """;

        // Act
        XmlDocument doc = DependencyFileManager.GetXmlDocument(xml);

        // Assert
        Assert.That(doc, Is.Not.Null);
        Assert.That(doc.PreserveWhitespace, Is.True);
        Assert.That(doc.DocumentElement, Is.Not.Null);
        bool hasWhitespace = doc.DocumentElement.ChildNodes.OfType<XmlWhitespace>().Any()
                             || doc.DocumentElement.ChildNodes.Cast<XmlNode>().Any(n => n.NodeType == XmlNodeType.Whitespace || n.NodeType == XmlNodeType.SignificantWhitespace);
        Assert.That(hasWhitespace, Is.True);
    }
}


/// <summary>
/// Tests for DependencyFileManager.FlattenLocationsAndSplitIntoGroups focusing on:
/// - Ignoring assets that are not exclusively in Maestro-managed feeds.
/// - Grouping feeds by the repository name extracted via regex.
/// - Deduplicating feeds across assets.
/// - Handling empty and null inputs.
/// </summary>
[TestFixture]
public class DependencyFileManager_FlattenLocationsAndSplitIntoGroups_Tests
{
    /// <summary>
    /// Ensures that when the input dictionary is empty, the method returns an empty result without logging errors.
    /// Input: Empty assetLocationMap.
    /// Expected: Returned dictionary is empty; no error logs are emitted.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void FlattenLocationsAndSplitIntoGroups_EmptyInput_ReturnsEmptyDictionary()
    {
        // Arrange
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Loose);
        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var manager = new DependencyFileManager(gitRepo.Object, parser.Object, logger.Object);

        var assetLocationMap = new Dictionary<string, HashSet<string>>();

        // Act
        var result = manager.FlattenLocationsAndSplitIntoGroups(assetLocationMap);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(0));

        logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that only assets whose feeds are exclusively Maestro-managed are considered,
    /// that feeds are grouped by repository name parsed from the feed URL, and duplicates across assets are deduplicated.
    /// Inputs:
    /// - AssetA: Two managed feeds (pub and int) for repo "dotnet-wpf" (both included).
    /// - AssetB: One managed Azure Storage proxy feed for repo "dotnet-arcade-services" (included).
    /// - AssetC: Mixed managed and non-managed feeds (ignored).
    /// - AssetD: null locations (ignored).
    /// - AssetE: empty set (ignored).
    /// - AssetF: duplicate of a managed feed from AssetA (should be deduplicated).
    /// Expected:
    /// - Result has two keys: "dotnet-wpf" and "dotnet-arcade-services".
    /// - "dotnet-wpf" contains two unique feeds (pub and int).
    /// - "dotnet-arcade-services" contains the Azure Storage proxy feed.
    /// - No error logs are emitted.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void FlattenLocationsAndSplitIntoGroups_MixedInputs_GroupsManagedFeedsAndDeduplicates()
    {
        // Arrange
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Loose);
        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var manager = new DependencyFileManager(gitRepo.Object, parser.Object, logger.Object);

        // Managed (DevAzure) feeds for same repo "dotnet-wpf"
        string devAzurePub = "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-wpf-8182abc8/nuget/v3/index.json";
        string devAzureInt = "https://pkgs.dev.azure.com/dnceng/internal/_packaging/darc-int-dotnet-wpf-deadbeef/nuget/v3/index.json";

        // Managed Azure Storage proxy feed for "dotnet-arcade-services"
        string storageProxy = "https://dotnet-feed-internal.azurewebsites.net/container/dotnet-core-internal/sig/sometoken/se/2024-10-01/darc-int-dotnet-arcade-services-deadbeef08-08/index.json";

        // Non-managed feed (should cause the asset to be ignored)
        string nonManaged = "https://api.nuget.org/v3/index.json";

        var assetLocationMap = new Dictionary<string, HashSet<string>>
            {
                { "AssetA", new HashSet<string> { devAzurePub, devAzureInt } },           // exclusively managed
                { "AssetB", new HashSet<string> { storageProxy } },                       // exclusively managed
                { "AssetC", new HashSet<string> { devAzurePub, nonManaged } },            // mixed -> ignored
                { "AssetD", null },                                                       // null -> ignored
                { "AssetE", new HashSet<string>() },                                      // empty -> ignored
                { "AssetF", new HashSet<string> { devAzurePub } }                         // duplicate feed -> deduped
            };

        // Act
        var result = manager.FlattenLocationsAndSplitIntoGroups(assetLocationMap);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Keys, Is.EquivalentTo(new[] { "dotnet-wpf", "unknown", "dotnet-arcade-services" }.Where(k => k != "unknown"))); // defensive structure
        Assert.That(result.ContainsKey("dotnet-wpf"), Is.True);
        Assert.That(result.ContainsKey("dotnet-arcade-services"), Is.True);

        var wpfFeeds = result["dotnet-wpf"];
        Assert.That(wpfFeeds.Count, Is.EqualTo(2));
        Assert.That(wpfFeeds.Contains(devAzurePub), Is.True);
        Assert.That(wpfFeeds.Contains(devAzureInt), Is.True);

        var dasFeeds = result["dotnet-arcade-services"];
        Assert.That(dasFeeds.Count, Is.EqualTo(1));
        Assert.That(dasFeeds.Contains(storageProxy), Is.True);

        logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
    }

    /// <summary>
    /// Validates that duplicate managed feeds present across multiple assets are returned only once per group.
    /// Inputs:
    /// - Two assets each containing the same single managed feed for repo "dotnet-wpf".
    /// Expected:
    /// - Result contains a single group "dotnet-wpf" and within it the feed appears exactly once.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void FlattenLocationsAndSplitIntoGroups_DuplicateFeedsAcrossAssets_DeduplicatedInResult()
    {
        // Arrange
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Loose);
        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var manager = new DependencyFileManager(gitRepo.Object, parser.Object, logger.Object);

        string feed = "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-wpf-aaaaaaaa/nuget/v3/index.json";

        var assetLocationMap = new Dictionary<string, HashSet<string>>
            {
                { "Asset1", new HashSet<string> { feed } },
                { "Asset2", new HashSet<string> { feed } }
            };

        // Act
        var result = manager.FlattenLocationsAndSplitIntoGroups(assetLocationMap);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ContainsKey("dotnet-wpf"), Is.True);

        var feeds = result["dotnet-wpf"];
        Assert.That(feeds.Count, Is.EqualTo(1));
        Assert.That(feeds.Contains(feed), Is.True);

        logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
    }

    /// <summary>
    /// Confirms that providing a null dictionary throws a NullReferenceException due to direct access of Keys on a null instance.
    /// Input: assetLocationMap = null.
    /// Expected: NullReferenceException is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void FlattenLocationsAndSplitIntoGroups_NullInput_ThrowsNullReferenceException()
    {
        // Arrange
        var gitRepo = new Mock<IGitRepo>(MockBehavior.Loose);
        var parser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var manager = new DependencyFileManager(gitRepo.Object, parser.Object, logger.Object);

        // Act & Assert
        Assert.Throws<NullReferenceException>(() => manager.FlattenLocationsAndSplitIntoGroups(null));
    }
}



[TestFixture]
public class DependencyFileManager_GenerateVersionDetailsProps_Tests
{
    /// <summary>
    /// Verifies that GenerateVersionDetailsProps:
    /// - Produces a valid XmlDocument with XML declaration and project root.
    /// - Creates two PropertyGroup elements (main and alternate) with repo-specific comments.
    /// - Sanitizes dependency names (removes '.' and '-') when generating element names.
    /// - Orders properties by dependency Name ascending within each repo group.
    /// - Skips dependencies marked with SkipProperty but still emits the group comment.
    /// - Sets alternate properties to reference the primary properties via MSBuild syntax.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GenerateVersionDetailsProps_WithMultipleReposAndSkipAndSanitization_GeneratesExpectedStructure()
    {
        // Arrange
        var dependencies = new List<DependencyDetail>
        {
            // Repo group 1 (only skipped dependency; ensures group comment is still present)
            new DependencyDetail
            {
                Name = "Microsoft.DotNet.Arcade.Sdk",
                Version = "8.0.0",
                RepoUri = "https://github.com/dotnet/arcade",
                Commit = "sha-arcade",
                SkipProperty = true
            },
            // Repo group 2 (two dependencies to validate sorting and sanitization)
            new DependencyDetail
            {
                Name = "Foo.Bar",
                Version = "1.2.3",
                RepoUri = "https://github.com/dotnet/foo",
                Commit = "sha-foo"
            },
            new DependencyDetail
            {
                Name = "Baz-Quux",
                Version = "2.3.4",
                RepoUri = "https://github.com/dotnet/foo",
                Commit = "sha-baz"
            },
            // Repo group 3 (single dependency; tests org/repo name extraction on dev.azure.com-like URI)
            new DependencyDetail
            {
                Name = "Zed",
                Version = "0.1.0",
                RepoUri = "https://dev.azure.com/org/project/repo",
                Commit = "sha-zed"
            }
        };

        var versionDetails = new VersionDetails(dependencies, null);

        // Act
        XmlDocument doc = DependencyFileManager.GenerateVersionDetailsProps(versionDetails);

        // Assert
        // 1) XML declaration exists and encoding is utf-8
        doc.ChildNodes.Count.Should().BeGreaterThan(0);
        var decl = doc.ChildNodes.OfType<XmlDeclaration>().FirstOrDefault();
        decl.Should().NotBeNull();
        decl!.Version.Should().Be("1.0");
        decl.Encoding.Should().Be("utf-8");

        // 2) Top-level generated comment exists and contains the expected automation text
        var topLevelComments = doc.ChildNodes.OfType<XmlComment>().ToList();
        topLevelComments.Count.Should().BeGreaterThan(0);
        topLevelComments.Any(c => c.Value.Contains("auto-generated", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();

        // 3) Root element is <Project>, with two PropertyGroup elements
        doc.DocumentElement.Should().NotBeNull();
        doc.DocumentElement!.Name.Should().Be("Project");
        var propertyGroups = doc.DocumentElement.GetElementsByTagName("PropertyGroup").OfType<XmlElement>().ToList();
        propertyGroups.Count.Should().Be(2, "there should be exactly two PropertyGroup elements (primary and alternate)");

        // 4) Repo-specific comments exist in both groups (including the group with only skipped dependencies)
        var expectedRepoComments = new[]
        {
            " dotnet/arcade dependencies ",
            " dotnet/foo dependencies ",
            " project/repo dependencies "
        };

        foreach (var group in propertyGroups)
        {
            var commentTexts = group.ChildNodes.OfType<XmlComment>().Select(c => c.Value).ToList();
            foreach (var expected in expectedRepoComments)
            {
                commentTexts.Any(c => c == expected).Should().BeTrue($"missing expected repo comment '{expected}' in property group");
            }
        }

        // 5) Verify element names and values in the main group (sanitization and sorting)
        // For "Foo.Bar" -> FooBarPackageVersion; for "Baz-Quux" -> BazQuuxPackageVersion; sorted by name => Baz before Foo
        var mainGroup = propertyGroups[0];
        var mainElements = mainGroup.ChildNodes.OfType<XmlElement>()
            .Where(e => e.NodeType == XmlNodeType.Element && !string.Equals(e.Name, "PropertyGroup", StringComparison.Ordinal))
            .Where(e => !e.OuterXml.StartsWith("<!--", StringComparison.Ordinal)) // exclude comments
            .ToList();

        // Only non-skipped dependencies should be generated: Baz-Quux, Foo.Bar, Zed
        mainElements.Count.Should().Be(3);

        // Check order and names
        mainElements[0].Name.Should().Be("BazQuuxPackageVersion");
        mainElements[0].InnerText.Should().Be("2.3.4");

        mainElements[1].Name.Should().Be("FooBarPackageVersion");
        mainElements[1].InnerText.Should().Be("1.2.3");

        mainElements[2].Name.Should().Be("ZedPackageVersion");
        mainElements[2].InnerText.Should().Be("0.1.0");

        // 6) Verify alternate group references the main properties with $()
        var altGroup = propertyGroups[1];
        var altElements = altGroup.ChildNodes.OfType<XmlElement>()
            .Where(e => e.NodeType == XmlNodeType.Element && !e.OuterXml.StartsWith("<!--", StringComparison.Ordinal))
            .ToList();

        altElements.Count.Should().Be(3);
        altElements[0].Name.Should().Be("BazQuuxVersion");
        altElements[0].InnerText.Should().Be("$(BazQuuxPackageVersion)");

        altElements[1].Name.Should().Be("FooBarVersion");
        altElements[1].InnerText.Should().Be("$(FooBarPackageVersion)");

        altElements[2].Name.Should().Be("ZedVersion");
        altElements[2].InnerText.Should().Be("$(ZedPackageVersion)");
    }

    /// <summary>
    /// Ensures that when no dependencies are provided:
    /// - The basic XML scaffolding is created (declaration, comment, Project node).
    /// - Two PropertyGroup elements are present.
    /// - No dependency version elements are generated.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GenerateVersionDetailsProps_EmptyDependencies_GeneratesMinimalStructure()
    {
        // Arrange
        var versionDetails = new VersionDetails(Array.Empty<DependencyDetail>(), null);

        // Act
        XmlDocument doc = DependencyFileManager.GenerateVersionDetailsProps(versionDetails);

        // Assert
        doc.DocumentElement.Should().NotBeNull();
        doc.DocumentElement!.Name.Should().Be("Project");

        var propertyGroups = doc.DocumentElement.GetElementsByTagName("PropertyGroup").OfType<XmlElement>().ToList();
        propertyGroups.Count.Should().Be(2);

        // Ensure there are no generated dependency version elements
        var allElements = propertyGroups.SelectMany(pg => pg.ChildNodes.OfType<XmlElement>()).ToList();
        allElements.Count.Should().Be(0);
    }
}
