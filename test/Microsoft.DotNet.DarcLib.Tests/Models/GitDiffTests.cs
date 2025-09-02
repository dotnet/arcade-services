// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Models.UnitTests;

public class GitDiffTests
{
    /// <summary>
    /// Validates that NoDiff constructs a GitDiff where BaseVersion and TargetVersion are set to the input version,
    /// Ahead and Behind are zero, and Valid is true.
    /// Inputs:
    ///  - Various version strings including empty, whitespace, semantic versions, special characters, newlines, and very long strings.
    /// Expected:
    ///  - BaseVersion == version
    ///  - TargetVersion == version
    ///  - Ahead == 0
    ///  - Behind == 0
    ///  - Valid == true
    /// </summary>
    [Test]
    [TestCaseSource(nameof(VersionInputs))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void NoDiff_VersionPropagated_ZeroAheadBehindAndValidTrue(string version)
    {
        // Arrange
        // (No additional arrangement required)

        // Act
        var result = GitDiff.NoDiff(version);

        // Assert
        result.Should().NotBeNull();
        result.BaseVersion.Should().Be(version);
        result.TargetVersion.Should().Be(version);
        result.Ahead.Should().Be(0);
        result.Behind.Should().Be(0);
        result.Valid.Should().BeTrue();
    }

    private static System.Collections.Generic.IEnumerable<string> VersionInputs()
    {
        yield return string.Empty;                // empty
        yield return " ";                         // whitespace
        yield return "1.2.3";                     // semantic version
        yield return "1.2.3-beta+build.45";       // pre-release + metadata
        yield return "line1\nline2";              // with newline
        yield return "special-!@#$%^&*()";        // special characters
        yield return new string('v', 2048);       // very long string
    }

    /// <summary>
    /// Ensures UnknownDiff returns a new GitDiff instance that is explicitly marked as invalid,
    /// and leaves all other properties at their default values.
    /// Inputs:
    ///  - No inputs.
    /// Expected:
    ///  - Result is not null.
    ///  - Valid == false.
    ///  - BaseVersion == null, TargetVersion == null.
    ///  - Ahead == 0, Behind == 0.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void UnknownDiff_NoInputs_ReturnsInvalidGitDiffWithDefaultValues()
    {
        // Arrange
        // (no inputs)

        // Act
        var result = GitDiff.UnknownDiff();

        // Assert
        result.Should().NotBeNull();
        result.Valid.Should().BeFalse();
        result.BaseVersion.Should().BeNull();
        result.TargetVersion.Should().BeNull();
        result.Ahead.Should().Be(0);
        result.Behind.Should().Be(0);
    }

    /// <summary>
    /// Verifies that each call to UnknownDiff returns a distinct instance,
    /// preventing unintended shared state.
    /// Inputs:
    ///  - Two independent calls to UnknownDiff().
    /// Expected:
    ///  - Returned instances are not the same reference.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void UnknownDiff_MultipleCalls_ReturnDistinctInstances()
    {
        // Arrange
        // (no inputs)

        // Act
        var first = GitDiff.UnknownDiff();
        var second = GitDiff.UnknownDiff();

        // Assert
        first.Should().NotBeSameAs(second);
    }
}
