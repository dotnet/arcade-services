// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

/// <summary>
/// Tests for SourceMappingParser.ParseMappings which:
///  - Reads mapping file contents via IFileSystem.ReadAllTextAsync.
///  - Delegates parsing to the internal JSON parser and returns the parsed mappings.
///  - Propagates exceptions from file system and parser without alteration.
/// </summary>
public class SourceMappingParserTests
{
    /// <summary>
    /// Ensures ParseMappings:
    ///  - Calls IFileSystem.ReadAllTextAsync with the exact provided path.
    ///  - Parses JSON content into mappings and sets IVmrInfo.ThirdPartyNoticesTemplatePath.
    /// Inputs:
    ///  - mappingFilePath: typical, empty, whitespace, long, Unicode, and Windows-style paths.
    /// Expected:
    ///  - The returned mappings correctly reflect include/exclude merging rules and defaults.
    ///  - ThirdPartyNoticesTemplatePath is set from the JSON.
    ///  - File system is called once with the exact path.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(ParseMappings_Success_PathCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ParseMappings_VariousPaths_ParsesAndSetsVmrInfo(string mappingFilePath)
    {
        // Arrange
        var fileContent = GetValidSourceMappingsJson();

        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        // We only care about setting ThirdPartyNoticesTemplatePath
        vmrInfoMock.SetupProperty(m => m.ThirdPartyNoticesTemplatePath);

        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystemMock
            .Setup(fs => fs.ReadAllTextAsync(mappingFilePath))
            .ReturnsAsync(fileContent);

        var parser = new SourceMappingParser(vmrInfoMock.Object, fileSystemMock.Object);

        // Act
        var result = await parser.ParseMappings(mappingFilePath);

        // Assert
        fileSystemMock.Verify(fs => fs.ReadAllTextAsync(mappingFilePath), Times.Once);
        fileSystemMock.VerifyNoOtherCalls();

        vmrInfoMock.Object.ThirdPartyNoticesTemplatePath.Should().Be("templates/THIRD-PARTY-NOTICES.txt");

        result.Should().NotBeNull();
        result.Count.Should().Be(2);

        var mappings = result.ToList();
        mappings[0].Name.Should().Be("repo1");
        mappings[0].DefaultRemote.Should().Be("https://github.com/org/repo1.git");
        mappings[0].DefaultRef.Should().Be("dev");
        mappings[0].DisableSynchronization.Should().BeTrue();
        mappings[0].Include.Count.Should().Be(2);
        mappings[0].Exclude.Count.Should().Be(2);
        mappings[0].Include.First().Should().Be("**/*");
        mappings[0].Include.Skip(1).First().Should().Be("src/**");
        mappings[0].Exclude.First().Should().Be("**/*.tmp");
        mappings[0].Exclude.Skip(1).First().Should().Be("test/**");

        mappings[1].Name.Should().Be("repo2");
        mappings[1].DefaultRemote.Should().Be("https://github.com/org/repo2.git");
        mappings[1].DefaultRef.Should().Be("main"); // from defaults
        mappings[1].DisableSynchronization.Should().BeFalse();
        mappings[1].Include.Count.Should().Be(1);
        mappings[1].Exclude.Count.Should().Be(1);
        mappings[1].Include.First().Should().Be("Lib/**"); // ignoreDefaults = true, do not prepend defaults
        mappings[1].Exclude.First().Should().Be("Obj/**");

        vmrInfoMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Validates that when IFileSystem.ReadAllTextAsync throws, ParseMappings propagates the exception unchanged
    /// and does not attempt to modify IVmrInfo.
    /// Inputs:
    ///  - mappingFilePath: a sample path.
    /// Expected:
    ///  - InvalidOperationException is thrown.
    ///  - IVmrInfo.ThirdPartyNoticesTemplatePath is not set.
    ///  - File system is called exactly once.
    /// </summary>
    [Test]
    [TestCase("src/source-mappings.json")]
    [TestCase("C:\\vmr\\src\\source-mappings.json")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ParseMappings_FileSystemThrows_ExceptionPropagates(string mappingFilePath)
    {
        // Arrange
        var ex = new InvalidOperationException("boom");
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);

        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystemMock
            .Setup(fs => fs.ReadAllTextAsync(mappingFilePath))
            .ThrowsAsync(ex);

        var parser = new SourceMappingParser(vmrInfoMock.Object, fileSystemMock.Object);

        // Act
        Func<Task> act = async () => await parser.ParseMappings(mappingFilePath);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        fileSystemMock.Verify(fs => fs.ReadAllTextAsync(mappingFilePath), Times.Once);
        fileSystemMock.VerifyNoOtherCalls();

        vmrInfoMock.VerifySet(m => m.ThirdPartyNoticesTemplatePath = It.IsAny<string>(), Times.Never);
        vmrInfoMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures ParseMappings propagates exceptions thrown due to invalid JSON content:
    ///  - "null" JSON causes a custom Exception (failed deserialization).
    ///  - Malformed/empty/whitespace JSON causes JsonException from the deserializer.
    /// Inputs:
    ///  - mappingFilePath: a sample path.
    ///  - fileContent: invalid JSON variants.
    ///  - expectedException: the expected exception type.
    /// Expected:
    ///  - The exact expected exception type is thrown.
    ///  - IVmrInfo.ThirdPartyNoticesTemplatePath is not set.
    ///  - File system is called exactly once.
    /// </summary>
    [Test]
    [TestCase("src/source-mappings.json", "null", typeof(Exception))]
    [TestCase("src/source-mappings.json", "", typeof(JsonException))]
    [TestCase("src/source-mappings.json", "   ", typeof(JsonException))]
    [TestCase("src/source-mappings.json", "not a json", typeof(JsonException))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ParseMappings_InvalidJson_ExceptionsArePropagated(string mappingFilePath, string fileContent, Type expectedException)
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);

        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystemMock
            .Setup(fs => fs.ReadAllTextAsync(mappingFilePath))
            .ReturnsAsync(fileContent);

        var parser = new SourceMappingParser(vmrInfoMock.Object, fileSystemMock.Object);

        // Act
        Func<Task> act = async () => await parser.ParseMappings(mappingFilePath);

        // Assert
        await act.Should().ThrowAsync(expectedException);
        fileSystemMock.Verify(fs => fs.ReadAllTextAsync(mappingFilePath), Times.Once);
        fileSystemMock.VerifyNoOtherCalls();

        vmrInfoMock.VerifySet(m => m.ThirdPartyNoticesTemplatePath = It.IsAny<string>(), Times.Never);
        vmrInfoMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures ParseMappings propagates InvalidOperationException thrown by CreateMapping when required fields
    /// are missing in a mapping entry (Name or DefaultRemote).
    /// Inputs:
    ///  - hasName: whether the mapping includes 'name'.
    ///  - hasDefaultRemote: whether the mapping includes 'defaultRemote'.
    /// Expected:
    ///  - InvalidOperationException is thrown.
    ///  - IVmrInfo.ThirdPartyNoticesTemplatePath is not set.
    /// </summary>
    [Test]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ParseMappings_MissingRequiredFields_CreateMappingThrows(bool hasName, bool hasDefaultRemote)
    {
        // Arrange
        string mappingJson = BuildInvalidMappingJson(hasName, hasDefaultRemote);

        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystemMock
            .Setup(fs => fs.ReadAllTextAsync("path.json"))
            .ReturnsAsync(mappingJson);

        var parser = new SourceMappingParser(vmrInfoMock.Object, fileSystemMock.Object);

        // Act
        Func<Task> act = async () => await parser.ParseMappings("path.json");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        fileSystemMock.Verify(fs => fs.ReadAllTextAsync("path.json"), Times.Once);
        fileSystemMock.VerifyNoOtherCalls();

        vmrInfoMock.VerifySet(m => m.ThirdPartyNoticesTemplatePath = It.IsAny<string>(), Times.Never);
        vmrInfoMock.VerifyNoOtherCalls();
    }

    private static IEnumerable ParseMappings_Success_PathCases()
    {
        yield return "src/source-mappings.json";
        yield return "";
        yield return "   ";
        yield return new string('a', 1024);
        yield return "C:\\vmr\\src\\source-mappings.json";
        yield return "/var/路径/репозиторий/source-mappings.json";
    }

    private static string GetValidSourceMappingsJson()
    {
        // defaults merged unless ignoreDefaults = true on the mapping
        return @"
            {
              ""thirdPartyNoticesTemplatePath"": ""templates/THIRD-PARTY-NOTICES.txt"",
              ""defaults"": {
                ""defaultRef"": ""main"",
                ""include"": [ ""**/*"" ],
                ""exclude"": [ ""**/*.tmp"" ]
              },
              ""mappings"": [
                {
                  ""name"": ""repo1"",
                  ""version"": ""1.0.0"",
                  ""defaultRemote"": ""https://github.com/org/repo1.git"",
                  ""defaultRef"": ""dev"",
                  ""include"": [ ""src/**"" ],
                  ""exclude"": [ ""test/**"" ],
                  ""disableSynchronization"": true
                },
                {
                  ""name"": ""repo2"",
                  ""defaultRemote"": ""https://github.com/org/repo2.git"",
                  ""include"": [ ""Lib/**"" ],
                  ""exclude"": [ ""Obj/**"" ],
                  ""ignoreDefaults"": true
                }
              ]
            }";
    }

    private static string BuildInvalidMappingJson(bool hasName, bool hasDefaultRemote)
    {
        var namePart = hasName ? @"""name"": ""repoX""," : "";
        var defaultRemotePart = hasDefaultRemote ? @"""defaultRemote"": ""https://github.com/org/x.git""," : "";
        return $@"
            {{
              ""defaults"": {{
                ""defaultRef"": ""main"",
                ""include"": [],
                ""exclude"": []
              }},
              ""mappings"": [
                {{
                  {namePart}
                  {defaultRemotePart}
                  ""include"": [ ""**/*"" ],
                  ""exclude"": []
                }}
              ]
            }}";
    }

    /// <summary>
    /// Ensures ParseMappingsFromJson:
    ///  - Deserializes JSON with camelCase properties, comments, and trailing commas.
    ///  - Sets IVmrInfo.ThirdPartyNoticesTemplatePath from the file settings.
    ///  - Merges defaults.Include/Exclude when IgnoreDefaults == false.
    ///  - Does not merge defaults.Include/Exclude when IgnoreDefaults == true.
    ///  - Preserves DefaultRef from mapping, otherwise uses defaults.DefaultRef.
    ///  - Maps Version, DefaultRemote, DisableSynchronization, and other fields correctly.
    /// Inputs:
    ///  - JSON containing defaults and two mappings, including comments and trailing commas.
    /// Expected:
    ///  - Two SourceMapping instances with correctly merged and assigned values.
    ///  - ThirdPartyNoticesTemplatePath set exactly.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ParseMappingsFromJson_ValidJson_ParsesAndMergesDefaults()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupSet(v => v.ThirdPartyNoticesTemplatePath = "tpn/T.txt");
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);

        var sut = new SourceMappingParser(vmrInfo.Object, fileSystem.Object);

        var json = @"
            {
                // Template path with comment
                ""thirdPartyNoticesTemplatePath"": ""tpn/T.txt"",
                ""defaults"": {
                    ""defaultRef"": ""main-default"",
                    ""include"": [ ""common/**"", ],
                    ""exclude"": [ ""*.md"" ]
                },
                ""mappings"": [
                    {
                        ""name"": ""repoA"",
                        ""version"": ""v1"",
                        ""defaultRemote"": ""https://example.org/a.git"",
                        ""defaultRef"": ""develop"",
                        ""include"": [ ""src/**"" ],
                        ""exclude"": [ ""tests/**"" ],
                        ""ignoreDefaults"": false,
                        ""disableSynchronization"": true,
                    },
                    {
                        ""name"": ""repoB"",
                        ""defaultRemote"": ""https://example.org/b.git"",
                        ""ignoreDefaults"": true
                    }
                ]
            }";

        // Act
        var result = sut.ParseMappingsFromJson(json);

        // Assert
        vmrInfo.VerifySet(v => v.ThirdPartyNoticesTemplatePath = "tpn/T.txt", Times.Once);
        vmrInfo.VerifyNoOtherCalls();
        fileSystem.VerifyNoOtherCalls();

        result.Should().NotBeNull();
        result.Count.Should().Be(2);

        var mappingA = result.First(m => m.Name == "repoA");
        mappingA.DefaultRemote.Should().Be("https://example.org/a.git");
        mappingA.DefaultRef.Should().Be("develop");
        mappingA.Include.Should().Equal(new[] { "common/**", "src/**" });
        mappingA.Exclude.Should().Equal(new[] { "*.md", "tests/**" });
        mappingA.DisableSynchronization.Should().BeTrue();
        mappingA.Version.Should().Be("v1");

        var mappingB = result.First(m => m.Name == "repoB");
        mappingB.DefaultRemote.Should().Be("https://example.org/b.git");
        mappingB.DefaultRef.Should().Be("main-default");
        mappingB.Include.Should().BeEmpty();
        mappingB.Exclude.Should().BeEmpty();
        mappingB.DisableSynchronization.Should().BeFalse();
        mappingB.Version.Should().BeNull();
    }

    /// <summary>
    /// Validates that when a required field is missing in a mapping, an InvalidOperationException is thrown,
    /// and ThirdPartyNoticesTemplatePath is still set before the failure.
    /// Inputs:
    ///  - missingField: "name" or "defaultRemote".
    /// Expected:
    ///  - InvalidOperationException with a message indicating which field is missing.
    ///  - Verify that IVmrInfo.ThirdPartyNoticesTemplatePath was set.
    /// </summary>
    [Test]
    [TestCase("name", "Missing `name` in source-mappings.json")]
    [TestCase("defaultRemote", "Missing `defaultremote` in source-mappings.json")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ParseMappingsFromJson_MissingRequiredField_ThrowsInvalidOperationException(string missingField, string expectedMessage)
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupSet(v => v.ThirdPartyNoticesTemplatePath = "x/tpn.txt");
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);

        var sut = new SourceMappingParser(vmrInfo.Object, fileSystem.Object);

        string mappingJson;
        if (missingField == "name")
        {
            mappingJson = @"
                {
                    ""thirdPartyNoticesTemplatePath"": ""x/tpn.txt"",
                    ""defaults"": {},
                    ""mappings"": [
                        {
                            ""defaultRemote"": ""https://example.org/a.git""
                        }
                    ]
                }";
        }
        else
        {
            mappingJson = @"
                {
                    ""thirdPartyNoticesTemplatePath"": ""x/tpn.txt"",
                    ""defaults"": {},
                    ""mappings"": [
                        {
                            ""name"": ""repoX""
                        }
                    ]
                }";
        }

        // Act
        Action act = () => sut.ParseMappingsFromJson(mappingJson);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage(expectedMessage);
        vmrInfo.VerifySet(v => v.ThirdPartyNoticesTemplatePath = "x/tpn.txt", Times.Once);
        vmrInfo.VerifyNoOtherCalls();
        fileSystem.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures invalid JSON content results in appropriate exceptions and no side effects on IVmrInfo.
    /// Inputs:
    ///  - json: invalid syntax or "null".
    ///  - expectedException: type of the expected exception.
    ///  - expectedMessage: when provided, the exact message to be asserted.
    /// Expected:
    ///  - JsonException for invalid syntax.
    ///  - Exception("Failed to deserialize source-mappings.json") when the JSON literal is "null".
    ///  - No property sets on IVmrInfo.
    /// </summary>
    [Test]
    [TestCase("null", typeof(Exception), "Failed to deserialize source-mappings.json")]
    [TestCase("{ invalid ]", typeof(JsonException), null)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ParseMappingsFromJson_InvalidJson_Throws(string json, Type expectedException, string expectedMessage)
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var sut = new SourceMappingParser(vmrInfo.Object, fileSystem.Object);

        // Act
        Action act = () => sut.ParseMappingsFromJson(json);

        // Assert
        if (expectedException == typeof(JsonException))
        {
            act.Should().Throw<JsonException>();
        }
        else
        {
            act.Should().Throw<Exception>().WithMessage(expectedMessage);
        }

        vmrInfo.VerifyNoOtherCalls();
        fileSystem.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies DefaultRef fallback order:
    ///  - When mapping.DefaultRef is missing and defaults.DefaultRef is null,
    ///    the DefaultRef falls back to "main".
    /// Inputs:
    ///  - JSON with defaults.defaultRef explicitly set to null and mapping.defaultRef omitted.
    /// Expected:
    ///  - The produced SourceMapping.DefaultRef equals "main".
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ParseMappingsFromJson_DefaultRefMissing_UsesMainAsFallback()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupSet(v => v.ThirdPartyNoticesTemplatePath = null);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var sut = new SourceMappingParser(vmrInfo.Object, fileSystem.Object);

        var json = @"
            {
                ""defaults"": { ""defaultRef"": null },
                ""mappings"": [
                    {
                        ""name"": ""repo"",
                        ""defaultRemote"": ""https://example.org/r.git"",
                        ""ignoreDefaults"": false
                    }
                ]
            }";

        // Act
        var result = sut.ParseMappingsFromJson(json);

        // Assert
        vmrInfo.VerifySet(v => v.ThirdPartyNoticesTemplatePath = null, Times.Once);
        vmrInfo.VerifyNoOtherCalls();
        fileSystem.VerifyNoOtherCalls();

        result.Should().NotBeNull();
        result.Count.Should().Be(1);
        var mapping = result.Single();
        mapping.DefaultRef.Should().Be("main");
    }

    /// <summary>
    /// Ensures that when defaults.Include/Exclude are null, they are not merged,
    /// and only mapping's Include/Exclude are used.
    /// Inputs:
    ///  - JSON with defaults.include/exclude explicitly null and mapping include/exclude specified.
    /// Expected:
    ///  - Include/Exclude in result equal exactly to mapping's arrays.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ParseMappingsFromJson_DefaultsFiltersNull_DoNotMerge()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupSet(v => v.ThirdPartyNoticesTemplatePath = "");
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var sut = new SourceMappingParser(vmrInfo.Object, fileSystem.Object);

        var json = @"
            {
                ""thirdPartyNoticesTemplatePath"": """",
                ""defaults"": { ""include"": null, ""exclude"": null },
                ""mappings"": [
                    {
                        ""name"": ""repoC"",
                        ""defaultRemote"": ""https://example.org/c.git"",
                        ""include"": [ ""a/**"" ],
                        ""exclude"": [ ""b/**"" ],
                        ""ignoreDefaults"": false
                    }
                ]
            }";

        // Act
        var result = sut.ParseMappingsFromJson(json);

        // Assert
        vmrInfo.VerifySet(v => v.ThirdPartyNoticesTemplatePath = "", Times.Once);
        vmrInfo.VerifyNoOtherCalls();
        fileSystem.VerifyNoOtherCalls();

        result.Should().NotBeNull();
        result.Count.Should().Be(1);
        var mapping = result.Single();
        mapping.Include.Should().Equal(new[] { "a/**" });
        mapping.Exclude.Should().Equal(new[] { "b/**" });
    }

    /// <summary>
    /// Verifies that the constructor successfully creates an instance when provided with valid dependencies.
    /// Inputs:
    ///  - vmrInfoBehavior: Moq behavior for IVmrInfo (Strict or Loose).
    ///  - fileSystemBehavior: Moq behavior for IFileSystem (Strict or Loose).
    /// Expected:
    ///  - No exception is thrown and the created instance is non-null and implements ISourceMappingParser.
    /// </summary>
    /// <param name="vmrInfoBehavior">Moq behavior for IVmrInfo mock.</param>
    /// <param name="fileSystemBehavior">Moq behavior for IFileSystem mock.</param>
    [Test]
    [TestCase(MockBehavior.Strict, MockBehavior.Strict)]
    [TestCase(MockBehavior.Loose, MockBehavior.Loose)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithValidDependencies_CreatesInstance(MockBehavior vmrInfoBehavior, MockBehavior fileSystemBehavior)
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(vmrInfoBehavior);
        var fileSystemMock = new Mock<IFileSystem>(fileSystemBehavior);

        // Act
        var sut = new SourceMappingParser(vmrInfoMock.Object, fileSystemMock.Object);

        // Assert
        sut.Should().NotBeNull();
        sut.Should().BeAssignableTo<ISourceMappingParser>();
    }

    /// <summary>
    /// Ensures ParseMappings reads the specified path and returns mappings based on the JSON content.
    /// Inputs:
    ///  - mappingFilePath variations including empty, whitespace, relative, absolute, long, and special-character paths.
    ///  - File system returns valid JSON with two mappings.
    /// Expected:
    ///  - IFileSystem.ReadAllTextAsync is invoked exactly once with the same mappingFilePath.
    ///  - Returned collection contains two mappings with names "repo1" and "repo2".
    /// </summary>
    [Test]
    [TestCaseSource(nameof(ValidPaths))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ParseMappings_ValidJsonFromFile_ReturnsParsedMappingsAndReadsFromGivenPath(string mappingFilePath)
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Loose);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var json = CreateValidMappingsJson();

        fileSystemMock
            .Setup(fs => fs.ReadAllTextAsync(mappingFilePath))
            .ReturnsAsync(json);

        var parser = new SourceMappingParser(vmrInfoMock.Object, fileSystemMock.Object);

        // Act
        var result = await parser.ParseMappings(mappingFilePath);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Select(m => m.Name).Should().BeEquivalentTo(new[] { "repo1", "repo2" });

        fileSystemMock.Verify(fs => fs.ReadAllTextAsync(mappingFilePath), Times.Once);
    }

    /// <summary>
    /// Verifies that exceptions thrown by the file system read are propagated by ParseMappings.
    /// Inputs:
    ///  - Any mappingFilePath string.
    ///  - IFileSystem.ReadAllTextAsync throws an IOException.
    /// Expected:
    ///  - ParseMappings throws the same IOException.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task ParseMappings_ReadAllTextAsyncThrows_PropagatesIOException()
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Loose);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);

        var ioException = new IOException("boom");
        fileSystemMock
            .Setup(fs => fs.ReadAllTextAsync("file"))
            .ThrowsAsync(ioException);

        var parser = new SourceMappingParser(vmrInfoMock.Object, fileSystemMock.Object);

        // Act
        Func<Task> act = () => parser.ParseMappings("file");

        // Assert
        await act.Should().ThrowAsync<IOException>();
        fileSystemMock.Verify(fs => fs.ReadAllTextAsync("file"), Times.Once);
    }

    private static IEnumerable ValidPaths()
    {
        yield return "";
        yield return " ";
        yield return "relative/path.json";
        yield return "C:\\temp\\mappings.json";
        yield return "/var/tmp/source-mappings.json";
        yield return new string('a', 2048) + ".json";
        yield return "path/with/special/chars/?:*<>|.json";
    }

    private static string CreateValidMappingsJson()
    {
        // Matches ParseMappingsFromJson expectations (camelCase properties, defaults + mappings).
        return @"
            {
              ""defaults"": {
                ""defaultRef"": ""main"",
                ""include"": [ ""src/**"" ],
                ""exclude"": [ ""**/*.md"" ]
              },
              ""mappings"": [
                {
                  ""name"": ""repo1"",
                  ""defaultRemote"": ""https://github.com/org/repo1"",
                  ""include"": [ ""src/repo1/**"" ],
                  ""exclude"": []
                },
                {
                  ""name"": ""repo2"",
                  ""defaultRemote"": ""https://github.com/org/repo2"",
                  ""ignoreDefaults"": true,
                  ""disableSynchronization"": true
                }
              ],
              ""thirdPartyNoticesTemplatePath"": ""/path/to/template""
            }";
    }
}
