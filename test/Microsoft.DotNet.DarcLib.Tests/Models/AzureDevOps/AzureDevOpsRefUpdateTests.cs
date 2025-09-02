// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.Tests.Microsoft.DotNet.DarcLib.Models.AzureDevOps.UnitTests;

public class AzureDevOpsRefUpdateTests
{
    /// <summary>
    /// Validates that the constructor assigns the provided branch and SHA to Name and OldObjectId respectively.
    /// Inputs:
    ///  - branch: edge-case strings (normal ref, empty, whitespace-only, special chars, very long).
    ///  - currentSha: edge-case strings (normal SHA-like, empty, whitespace, special chars, very long).
    /// Expected:
    ///  - Instance is created without exception.
    ///  - Name equals branch.
    ///  - OldObjectId equals currentSha.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(ValidConstructorCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void AzureDevOpsRefUpdate_ValidInputs_AssignsProperties(string branch, string currentSha)
    {
        // Arrange
        // inputs come from TestCaseSource

        // Act
        var sut = new AzureDevOpsRefUpdate(branch, currentSha);

        // Assert
        sut.Name.Should().Be(branch);
        sut.OldObjectId.Should().Be(currentSha);
    }

    private static IEnumerable<TestCaseData> ValidConstructorCases()
    {
        yield return new TestCaseData(
            "refs/heads/main",
            "0123456789abcdef0123456789abcdef01234567");

        yield return new TestCaseData(
            "",
            "");

        yield return new TestCaseData(
            "   ",
            " \t\n");

        yield return new TestCaseData(
            "refs/heads/feature/äÖ-🚀",
            "deadbeef!!@@##\n");

        yield return new TestCaseData(
            new string('r', 2048),
            new string('a', 4096));
    }
}
