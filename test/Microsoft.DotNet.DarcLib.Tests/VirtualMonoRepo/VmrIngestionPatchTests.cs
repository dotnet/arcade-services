// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class VmrIngestionPatchTests
{
    /// <summary>
    /// Verifies that the constructor assigns Path verbatim for various inputs.
    /// Inputs:
    ///  - path: different string edge cases including empty, whitespace, Windows and Unix-like values, and Unicode.
    ///  - applicationPath: null to isolate Path behavior.
    /// Expected:
    ///  - Path equals the provided path string exactly (no normalization or trimming).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("p.patch")]
    [TestCase("")]
    [TestCase(" \t")]
    [TestCase("C:\\windows\\path\\file.patch")]
    [TestCase("/unix/path/file.patch")]
    [TestCase("特殊/路径.patch")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_VariousPathValues_AssignsPathVerbatim(string pathInput)
    {
        // Arrange
        string applicationPath = null;

        // Act
        var patch = new VmrIngestionPatch(pathInput, applicationPath);

        // Assert
        patch.Path.Should().Be(pathInput);
    }

    /// <summary>
    /// Verifies that the constructor sets ApplicationPath to null when applicationPath is null,
    /// and otherwise creates a UnixPath that normalizes backslashes to forward slashes while preserving other characters.
    /// Inputs:
    ///  - applicationPath: null, empty, whitespace, Windows-style, Unix-style, mixed slashes, spaces, and Unicode.
    ///  - path: a fixed non-empty string to ensure object construction.
    /// Expected:
    ///  - If applicationPath is null, ApplicationPath is null.
    ///  - Otherwise, ApplicationPath is not null and its string representation equals the normalized (Unix-style) path.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(null, null, TestName = "Constructor_ApplicationPathNull_ApplicationPathIsNull")]
    [TestCase("", "", TestName = "Constructor_ApplicationPathEmpty_ApplicationPathEmptyUnix")]
    [TestCase(" \t", " \t", TestName = "Constructor_ApplicationPathWhitespace_ApplicationPathPreserved")]
    [TestCase("dir\\sub\\file.patch", "dir/sub/file.patch", TestName = "Constructor_ApplicationPathWindowsSlashes_NormalizedToUnix")]
    [TestCase("dir/sub/file.patch", "dir/sub/file.patch", TestName = "Constructor_ApplicationPathUnixSlashes_Unchanged")]
    [TestCase("/root\\dir//file", "/root/dir//file", TestName = "Constructor_ApplicationPathMixedSlashes_BackslashesReplacedOnly")]
    [TestCase("a b\\c d", "a b/c d", TestName = "Constructor_ApplicationPathSpaces_PreservedAndSlashesNormalized")]
    [TestCase("特殊\\路径", "特殊/路径", TestName = "Constructor_ApplicationPathUnicode_SlashesNormalized")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_VariousApplicationPathValues_AssignsApplicationPathNormalizedOrNull(string applicationPathInput, string expectedNormalized)
    {
        // Arrange
        var path = "any.patch";

        // Act
        var patch = new VmrIngestionPatch(path, applicationPathInput);

        // Assert
        if (expectedNormalized == null)
        {
            patch.ApplicationPath.Should().BeNull();
        }
        else
        {
            patch.ApplicationPath.Should().NotBeNull();
            // Validate the normalized UNIX-style path via string representation.
            patch.ApplicationPath.ToString().Should().Be(expectedNormalized);
        }

        // Path should always be assigned verbatim.
        patch.Path.Should().Be(path);
    }

    /// <summary>
    /// Parameterized test validating the constructor sets Path and computes ApplicationPath from SourceMapping.
    /// Inputs:
    ///  - path: various string values including empty, whitespace, long and special characters.
    ///  - mappingName: various names including empty, whitespace, long and special characters.
    /// Expected:
    ///  - Path equals the provided path string.
    ///  - ApplicationPath equals VmrInfo.GetRelativeRepoSourcesPath(mapping).ToString().
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("simple/path.patch", "arcade", TestName = "Constructor_ValidInputs_SetsPathAndApplicationPathDerivedFromMapping_Simple")]
    [TestCase("", "arcade", TestName = "Constructor_ValidInputs_SetsPathAndApplicationPathDerivedFromMapping_EmptyPath")]
    [TestCase(" ", "whitespace", TestName = "Constructor_ValidInputs_SetsPathAndApplicationPathDerivedFromMapping_WhitespacePath")]
    [TestCase("~!@#$%^&()_+|{}:\"<>?`-=[]\\;',./", "name-with-specials", TestName = "Constructor_ValidInputs_SetsPathAndApplicationPathDerivedFromMapping_SpecialChars")]
    [TestCase("a-a-a-a-a-a-a-a-a-a-a-a-a-a-a-a-a-a-a-a-a-a", "", TestName = "Constructor_ValidInputs_SetsPathAndApplicationPathDerivedFromMapping_EmptyMappingName")]
    [TestCaseSource(nameof(LongInputCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_ValidInputs_SetsPathAndApplicationPathDerivedFromMapping(string path, string mappingName)
    {
        // Arrange
        var mapping = new SourceMapping(
            Name: mappingName,
            DefaultRemote: "https://example.org/repo.git",
            DefaultRef: "main",
            Include: new[] { "src/**" },
            Exclude: new[] { "**/*.tmp" },
            DisableSynchronization: false,
            Version: null);

        // Act
        var patch = new VmrIngestionPatch(path, mapping);

        // Assert
        patch.Path.Should().Be(path);

        var expectedAppPath = VmrInfo.GetRelativeRepoSourcesPath(mapping);
        patch.ApplicationPath.Should().NotBeNull();
        patch.ApplicationPath?.ToString().Should().Be(expectedAppPath.ToString());
    }

    private static readonly TestCaseData[] LongInputCases = new[]
    {
            new TestCaseData(new string('a', 1024), "long-name")
                .SetName("Constructor_ValidInputs_SetsPathAndApplicationPathDerivedFromMapping_VeryLongPath"),
            new TestCaseData("path", new string('b', 512))
                .SetName("Constructor_ValidInputs_SetsPathAndApplicationPathDerivedFromMapping_VeryLongMappingName"),
            new TestCaseData(new string('x', 2048), new string('y', 2048))
                .SetName("Constructor_ValidInputs_SetsPathAndApplicationPathDerivedFromMapping_VeryLongPathAndMappingName")
        };
}
