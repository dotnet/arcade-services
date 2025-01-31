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
            <Dependency Name="foo" Version="1.0.0">
              <Uri>https://github.com/dotnet/foo</Uri>
              <Sha>sha1</Sha>
            </Dependency>
            <Dependency Name="bar" Version="1.0.0">
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
            <fooPackageVersion>1.0.0</fooPackageVersion>
            <barPackageVersion>1.0.0</barPackageVersion>
          </PropertyGroup>
        </Project>
        """;

    [Test]
    public async Task RemoveDependencyShouldRemoveDependency()
    {
        var expectedVersionDetails = """
        <?xml version="1.0" encoding="utf-8"?>
        <Dependencies>
          <!-- Elements contains all product dependencies -->
          <ProductDependencies>
            <Dependency Name="bar" Version="1.0.0">
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
                <barPackageVersion>1.0.0</barPackageVersion>
              </PropertyGroup>
            </Project>
            """;


        var tmpVersionDetailsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tmpVersionPropsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        DependencyDetail dependency = new()
        {
            Name = "foo"
        };

        Mock<IGitRepo> repo = new();
        Mock<IGitRepoFactory> repoFactory = new();

        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionDetailsXml, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionDetails);
        repo.Setup(r => r.GetFileContentsAsync(VersionFiles.VersionProps, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(VersionProps);
        repo.Setup(r => r.CommitFilesAsync(
            It.Is<List<GitFile>>(files =>
                files.Count == 2 && files.Any(f => f.FilePath == VersionFiles.VersionDetailsXml) && files.Any(f => f.FilePath == VersionFiles.VersionProps)),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()))
            .Callback<List<GitFile>, string, string, string>((files, repoUri, branch, commitMessage) =>
            {
                File.WriteAllText(tmpVersionDetailsPath, files[0].Content);
                File.WriteAllText(tmpVersionPropsPath, files[1].Content);
            });

        repoFactory.Setup(repoFactory => repoFactory.CreateClient(It.IsAny<string>())).Returns(repo.Object);

        DependencyFileManager manager = new(
            repoFactory.Object,
            new VersionDetailsParser(),
            NullLogger.Instance);

        try
        {
            await manager.RemoveDependencyAsync(dependency, string.Empty, string.Empty);

            File.ReadAllText(tmpVersionDetailsPath).Replace("\r\n", "\n").TrimEnd().Should()
                .Be(expectedVersionDetails.Replace("\r\n", "\n").TrimEnd());
            File.ReadAllText(tmpVersionPropsPath).Replace("\r\n", "\n").TrimEnd().Should()
                .Be(expectedVersionProps.Replace("\r\n", "\n").TrimEnd());
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
        }
    }
}
