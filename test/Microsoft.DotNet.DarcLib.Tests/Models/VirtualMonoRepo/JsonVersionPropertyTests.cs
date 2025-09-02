// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Moq;
using NuGet;
using NuGet.Versioning;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo.UnitTests;

public class JsonVersionPropertyTests
{
    /// <summary>
    /// Verifies that the constructor preserves the provided name verbatim without applying any normalization or validation.
    /// Input:
    /// - Various strings including empty, whitespace, special characters, colons, and very long strings.
    /// Expected:
    /// - The Name property equals the input name exactly for all cases.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(NameCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithName_StoresNameVerbatim(string name)
    {
        // Arrange
        var result = NodeComparisonResult.Added;
        object value = "value";

        // Act
        var prop = new JsonVersionProperty(name, result, value);

        // Assert
        prop.Name.Should().Be(name);
    }

    /// <summary>
    /// Ensures that the constructor assigns the provided value as-is, including null and heterogeneous types.
    /// Input:
    /// - Value is null, string, int, and list of strings.
    /// Expected:
    /// - The Value property equals the provided object reference (or null).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(ValueCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithValue_PreservesValueReference(object value)
    {
        // Arrange
        const string name = "some:name";
        var result = NodeComparisonResult.Added;

        // Act
        var prop = new JsonVersionProperty(name, result, value);

        // Assert
        prop.Value.Should().Be(value);
    }

    /// <summary>
    /// Validates that the constructor sets internal state so that the state-check helpers reflect the provided result.
    /// Input:
    /// - Added, Removed, Updated, and an undefined enum value.
    /// Expected:
    /// - IsAdded/IsRemoved/IsUpdated match the expected booleans for each input.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase((int)NodeComparisonResult.Added, true, false, false)]
    [TestCase((int)NodeComparisonResult.Removed, false, true, false)]
    [TestCase((int)NodeComparisonResult.Updated, false, false, true)]
    [TestCase(123, false, false, false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_Result_SetsStateFlags(int resultValue, bool expectAdded, bool expectRemoved, bool expectUpdated)
    {
        // Arrange
        const string name = "prop";
        object value = "v";
        var result = (NodeComparisonResult)resultValue;

        // Act
        var prop = new JsonVersionProperty(name, result, value);

        // Assert
        prop.Name.Should().Be(name);
        prop.IsAdded().Should().Be(expectAdded);
        prop.IsRemoved().Should().Be(expectRemoved);
        prop.IsUpdated().Should().Be(expectUpdated);
    }

    private static IEnumerable<object[]> NameCases()
    {
        yield return new object[] { "" };
        yield return new object[] { " " };
        yield return new object[] { "\t\n" };
        yield return new object[] { "sdk:version" };
        yield return new object[] { "name:with:special/chars🚀" };
        yield return new object[] { new string('x', 1024) };
    }

    private static IEnumerable<object[]> ValueCases()
    {
        yield return new object[] { null };
        yield return new object[] { "text" };
        yield return new object[] { 42 };
        yield return new object[] { new List<string> { "a", "b" } };
    }

    /// <summary>
    /// Verifies that the Name property returns exactly the value provided to the constructor
    /// across edge-case inputs (empty, whitespace, control characters, long, and special Unicode).
    /// Ensures behavior is consistent for all NodeComparisonResult enum values.
    /// </summary>
    /// <param name="inputName">The JSON version property name passed to the constructor.</param>
    [Test]
    [TestCaseSource(nameof(NameEdgeCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Name_ReturnsConstructorValue_ForEdgeCaseNames(string inputName)
    {
        // Arrange
        var allResults = new[]
        {
                NodeComparisonResult.Added,
                NodeComparisonResult.Removed,
                NodeComparisonResult.Updated
            };

        foreach (var result in allResults)
        {
            var property = new JsonVersionProperty(inputName, result, null);

            // Act
            var actual = property.Name;

            // Assert
            actual.Should().Be(inputName);
        }
    }

    private static IEnumerable<string> NameEdgeCases()
    {
        yield return string.Empty;
        yield return "   ";
        yield return "\t\n\r";
        yield return new string('a', 8192);
        yield return "名前-Üñí©ødê-💡-!@#$%^&*()[]{};:'\",.<>?/\\|`~";
    }

    /// <summary>
    /// Verifies that Value returns the same instance that was provided to the constructor,
    /// for a wide spectrum of input values (null, strings, numerics, floating points, collections, enums, and other objects).
    /// Expected: Value is the same object reference as the constructor's newValue argument (including null).
    /// </summary>
    /// <param name="newValue">The value to pass to the constructor as newValue.</param>
    [TestCaseSource(nameof(Value_ReturnsProvidedValue_AsIs_TestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Value_ReturnsProvidedValue_AsIs(object newValue)
    {
        // Arrange
        var property = new JsonVersionProperty("version-property", NodeComparisonResult.Added, newValue);

        // Act
        var actual = property.Value;

        // Assert
        actual.Should().BeSameAs(newValue);
    }

    /// <summary>
    /// Ensures that when the constructor is called without specifying the newValue parameter,
    /// Value returns null.
    /// Expected: Value is null.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Value_WhenConstructorNewValueNotProvided_IsNull()
    {
        // Arrange
        var property = new JsonVersionProperty("version-property", NodeComparisonResult.Added);

        // Act
        var actual = property.Value;

        // Assert
        actual.Should().BeNull();
    }

    // Test data covering nulls, strings (including edge cases), numerics, doubles with special values,
    // collections, enums (valid and out-of-range), and other domain-relevant objects.
    public static IEnumerable Value_ReturnsProvidedValue_AsIs_TestCases()
    {
        // Null
        yield return new object[] { null };

        // Strings
        yield return new object[] { string.Empty };
        yield return new object[] { " " };
        yield return new object[] { new string('a', 2048) };
        yield return new object[] { "special\t\n\r\u0000\u2603" };

        // Integers
        yield return new object[] { int.MinValue };
        yield return new object[] { -1 };
        yield return new object[] { 0 };
        yield return new object[] { 1 };
        yield return new object[] { int.MaxValue };

        // Doubles, including special values
        yield return new object[] { double.NaN };
        yield return new object[] { double.NegativeInfinity };
        yield return new object[] { double.PositiveInfinity };

        // DateTimes
        yield return new object[] { DateTime.MinValue };
        yield return new object[] { DateTime.MaxValue };

        // Booleans
        yield return new object[] { true };
        yield return new object[] { false };

        // Arrays and collections
        yield return new object[] { new int[0] };
        yield return new object[] { new[] { 1 } };
        yield return new object[] { new[] { 1, 1 } };
        yield return new object[] { new List<string>() };
        yield return new object[] { new List<string> { "one" } };

        // Enums
        yield return new object[] { NodeComparisonResult.Updated };
        yield return new object[] { (NodeComparisonResult)999 };

        // Other object types
        yield return new object[] { new Version(1, 2, 3) };
        yield return new object[] { NuGetVersion.Parse("1.2.3") };
    }

    /// <summary>
    /// Verifies that IsAdded returns true only when the underlying comparison result is Added.
    /// Input:
    /// - Construct JsonVersionProperty with a fixed name and null value, varying the NodeComparisonResult across:
    ///   Added, Removed, Updated, and two out-of-range values (-1, 999).
    /// Expected:
    /// - True for Added; false for all other values including out-of-range enum values.
    /// </summary>
    [TestCase(NodeComparisonResult.Added, true)]
    [TestCase(NodeComparisonResult.Removed, false)]
    [TestCase(NodeComparisonResult.Updated, false)]
    [TestCase(-1, false)]
    [TestCase(999, false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsAdded_ResultVariants_ReturnsExpected(NodeComparisonResult result, bool expected)
    {
        // Arrange
        const string name = "any:name";
        object value = null;
        var prop = new JsonVersionProperty(name, result, value);

        // Act
        var isAdded = prop.IsAdded();

        // Assert
        isAdded.Should().Be(expected);
    }

    /// <summary>
    /// Verifies IsRemoved returns true only when the NodeComparisonResult is Removed.
    /// Inputs:
    /// - result: The NodeComparisonResult to evaluate, including defined enum values and an out-of-range cast.
    /// - name: Various non-null string values (empty, whitespace, normal, special) to ensure name does not affect behavior.
    /// Expected:
    /// - True if result == NodeComparisonResult.Removed; otherwise false. No exceptions are thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(NodeComparisonResult.Removed, "", true)]
    [TestCase(NodeComparisonResult.Added, "name", false)]
    [TestCase(NodeComparisonResult.Updated, "  \t", false)]
    [TestCase((NodeComparisonResult)12345, "special-\u2603", false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void IsRemoved_ResultVariants_ReturnsExpected(NodeComparisonResult result, string name, bool expected)
    {
        // Arrange
        var property = new JsonVersionProperty(name, result, null);

        // Act
        var isRemoved = property.IsRemoved();

        // Assert
        isRemoved.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that IsUpdated returns true only when the internal comparison result equals NodeComparisonResult.Updated.
    /// Inputs cover:
    /// - Property names with edge cases (empty, whitespace, long, and special/control characters).
    /// - Values including null, primitive types, NaN, and collections.
    /// - Enum values: all defined (Added, Removed, Updated) and out-of-range values.
    /// Expected:
    /// - Returns true only for NodeComparisonResult.Updated, and false for all other values (including invalid/out-of-range).
    /// - No exceptions are thrown regardless of propertyName/value.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(IsUpdated_ReturnsExpected_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void IsUpdated_ResultVariants_ReturnsExpected(string propertyName, NodeComparisonResult result, object value, bool expected)
    {
        // Arrange
        var sut = new JsonVersionProperty(propertyName, result, value);

        // Act
        var isUpdated = sut.IsUpdated();

        // Assert
        isUpdated.Should().Be(expected);
    }

    private static IEnumerable<TestCaseData> IsUpdated_ReturnsExpected_Cases()
    {
        var longName = new string('x', 1024);

        yield return new TestCaseData(string.Empty, NodeComparisonResult.Updated, null, true)
            .SetName("IsUpdated_UpdatedResult_True_WithEmptyNameAndNullValue");

        yield return new TestCaseData(" ", NodeComparisonResult.Added, "value", false)
            .SetName("IsUpdated_AddedResult_False_WithWhitespaceNameAndStringValue");

        yield return new TestCaseData(longName, NodeComparisonResult.Removed, int.MinValue, false)
            .SetName("IsUpdated_RemovedResult_False_WithLongNameAndIntMin");

        yield return new TestCaseData("special:\n\tchars", (NodeComparisonResult)(-1), double.NaN, false)
            .SetName("IsUpdated_InvalidNegativeEnum_False_WithSpecialCharsAndNaN");

        yield return new TestCaseData("frameworks", (NodeComparisonResult)999, new List<string> { "a", "b" }, false)
            .SetName("IsUpdated_InvalidLargeEnum_False_WithListValue");
    }
    private const string Key = "test:key";

    /// <summary>
    /// Validates that when both compared values are null, the method throws ArgumentException with the key name included.
    /// Input:
    /// - repoProp.Value = null
    /// - vmrProp.Value = null
    /// Expected:
    /// - ArgumentException is thrown with message: "Compared values for 'test:key' are null".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SelectJsonVersionProperty_BothValuesNull_ThrowsArgumentExceptionWithKeyNameInMessage()
    {
        // Arrange
        var repoProp = CreateNull(Key);
        var vmrProp = CreateNull(Key);

        // Act
        Action act = () => JsonVersionProperty.SelectJsonVersionProperty(repoProp, vmrProp);

        // Assert
        act.Should().Throw<ArgumentException>()
           .WithMessage($"Compared values for '{Key}' are null");
    }

    /// <summary>
    /// Ensures that if the repo property's value is null and vmr property's value is not null, vmr property is selected.
    /// Input:
    /// - repoProp.Value = null
    /// - vmrProp.Value = "value"
    /// Expected:
    /// - Returns vmrProp.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SelectJsonVersionProperty_OnlyRepoValueNull_ReturnsVmrProperty()
    {
        // Arrange
        var repoProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, null);
        var vmrProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, "value");

        // Act
        var selected = JsonVersionProperty.SelectJsonVersionProperty(repoProp, vmrProp);

        // Assert
        selected.Should().BeSameAs(vmrProp);
        selected.Value.Should().Be("value");
    }

    /// <summary>
    /// Ensures that if the vmr property's value is null and repo property's value is not null, repo property is selected.
    /// Input:
    /// - repoProp.Value = "value"
    /// - vmrProp.Value = null
    /// Expected:
    /// - Returns repoProp.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SelectJsonVersionProperty_OnlyVmrValueNull_ReturnsRepoProperty()
    {
        // Arrange
        var repoProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, "value");
        var vmrProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, null);

        // Act
        var selected = JsonVersionProperty.SelectJsonVersionProperty(repoProp, vmrProp);

        // Assert
        selected.Should().BeSameAs(repoProp);
        selected.Value.Should().Be("value");
    }

    /// <summary>
    /// Verifies that when the two values are of different runtime types, an ArgumentException is thrown describing the type mismatch.
    /// Input:
    /// - repoProp.Value = "1"
    /// - vmrProp.Value = 1
    /// Expected:
    /// - ArgumentException with message indicating System.String vs System.Int32 type mismatch.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SelectJsonVersionProperty_DifferentValueTypes_ThrowsArgumentException()
    {
        // Arrange
        var repoProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, "1");
        var vmrProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, 1);

        // Act
        Action act = () => JsonVersionProperty.SelectJsonVersionProperty(repoProp, vmrProp);

        // Assert
        var expected = $"Cannot compare {typeof(string)} with {typeof(int)} because their values are of different types.";
        act.Should().Throw<ArgumentException>()
           .WithMessage(expected);
    }

    /// <summary>
    /// Ensures that list values are not comparable and produce a clear exception.
    /// Input:
    /// - repoProp.Value and vmrProp.Value are both List&lt;string&gt;
    /// Expected:
    /// - ArgumentException with message: "Cannot compare properties with List&lt;string&gt; values."
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SelectJsonVersionProperty_ListValues_ThrowsArgumentException()
    {
        // Arrange
        var repoProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, new List<string> { "a" });
        var vmrProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, new List<string> { "b" });

        // Act
        Action act = () => JsonVersionProperty.SelectJsonVersionProperty(repoProp, vmrProp);

        // Assert
        act.Should().Throw<ArgumentException>()
           .WithMessage("Cannot compare properties with List<string> values.");
    }

    /// <summary>
    /// Verifies that equal boolean values result in selecting the repo property instance.
    /// Input:
    /// - repoProp.Value = true
    /// - vmrProp.Value = true
    /// Expected:
    /// - Returns repoProp (same instance).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SelectJsonVersionProperty_BoolValuesEqual_ReturnsRepoProperty()
    {
        // Arrange
        var repoProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, true);
        var vmrProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, true);

        // Act
        var selected = JsonVersionProperty.SelectJsonVersionProperty(repoProp, vmrProp);

        // Assert
        selected.Should().BeSameAs(repoProp);
        selected.Value.Should().Be(true);
    }

    /// <summary>
    /// Ensures that differing boolean values cause an exception indicating differing boolean values for the key.
    /// Input:
    /// - repoProp.Value = true
    /// - vmrProp.Value = false
    /// Expected:
    /// - ArgumentException with message mentioning the key and boolean mismatch.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SelectJsonVersionProperty_BoolValuesDifferent_ThrowsArgumentException()
    {
        // Arrange
        var repoProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, true);
        var vmrProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, false);

        // Act
        Action act = () => JsonVersionProperty.SelectJsonVersionProperty(repoProp, vmrProp);

        // Assert
        act.Should().Throw<ArgumentException>()
           .WithMessage($"Key {Key} value has different boolean values in properties.");
    }

    /// <summary>
    /// Validates integer comparison behavior including extremes and equality tie-breaker (vmr wins on equality).
    /// Input:
    /// - repoProp.Value and vmrProp.Value are integers.
    /// Cases:
    /// - (int.MinValue, 0) -> vmr
    /// - (int.MaxValue, int.MinValue) -> repo
    /// - (42, 42) -> vmr (tie goes to vmr)
    /// Expected:
    /// - The property with the greater integer value is returned; vmr returned when equal.
    /// </summary>
    [TestCase(int.MinValue, 0, "vmr")]
    [TestCase(int.MaxValue, int.MinValue, "repo")]
    [TestCase(42, 42, "vmr")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SelectJsonVersionProperty_IntValues_ChoosesPropertyWithGreaterNumber(int repoValue, int vmrValue, string expectedWinner)
    {
        // Arrange
        var repoProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, repoValue);
        var vmrProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, vmrValue);

        // Act
        var selected = JsonVersionProperty.SelectJsonVersionProperty(repoProp, vmrProp);

        // Assert
        if (expectedWinner == "repo")
        {
            selected.Should().BeSameAs(repoProp);
            selected.Value.Should().Be(repoValue);
        }
        else
        {
            selected.Should().BeSameAs(vmrProp);
            selected.Value.Should().Be(vmrValue);
        }
    }

    /// <summary>
    /// Ensures that when both string values parse as SemanticVersion, the higher version is selected.
    /// Input:
    /// - repoProp.Value and vmrProp.Value are semver strings.
    /// Cases:
    /// - ("1.2.3", "1.2.4") -> vmr
    /// - ("2.0.0", "1.9.9") -> repo
    /// - ("1.0.0-alpha", "1.0.0") -> vmr (release > prerelease)
    /// Expected:
    /// - The property representing the higher semantic version is returned.
    /// </summary>
    [TestCase("1.2.3", "1.2.4", "vmr")]
    [TestCase("2.0.0", "1.9.9", "repo")]
    [TestCase("1.0.0-alpha", "1.0.0", "vmr")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SelectJsonVersionProperty_StringSemanticVersions_ChoosesHigherVersion(string repoVersion, string vmrVersion, string expectedWinner)
    {
        // Arrange
        var repoProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, repoVersion);
        var vmrProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, vmrVersion);

        // Act
        var selected = JsonVersionProperty.SelectJsonVersionProperty(repoProp, vmrProp);

        // Assert
        if (expectedWinner == "repo")
        {
            selected.Should().BeSameAs(repoProp);
            selected.Value.Should().Be(repoVersion);
        }
        else
        {
            selected.Should().BeSameAs(vmrProp);
            selected.Value.Should().Be(vmrVersion);
        }
    }

    /// <summary>
    /// Verifies that when either or both strings cannot be parsed as SemanticVersion, an exception is thrown.
    /// Input:
    /// - repoProp.Value and vmrProp.Value are strings where at least one is non-semver (e.g., "$(Version)").
    /// Expected:
    /// - ArgumentException with message indicating non-parsable string values for the key.
    /// </summary>
    [TestCase("abc", "def")]
    [TestCase("1.0.0", "$(Version)")]
    [TestCase("$(Version)", "1.0.0")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SelectJsonVersionProperty_StringValuesNotBothSemantic_ThrowsArgumentException(string repoValue, string vmrValue)
    {
        // Arrange
        var repoProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, repoValue);
        var vmrProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, vmrValue);

        // Act
        Action act = () => JsonVersionProperty.SelectJsonVersionProperty(repoProp, vmrProp);

        // Assert
        act.Should().Throw<ArgumentException>()
           .WithMessage($"Key {Key} value has different string values in properties, and cannot be parsed as SemanticVersion");
    }

    /// <summary>
    /// Ensures that unsupported but same-typed values (e.g., double) trigger the generic unsupported-type exception.
    /// Input:
    /// - repoProp.Value = 1.23 (double)
    /// - vmrProp.Value = 4.56 (double)
    /// Expected:
    /// - ArgumentException with message: "Cannot compare properties with System.Double values."
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SelectJsonVersionProperty_UnsupportedValueType_ThrowsArgumentException()
    {
        // Arrange
        var repoProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, 1.23);
        var vmrProp = new JsonVersionProperty(Key, NodeComparisonResult.Updated, 4.56);

        // Act
        Action act = () => JsonVersionProperty.SelectJsonVersionProperty(repoProp, vmrProp);

        // Assert
        act.Should().Throw<ArgumentException>()
           .WithMessage($"Cannot compare properties with {typeof(double)} values.");
    }

    private static JsonVersionProperty Create(string name, object value)
        => new JsonVersionProperty(name, NodeComparisonResult.Updated, value);

    private static JsonVersionProperty CreateNull(string name)
        => new JsonVersionProperty(name, NodeComparisonResult.Updated, null);
}
