// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Moq;
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

    /// <summary>
    /// Verifies that ParseVersionDetailsFile reads the file from disk and forwards the includePinned flag
    /// to the underlying XML parser, which controls whether pinned dependencies are filtered out.
    /// Inputs:
    /// - A temporary Version.Details.xml file containing one pinned and one non-pinned dependency.
    /// - The includePinned flag is varied via TestCase.
    /// Expected:
    /// - When includePinned is true, both dependencies are returned (count = 2).
    /// - When includePinned is false, pinned dependencies are filtered out (count = 1), and no dependency is pinned.
    /// </summary>
    /// <param name="includePinned">Whether pinned dependencies should be included.</param>
    /// <param name="expectedCount">Expected number of dependencies returned.</param>
    [TestCase(true, 2)]
    [TestCase(false, 1)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsFile_IncludePinnedFlag_FiltersPinnedCorrectly(bool includePinned, int expectedCount)
    {
        // Arrange
        var xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <Dependency Name="A" Version="1.0.0">
                  <Uri>https://example/repo</Uri>
                  <Sha>aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa</Sha>
                </Dependency>
                <Dependency Name="B" Version="2.0.0" Pinned="true">
                  <Uri>https://example/repo</Uri>
                  <Sha>bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb</Sha>
                </Dependency>
              </ProductDependencies>
              <ToolsetDependencies />
            </Dependencies>
            """;

        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, xml);

        var sut = new VersionDetailsParser();

        try
        {
            // Act
            var result = sut.ParseVersionDetailsFile(tempFile, includePinned);

            // Assert
            result.Dependencies.Count().Should().Be(expectedCount);
            if (!includePinned)
            {
                result.Dependencies.Any(d => d.Pinned).Should().BeFalse();
            }
            else
            {
                result.Dependencies.Any(d => d.Pinned).Should().BeTrue();
            }
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that ParseVersionDetailsFile throws an ArgumentException when given an empty path.
    /// Inputs:
    /// - path = "" (empty string).
    /// Expected:
    /// - ArgumentException is thrown by the underlying File.ReadAllText call.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsFile_EmptyPath_ThrowsArgumentException()
    {
        // Arrange
        var sut = new VersionDetailsParser();
        var emptyPath = string.Empty;

        // Act
        Action act = () => sut.ParseVersionDetailsFile(emptyPath);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies that ParseVersionDetailsFile throws a FileNotFoundException when the file does not exist.
    /// Inputs:
    /// - path points to a non-existent file in an existing directory.
    /// Expected:
    /// - FileNotFoundException is thrown by the underlying File.ReadAllText call.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsFile_FileMissing_ThrowsFileNotFoundException()
    {
        // Arrange
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "vdparser", Guid.NewGuid().ToString())).FullName;
        var missingPath = Path.Combine(tempDir, "missing-Version.Details.xml");
        var sut = new VersionDetailsParser();

        try
        {
            // Act
            Action act = () => sut.ParseVersionDetailsFile(missingPath);

            // Assert
            act.Should().Throw<FileNotFoundException>();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that ParseVersionDetailsXml throws an Exception with a helpful message
    /// when provided an XmlDocument without a DocumentElement (i.e., empty document).
    /// Inputs:
    /// - document: new XmlDocument() without any elements
    /// Expected:
    /// - Exception is thrown with a message indicating Version.Details.xml came back empty.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsXml_EmptyDocument_ThrowsWithHelpfulMessage()
    {
        // Arrange
        var sut = new VersionDetailsParser();
        var document = new XmlDocument();

        // Act
        Action act = () => sut.ParseVersionDetailsXml(document, includePinned: true);

        // Assert
        act.Should().ThrowExactly<Exception>()
            .WithMessage("There was an error while reading 'eng/Version.Details.xml' and it came back empty. Look for exceptions above.");
    }

    /// <summary>
    /// Verifies that ParseVersionDetailsXml returns an empty dependency collection and null Source
    /// when the XML contains no Dependency elements but a valid root structure.
    /// Inputs:
    /// - XML with <Dependencies> root and empty product/toolset groups
    /// Expected:
    /// - Dependencies collection is empty
    /// - Source is null
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsXml_NoDependencies_ReturnsEmptyAndNullSource()
    {
        // Arrange
        var sut = new VersionDetailsParser();
        var xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies />
              <ToolsetDependencies />
            </Dependencies>
            """;
        var document = LoadXml(xml);

        // Act
        var result = sut.ParseVersionDetailsXml(document, includePinned: true);

        // Assert
        result.Should().NotBeNull();
        result.Dependencies.Count.Should().Be(0);
        result.Source.Should().BeNull();
    }

    /// <summary>
    /// Verifies that ParseVersionDetailsXml filters out pinned dependencies when includePinned is false,
    /// and includes them when includePinned is true. Also confirms that whitespace around values is trimmed.
    /// Inputs:
    /// - XML with three dependencies (one pinned=true, one without Pinned attribute, one Pinned=false) and mixed whitespace
    /// - includePinned parameter varied via TestCase
    /// Expected:
    /// - When includePinned=true: 3 dependencies returned
    /// - When includePinned=false: only non-pinned dependencies returned (2)
    /// - Trimming is applied to values (e.g., name and inner text)
    /// </summary>
    [TestCase(true, 3)]
    [TestCase(false, 2)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsXml_PinnedFilteringAndTrimming_Works(bool includePinned, int expectedCount)
    {
        // Arrange
        var sut = new VersionDetailsParser();
        var xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <Dependency Name=" A " Version=" 1.0.0 " Pinned="true">
                  <Uri> https://repo/A </Uri>
                  <Sha> 111aaa </Sha>
                </Dependency>
                <Dependency Name="  B  " Version="2.0.0">
                  <Uri>  https://repo/B  </Uri>
                  <Sha> 222bbb </Sha>
                </Dependency>
              </ProductDependencies>
              <ToolsetDependencies>
                <Dependency Name="C" Version="3.0.0" Pinned="false">
                  <Uri>https://repo/C</Uri>
                  <Sha>333ccc</Sha>
                </Dependency>
              </ToolsetDependencies>
            </Dependencies>
            """;
        var document = LoadXml(xml);

        // Act
        var result = sut.ParseVersionDetailsXml(document, includePinned);

        // Assert
        result.Should().NotBeNull();
        result.Dependencies.Count.Should().Be(expectedCount);

        var names = result.Dependencies.Select(d => d.Name).ToArray();
        if (includePinned)
        {
            names.Should().BeEquivalentTo(new[] { "A", "B", "C" });
        }
        else
        {
            names.Should().BeEquivalentTo(new[] { "B", "C" });
        }

        // Validate trimming for dependency "B"
        var depB = result.Dependencies.FirstOrDefault(d => d.Name == "B");
        depB.Should().NotBeNull();
        depB.Version.Should().Be("2.0.0");
        depB.RepoUri.Should().Be("https://repo/B");
        depB.Commit.Should().Be("222bbb");
    }

    /// <summary>
    /// Verifies that ParseVersionDetailsXml parses the Source element when present and maps it to VersionDetails.Source correctly.
    /// Inputs:
    /// - XML with a valid <Source Uri="" Sha="" Mapping="" BarId=""/> element
    /// Expected:
    /// - VersionDetails.Source equals the expected SourceDependency (value equality)
    /// - Dependencies collection is empty
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsXml_WithSourceElement_ParsesSourceCorrectly()
    {
        // Arrange
        var sut = new VersionDetailsParser();
        var xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies />
              <ToolsetDependencies />
              <Source Uri="https://example/repo" Sha="abcdef" Mapping="/src" BarId="42" />
            </Dependencies>
            """;
        var document = LoadXml(xml);

        // Act
        var result = sut.ParseVersionDetailsXml(document, includePinned: true);

        // Assert
        result.Dependencies.Count.Should().Be(0);
        result.Source.Should().Be(new SourceDependency("https://example/repo", "/src", "abcdef", 42));
    }

    /// <summary>
    /// Verifies that ParseVersionDetailsXml propagates a DarcException when the dependency has an unknown parent category.
    /// Inputs:
    /// - XML where a Dependency is under an unknown <UnknownCategory> element
    /// Expected:
    /// - DarcException is thrown with an informative message about the unknown dependency type
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsXml_UnknownDependencyCategory_ThrowsDarcException()
    {
        // Arrange
        var sut = new VersionDetailsParser();
        var xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <UnknownCategory>
                <Dependency Name="X" Version="1.0.0">
                  <Uri>https://repo/X</Uri>
                  <Sha>abc</Sha>
                </Dependency>
              </UnknownCategory>
            </Dependencies>
            """;
        var document = LoadXml(xml);

        // Act
        Action act = () => sut.ParseVersionDetailsXml(document, includePinned: true);

        // Assert
        act.Should().ThrowExactly<DarcException>()
           .WithMessage("Unknown dependency type 'UnknownCategory'");
    }

    /// <summary>
    /// Verifies that ParseVersionDetailsXml propagates a DarcException when a boolean attribute contains an invalid value.
    /// Inputs:
    /// - XML with a Dependency that has Pinned="maybe" (invalid boolean)
    /// Expected:
    /// - DarcException is thrown indicating the attribute is not a valid boolean
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsXml_InvalidBooleanAttribute_ThrowsDarcException()
    {
        // Arrange
        var sut = new VersionDetailsParser();
        var xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <Dependency Name="X" Version="1.0.0" Pinned="maybe">
                  <Uri>https://repo/X</Uri>
                  <Sha>abc</Sha>
                </Dependency>
              </ProductDependencies>
            </Dependencies>
            """;
        var document = LoadXml(xml);

        // Act
        Action act = () => sut.ParseVersionDetailsXml(document, includePinned: true);

        // Assert
        act.Should().ThrowExactly<DarcException>()
           .WithMessage("The 'Pinned' attribute is set but the value 'maybe' is not a valid boolean...");
    }

    /// <summary>
    /// Verifies that ParseVersionDetailsXml throws when the Source element is present but missing required attributes.
    /// Inputs:
    /// - XML with <Source> element missing the Uri attribute
    /// Expected:
    /// - DarcException is thrown indicating the missing Uri attribute
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsXml_SourceMissingAttribute_ThrowsDarcException()
    {
        // Arrange
        var sut = new VersionDetailsParser();
        var xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies />
              <ToolsetDependencies />
              <Source Sha="abcdef" Mapping="/src" BarId="42" />
            </Dependencies>
            """;
        var document = LoadXml(xml);

        // Act
        Action act = () => sut.ParseVersionDetailsXml(document, includePinned: true);

        // Assert
        act.Should().ThrowExactly<DarcException>()
           .WithMessage("The XML tag `Source` does not contain a value for attribute `Uri`");
    }

    private static XmlDocument LoadXml(string xml)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(xml);
        return document;
    }

    /// <summary>
    /// Verifies that ParseVersionDetailsXml(XmlDocument,bool) filters out pinned dependencies when includePinned is false,
    /// and keeps all dependencies when includePinned is true.
    /// Inputs:
    /// - XmlDocument with two dependencies: one with Pinned="true" and one without the Pinned attribute.
    /// - includePinned provided via TestCase.
    /// Expected:
    /// - When includePinned == true: both dependencies are returned.
    /// - When includePinned == false: only the non-pinned dependency is returned.
    /// </summary>
    /// <param name="includePinned">Whether pinned dependencies should be included.</param>
    /// <param name="expectedCount">Expected number of dependencies after filtering.</param>
    [Test]
    [TestCase(true, 2)]
    [TestCase(false, 1)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsXml_IncludePinnedControlsFiltering(bool includePinned, int expectedCount)
    {
        // Arrange
        var xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <Dependency Name="PinnedDep" Version="1.0.0" Pinned="true">
                  <Uri>https://example/pinned</Uri>
                  <Sha>abc</Sha>
                </Dependency>
                <Dependency Name="FreeDep" Version="2.0.0">
                  <Uri>https://example/free</Uri>
                  <Sha>def</Sha>
                </Dependency>
              </ProductDependencies>
            </Dependencies>
            """;

        var doc = new XmlDocument();
        doc.LoadXml(xml);
        var sut = new VersionDetailsParser();

        // Act
        VersionDetails result = sut.ParseVersionDetailsXml(doc, includePinned);

        // Assert
        result.Dependencies.Count.Should().Be(expectedCount);
        var names = result.Dependencies.Select(d => d.Name).ToArray();
        if (includePinned)
        {
            names.Should().Contain("PinnedDep");
            names.Should().Contain("FreeDep");
        }
        else
        {
            names.Should().Contain("FreeDep");
            names.Should().NotContain("PinnedDep");
        }
    }

    /// <summary>
    /// Verifies that ParseVersionDetailsXml(XmlDocument,bool) throws an informative exception
    /// when the provided XmlDocument has no DocumentElement (root element).
    /// Inputs:
    /// - An empty XmlDocument without a root element.
    /// Expected:
    /// - Exception thrown with a message indicating Version.Details.xml came back empty.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsXml_DocumentWithoutRoot_ThrowsInformativeException()
    {
        // Arrange
        var emptyDoc = new XmlDocument(); // DocumentElement is null here
        var sut = new VersionDetailsParser();

        // Act
        Action act = () => sut.ParseVersionDetailsXml(emptyDoc, includePinned: true);

        // Assert
        act.Should().Throw<Exception>()
            .And.Message.Should().Contain("Version.Details.xml")
            .And.Message.Should().Contain("came back empty");
    }

    /// <summary>
    /// Verifies that when the XML document has no root element (DocumentElement is null),
    /// the parser throws a generic Exception with a helpful message indicating that the
    /// Version.Details.xml content came back empty.
    /// Input: XmlDocument without a root element.
    /// Expected: Exception is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsXml_DocumentElementMissing_ThrowsHelpfulException()
    {
        // Arrange
        var parser = new VersionDetailsParser();
        var documentWithoutRoot = new XmlDocument(); // DocumentElement == null

        // Act
        Action act = () => parser.ParseVersionDetailsXml(documentWithoutRoot);

        // Assert
        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// Ensures that an empty Dependencies element results in an empty dependency collection
    /// and a null Source entry.
    /// Input: <Dependencies></Dependencies>
    /// Expected: Dependencies is empty; Source is null; no exception thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsXml_EmptyDependencies_ReturnsEmptyCollection()
    {
        // Arrange
        const string xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies></Dependencies>
            """;

        var parser = new VersionDetailsParser();
        var doc = new XmlDocument();
        doc.LoadXml(xml);

        // Act
        var result = parser.ParseVersionDetailsXml(doc);

        // Assert
        result.Dependencies.Should().BeEmpty();
        result.Source.Should().BeNull();
    }

    /// <summary>
    /// Verifies that the includePinned parameter controls whether pinned dependencies are included.
    /// Input: XML with one pinned and one non-pinned dependency, plus a valid Source section.
    /// Expected: When includePinned is true, both dependencies are present; when false, only the non-pinned remains.
    /// Additionally validates that Source is parsed and returned.
    /// </summary>
    /// <param name="includePinned">Flag indicating whether pinned dependencies should be included.</param>
    /// <param name="expectedCount">Expected number of dependencies in the result.</param>
    [TestCase(true, 2)]
    [TestCase(false, 1)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseVersionDetailsXml_IncludePinnedFlag_FiltersPinnedDependencies(bool includePinned, int expectedCount)
    {
        // Arrange
        const string xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <Dependency Name="Pinned.A" Version="1.2.3" Pinned="true">
                  <Uri>https://example.org/repoA</Uri>
                  <Sha>aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa</Sha>
                </Dependency>
                <Dependency Name="Regular.B" Version="2.3.4">
                  <Uri>https://example.org/repoB</Uri>
                  <Sha>bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb</Sha>
                </Dependency>
              </ProductDependencies>
              <Source Uri="https://github.com/org/vmr" Sha="1234567890abcdef" Mapping="src/vmr" />
            </Dependencies>
            """;

        var parser = new VersionDetailsParser();
        var doc = new XmlDocument();
        doc.LoadXml(xml);

        // Act
        var result = parser.ParseVersionDetailsXml(doc, includePinned);

        // Assert
        result.Dependencies.Should().HaveCount(expectedCount);
        if (!includePinned)
        {
            result.Dependencies.Should().OnlyContain(d => !d.Pinned);
        }

        result.Source.Should().NotBeNull();
        result.Source!.Uri.Should().Be("https://github.com/org/vmr");
        result.Source.Mapping.Should().Be("src/vmr");
        result.Source.Sha.Should().Be("1234567890abcdef");
    }

    /// <summary>
    /// Verifies that SerializeSourceDependency produces a single self-closing Source element
    /// with attributes in the exact expected order and spacing, preserving values exactly
    /// (including special characters), and converting null BarId to an empty attribute value.
    /// </summary>
    /// <param name="input">The SourceDependency instance to serialize.</param>
    /// <param name="expected">The exact expected XML string.</param>
    [Test]
    [TestCaseSource(nameof(SerializationCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SerializeSourceDependency_VariousInputs_ProducesExpectedXml(SourceDependency input, string expected)
    {
        // Arrange is provided by test case source.

        // Act
        var result = VersionDetailsParser.SerializeSourceDependency(input);

        // Assert
        result.Should().Be(expected);
    }

    private static IEnumerable<TestCaseData> SerializationCases()
    {
        // 1) Typical values - verify attribute order and spacing exactly
        var typical = new SourceDependency("https://github.com/repo", "mapping", "abc", 42);
        yield return new TestCaseData(
                typical,
                "<Source Uri=\"https://github.com/repo\" Mapping=\"mapping\" Sha=\"abc\" BarId=\"42\" />")
            .SetName("SerializeSourceDependency_AllFields_AttributesOrderedAndValuesPreserved");

        // 2) Null BarId -> empty attribute value
        var nullBarId = new SourceDependency("u", "m", "s", null);
        yield return new TestCaseData(
                nullBarId,
                "<Source Uri=\"u\" Mapping=\"m\" Sha=\"s\" BarId=\"\" />")
            .SetName("SerializeSourceDependency_NullBarId_EmptyAttributeValue");

        // 3) Empty/whitespace strings and int.MaxValue
        var emptyAndWhitespace = new SourceDependency("", " ", "", int.MaxValue);
        yield return new TestCaseData(
                emptyAndWhitespace,
                "<Source Uri=\"\" Mapping=\" \" Sha=\"\" BarId=\"2147483647\" />")
            .SetName("SerializeSourceDependency_EmptyAndWhitespaceStrings_MaxIntBarId_PreservedAsIs");

        // 4) Special characters including quotes and angle brackets, and int.MinValue
        var uriSpecial = "https://ex\"am<ple>.com?x&y";
        var mappingSpecial = "pa th\"<>&";
        var shaSpecial = "dead\"beef<>&";
        var specialChars = new SourceDependency(uriSpecial, mappingSpecial, shaSpecial, int.MinValue);
        var expectedSpecial =
            "<Source Uri=\"https://ex\"am<ple>.com?x&y\" Mapping=\"pa th\"<>&\" Sha=\"dead\"beef<>&\" BarId=\"-2147483648\" />";
        yield return new TestCaseData(
                specialChars,
                expectedSpecial)
            .SetName("SerializeSourceDependency_SpecialCharacters_NoEscapingOrMutation");
    }
}

