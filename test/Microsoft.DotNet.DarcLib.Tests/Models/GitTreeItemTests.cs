// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Moq;
using NUnit.Framework;
using System;

namespace Microsoft.DotNet.DarcLib.Models.UnitTests;

public class GitTreeItemTests
{
    /// <summary>
    /// Verifies that reading Type without initializing it returns null.
    /// Input: A new GitTreeItem instance with no properties set.
    /// Expected: The Type getter returns null.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Type_WhenNotInitialized_ReturnsNull()
    {
        // Arrange
        var item = new GitTreeItem();

        // Act
        var actual = item.Type;

        // Assert
        actual.Should().BeNull();
    }

    /// <summary>
    /// Ensures the init accessor stores the lowercase version of the provided value.
    /// Inputs: Various casing and content for the Type init value.
    /// Expected: The Type property returns the lowercased string (culture-sensitive ToLower).
    /// </summary>
    [TestCase("blob", "blob")]
    [TestCase("Blob", "blob")]
    [TestCase("BLOB", "blob")]
    [TestCase("tree", "tree")]
    [TestCase("TrEe", "tree")]
    [TestCase("commit", "commit")]
    [TestCase("", "")]
    [TestCase(" ", " ")]
    [TestCase("123", "123")]
    [TestCase("BlOb with Spaces", "blob with spaces")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Type_InitWithCasedValue_IsLowercased(string input, string expected)
    {
        // Arrange
        var item = new GitTreeItem { Type = input };

        // Act
        var actual = item.Type;

        // Assert
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that IsBlob returns true only when the Type property equals "blob" after being lowercased by the init accessor.
    /// Inputs:
    ///  - Various string values assigned to Type with differing case, surrounding whitespace, empty string, and non-matching values.
    /// Expected:
    ///  - True for "blob" in any casing, false otherwise (including whitespace variations and other words).
    /// </summary>
    /// <param name="typeInput">The value assigned to the Type init-only property.</param>
    /// <param name="expected">The expected boolean result of IsBlob.</param>
    [Test]
    [TestCase("blob", true)]
    [TestCase("BLOB", true)]
    [TestCase("Blob", true)]
    [TestCase("tree", false)]
    [TestCase("commit", false)]
    [TestCase("", false)]
    [TestCase(" blob", false)]
    [TestCase("blob ", false)]
    [TestCase("blobx", false)]
    [TestCase(" \t\n", false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsBlob_TypeVariants_ReturnsExpected(string typeInput, bool expected)
    {
        // Arrange
        var item = new GitTreeItem
        {
            Sha = string.Empty,
            Path = string.Empty,
            Type = typeInput
        };

        // Act
        var isBlob = item.IsBlob();

        // Assert
        isBlob.Should().Be(expected);
    }

    /// <summary>
    /// Ensures that when Type is not initialized (remains its default), IsBlob safely returns false.
    /// Inputs:
    ///  - GitTreeItem instance without setting the Type property in the object initializer.
    /// Expected:
    ///  - IsBlob returns false (no exception is thrown).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsBlob_TypeNotInitialized_ReturnsFalse()
    {
        // Arrange
        var item = new GitTreeItem
        {
            Sha = string.Empty,
            Path = string.Empty
            // Type not set; internal _type remains default
        };

        // Act
        var isBlob = item.IsBlob();

        // Assert
        isBlob.Should().BeFalse();
    }

    /// <summary>
    /// Verifies IsTree returns expected boolean for various Type values, including casing and near-miss strings.
    /// Inputs:
    ///  - typeValue: value assigned to the Type property (lowercased by init accessor).
    /// Expected:
    ///  - True only when the normalized Type equals "tree"; otherwise false.
    /// </summary>
    [TestCase("tree", true, Description = "Exact match 'tree' should return true.")]
    [TestCase("TREE", true, Description = "Uppercase value is lowercased by init and should match.")]
    [TestCase("TrEe", true, Description = "Mixed case value should be lowercased to 'tree'.")]
    [TestCase("blob", false, Description = "Non-tree type should return false.")]
    [TestCase("commit", false, Description = "Non-tree type should return false.")]
    [TestCase("", false, Description = "Empty string should not match 'tree'.")]
    [TestCase(" tree ", false, Description = "Whitespace around should not match since no trim happens.")]
    [TestCase("trees", false, Description = "Superstring should not match exactly.")]
    [TestCase("tre", false, Description = "Substring should not match exactly.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsTree_VariousTypeValues_ReturnsExpected(string typeValue, bool expected)
    {
        // Arrange
        var item = new GitTreeItem { Type = typeValue };

        // Act
        var result = item.IsTree();

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Ensures IsTree returns false when Type has not been initialized (backing field remains null).
    /// Inputs:
    ///  - GitTreeItem instance with Type not set.
    /// Expected:
    ///  - False, because null does not equal "tree".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsTree_WhenTypeNotInitialized_ReturnsFalse()
    {
        // Arrange
        var item = new GitTreeItem();

        // Act
        var result = item.IsTree();

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that IsCommit returns the expected boolean for various Type values.
    /// Inputs:
    ///  - Type set via init to different strings including exact "commit", mixed casing, other valid types, empty/whitespace, and near-misses.
    /// Expected:
    ///  - True only when Type normalizes (lowercases) to exactly "commit"; otherwise false.
    /// </summary>
    /// <param name="typeValue">The value assigned to the Type property during initialization.</param>
    /// <param name="expected">The expected result from IsCommit.</param>
    [Test]
    [TestCase("commit", true)]
    [TestCase("COMMIT", true)]
    [TestCase("CoMmIt", true)]
    [TestCase("blob", false)]
    [TestCase("tree", false)]
    [TestCase("", false)]
    [TestCase(" ", false)]
    [TestCase("commit ", false)]
    [TestCase(" commit", false)]
    [TestCase("commitx", false)]
    [TestCase("xcommit", false)]
    [TestCase("co mmit", false)]
    [TestCase("comm\tit", false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsCommit_VariousTypeValues_ReturnsExpected(string typeValue, bool expected)
    {
        // Arrange
        var item = new GitTreeItem
        {
            Type = typeValue
        };

        // Act
        var result = item.IsCommit();

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Ensures IsCommit returns false when the Type property was never initialized.
    /// Inputs:
    ///  - GitTreeItem created without setting Type.
    /// Expected:
    ///  - IsCommit returns false (since Type is null internally and does not equal "commit").
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsCommit_TypeNotInitialized_ReturnsFalse()
    {
        // Arrange
        var item = new GitTreeItem();

        // Act
        var result = item.IsCommit();

        // Assert
        result.Should().BeFalse();
    }
}
