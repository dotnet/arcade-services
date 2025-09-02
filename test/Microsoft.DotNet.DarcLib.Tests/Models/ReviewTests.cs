// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.Tests.Microsoft.DotNet.DarcLib.Models.UnitTests;

/// <summary>
/// Tests for the Review model constructor to ensure state and URL are assigned as provided.
/// </summary>
public class ReviewTests
{
    /// <summary>
    /// Validates that the constructor correctly assigns the Status and Url properties
    /// for each defined ReviewState value.
    /// Inputs:
    ///  - state: One of the defined ReviewState enum values.
    ///  - url: A typical non-empty URL string.
    /// Expected:
    ///  - Status equals the provided state.
    ///  - Url equals the provided url.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(ReviewState.Approved, "https://example.org/a")]
    [TestCase(ReviewState.ChangesRequested, "https://example.org/changes")]
    [TestCase(ReviewState.Commented, "https://example.org/commented")]
    [TestCase(ReviewState.Rejected, "https://example.org/rejected")]
    [TestCase(ReviewState.Pending, "https://example.org/pending")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Review_Ctor_AssignsStateAndUrl_ForDefinedEnumValues(ReviewState state, string url)
    {
        // Arrange

        // Act
        var review = new Review(state, url);

        // Assert
        review.Status.Should().Be(state);
        review.Url.Should().Be(url);
    }

    /// <summary>
    /// Ensures the constructor accepts and preserves various URL string edge cases.
    /// Inputs:
    ///  - url: Edge case strings including empty, whitespace, special characters, control characters, long strings, and relative-like paths.
    ///  - state: A valid enum value.
    /// Expected:
    ///  - Url equals the provided url verbatim.
    ///  - Status equals the provided state.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(UrlEdgeCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Review_Ctor_AllowsVariousUrlStrings_PreservesValue(string url)
    {
        // Arrange
        var state = ReviewState.Approved;

        // Act
        var review = new Review(state, url);

        // Assert
        review.Status.Should().Be(state);
        review.Url.Should().Be(url);
    }

    /// <summary>
    /// Verifies that the constructor stores out-of-range enum values as-is,
    /// since no validation is performed.
    /// Inputs:
    ///  - rawState: Integer values outside the defined ReviewState range (including min and max int).
    ///  - url: A typical non-empty URL string.
    /// Expected:
    ///  - Status equals the casted enum value.
    ///  - Url equals the provided url.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(123456)]
    [TestCase(int.MaxValue)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Review_Ctor_AssignsOutOfRangeEnumValues_AsIs(int rawState)
    {
        // Arrange
        var state = (ReviewState)rawState;
        var url = "https://example.org/out-of-range";

        // Act
        var review = new Review(state, url);

        // Assert
        review.Status.Should().Be(state);
        review.Url.Should().Be(url);
    }

    public static IEnumerable<string> UrlEdgeCases()
    {
        yield return string.Empty;                       // empty
        yield return "   ";                              // whitespace
        yield return "/relative/path";                   // relative-like
        yield return "file:///C:/path/with spaces";      // spaces and scheme
        yield return "https://exa mple.org/spa ces";     // spaces in URL
        yield return "https://example.org/q?ä=✓&ß=%20";  // unicode and encoded
        yield return "line1\nline2\tend\0";              // control characters including null terminator
        yield return new string('x', 8192);              // very long string
    }
}
