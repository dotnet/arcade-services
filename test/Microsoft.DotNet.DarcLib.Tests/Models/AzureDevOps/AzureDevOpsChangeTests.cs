// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Models.AzureDevOps.UnitTests;

public class AzureDevOpsChangeTests
{
    /// <summary>
    /// Verifies that the constructor initializes Item, NewContent, and sets ChangeType to Edit
    /// for a variety of file paths and content strings, including empty and whitespace-only cases.
    /// Inputs:
    ///  - filePath: diverse path strings (empty, whitespace, absolute, Windows, unicode).
    ///  - content: diverse content strings (empty, whitespace, special/control characters).
    /// Expected:
    ///  - Item is not null and Item.Path equals filePath.
    ///  - NewContent is not null and NewContent.Content equals content.
    ///  - NewContent.ContentType defaults to "rawtext" when contentType is not provided (null).
    ///  - ChangeType equals AzureDevOpsChangeType.Edit.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(ValidInitializationCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_ValidInputs_InitializesPropertiesAndSetsEditChangeType(string filePath, string content)
    {
        // Arrange
        var providedContentType = (string)null;

        // Act
        var change = new AzureDevOpsChange(filePath, content, providedContentType);

        // Assert
        change.Should().NotBeNull();
        change.Item.Should().NotBeNull();
        change.Item.Path.Should().Be(filePath);

        change.NewContent.Should().NotBeNull();
        change.NewContent.Content.Should().Be(content);
        change.NewContent.ContentType.Should().Be("rawtext");

        change.ChangeType.Should().Be(AzureDevOpsChangeType.Edit);
    }

    /// <summary>
    /// Ensures the constructor applies contentType correctly:
    ///  - When contentType is null or empty, ContentType remains the default "rawtext".
    ///  - When contentType is non-empty (including whitespace), ContentType equals the supplied value.
    /// Inputs:
    ///  - suppliedContentType: null, empty string, whitespace, and custom types.
    /// Expected:
    ///  - NewContent.ContentType equals expectedContentType per the rules above.
    ///  - ChangeType remains set to Edit.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(null, "rawtext")]
    [TestCase("", "rawtext")]
    [TestCase(" ", " ")]
    [TestCase("base64encoded", "base64encoded")]
    [TestCase("application/json", "application/json")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_ContentTypeVariants_SetsExpectedContentType(string suppliedContentType, string expectedContentType)
    {
        // Arrange
        const string filePath = "file.txt";
        const string content = "payload";

        // Act
        var change = new AzureDevOpsChange(filePath, content, suppliedContentType);

        // Assert
        change.NewContent.Should().NotBeNull();
        change.NewContent.ContentType.Should().Be(expectedContentType);
        change.ChangeType.Should().Be(AzureDevOpsChangeType.Edit);
    }

    private static IEnumerable<TestCaseData> ValidInitializationCases()
    {
        yield return new TestCaseData("repo/file.txt", "content");
        yield return new TestCaseData("/abs/path/file.yml", "line1\nline2");
        yield return new TestCaseData("C:\\a\\b\\c.csproj", "\tTabbed Content\t");
        yield return new TestCaseData("name-🙂-特殊", "!@#$%^&*()<>?\"'\\/ \r\n");
        yield return new TestCaseData(string.Empty, string.Empty);
        yield return new TestCaseData(" ", " ");
        yield return new TestCaseData("very/long/" + new string('a', 1000), new string('b', 2000));
    }
}

/// <summary>
/// Tests for the Item constructor to verify that the provided path is assigned directly to the Path property.
/// </summary>
public class ItemTests
{
    /// <summary>
    /// Ensures the constructor assigns the provided string to the Path property without modification.
    /// Inputs:
    ///  - Various string values including null, empty, whitespace-only, absolute/relative paths, special/unicode characters, and very long strings.
    /// Expected:
    ///  - The created Item instance has Path equal to the input value.
    /// </summary>
    /// <param name="inputPath">The path string to pass to the constructor.</param>
    [Test]
    [TestCaseSource(nameof(PathInputs))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Item_Ctor_AssignsPathExactly(string inputPath)
    {
        // Arrange
        // (No additional arrangement required.)

        // Act
        var item = new Item(inputPath);

        // Assert
        item.Path.Should().Be(inputPath);
    }

    private static System.Collections.Generic.IEnumerable<string> PathInputs()
    {
        yield return null;
        yield return string.Empty;
        yield return " ";
        yield return "\t";
        yield return " \t\n ";
        yield return "file.txt";
        yield return "C:\\dir\\file.txt";
        yield return "/usr/local/bin";
        yield return "inva|id:pa<th>?*\"/name";
        yield return "路径/файл.txt";
        yield return new string('a', 10000);
    }
}

public class NewContentTests
{
    /// <summary>
    /// Verifies that when contentType is null or empty, the constructor preserves the default ContentType ("rawtext").
    /// Inputs:
    ///  - content: various values (null, empty, whitespace, special characters).
    ///  - contentType: null or empty string.
    /// Expected:
    ///  - Content equals the provided content.
    ///  - ContentType remains "rawtext".
    /// </summary>
    [TestCase(null, null)]
    [TestCase("", null)]
    [TestCase(" ", "")]
    [TestCase("unicode-🙂\t\n", "")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void NewContent_NullOrEmptyContentType_DefaultsToRawText(string content, string contentType)
    {
        // Arrange
        const string expectedDefaultType = "rawtext";

        // Act
        var instance = new NewContent(content, contentType);

        // Assert
        instance.Content.Should().Be(content);
        instance.ContentType.Should().Be(expectedDefaultType);
    }

    /// <summary>
    /// Ensures that when a non-empty contentType is provided, it overrides the default ContentType.
    /// Inputs:
    ///  - content: representative strings.
    ///  - contentType: non-empty values including whitespace-only, control characters, and specific known values.
    /// Expected:
    ///  - Content equals the provided content.
    ///  - ContentType equals the provided contentType.
    /// </summary>
    [TestCase("hello", "rawtext")]
    [TestCase("data", "base64")]
    [TestCase("", " ")]
    [TestCase("with-specials", "\t\n")]
    [TestCase("control-char", "\0")]
    [TestCase("json", "application/json")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void NewContent_NonEmptyContentType_OverridesDefault(string content, string contentType)
    {
        // Arrange

        // Act
        var instance = new NewContent(content, contentType);

        // Assert
        instance.Content.Should().Be(content);
        instance.ContentType.Should().Be(contentType);
    }

    /// <summary>
    /// Validates that very long content and contentType values are preserved without truncation or exception.
    /// Inputs:
    ///  - content: a 10,000-character string.
    ///  - contentType: a 256-character string.
    /// Expected:
    ///  - Content and ContentType match the provided values exactly and retain their lengths.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void NewContent_VeryLongContentAndContentType_ArePreserved()
    {
        // Arrange
        var longContent = new string('x', 10_000);
        var longContentType = new string('y', 256);

        // Act
        var instance = new NewContent(longContent, longContentType);

        // Assert
        instance.Content.Should().Be(longContent);
        instance.Content.Length.Should().Be(10_000);
        instance.ContentType.Should().Be(longContentType);
        instance.ContentType.Length.Should().Be(256);
    }
}
