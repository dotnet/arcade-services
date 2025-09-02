// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Moq;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.Tests.Microsoft.DotNet.DarcLib.Models.AzureDevOps.UnitTests;

public class AzureDevOpsRefTests
{
    /// <summary>
    /// Ensures OldObjectId is only assigned when a non-empty value is provided.
    /// Inputs:
    ///  - oldObjectId: null, empty, whitespace, control characters, special chars, and a very long string.
    /// Expected:
    ///  - OldObjectId remains null when oldObjectId is null or empty string; otherwise equals the provided value.
    /// </summary>
    [TestCaseSource(nameof(OldObjectIdCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_SetsOldObjectId_OnlyWhenNonEmpty(string oldObjectId, string expectedOldObjectId)
    {
        // Arrange
        var name = "refs/heads/main";
        var sha = "abcdef1234567890";

        // Act
        var sut = new AzureDevOpsRef(name, sha, oldObjectId);

        // Assert
        sut.OldObjectId.Should().Be(expectedOldObjectId);
    }

    /// <summary>
    /// Validates that Name and NewObjectId are directly assigned from inputs without transformation.
    /// Inputs:
    ///  - name and sha covering empty, whitespace, control/special characters, and very long strings.
    /// Expected:
    ///  - Name equals the provided name; NewObjectId equals the provided sha.
    /// </summary>
    [TestCase("refs/heads/main", "abc123")]
    [TestCase("", "nonempty-sha")]
    [TestCase("   ", "   ")]
    [TestCase("feature/🔥-branch", "def456!@#")]
    [TestCase("\n\t", "sha-with-\n")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_AssignsNameAndNewObjectId_WithVariousInputs(string name, string sha)
    {
        // Arrange
        var oldObjectId = "old-sha";

        // Act
        var sut = new AzureDevOpsRef(name, sha, oldObjectId);

        // Assert
        sut.Name.Should().Be(name);
        sut.NewObjectId.Should().Be(sha);
    }

    /// <summary>
    /// Confirms that when the optional oldObjectId parameter is omitted, OldObjectId remains null.
    /// Inputs:
    ///  - name and sha with typical non-empty values; oldObjectId parameter omitted.
    /// Expected:
    ///  - OldObjectId is null.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_OmitsOldObjectId_LeavesOldObjectIdNull()
    {
        // Arrange
        var name = "refs/heads/main";
        var sha = "abcdef1234567890";

        // Act
        var sut = new AzureDevOpsRef(name, sha);

        // Assert
        sut.OldObjectId.Should().BeNull();
    }

    private static System.Collections.IEnumerable OldObjectIdCases()
    {
        yield return new TestCaseData(null, null).SetName("OldObjectId_Null_YieldsNullProperty");
        yield return new TestCaseData(string.Empty, null).SetName("OldObjectId_Empty_YieldsNullProperty");
        yield return new TestCaseData(" ", " ").SetName("OldObjectId_Whitespace_Assigned");
        yield return new TestCaseData("\t\n", "\t\n").SetName("OldObjectId_ControlChars_Assigned");
        yield return new TestCaseData("abcDEF-123_!@", "abcDEF-123_!@").SetName("OldObjectId_SpecialChars_Assigned");
        yield return new TestCaseData(new string('x', 2048), new string('x', 2048)).SetName("OldObjectId_VeryLong_Assigned");
    }
}
