// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Moq;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.Models.AzureDevOps.UnitTests;

[TestFixture]
[Author("Code Testing Agent v0.3.0")]
[Category("auto-generated")]
public class AzureDevOpsRepositoryResourceParameterTests
{
    private static IEnumerable<TestCaseData> ValidConstructorInputs()
    {
        yield return new TestCaseData("refs/heads/main", "1.0.0");
        yield return new TestCaseData(string.Empty, string.Empty);
        yield return new TestCaseData(" ", " ");
        yield return new TestCaseData("refs/heads/🔥-🚀", "v2.0.0-beta+build.123");
        yield return new TestCaseData("name_with_ctrl_\u0000\u0001", "ver_with_specials_!@#$%^&*()");
        yield return new TestCaseData(new string('r', 4096), new string('v', 4096));
    }

    /// <summary>
    /// Validates that the constructor assigns the provided refName and version directly to the RefName and Version properties.
    /// Inputs:
    ///  - refName/version covering empty, whitespace-only, very long, Unicode/special, and typical values.
    /// Expected:
    ///  - The created instance has RefName equal to refName and Version equal to version with no transformation or exception.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(ValidConstructorInputs))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void AzureDevOpsRepositoryResourceParameter_Ctor_AssignsPropertiesAsProvided(string refName, string version)
    {
        // Arrange
        // Inputs are provided by TestCaseSource.

        // Act
        var sut = new AzureDevOpsRepositoryResourceParameter(refName, version);

        // Assert
        sut.RefName.Should().Be(refName);
        sut.Version.Should().Be(version);
    }
}
