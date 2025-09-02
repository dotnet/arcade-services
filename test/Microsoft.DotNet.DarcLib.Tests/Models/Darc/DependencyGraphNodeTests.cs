// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Moq;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Models.Darc.UnitTests;

public class DependencyGraphNodeTests
{
    /// <summary>
    /// Validates that the convenience constructor initializes:
    ///  - Repository and Commit fields with provided values (including edge string cases),
    ///  - VisitedNodes as an empty set using OrdinalIgnoreCase comparer,
    ///  - Children and Parents as empty sets,
    ///  - DiffFrom as null.
    /// Inputs:
    ///  - repoUri and commit with various edge-case string values.
    /// Expected:
    ///  - Properties set accordingly, no exceptions.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("https://github.com/org/repo", "abc123")]
    [TestCase("", "")]
    [TestCase(" ", " ")]
    [TestCase("repo:!@#$%^&*()_+|{}[];':\",./<>?", "commit:!@#$%^&*()_+|{}[];':\",./<>?")]
    [TestCase("r-" + "a", "c-" + "b")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_InitializesCoreStateWithEdgeStrings(string repoUri, string commit)
    {
        // Arrange
        var dependencies = new List<DependencyDetail>(); // empty list
        var contributingBuilds = new HashSet<Build>();   // empty set

        // Act
        var node = new DependencyGraphNode(repoUri, commit, dependencies, contributingBuilds);

        // Assert
        node.Repository.Should().Be(repoUri);
        node.Commit.Should().Be(commit);

        node.VisitedNodes.Should().BeEmpty();
        node.VisitedNodes.Comparer.Should().BeSameAs(StringComparer.OrdinalIgnoreCase);

        // Case-insensitive behavior proof
        node.VisitedNodes.Add("VisitedA");
        node.VisitedNodes.Contains("visiteda").Should().BeTrue();

        node.Children.Should().BeEmpty();
        node.Parents.Should().BeEmpty();

        node.DiffFrom.Should().BeNull();
    }

    /// <summary>
    /// Ensures that the convenience constructor assigns reference-equal instances for
    /// Dependencies and ContributingBuilds as provided, while also initializing other
    /// collections to empty.
    /// Inputs:
    ///  - A non-empty dependencies list and contributing builds set.
    /// Expected:
    ///  - node.Dependencies and node.ContributingBuilds are the same instances provided.
    ///  - Children and Parents are empty; VisitedNodes is empty and case-insensitive.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_AssignsDependenciesAndContributingBuildsByReference()
    {
        // Arrange
        var dep1 = new DependencyDetail { Name = "A", Version = "1.0.0", RepoUri = "https://r", Commit = "c1" };
        var dep2 = new DependencyDetail { Name = "B", Version = "2.0.0", RepoUri = "https://r", Commit = "c2" };
        var dependencies = new List<DependencyDetail> { dep1, dep2 };

        var build1 = new Build();
        var contributingBuilds = new HashSet<Build> { build1 };

        // Act
        var node = new DependencyGraphNode("https://repo", "commit-sha", dependencies, contributingBuilds);

        // Assert
        node.Dependencies.Should().BeSameAs(dependencies);
        node.ContributingBuilds.Should().BeSameAs(contributingBuilds);

        node.Children.Should().BeEmpty();
        node.Parents.Should().BeEmpty();
        node.VisitedNodes.Should().BeEmpty();
        node.VisitedNodes.Comparer.Should().BeSameAs(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates that the constructor assigns Repository and Commit fields exactly as provided,
    /// across a range of input string variations (empty, whitespace, typical URL/commit, long, and special characters).
    /// Inputs:
    ///  - repoUri and commit strings in various forms (non-null).
    /// Expected:
    ///  - The created node's Repository and Commit fields equal the inputs exactly.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(RepositoryCommitCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_AssignsRepositoryAndCommit_ExactValues(string repoUri, string commit)
    {
        // Arrange
        var dependencies = new List<DependencyDetail>();
        var visited = new HashSet<string>();
        var contributing = new HashSet<Microsoft.DotNet.ProductConstructionService.Client.Models.Build>();

        // Act
        var node = new DependencyGraphNode(repoUri, commit, dependencies, visited, contributing);

        // Assert
        node.Repository.Should().Be(repoUri);
        node.Commit.Should().Be(commit);
    }

    /// <summary>
    /// Ensures that VisitedNodes is cloned with an OrdinalIgnoreCase comparer, deduplicating items that differ only by case,
    /// and that subsequent changes to the source visitedNodes do not affect the node's VisitedNodes.
    /// Inputs:
    ///  - visitedNodes containing "alpha" and "ALPHA".
    ///  - After construction, source visitedNodes is mutated by adding "beta".
    /// Expected:
    ///  - node.VisitedNodes is a different instance than the source.
    ///  - node.VisitedNodes contains "alpha" (case-insensitive) and has only one item for "alpha"/"ALPHA".
    ///  - node.VisitedNodes does not contain "beta".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_VisitedNodesClonedWithOrdinalIgnoreCase_DeduplicatesAndIsIndependentFromSource()
    {
        // Arrange
        var repoUri = "https://example/repo";
        var commit = "abcdef123";
        var dependencies = new List<DependencyDetail>();

        var sourceVisited = new HashSet<string>(); // default comparer is case-sensitive
        sourceVisited.Add("alpha");
        sourceVisited.Add("ALPHA"); // duplicate under case-insensitive comparison

        var contributing = new HashSet<Microsoft.DotNet.ProductConstructionService.Client.Models.Build>();

        // Act
        var node = new DependencyGraphNode(repoUri, commit, dependencies, sourceVisited, contributing);
        sourceVisited.Add("beta");

        // Assert
        node.VisitedNodes.Should().NotBeSameAs(sourceVisited);
        node.VisitedNodes.Count.Should().Be(1);
        node.VisitedNodes.Contains("alpha").Should().BeTrue();
        node.VisitedNodes.Contains("ALPHA").Should().BeTrue();
        node.VisitedNodes.Contains("beta").Should().BeFalse();
    }

    /// <summary>
    /// Verifies that:
    ///  - Dependencies and ContributingBuilds are assigned by reference (not cloned).
    ///  - Children and Parents are initialized with a DependencyGraphNodeComparer so that nodes with the same
    ///    Repository and Commit are considered equal (deduplicated within the sets).
    /// Inputs:
    ///  - Two distinct node instances with identical Repository and Commit values added to Children/Parents.
    /// Expected:
    ///  - Children and Parents each contain a single entry after adding duplicates.
    ///  - Dependencies and ContributingBuilds refer to the same instances passed to the constructor.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_InitializesSetsWithComparer_AndAssignsCollectionsByReference()
    {
        // Arrange
        var repoUriParent = "https://example/parent";
        var commitParent = "commit-parent";

        var dependencies = new List<DependencyDetail>
            {
                new DependencyDetail { Name = "A", Version = "1.0.0", RepoUri = "r", Commit = "c" }
            };
        var visited = new HashSet<string>();
        var contributing = new HashSet<Microsoft.DotNet.ProductConstructionService.Client.Models.Build>();

        var parent = new DependencyGraphNode(repoUriParent, commitParent, dependencies, visited, contributing);

        var repoUriChild = "https://example/child";
        var commitChild = "commit-child";

        var child1 = new DependencyGraphNode(repoUriChild, commitChild, new List<DependencyDetail>(), new HashSet<string>(), new HashSet<Microsoft.DotNet.ProductConstructionService.Client.Models.Build>());
        var child2 = new DependencyGraphNode(repoUriChild, commitChild, new List<DependencyDetail>(), new HashSet<string>(), new HashSet<Microsoft.DotNet.ProductConstructionService.Client.Models.Build>());

        var parentDup1 = new DependencyGraphNode(repoUriParent, commitParent, new List<DependencyDetail>(), new HashSet<string>(), new HashSet<Microsoft.DotNet.ProductConstructionService.Client.Models.Build>());
        var parentDup2 = new DependencyGraphNode(repoUriParent, commitParent, new List<DependencyDetail>(), new HashSet<string>(), new HashSet<Microsoft.DotNet.ProductConstructionService.Client.Models.Build>());

        // Act
        parent.Children.Add(child1);
        parent.Children.Add(child2); // should deduplicate by comparer

        child1.Parents.Add(parentDup1);
        child1.Parents.Add(parentDup2); // should deduplicate by comparer

        // Assert
        parent.Dependencies.Should().BeSameAs(dependencies);
        parent.ContributingBuilds.Should().BeSameAs(contributing);

        parent.Children.Count.Should().Be(1);
        child1.Parents.Count.Should().Be(1);
    }

    private static IEnumerable<object[]> RepositoryCommitCases()
    {
        yield return new object[] { "", "" };
        yield return new object[] { " ", "\t\n" };
        yield return new object[] { "https://github.com/dotnet/arcade", "abcdef1234567890" };
        yield return new object[] { new string('a', 1024), "bbbbbbbbbbbbbbbb" };
        yield return new object[] { "Ω≈ç√∫˜µ≤≥÷", "𝄞🎵" };
    }

    /// <summary>
    /// Ensures that calling AddChild links the parent and child in both directions.
    /// Inputs:
    ///  - selfAsChild: when true, the node adds itself as a child; otherwise, a distinct child node is used.
    ///  - passNullDependency: when true, the 'dependency' argument is null; otherwise, a valid DependencyDetail is provided.
    /// Expected:
    ///  - Parent.Children contains the child.
    ///  - Child.Parents contains the parent.
    ///  - No exceptions are thrown.
    /// </summary>
    [TestCase(false, true)]
    [TestCase(true, false)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void AddChild_ValidChild_LinksBothSides(bool selfAsChild, bool passNullDependency)
    {
        // Arrange
        var parent = CreateNode("https://repo/p", "sha-p");
        var child = selfAsChild ? parent : CreateNode("https://repo/c", "sha-c");
        var dependency = passNullDependency ? null : new DependencyDetail();

        // Act
        parent.AddChild(child, dependency);

        // Assert
        parent.Children.Should().Contain(child);
        child.Parents.Should().Contain(parent);
        parent.Children.Should().HaveCount(1);
        child.Parents.Should().HaveCount(1);
    }

    /// <summary>
    /// Verifies that adding the same child twice does not create duplicate entries
    /// in either the parent's Children or the child's Parents sets.
    /// Inputs:
    ///  - A parent node and a distinct child node; AddChild is invoked twice with the same child and dependency.
    /// Expected:
    ///  - Parent.Children contains exactly one instance of the child.
    ///  - Child.Parents contains exactly one instance of the parent.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void AddChild_SameChildAddedTwice_DoesNotCreateDuplicates()
    {
        // Arrange
        var parent = CreateNode("https://repo/p", "sha-p");
        var child = CreateNode("https://repo/c", "sha-c");
        var dependency = new DependencyDetail();

        // Act
        parent.AddChild(child, dependency);
        parent.AddChild(child, dependency);

        // Assert
        parent.Children.Should().Contain(child);
        child.Parents.Should().Contain(parent);
        parent.Children.Should().HaveCount(1);
        child.Parents.Should().HaveCount(1);
    }

    /// <summary>
    /// Ensures that passing a null child to AddChild throws an ArgumentNullException.
    /// Inputs:
    ///  - newChild: null
    ///  - dependency: valid DependencyDetail instance
    /// Expected:
    ///  - ArgumentNullException is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void AddChild_NullChild_ThrowsArgumentNullException()
    {
        // Arrange
        var parent = CreateNode("https://repo/p", "sha-p");
        var dependency = new DependencyDetail();

        // Act
        Action act = () => parent.AddChild(null, dependency);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    private static DependencyGraphNode CreateNode(string repo, string sha)
    {
        return new DependencyGraphNode(
            repo,
            sha,
            new List<DependencyDetail>(),
            new HashSet<Build>());
    }
}
