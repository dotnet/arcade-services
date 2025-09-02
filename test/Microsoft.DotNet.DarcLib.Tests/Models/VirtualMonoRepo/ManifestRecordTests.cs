// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Maestro;
using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.Models.VirtualMonoRepo;


[TestFixture]
public class ManifestRecordTests
{
    [Test]
    public void GitHubCommitUrlIsConstructedTest()
    {
        ISourceComponent record = new RepositoryRecord(
            path: "arcade",
            remoteUri: "https://github.com/dotnet/arcade",
            commitSha: "4ee620cc1b57da45d93135e064d43a83e65bbb6e",
            barId: null);

        record.GetPublicUrl().Should().Be("https://github.com/dotnet/arcade/tree/4ee620cc1b57da45d93135e064d43a83e65bbb6e");

        record = new RepositoryRecord(
            path: "arcade",
            remoteUri: "https://github.com/dotnet/some.git.repo.git",
            commitSha: "4ee620cc1b57da45d93135e064d43a83e65bbb6e",
            barId: null);

        record.GetPublicUrl().Should().Be("https://github.com/dotnet/some.git.repo/tree/4ee620cc1b57da45d93135e064d43a83e65bbb6e");
    }

    [Test]
    public void AzDOCommitUrlIsConstructedTest()
    {
        ISourceComponent record = new RepositoryRecord(
            path: "command-line-api",
            remoteUri: "https://dev.azure.com/dnceng/internal/_git/dotnet-command-line-api",
            commitSha: "4ee620cc1b57da45d93135e064d43a83e65bbb6e",
            barId: null);

        record.GetPublicUrl().Should().Be("https://dev.azure.com/dnceng/internal/_git/dotnet-command-line-api/?version=GC4ee620cc1b57da45d93135e064d43a83e65bbb6e");
    }

    /// <summary>
    /// Verifies that for GitHub repository URIs, GetPublicUrl:
    /// - strips a trailing ".git" suffix if present,
    /// - ensures there is exactly one trailing slash before appending,
    /// - appends "tree/{commitSha}".
    /// Inputs are various GitHub URL forms, including with and without ".git" and trailing slash.
    /// Expected: A canonical GitHub tree URL including the commit SHA.
    /// </summary>
    [TestCase("https://github.com/dotnet/arcade", "sha123", "https://github.com/dotnet/arcade/tree/sha123")]
    [TestCase("https://github.com/dotnet/arcade.git", "sha123", "https://github.com/dotnet/arcade/tree/sha123")]
    [TestCase("https://github.com/dotnet/some.git.repo.git", "abcdef", "https://github.com/dotnet/some.git.repo/tree/abcdef")]
    [TestCase("https://github.com/dotnet/arcade/", "commit", "https://github.com/dotnet/arcade/tree/commit")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetPublicUrl_GitHubUris_TreeUrlReturned(string remoteUri, string commitSha, string expected)
    {
        // Arrange
        ISourceComponent record = new RepositoryRecord(
            path: "arcade",
            remoteUri: remoteUri,
            commitSha: commitSha,
            barId: null);

        // Act
        string actual = record.GetPublicUrl();

        // Assert
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that for Azure DevOps repository URIs, GetPublicUrl appends "/?version=GC{commitSha}".
    /// Inputs include URIs with and without a trailing slash to expose potential double-slash behavior.
    /// Expected: For URIs without trailing slash, a single slash precedes "?version=GC...".
    ///           For URIs with trailing slash, the resulting URL contains a double slash before "?".
    /// </summary>
    [TestCase(
        "https://dev.azure.com/dnceng/internal/_git/dotnet-command-line-api",
        "4ee620cc1b57da45d93135e064d43a83e65bbb6e",
        "https://dev.azure.com/dnceng/internal/_git/dotnet-command-line-api/?version=GC4ee620cc1b57da45d93135e064d43a83e65bbb6e")]
    [TestCase(
        "https://dev.azure.com/org/project/_git/repo/",
        "sha123",
        "https://dev.azure.com/org/project/_git/repo//?version=GCsha123")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetPublicUrl_AzureDevOpsUris_VersionQueryWithLeadingSlashReturned(string remoteUri, string commitSha, string expected)
    {
        // Arrange
        ISourceComponent record = new RepositoryRecord(
            path: "any",
            remoteUri: remoteUri,
            commitSha: commitSha,
            barId: null);

        // Act
        string actual = record.GetPublicUrl();

        // Assert
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that for non-GitHub and non-Azure DevOps URIs (including unknown hosts and local/relative paths),
    /// GetPublicUrl returns the original RemoteUri unchanged, ignoring CommitSha.
    /// Inputs include an unknown host URL, a relative local path, and a non-URL string.
    /// Expected: The method returns the input RemoteUri unchanged.
    /// </summary>
    [TestCase("https://example.com/owner/repo", "deadbeef", "https://example.com/owner/repo")]
    [TestCase("src/local/repo", "abc", "src/local/repo")]
    [TestCase("not a url", "anything", "not a url")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetPublicUrl_UnknownOrLocalUris_Unchanged(string remoteUri, string commitSha, string expected)
    {
        // Arrange
        ISourceComponent record = new RepositoryRecord(
            path: "p",
            remoteUri: remoteUri,
            commitSha: commitSha,
            barId: null);

        // Act
        string actual = record.GetPublicUrl();

        // Assert
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Provides diverse input combinations for the ManifestRecord constructor via SubmoduleRecord,
    /// covering typical, empty, whitespace-only, very long, and special/control character strings.
    /// Inputs:
    ///  - path, remoteUri, commitSha as strings (non-null).
    /// Expected:
    ///  - Constructed record's Path, RemoteUri, and CommitSha properties equal the provided inputs.
    /// </summary>
    public static IEnumerable<TestCaseData> ConstructorInputCases()
    {
        yield return new TestCaseData(
            "src/modules/foo",
            "https://example.com/org/repo.git",
            "abcdef1234567890")
            .SetName("Constructor_TypicalInputs_PropertiesMatch");

        yield return new TestCaseData(
            "",
            "",
            "")
            .SetName("Constructor_EmptyStrings_PropertiesMatch");

        yield return new TestCaseData(
            "   ",
            " \t ",
            " \r\n ")
            .SetName("Constructor_WhitespaceStrings_PropertiesMatch");

        var longPart = new string('x', 4096);
        yield return new TestCaseData(
            longPart,
            "https://example.com/" + longPart + ".git",
            new string('a', 40))
            .SetName("Constructor_VeryLongStrings_PropertiesMatch");

        yield return new TestCaseData(
            "con<>:\"/\\|?*\u2603",
            "ssh://user@host:22/repo\n.git",
            "deadbeef💥\0cafebabe")
            .SetName("Constructor_SpecialAndControlChars_PropertiesMatch");
    }

    /// <summary>
    /// Verifies that the ManifestRecord constructor (exercised via SubmoduleRecord)
    /// assigns the provided string inputs directly to Path, RemoteUri, and CommitSha.
    /// Inputs:
    ///  - Various non-null strings for path, remoteUri, and commitSha including edge cases.
    /// Expected:
    ///  - The constructed instance exposes properties equal to the inputs with no transformations or validation.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(ConstructorInputCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_VariousStringInputs_PropertiesMatch(string path, string remoteUri, string commitSha)
    {
        // Arrange
        // (Inputs provided by TestCaseSource)

        // Act
        var record = new SubmoduleRecord(path, remoteUri, commitSha);

        // Assert
        record.Path.Should().Be(path);
        record.RemoteUri.Should().Be(remoteUri);
        record.CommitSha.Should().Be(commitSha);
    }

    /// <summary>
    /// Validates that comparing to a null ISourceComponent returns 1.
    /// Inputs:
    ///  - A RepositoryRecord with non-empty Path.
    ///  - other = null.
    /// Expected:
    ///  - CompareTo returns 1.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CompareTo_OtherIsNull_ReturnsPositiveOne()
    {
        // Arrange
        var left = new RepositoryRecord(path: "a", remoteUri: "https://repo", commitSha: "sha", barId: null);

        // Act
        var result = left.CompareTo(null);

        // Assert
        result.Should().Be(1);
    }

    /// <summary>
    /// Verifies lexicographical ordering based on Path when other is not null.
    /// Inputs:
    ///  - Pairs of left/right Path values covering empty, equal, and ordering cases.
    /// Expected:
    ///  - The sign of the CompareTo result matches expectedSign (-1, 0, 1).
    /// </summary>
    /// <param name="leftPath">Path of the left ManifestRecord.</param>
    /// <param name="rightPath">Path of the right ISourceComponent.</param>
    /// <param name="expectedSign">Expected sign of the comparison (-1, 0, 1).</param>
    [TestCase("a", "b", -1)]
    [TestCase("b", "a", 1)]
    [TestCase("a", "a", 0)]
    [TestCase("", "a", -1)]
    [TestCase("a", "", 1)]
    [TestCase("abc", "abcd", -1)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CompareTo_PathOrdering_ReturnsExpectedSign(string leftPath, string rightPath, int expectedSign)
    {
        // Arrange
        var left = new RepositoryRecord(path: leftPath, remoteUri: "https://left", commitSha: "sha-left", barId: 0);
        var right = new SubmoduleRecord(path: rightPath, remoteUri: "https://right", commitSha: "sha-right");

        // Act
        var result = left.CompareTo(right);

        // Assert
        Math.Sign(result).Should().Be(expectedSign);
    }

    /// <summary>
    /// Ensures that very long but equal Path strings compare as equal (0).
    /// Inputs:
    ///  - Two components with identical long paths.
    /// Expected:
    ///  - CompareTo returns a value whose sign is 0.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CompareTo_LongEqualPaths_ReturnsZero()
    {
        // Arrange
        var longPath = new string('x', 1024);
        var left = new RepositoryRecord(path: longPath, remoteUri: "https://left", commitSha: "sha-left", barId: 1);
        var right = new RepositoryRecord(path: longPath, remoteUri: "https://right", commitSha: "sha-right", barId: 2);

        // Act
        var result = left.CompareTo(right);

        // Assert
        Math.Sign(result).Should().Be(0);
    }
}



[TestFixture]
public class RepositoryRecordTests
{
    /// <summary>
    /// Verifies that the RepositoryRecord constructor assigns all properties as provided
    /// when barId is a non-null integer at important boundary values.
    /// Inputs:
    ///  - path, remoteUri, commitSha as normal non-empty strings.
    ///  - barId values: int.MinValue, -1, 0, 1, int.MaxValue.
    /// Expected:
    ///  - Object is created without exceptions.
    ///  - Path, RemoteUri, CommitSha equal the inputs exactly.
    ///  - BarId equals the provided barId.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(int.MaxValue)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RepositoryRecord_Constructor_AssignsProperties_ForBoundaryBarIdValues(int barId)
    {
        // Arrange
        var path = "src/repo";
        var remoteUri = "https://example.com/repo.git";
        var commitSha = "abcdef1234567890";

        // Act
        var record = new RepositoryRecord(path, remoteUri, commitSha, barId);

        // Assert
        record.Should().NotBeNull();
        record.Path.Should().Be(path);
        record.RemoteUri.Should().Be(remoteUri);
        record.CommitSha.Should().Be(commitSha);
        record.BarId.Should().Be(barId);
    }

    /// <summary>
    /// Verifies that the RepositoryRecord constructor accepts a null BarId and assigns it as null.
    /// Inputs:
    ///  - Valid non-empty strings for path, remoteUri, commitSha.
    ///  - barId = null.
    /// Expected:
    ///  - Object is created without exceptions.
    ///  - Path, RemoteUri, CommitSha equal the inputs exactly.
    ///  - BarId is null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RepositoryRecord_Constructor_AssignsProperties_WhenBarIdIsNull()
    {
        // Arrange
        var path = "eng/tools";
        var remoteUri = "https://dev.azure.com/org/project/_git/repo";
        var commitSha = "1234567890abcdef";

        // Act
        var record = new RepositoryRecord(path, remoteUri, commitSha, null);

        // Assert
        record.Should().NotBeNull();
        record.Path.Should().Be(path);
        record.RemoteUri.Should().Be(remoteUri);
        record.CommitSha.Should().Be(commitSha);
        record.BarId.Should().BeNull();
    }

    /// <summary>
    /// Ensures the constructor accepts and assigns string edge cases verbatim for Path, RemoteUri, and CommitSha.
    /// Inputs:
    ///  - value: "", whitespace, control characters, or special/unicode characters.
    ///  - barId: a fixed valid integer (123).
    /// Expected:
    ///  - Corresponding properties are set exactly to the provided value without transformation.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t \n")]
    [TestCase("C:\\path\\with\\unicode-Ω\n")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RepositoryRecord_Constructor_AssignsStringInputs_Verbatim(string value)
    {
        // Arrange
        var barId = 123;

        // Act
        var record = new RepositoryRecord(value, value, value, barId);

        // Assert
        record.Should().NotBeNull();
        record.Path.Should().Be(value);
        record.RemoteUri.Should().Be(value);
        record.CommitSha.Should().Be(value);
        record.BarId.Should().Be(barId);
    }

    /// <summary>
    /// Validates that very long strings are accepted and assigned without truncation or alteration.
    /// Inputs:
    ///  - Strings of length 10,000 for path, remoteUri, and commitSha.
    ///  - barId: a fixed valid integer (7).
    /// Expected:
    ///  - Properties match the very long inputs exactly.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RepositoryRecord_Constructor_AssignsVeryLongStrings()
    {
        // Arrange
        var longStr = new string('a', 10_000);
        var barId = 7;

        // Act
        var record = new RepositoryRecord(longStr, longStr, longStr, barId);

        // Assert
        record.Should().NotBeNull();
        record.Path.Should().Be(longStr);
        record.RemoteUri.Should().Be(longStr);
        record.CommitSha.Should().Be(longStr);
        record.BarId.Should().Be(barId);
    }
}



[TestFixture]
public class SubmoduleRecordTests
{
    /// <summary>
    /// Validates that the SubmoduleRecord constructor assigns provided values verbatim to
    /// the base ManifestRecord properties without normalization or validation.
    /// Inputs:
    ///  - path: Various string inputs including empty, whitespace, very long, Unicode, and special/control characters.
    ///  - remoteUri: Various string inputs as above.
    ///  - commitSha: Various string inputs as above.
    /// Expected:
    ///  - The constructed instance exposes Path, RemoteUri, and CommitSha exactly matching the inputs.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(ConstructorCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SubmoduleRecord_Constructor_AssignsProperties_Verbatim(string path, string remoteUri, string commitSha)
    {
        // Arrange
        // Inputs provided by TestCaseSource.

        // Act
        var record = new SubmoduleRecord(path, remoteUri, commitSha);

        // Assert
        record.Path.Should().Be(path);
        record.RemoteUri.Should().Be(remoteUri);
        record.CommitSha.Should().Be(commitSha);
    }

    private static IEnumerable<TestCaseData> ConstructorCases()
    {
        // Typical values
        yield return new TestCaseData(
            "src/modules/modA",
            "https://github.com/org/repo.git",
            "abc123"
        ).SetName("TypicalValues_AssignedVerbatim");

        // Empty strings
        yield return new TestCaseData(
            string.Empty,
            string.Empty,
            string.Empty
        ).SetName("EmptyStrings_AssignedVerbatim");

        // Whitespace-only strings
        yield return new TestCaseData(
            "   ",
            " \t ",
            "   "
        ).SetName("WhitespaceOnlyStrings_AssignedVerbatim");

        // Very long strings
        var longPath = new string('a', 2048);
        var longUri = "https://example.com/" + new string('b', 1500);
        var longSha = new string('c', 64);
        yield return new TestCaseData(
            longPath,
            longUri,
            longSha
        ).SetName("VeryLongStrings_AssignedVerbatim");

        // Special characters and Unicode (including control chars)
        var unicodePath = "路径/модуль/😀\n\t";
        var unicodeUri = "ssh://git@例子.测试/仓库?参数=✓#片段\n";
        var unicodeSha = "çömmit-ßha-✓\t\n";
        yield return new TestCaseData(
            unicodePath,
            unicodeUri,
            unicodeSha
        ).SetName("UnicodeAndControlCharacters_AssignedVerbatim");
    }
}
