// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Moq;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Models.Darc.UnitTests;

/// <summary>
/// Tests for DependencyDetailComparer.Equals to verify logical equality is determined
/// by Commit, Name, RepoUri, Version, and Type fields.
/// </summary>
[TestFixture]
[Category("auto-generated")]
public class DependencyDetailComparerTests
{
    /// <summary>
    /// Verifies that Equals returns true when all compared fields (Commit, Name, RepoUri, Version, Type)
    /// have identical values in both DependencyDetail instances.
    /// Inputs:
    ///  - Two DependencyDetail instances with the same Commit, Name, RepoUri, Version, and Type.
    /// Expected:
    ///  - Equals returns true and no exception is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Equals_AllFieldsMatch_ReturnsTrue()
    {
        // Arrange
        var comparer = new DependencyDetailComparer();
        var left = BuildDependency("sha-abc", "Package.X", "https://repo/x", "1.2.3", 1);
        var right = BuildDependency("sha-abc", "Package.X", "https://repo/x", "1.2.3", 1);

        // Act
        var areEqual = comparer.Equals(left, right);

        // Assert
        areEqual.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that Equals returns false when exactly one of the compared fields differs.
    /// Inputs:
    ///  - Two DependencyDetail instances where only the specified field (parameter 'changedField')
    ///    is different; all other fields are identical.
    /// Expected:
    ///  - Equals returns false.
    /// </summary>
    [TestCase("Commit")]
    [TestCase("Name")]
    [TestCase("RepoUri")]
    [TestCase("Version")]
    [TestCase("Type")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Equals_SingleFieldDiffers_ReturnsFalse(string changedField)
    {
        // Arrange
        var comparer = new DependencyDetailComparer();
        var left = BuildDependency("sha-abc", "Package.X", "https://repo/x", "1.2.3", 1);
        var right = BuildDependency("sha-abc", "Package.X", "https://repo/x", "1.2.3", 1);

        switch (changedField)
        {
            case "Commit":
                right.Commit = "sha-different";
                break;
            case "Name":
                right.Name = "Package.Y";
                break;
            case "RepoUri":
                right.RepoUri = "https://repo/other";
                break;
            case "Version":
                right.Version = "2.0.0";
                break;
            case "Type":
                right.Type = (DependencyType)2;
                break;
            default:
                Assert.Inconclusive("Unexpected test input for 'changedField'.");
                break;
        }

        // Act
        var areEqual = comparer.Equals(left, right);

        // Assert
        areEqual.Should().BeFalse();
    }

    /// <summary>
    /// Verifies equality with empty-string inputs across all compared fields, ensuring
    /// the comparison is strictly value-based and treats identical empty values as equal.
    /// Inputs:
    ///  - Two DependencyDetail instances where Commit, Name, RepoUri, and Version are all empty strings, and Type is the same.
    /// Expected:
    ///  - Equals returns true.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Equals_EmptyStringsAcrossFields_ReturnsTrue()
    {
        // Arrange
        var comparer = new DependencyDetailComparer();
        var left = BuildDependency(string.Empty, string.Empty, string.Empty, string.Empty, 0);
        var right = BuildDependency(string.Empty, string.Empty, string.Empty, string.Empty, 0);

        // Act
        var areEqual = comparer.Equals(left, right);

        // Assert
        areEqual.Should().BeTrue();
    }

    /// <summary>
    /// Verifies equality with very long and special-character strings to ensure comparisons
    /// are content-based and unaffected by string length or character variety.
    /// Inputs:
    ///  - Two DependencyDetail instances with identical long/special-character strings in all compared fields and the same Type.
    /// Expected:
    ///  - Equals returns true.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Equals_LongAndSpecialCharacterStrings_ReturnsTrue()
    {
        // Arrange
        var comparer = new DependencyDetailComparer();
        var longSpecial = new string('A', 1024) + "-特殊字符\n\t\r!@#$%^&*()_+|~{}[]:;<>,.?/";
        var left = BuildDependency(longSpecial, longSpecial, longSpecial, longSpecial, 42);
        var right = BuildDependency(longSpecial, longSpecial, longSpecial, longSpecial, 42);

        // Act
        var areEqual = comparer.Equals(left, right);

        // Assert
        areEqual.Should().BeTrue();
    }

    // Helper to build a DependencyDetail with essential fields used by the comparer.
    private static DependencyDetail BuildDependency(string commit, string name, string repoUri, string version, int typeValue)
    {
        return new DependencyDetail
        {
            Commit = commit,
            Name = name,
            RepoUri = repoUri,
            Version = version,
            Type = (DependencyType)typeValue
        };
    }

    /// <summary>
    /// Verifies that GetHashCode returns the same result for two different instances
    /// with identical property values for Commit, Name, RepoUri, Version, and Type.
    /// Inputs:
    ///  - Two DependencyDetail instances with the same values across all properties used by hashing.
    /// Expected:
    ///  - Hash codes are equal, proving determinism and consistency with value equality.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetHashCode_SamePropertyValues_ReturnsSameHash()
    {
        // Arrange
        var comparer = new DependencyDetailComparer();

        var a = Create("commit-1", "Package.A", "https://repo/a", "1.2.3", DependencyType.Product);
        var b = Create("commit-1", "Package.A", "https://repo/a", "1.2.3", DependencyType.Product);

        // Act
        var hashA = comparer.GetHashCode(a);
        var hashB = comparer.GetHashCode(b);

        // Assert
        hashA.Should().Be(hashB);
    }

    /// <summary>
    /// Ensures that changing a single field among Commit, Name, RepoUri, Version, or Type
    /// affects the computed hash code.
    /// Inputs:
    ///  - A baseline DependencyDetail and a variant differing only in the specified field.
    /// Expected:
    ///  - Hash codes are not equal, indicating the hash depends on that field.
    /// Notes:
    ///  - While hash collisions are theoretically possible, these cases use distinct values
    ///    making collisions extremely unlikely.
    /// </summary>
    [TestCase("Commit")]
    [TestCase("Name")]
    [TestCase("RepoUri")]
    [TestCase("Version")]
    [TestCase("Type")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetHashCode_DifferentSingleField_ProducesDifferentHash(string changedField)
    {
        // Arrange
        var comparer = new DependencyDetailComparer();

        var baseline = Create("commit-1", "Package.A", "https://repo/a", "1.2.3", DependencyType.Product);
        var variant = Create("commit-1", "Package.A", "https://repo/a", "1.2.3", DependencyType.Product);

        switch (changedField)
        {
            case "Commit":
                variant.Commit = "commit-2";
                break;
            case "Name":
                variant.Name = "Package.B";
                break;
            case "RepoUri":
                variant.RepoUri = "https://repo/b";
                break;
            case "Version":
                variant.Version = "2.0.0";
                break;
            case "Type":
                variant.Type = DependencyType.Toolset;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(changedField), changedField, "Unsupported field identifier.");
        }

        // Act
        var hashBaseline = comparer.GetHashCode(baseline);
        var hashVariant = comparer.GetHashCode(variant);

        // Assert
        hashBaseline.Should().NotBe(hashVariant);
    }

    /// <summary>
    /// Validates that GetHashCode is deterministic for a single instance: repeated calls
    /// return the same result even with challenging string inputs.
    /// Inputs:
    ///  - DependencyDetails with boundary-like string values including empty, whitespace,
    ///    long strings, and special characters; both Toolset and Product types.
    /// Expected:
    ///  - Multiple invocations of GetHashCode return the same value.
    /// </summary>
    [TestCase("", "", "", "", DependencyType.Toolset)]
    [TestCase(" ", "  ", "\t", "\r\n", DependencyType.Product)]
    [TestCase("αβγ", "Package.🔥", "https://repo/üñïçødê", "1.0.0+meta", DependencyType.Toolset)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetHashCode_RepeatedCalls_AreDeterministic(
        string commit, string name, string repoUri, string version, DependencyType type)
    {
        // Arrange
        var comparer = new DependencyDetailComparer();
        var detail = Create(commit, name, repoUri, version, type);

        // Act
        var hash1 = comparer.GetHashCode(detail);
        var hash2 = comparer.GetHashCode(detail);
        var hash3 = comparer.GetHashCode(detail);

        // Assert
        hash1.Should().Be(hash2);
        hash2.Should().Be(hash3);
    }

    private static DependencyDetail Create(string commit, string name, string repoUri, string version, DependencyType type)
    {
        return new DependencyDetail
        {
            Commit = commit,
            Name = name,
            RepoUri = repoUri,
            Version = version,
            Type = type
        };
    }
}
