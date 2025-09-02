// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.GitHub;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.Models.GitHub.UnitTests;

/// <summary>
/// Tests for GitHubPullRequest constructor to ensure it assigns all properties correctly.
/// Focuses on edge cases for string parameters: nulls, empty, whitespace, long, and special characters.
/// </summary>
public class GitHubPullRequestTests
{
    /// <summary>
    /// Validates that the constructor assigns Title, Body, Head, and Base from inputs exactly as provided.
    /// Inputs:
    ///  - title, body, head, baseBranch covering normal, null, empty, whitespace, long, and special character values.
    /// Expected:
    ///  - The resulting instance has properties equal to the corresponding inputs without throwing exceptions.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(Constructor_AssignsProperties_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GitHubPullRequest_Ctor_AssignsProperties_AsProvided(string title, string body, string head, string baseBranch)
    {
        // Arrange
        // Inputs provided by TestCaseSource.

        // Act
        var pr = new GitHubPullRequest(title, body, head, baseBranch);

        // Assert
        pr.Title.Should().Be(title);
        pr.Body.Should().Be(body);
        pr.Head.Should().Be(head);
        pr.Base.Should().Be(baseBranch);
    }

    private static System.Collections.IEnumerable Constructor_AssignsProperties_Cases()
    {
        // Typical valid values
        yield return new TestCaseData("Fix bug in resolver", "This PR fixes issue #123.", "feature/fix-resolver", "main")
            .SetName("Ctor_Assigns_NormalValues");

        // All nulls (no nullability annotations in source; constructor should accept and assign nulls)
        yield return new TestCaseData(null, null, null, null)
            .SetName("Ctor_Assigns_NullValues");

        // All empty strings
        yield return new TestCaseData(string.Empty, string.Empty, string.Empty, string.Empty)
            .SetName("Ctor_Assigns_EmptyStrings");

        // All whitespace-only strings
        yield return new TestCaseData("   ", "\t\r\n", " ", "\n")
            .SetName("Ctor_Assigns_WhitespaceStrings");

        // Very long strings and special characters
        var longTitle = new string('T', 4096);
        var longBody = new string('B', 8192) + " Δ✓漢字\n\t\r";
        var specialHead = "feature/🔥-emoji_üñïçødë-branch";
        var specialBase = "release/1.0.0-β";
        yield return new TestCaseData(longTitle, longBody, specialHead, specialBase)
            .SetName("Ctor_Assigns_LongAndSpecialCharacterStrings");
    }
}
