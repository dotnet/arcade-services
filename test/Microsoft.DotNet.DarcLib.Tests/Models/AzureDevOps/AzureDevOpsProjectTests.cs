// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.Models.AzureDevOps.UnitTests;

/// <summary>
/// Tests for AzureDevOpsProject constructor behavior.
/// Validates that provided name and id inputs are assigned to corresponding properties without modification.
/// </summary>
public partial class AzureDevOpsProjectTests
{
    /// <summary>
    /// Provides a set of representative inputs for name and id:
    /// - Typical non-empty values.
    /// - Empty strings.
    /// - Whitespace-only strings.
    /// - Null values (for each parameter independently and both together).
    /// - Special/control characters.
    /// - Very long strings (~10k chars).
    /// </summary>
    private static IEnumerable<TestCaseData> Constructor_AssignsProperties_Cases()
    {
        var longName = new string('n', 10_000);
        var longId = new string('i', 10_000);

        yield return new TestCaseData("project-name", "1234").SetName("Ctor_AssignsProperties_TypicalValues");
        yield return new TestCaseData(string.Empty, string.Empty).SetName("Ctor_AssignsProperties_EmptyStrings");
        yield return new TestCaseData(" ", " ").SetName("Ctor_AssignsProperties_WhitespaceOnly");
        yield return new TestCaseData(null, null).SetName("Ctor_AssignsProperties_BothNull");
        yield return new TestCaseData(null, "id-only").SetName("Ctor_AssignsProperties_NullName_NonNullId");
        yield return new TestCaseData("name-only", null).SetName("Ctor_AssignsProperties_NonNullName_NullId");
        yield return new TestCaseData("name\n\t\u0000!@#$%^&*()", "id:/\\?*<>|\"'").SetName("Ctor_AssignsProperties_SpecialAndControlChars");
        yield return new TestCaseData(longName, longId).SetName("Ctor_AssignsProperties_VeryLongStrings");
    }

    /// <summary>
    /// Ensures that the AzureDevOpsProject constructor assigns the provided name and id values
    /// directly to the Name and Id properties without throwing, across diverse inputs including:
    /// - Empty, whitespace, null, special/control chars, and very long strings.
    /// Expected:
    /// - project.Name equals the provided name.
    /// - project.Id equals the provided id.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Constructor_AssignsProperties_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_VariousNameAndId_AssignsProperties(string name, string id)
    {
        // Arrange
        // Inputs are provided by TestCaseSource.

        // Act
        var project = new AzureDevOpsProject(name, id);

        // Assert
        project.Name.Should().Be(name);
        project.Id.Should().Be(id);
    }
}
