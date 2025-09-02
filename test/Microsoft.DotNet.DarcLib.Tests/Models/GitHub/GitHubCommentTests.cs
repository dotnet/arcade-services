// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.GitHub;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.Models.GitHub.UnitTests;

public class GitHubCommentTests
{
    /// <summary>
    /// Verifies that the constructor assigns the provided comment body to the Body property without modification.
    /// Inputs:
    ///  - Various string inputs including empty, whitespace-only, special characters, and very long strings.
    /// Expected:
    ///  - An instance is created successfully.
    ///  - The Body property equals the provided input string.
    /// </summary>
    /// <param name="input">The comment body to initialize the GitHubComment with.</param>
    [TestCaseSource(nameof(ValidBodies))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_AssignsBody_ForVariousStrings(string input)
    {
        // Arrange
        var commentBody = input;

        // Act
        var comment = new GitHubComment(commentBody);

        // Assert
        comment.Body.Should().Be(commentBody);
    }

    private static IEnumerable<string> ValidBodies()
    {
        yield return "simple";
        yield return string.Empty;
        yield return " ";
        yield return " \t ";
        yield return "\t\n\r";
        yield return "line1\nline2\tend\0mid\u0001\u001F\"'\\🔥🚀";
        yield return new string('a', 10_000);
    }
}
