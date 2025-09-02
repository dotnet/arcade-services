// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Shouldly;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.DotNet.DarcLib.Models.Darc.UnitTests;

public class DependencyUpdateTests
{
    /// <summary>
    /// Validates that DependencyName returns the expected value across edge cases:
    /// - From and To are null -> null
    /// - Picks From.Name when not null, ignoring To
    /// - Falls back to To.Name when From.Name is null
    /// - Does not treat empty/whitespace as null (no fallback)
    /// - Preserves special characters and very long strings
    /// </summary>
    /// <param name="from">The current dependency detail (can be null or with null/empty/special Name).</param>
    /// <param name="to">The updated dependency detail (can be null or with null/empty/special Name).</param>
    /// <param name="expected">The expected dependency name, or null.</param>
    [Test]
    [TestCaseSource(nameof(DependencyNameCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DependencyName_Combinations_ReturnsExpected(DependencyDetail from, DependencyDetail to, string expected)
    {
        // Arrange
        var update = new DependencyUpdate
        {
            From = from,
            To = to
        };

        // Act
        var result = update.DependencyName;

        // Assert
        result.Should().Be(expected);
    }

    private static IEnumerable<TestCaseData> DependencyNameCases()
    {
        yield return new TestCaseData(null, null, null)
            .SetName("DependencyName_FromNull_ToNull_ReturnsNull");

        yield return new TestCaseData(D("A"), null, "A")
            .SetName("DependencyName_FromOnlyWithName_ReturnsFromName");

        yield return new TestCaseData(null, D("B"), "B")
            .SetName("DependencyName_ToOnlyWithName_ReturnsToName");

        yield return new TestCaseData(D(null), D("B"), "B")
            .SetName("DependencyName_FromNameNull_FallbacksToToName");

        yield return new TestCaseData(D(""), D("B"), "")
            .SetName("DependencyName_FromNameEmpty_NoFallback_ReturnsEmpty");

        yield return new TestCaseData(D("   "), D("B"), "   ")
            .SetName("DependencyName_FromWhitespace_NoFallback_ReturnsWhitespace");

        yield return new TestCaseData(D("N@me-☃-测试"), D("ignored"), "N@me-☃-测试")
            .SetName("DependencyName_FromWithSpecialCharacters_Preserved");

        var veryLong = new string('x', 1024);
        yield return new TestCaseData(D(veryLong), D("ignored"), veryLong)
            .SetName("DependencyName_FromVeryLongName_Preserved");

        yield return new TestCaseData(D(null), D(null), null)
            .SetName("DependencyName_BothPresentButBothNamesNull_ReturnsNull");

        yield return new TestCaseData(D(null), null, null)
            .SetName("DependencyName_FromNameNull_AndToNull_ReturnsNull");

        yield return new TestCaseData(null, D(null), null)
            .SetName("DependencyName_FromNull_AndToNameNull_ReturnsNull");
    }

    private static DependencyDetail D(string name)
        => new DependencyDetail { Name = name };

    /// <summary>
    /// Ensures Name resolves according to the following precedence and null-handling:
    /// - If From is null, Name falls back to To?.Name.
    /// - If From is not null and From.Name is non-null, Name equals From.Name (even if empty/whitespace).
    /// - If From is not null but From.Name is null, Name falls back to To?.Name.
    /// - If both From and To (or their Name) are null, Name is null.
    /// </summary>
    /// <param name="hasFrom">Whether the From dependency is present.</param>
    /// <param name="fromName">The From dependency Name value (can be null/empty/whitespace/long/special).</param>
    /// <param name="hasTo">Whether the To dependency is present.</param>
    /// <param name="toName">The To dependency Name value (can be null/empty/whitespace/long/special).</param>
    /// <param name="expected">The expected resolved Name value.</param>
    [TestCaseSource(nameof(NameCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Name_Resolution_PrioritizesFromAndFallsBackToTo(bool hasFrom, string fromName, bool hasTo, string toName, string expected)
    {
        // Arrange
        var update = new DependencyUpdate
        {
            From = hasFrom ? new DependencyDetail { Name = fromName } : null,
            To = hasTo ? new DependencyDetail { Name = toName } : null
        };

        // Act
        var actual = update.Name;

        // Assert
        if (expected == null)
        {
            actual.ShouldBeNull();
        }
        else
        {
            actual.ShouldBe(expected);
        }
    }

    private static IEnumerable<object[]> NameCases()
    {
        // Both From and To missing -> Name is null
        yield return new object[] { false, null, false, null, null };

        // Only To present -> Name == To.Name
        yield return new object[] { false, null, true, "to", "to" };

        // Only From present -> Name == From.Name
        yield return new object[] { true, "from", false, null, "from" };

        // Both present -> From takes precedence
        yield return new object[] { true, "from", true, "to", "from" };

        // From present but From.Name null; To present -> falls back to To.Name
        yield return new object[] { true, null, true, "to", "to" };

        // From present with empty string; To present -> empty string (non-null) should win
        yield return new object[] { true, string.Empty, true, "to", string.Empty };

        // From present with whitespace; To present -> whitespace (non-null) should win
        yield return new object[] { true, "   ", true, "to", "   " };

        // From present with special/unicode characters; To present -> From wins
        yield return new object[] { true, "name-äΩ🚀", true, "to", "name-äΩ🚀" };

        // From present but From.Name null; To missing -> Name is null
        yield return new object[] { true, null, false, null, null };

        // Only To present with very long name -> Name equals long name
        var longName = new string('a', 10000);
        yield return new object[] { false, null, true, longName, longName };

        // Both present with very long From name -> From wins
        yield return new object[] { true, longName, true, "to", longName };
    }

    /// <summary>
    /// Verifies that Value returns null when the 'To' property has not been set (is null).
    /// Input: A new DependencyUpdate instance with default values (To == null).
    /// Expected: Value is null.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Value_ToIsNull_ReturnsNull()
    {
        // Arrange
        var update = new DependencyUpdate();

        // Act
        var value = update.Value;

        // Assert
        value.Should().BeNull();
    }

    /// <summary>
    /// Partial test placeholder to verify that Value returns the exact instance assigned to 'To'.
    /// Input: A DependencyUpdate instance with 'To' set to a concrete DependencyDetail instance.
    /// Expected: Value references the same instance as 'To'.
    /// NOTE: The constructor/signature of DependencyDetail is not provided in this scope; replace the TODOs to complete.
    /// </summary>
    [Test]
    [Ignore("Pending: Provide a concrete DependencyDetail instance and assign to update.To, then assert Value is the same instance.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Value_ToIsNonNull_ReturnsSameInstance()
    {
        // Arrange
        var update = new DependencyUpdate();
        // TODO: Instantiate a valid DependencyDetail instance when available in scope, e.g.:
        // var detail = new DependencyDetail(/* provide required constructor args if any */);
        // update.To = detail;

        // Act
        // var value = update.Value;

        // Assert
        // value.Should().BeSameAs(detail);
    }

    /// <summary>
    /// Verifies that IsAdded returns true if and only if From is null, regardless of To.
    /// Inputs:
    ///  - hasFrom: whether the From dependency is set (non-null).
    ///  - hasTo: whether the To dependency is set (non-null).
    /// Expected:
    ///  - result: true when From is null; otherwise false.
    /// </summary>
    [TestCase(false, false, true, TestName = "IsAdded_FromNullToNull_ReturnsTrue")]
    [TestCase(false, true, true, TestName = "IsAdded_FromNullToNonNull_ReturnsTrue")]
    [TestCase(true, false, false, TestName = "IsAdded_FromNonNullToNull_ReturnsFalse")]
    [TestCase(true, true, false, TestName = "IsAdded_FromNonNullToNonNull_ReturnsFalse")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsAdded_FromPresenceDeterminesResult(bool hasFrom, bool hasTo, bool expected)
    {
        // Arrange
        var update = new DependencyUpdate
        {
            From = hasFrom ? new DependencyDetail() : null,
            To = hasTo ? new DependencyDetail() : null
        };

        // Act
        var result = update.IsAdded();

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Validates that IsRemoved returns true if and only if the 'To' dependency is null.
    /// Inputs:
    ///  - fromIsNull: whether the 'From' property is null.
    ///  - toIsNull: whether the 'To' property is null.
    /// Expected:
    ///  - Returns true when 'toIsNull' is true; otherwise returns false.
    /// </summary>
    [Test]
    [TestCase(true, true, true, TestName = "IsRemoved_FromNullToNull_ReturnsTrue")]
    [TestCase(false, true, true, TestName = "IsRemoved_FromNonNullToNull_ReturnsTrue")]
    [TestCase(true, false, false, TestName = "IsRemoved_FromNullToNonNull_ReturnsFalse")]
    [TestCase(false, false, false, TestName = "IsRemoved_FromNonNullToNonNull_ReturnsFalse")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsRemoved_CombinationsOfFromAndTo_NullToDeterminesRemoval(bool fromIsNull, bool toIsNull, bool expected)
    {
        // Arrange
        var update = new DependencyUpdate();
        if (!fromIsNull)
        {
            update.From = new DependencyDetail();
        }
        if (!toIsNull)
        {
            update.To = new DependencyDetail();
        }

        // Act
        var result = update.IsRemoved();

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that IsUpdated returns true only when both From and To are non-null.
    /// Inputs:
    ///  - hasFrom: whether From is set to a non-null DependencyDetail.
    ///  - hasTo: whether To is set to a non-null DependencyDetail.
    /// Expected:
    ///  - true if both are non-null; otherwise false.
    /// </summary>
    [Test]
    [TestCase(false, false, false, TestName = "IsUpdated_FromNullToNull_ReturnsFalse")]
    [TestCase(true, false, false, TestName = "IsUpdated_FromNonNullToNull_ReturnsFalse")]
    [TestCase(false, true, false, TestName = "IsUpdated_FromNullToNonNull_ReturnsFalse")]
    [TestCase(true, true, true, TestName = "IsUpdated_FromNonNullToNonNull_ReturnsTrue")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsUpdated_NullCombinations_ReturnsExpected(bool hasFrom, bool hasTo, bool expected)
    {
        // Arrange
        var update = new DependencyUpdate
        {
            From = hasFrom ? new DependencyDetail() : null,
            To = hasTo ? new DependencyDetail() : null
        };

        // Act
        var result = update.IsUpdated();

        // Assert
        result.Should().Be(expected);
    }
}
