// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Models.Darc.UnitTests;

public class DependencyGraphNodeComparerTests
{
    /// <summary>
    /// Verifies value-based equality on Commit and Repository across diverse string inputs.
    /// Inputs:
    ///  - Non-null Commit/Repository combinations including empty, whitespace, unicode, and case variations.
    /// Expected:
    ///  - True only when both Commit and Repository strings are exactly equal (case-sensitive), otherwise false.
    /// </summary>
    [TestCase("sha", "https://repo/a", "sha", "https://repo/a", true)]
    [TestCase("sha", "https://repo/a", "sha2", "https://repo/a", false)]
    [TestCase("sha", "https://repo/a", "sha", "https://repo/b", false)]
    [TestCase("sha", "https://repo/a", "sha2", "https://repo/b", false)]
    [TestCase("sha", "https://repo/a", "SHA", "https://repo/a", false)]
    [TestCase("sha", "https://repo/a", "sha", "HTTPS://REPO/A", false)]
    [TestCase("", "", "", "", true)]
    [TestCase(" ", " ", " ", " ", true)]
    [TestCase(" ", " ", "  ", " ", false)]
    [TestCase("shá🚀", "https://例子.测试/路径", "shá🚀", "https://例子.测试/路径", true)]
    [TestCase("shá🚀", "https://例子.测试/路径", "shá🚀!", "https://例子.测试/路径", false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Equals_CommitAndRepositoryCombinations_ReturnsExpected(string commitX, string repoX, string commitY, string repoY, bool expected)
    {
        // Arrange
        var comparer = new DependencyGraphNodeComparer();
        var nodeX = new DependencyGraphNode(
            repoUri: repoX,
            commit: commitX,
            dependencies: new List<DependencyDetail>(),
            contributingBuilds: new HashSet<Build>());
        var nodeY = new DependencyGraphNode(
            repoUri: repoY,
            commit: commitY,
            dependencies: new List<DependencyDetail>(),
            contributingBuilds: new HashSet<Build>());

        // Act
        var areEqual = comparer.Equals(nodeX, nodeY);

        // Assert
        areEqual.Should().Be(expected);
    }

    /// <summary>
    /// Ensures equality is based on full string value comparison even for very long inputs.
    /// Inputs:
    ///  - Very long Commit and Repository strings (thousands of characters) for both nodes.
    /// Expected:
    ///  - True when both fields are identical and false when a single character differs.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Equals_VeryLongStrings_PerformsValueComparison()
    {
        // Arrange
        var comparer = new DependencyGraphNodeComparer();
        var longCommit = new string('a', 10000);
        var longRepo = "https://repo/" + new string('b', 5000);

        var node1 = new DependencyGraphNode(
            repoUri: longRepo,
            commit: longCommit,
            dependencies: new List<DependencyDetail>(),
            contributingBuilds: new HashSet<Build>());
        var node2 = new DependencyGraphNode(
            repoUri: longRepo,
            commit: longCommit,
            dependencies: new List<DependencyDetail>(),
            contributingBuilds: new HashSet<Build>());
        var node3 = new DependencyGraphNode(
            repoUri: longRepo + "x",
            commit: longCommit,
            dependencies: new List<DependencyDetail>(),
            contributingBuilds: new HashSet<Build>());

        // Act
        var equalSameValues = comparer.Equals(node1, node2);
        var equalDifferentRepo = comparer.Equals(node1, node3);

        // Assert
        equalSameValues.Should().BeTrue();
        equalDifferentRepo.Should().BeFalse();
    }

    /// <summary>
    /// Ensures that passing a null DependencyGraphNode throws NullReferenceException due to member access on null.
    /// Inputs:
    ///  - obj: null
    /// Expected:
    ///  - Throws NullReferenceException.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetHashCode_NullNode_ThrowsNullReferenceException()
    {
        // Arrange
        var sut = new DependencyGraphNodeComparer();

        // Act
        Action act = () => sut.GetHashCode(null);

        // Assert
        act.Should().Throw<NullReferenceException>();
    }

    /// <summary>
    /// Verifies that the hash code is computed from the (Commit, Repository) tuple and remains stable and equal
    /// across different instances with identical values, including edge cases like nulls, empty, whitespace,
    /// long strings, and special characters.
    /// Inputs (parameterized):
    ///  - commit: various representative values (null, empty, whitespace, long, special characters)
    ///  - repository: various representative values (null, empty, whitespace, URI-like, long)
    /// Expected:
    ///  - The produced hash equals (commit, repository).GetHashCode().
    ///  - Identical inputs produce identical hash codes across instances.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(CommitRepositoryCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetHashCode_CommitAndRepository_TupleHashIsUsed(string commit, string repository)
    {
        // Arrange
        var sut = new DependencyGraphNodeComparer();
        var node1 = new DependencyGraphNode(repository, commit, null, null);
        var node2 = new DependencyGraphNode(repository, commit, null, null);
        var expected = (commit, repository).GetHashCode();

        // Act
        var hash1 = sut.GetHashCode(node1);
        var hash2 = sut.GetHashCode(node2);

        // Assert
        hash1.Should().Be(expected);
        hash2.Should().Be(expected);
    }

    private static IEnumerable<TestCaseData> CommitRepositoryCases()
    {
        yield return new TestCaseData(null, null).SetName("BothNull");
        yield return new TestCaseData(null, "").SetName("CommitNull_RepoEmpty");
        yield return new TestCaseData("", null).SetName("CommitEmpty_RepoNull");
        yield return new TestCaseData("", " ").SetName("CommitEmpty_RepoWhitespace");
        yield return new TestCaseData(" ", "").SetName("CommitWhitespace_RepoEmpty");
        yield return new TestCaseData("sha-1234567890abcdef", "https://github.com/org/repo").SetName("TypicalShaAndUri");
        yield return new TestCaseData("a/b:c?*|<>", "https://github.com/org/repo").SetName("SpecialCharsInCommit");
        yield return new TestCaseData(new string('x', 2048), new string('y', 2048)).SetName("VeryLongStrings");
    }
}
