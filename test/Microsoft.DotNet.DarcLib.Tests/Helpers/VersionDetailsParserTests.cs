// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.Helpers;

[TestFixture]
public class VersionDetailsParserTests
{
    [Test]
    public void AreDependencyMetadataParsedTest()
    {
        const string VersionDetailsXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <Dependency Name="NETStandard.Library.Ref" Version="2.1.0" Pinned="true">
                  <Uri>https://github.com/dotnet/core-setup</Uri>
                  <Sha>7d57652f33493fa022125b7f63aad0d70c52d810</Sha>
                </Dependency>
                <Dependency Name="NuGet.Build.Tasks" Version="6.4.0-preview.1.51" CoherentParentDependency="Microsoft.NET.Sdk">
                  <Uri>https://github.com/nuget/nuget.client</Uri>
                  <Sha>745617ea6fc239737c80abb424e13faca4249bf1</Sha>
                  <SourceBuildTarball RepoName="nuget-client" />
                </Dependency>
              </ProductDependencies>
              <ToolsetDependencies>
                <Dependency Name="Microsoft.DotNet.Arcade.Sdk" Version="7.0.0-beta.22426.1">
                  <Uri>https://github.com/dotnet/arcade</Uri>
                  <Sha>692746db3f08766bc29e91e826ff15e5e8a82b44</Sha>
                  <SourceBuild RepoName="arcade" ManagedOnly="true" TarballOnly="true" />
                </Dependency>
              </ToolsetDependencies>
            </Dependencies>
            """;

        var parser = new VersionDetailsParser();
        var versionDetails = parser.ParseVersionDetailsXml(VersionDetailsXml);

        versionDetails.Dependencies.Should().HaveCount(3);
        versionDetails.Dependencies.Should().Contain(d => d.Name == "NETStandard.Library.Ref"
            && d.Version == "2.1.0"
            && d.RepoUri == "https://github.com/dotnet/core-setup"
            && d.Commit == "7d57652f33493fa022125b7f63aad0d70c52d810"
            && d.Pinned
            && d.CoherentParentDependencyName == null
            && d.SourceBuild == null
            && d.Type == DependencyType.Product);

        versionDetails.Dependencies.Should().Contain(d => d.Name == "NuGet.Build.Tasks"
            && d.Version == "6.4.0-preview.1.51"
            && d.RepoUri == "https://github.com/nuget/nuget.client"
            && d.Commit == "745617ea6fc239737c80abb424e13faca4249bf1"
            && !d.Pinned
            && d.CoherentParentDependencyName == "Microsoft.NET.Sdk"
            && d.SourceBuild != null
            && d.SourceBuild.RepoName == "nuget-client"
            && !d.SourceBuild.ManagedOnly
            && d.Type == DependencyType.Product);

        versionDetails.Dependencies.Should().Contain(d => d.Name == "Microsoft.DotNet.Arcade.Sdk"
            && d.Version == "7.0.0-beta.22426.1"
            && d.RepoUri == "https://github.com/dotnet/arcade"
            && d.Commit == "692746db3f08766bc29e91e826ff15e5e8a82b44"
            && !d.Pinned
            && d.CoherentParentDependencyName == null
            && d.SourceBuild != null
            && d.SourceBuild.RepoName == "arcade"
            && d.SourceBuild.ManagedOnly
            && d.SourceBuild.TarballOnly
            && d.Type == DependencyType.Toolset);

        versionDetails.Source.Should().BeNull();
    }

    [Test]
    public void EmptyXmlIsHandledTest()
    {
        const string VersionDetailsXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies></Dependencies>
            """;

        var parser = new VersionDetailsParser();
        var versionDetails = parser.ParseVersionDetailsXml(VersionDetailsXml);
        versionDetails.Dependencies.Should().BeEmpty();
    }

    [Test]
    public void UnknownCategoryIsRecognizedTest()
    {
        const string VersionDetailsXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <Something>
                <Dependency Name="Microsoft.DotNet.Arcade.Sdk" Version="7.0.0-beta.22426.1">
                  <Uri>https://github.com/dotnet/arcade</Uri>
                  <Sha>692746db3f08766bc29e91e826ff15e5e8a82b44</Sha>
                  <SourceBuild RepoName="arcade" ManagedOnly="true" TarballOnly="true" />
                </Dependency>
              </Something>
            </Dependencies>
            """;

        var parser = new VersionDetailsParser();
        var action = () => parser.ParseVersionDetailsXml(VersionDetailsXml);
        action.Should().Throw<DarcException>().WithMessage("Unknown dependency type*Something*");
    }

    [Test]
    public void InvalidBooleanIsRecognizedTest()
    {
        const string VersionDetailsXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <Dependency Name="Microsoft.DotNet.Arcade.Sdk" Version="7.0.0-beta.22426.1">
                  <Uri>https://github.com/dotnet/arcade</Uri>
                  <Sha>692746db3f08766bc29e91e826ff15e5e8a82b44</Sha>
                  <SourceBuild RepoName="arcade" ManagedOnly="foobar" />
                </Dependency>
              </ProductDependencies>
            </Dependencies>
            """;

        var parser = new VersionDetailsParser();
        var action = () => parser.ParseVersionDetailsXml(VersionDetailsXml);
        action.Should().Throw<DarcException>().WithMessage("*is not a valid boolean*");
    }

    [Test]
    public void IsVmrCodeflowParsedTest()
    {
        const string VersionDetailsXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <Source Mapping="SomeRepo" Uri="https://github.com/dotnet/dotnet" Sha="86ba5fba7c39323011c2bfc6b713142affc76171" BarId="23412" />
              <ProductDependencies>
                <Dependency Name="NETStandard.Library.Ref" Version="2.1.0" Pinned="true">
                  <Uri>https://github.com/dotnet/core-setup</Uri>
                  <Sha>7d57652f33493fa022125b7f63aad0d70c52d810</Sha>
                </Dependency>
                <Dependency Name="NuGet.Build.Tasks" Version="6.4.0-preview.1.51" CoherentParentDependency="Microsoft.NET.Sdk">
                  <Uri>https://github.com/nuget/nuget.client</Uri>
                  <Sha>745617ea6fc239737c80abb424e13faca4249bf1</Sha>
                  <SourceBuildTarball RepoName="nuget-client" />
                </Dependency>
              </ProductDependencies>
              <ToolsetDependencies>
                <Dependency Name="Microsoft.DotNet.Arcade.Sdk" Version="7.0.0-beta.22426.1">
                  <Uri>https://github.com/dotnet/arcade</Uri>
                  <Sha>692746db3f08766bc29e91e826ff15e5e8a82b44</Sha>
                  <SourceBuild RepoName="arcade" ManagedOnly="true" TarballOnly="true" />
                </Dependency>
              </ToolsetDependencies>
            </Dependencies>
            """;

        var parser = new VersionDetailsParser();
        var versionDetails = parser.ParseVersionDetailsXml(VersionDetailsXml);

        versionDetails.Source.Should().NotBeNull();
        versionDetails.Source.Uri.Should().Be("https://github.com/dotnet/dotnet");
        versionDetails.Source.Sha.Should().Be("86ba5fba7c39323011c2bfc6b713142affc76171");
        versionDetails.Source.Mapping.Should().Be("SomeRepo");
        versionDetails.Source.BarId.Should().Be(23412);
    }

    [Test]
    public void XmlWithBomCharactersIsParsedTest()
    {
        // Create XML content without BOM
        const string xmlWithoutBom =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <Dependency Name="NETStandard.Library.Ref" Version="2.1.0">
                  <Uri>https://github.com/dotnet/core-setup</Uri>
                  <Sha>7d57652f33493fa022125b7f63aad0d70c52d810</Sha>
                </Dependency>
              </ProductDependencies>
            </Dependencies>
            """;

        var parser = new VersionDetailsParser();
        
        string xmlWithBom = "∩╗┐" + xmlWithoutBom;
        var action = () => parser.ParseVersionDetailsXml(xmlWithBom);
        action.Should().NotThrow<Exception>();
    }
}
