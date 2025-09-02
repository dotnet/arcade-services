// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Moq;
using NUnit.Framework;
using System;
using System.Diagnostics;


namespace Microsoft.DotNet.DarcLib.Tests.Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo.UnitTests;

/// <summary>
/// Tests for Codeflow.GetBranchName:
/// Ensures branch names are constructed as "darc/{Name}/{short-source}-{short-target}" where short SHAs are first 7 chars.
/// Validates both ForwardFlow and Backflow shapes, including boundary and special character cases, and exception paths.
/// </summary>
public class CodeflowTests
{
    /// <summary>
    /// Verifies branch name formatting for valid inputs across flow kinds.
    /// Inputs:
    ///  - flowKind: "forward" or "back" (selects derived type and thus Name).
    ///  - sourceSha, targetSha: strings with length >= 7 (including exact-7 and special/whitespace cases).
    /// Expected:
    ///  - "darc/{flowKind}/{sourceSha[0..7]}-{targetSha[0..7]}".
    /// </summary>
    [TestCase("forward", "abcdef1234567890", "1234567890abcdef")]
    [TestCase("back", "fedcba9876543210", "0fedcba987654321")]
    [TestCase("forward", "1234567", "abcdefg")]
    [TestCase("back", "       ", "       ")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetBranchName_ValidInputs_ReturnsExpectedBranchName(string flowKind, string sourceSha, string targetSha)
    {
        // Arrange
        var flow = CreateFlow(flowKind, sourceSha, targetSha);
        var expected = $"darc/{flowKind}/{sourceSha.Substring(0, 7)}-{targetSha.Substring(0, 7)}";

        // Act
        var branch = flow.GetBranchName();

        // Assert
        branch.Should().Be(expected);
    }

    /// <summary>
    /// Ensures an exception is thrown when SourceSha length is less than 7.
    /// Inputs:
    ///  - flowKind: "forward" or "back".
    ///  - shortSourceSha: string with length &lt; 7 (including empty).
    ///  - validTargetSha: a string with length &gt;= 7.
    /// Expected:
    ///  - ArgumentOutOfRangeException thrown while shortening SourceSha.
    /// </summary>
    [TestCase("forward", "", "1234567")]
    [TestCase("back", "short", "abcdef0")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetBranchName_SourceShaShorterThan7_ThrowsArgumentOutOfRangeException(string flowKind, string shortSourceSha, string validTargetSha)
    {
        // Arrange
        var flow = CreateFlow(flowKind, shortSourceSha, validTargetSha);

        // Act
        Action act = () => flow.GetBranchName();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Ensures an exception is thrown when TargetSha length is less than 7.
    /// Inputs:
    ///  - flowKind: "forward" or "back".
    ///  - validSourceSha: a string with length &gt;= 7.
    ///  - shortTargetSha: string with length &lt; 7 (including empty).
    /// Expected:
    ///  - ArgumentOutOfRangeException thrown while shortening TargetSha.
    /// </summary>
    [TestCase("forward", "abcdef0", "")]
    [TestCase("back", "1234567", "short")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetBranchName_TargetShaShorterThan7_ThrowsArgumentOutOfRangeException(string flowKind, string validSourceSha, string shortTargetSha)
    {
        // Arrange
        var flow = CreateFlow(flowKind, validSourceSha, shortTargetSha);

        // Act
        Action act = () => flow.GetBranchName();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Codeflow CreateFlow(string flowKind, string sourceSha, string targetSha)
    {
        // ForwardFlow(Source -> RepoSha, Target -> VmrSha)
        // Backflow(Source -> VmrSha, Target -> RepoSha)
        return flowKind switch
        {
            "forward" => new ForwardFlow(sourceSha, targetSha),
            "back" => new Backflow(sourceSha, targetSha),
            _ => throw new ArgumentException("Unsupported flow kind", nameof(flowKind))
        };
    }
}
