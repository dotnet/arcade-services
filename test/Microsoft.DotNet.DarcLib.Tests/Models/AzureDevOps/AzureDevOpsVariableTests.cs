// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Moq;
using NUnit.Framework;
using System;


namespace Microsoft.DotNet.DarcLib.Models.AzureDevOps.UnitTests;

public class AzureDevOpsVariableTests
{
    /// <summary>
    /// Validates that the constructor assigns the provided value and isSecret flag to the corresponding properties.
    /// Inputs:
    ///  - value: representative strings (normal, empty, whitespace, special chars).
    ///  - isSecret: both true and false.
    /// Expected:
    ///  - Value equals the input string exactly.
    ///  - IsSecret equals the provided boolean.
    /// </summary>
    [TestCase("value", true)]
    [TestCase("", false)]
    [TestCase(" ", false)]
    [TestCase("!@#$%^&*()_+-=[]{}|;:',.<>/?\t\n\r", true)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_WithValueAndIsSecret_SetsProperties(string input, bool isSecret)
    {
        // Arrange
        // (parameters provided by TestCase)

        // Act
        var variable = new AzureDevOpsVariable(input, isSecret);

        // Assert
        variable.Value.Should().Be(input);
        variable.IsSecret.Should().Be(isSecret);
    }

    /// <summary>
    /// Ensures that when the isSecret parameter is omitted, it defaults to false.
    /// Inputs:
    ///  - value: a standard non-empty string.
    /// Expected:
    ///  - IsSecret is false by default.
    ///  - Value equals the input.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_IsSecretOmitted_DefaultsToFalse()
    {
        // Arrange
        var input = "abc";

        // Act
        var variable = new AzureDevOpsVariable(input);

        // Assert
        variable.Value.Should().Be(input);
        variable.IsSecret.Should().Be(false);
    }

    /// <summary>
    /// Verifies that very long strings are accepted and assigned without modification.
    /// Inputs:
    ///  - value: a very long string (10,000 'x' characters).
    ///  - isSecret: false.
    /// Expected:
    ///  - Value equals the long input string.
    ///  - IsSecret equals false.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_WithVeryLongValue_AssignsValueUnchanged()
    {
        // Arrange
        var longValue = new string('x', 10_000);

        // Act
        var variable = new AzureDevOpsVariable(longValue, false);

        // Assert
        variable.Value.Should().Be(longValue);
        variable.IsSecret.Should().Be(false);
    }
}
