// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using FluentAssertions.Specialized;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Moq;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.DotNet.DarcLib.Models.Darc.UnitTests;

public class DependencyDetailTests
{
    /// <summary>
    /// Verifies that the parameterless constructor initializes Locations to a new, empty List of strings.
    /// Inputs:
    ///  - No parameters (default constructor).
    /// Expected:
    ///  - Locations is not null.
    ///  - Locations is empty.
    ///  - Locations is of type List&lt;string&gt; (not just an IEnumerable&lt;string&gt;).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DependencyDetail_DefaultConstructor_InitializesLocationsToEmptyList()
    {
        // Arrange
        // (no inputs)

        // Act
        var dependency = new DependencyDetail();

        // Assert
        dependency.Locations.Should().NotBeNull();
        dependency.Locations.Should().BeEmpty();
        dependency.Locations.Should().BeOfType<List<string>>();
    }

    /// <summary>
    /// Verifies that the copy constructor copies all scalar properties (Name, Version, RepoUri, Commit, Pinned, SkipProperty,
    /// Type, CoherentParentDependencyName) exactly as-is from the source instance.
    /// Inputs:
    ///  - Various combinations of strings (including empty, whitespace, special characters), booleans, and enum values (via int cast).
    /// Expected:
    ///  - The new instance has property values equal to those of the source instance.
    /// </summary>
    [TestCase("Pkg-α_🚀", "1.2.3", "https://example/repo", "deadbeef", true, true, "Parent.X", 1)]
    [TestCase("", "", "", "", false, false, "", 0)]
    [TestCase(" ", " \t ", " \r\n ", " ", false, true, " \t ", -1)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_CopiesAllScalarProperties_AsIs(
        string name,
        string version,
        string repoUri,
        string commit,
        bool pinned,
        bool skipProperty,
        string coherentParentName,
        int typeValue)
    {
        // Arrange
        var source = new DependencyDetail
        {
            Name = name,
            Version = version,
            RepoUri = repoUri,
            Commit = commit,
            Pinned = pinned,
            SkipProperty = skipProperty,
            Type = (DependencyType)typeValue,
            CoherentParentDependencyName = coherentParentName,
            Locations = new List<string> { "loc1", "loc2" }
        };

        // Act
        var clone = new DependencyDetail(source);

        // Assert
        clone.Name.Should().Be(name);
        clone.Version.Should().Be(version);
        clone.RepoUri.Should().Be(repoUri);
        clone.Commit.Should().Be(commit);
        clone.Pinned.Should().Be(pinned);
        clone.SkipProperty.Should().Be(skipProperty);
        clone.Type.Should().Be((DependencyType)typeValue);
        clone.CoherentParentDependencyName.Should().Be(coherentParentName);
        clone.Locations.Should().BeSameAs(source.Locations);
    }

    /// <summary>
    /// Ensures that the copy constructor does not create a defensive copy of the Locations collection,
    /// but instead assigns the same reference as the source instance.
    /// Inputs:
    ///  - A source DependencyDetail whose Locations points to a List<string>.
    /// Expected:
    ///  - The cloned instance's Locations reference is the same object as the source's Locations.
    ///  - Mutating the original list after construction is reflected in the cloned instance.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_LocationsReferenceIsShared_ModifyingOriginalReflectsInClone()
    {
        // Arrange
        var sharedLocations = new List<string> { "L1" };
        var source = new DependencyDetail
        {
            Name = "A",
            Version = "1.0.0",
            RepoUri = "https://example/repo",
            Commit = "sha",
            Pinned = false,
            SkipProperty = false,
            Type = (DependencyType)42,
            CoherentParentDependencyName = "Parent",
            Locations = sharedLocations
        };

        // Act
        var clone = new DependencyDetail(source);
        sharedLocations.Add("L2");

        // Assert
        object.ReferenceEquals(clone.Locations, sharedLocations).Should().BeTrue();
        clone.Locations.Should().BeEquivalentTo(sharedLocations);
    }

    /// <summary>
    /// Ensures Validate throws DarcException when any required property is null.
    /// Inputs:
    ///  - DependencyDetail with one of the required properties (Version, Name, Commit, RepoUri) set to null.
    /// Expected:
    ///  - Throws DarcException with message "{Property} of the dependency detail record is empty".
    /// </summary>
    [TestCase("Version")]
    [TestCase("Name")]
    [TestCase("Commit")]
    [TestCase("RepoUri")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Validate_NullRequiredProperty_ThrowsDarcException(string nullProperty)
    {
        // Arrange
        var sut = new DependencyDetail
        {
            Name = "name",
            Version = "1.0.0",
            Commit = "sha",
            RepoUri = "https://repo"
        };

        switch (nullProperty)
        {
            case nameof(DependencyDetail.Version):
                sut.Version = null;
                break;
            case nameof(DependencyDetail.Name):
                sut.Name = null;
                break;
            case nameof(DependencyDetail.Commit):
                sut.Commit = null;
                break;
            case nameof(DependencyDetail.RepoUri):
                sut.RepoUri = null;
                break;
        }

        var expectedMessage = $"{nullProperty} of the dependency detail record is empty";

        // Act
        Action act = () => sut.Validate();

        // Assert
        act.Should().ThrowExactly<DarcException>().Which.Message.Should().Be(expectedMessage);
    }

    /// <summary>
    /// Verifies that Validate does not throw when all required properties are non-null.
    /// Inputs:
    ///  - DependencyDetail with Version, Name, Commit, RepoUri all set to non-null strings.
    /// Expected:
    ///  - No exception is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Validate_AllRequiredPropertiesPresent_DoesNotThrow()
    {
        // Arrange
        var sut = new DependencyDetail
        {
            Name = "name",
            Version = "1.0.0",
            Commit = "sha",
            RepoUri = "https://repo"
        };

        // Act
        Action act = () => sut.Validate();

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Confirms that Validate allows empty, whitespace, long, and special-character strings
    /// for required properties (since only null is checked).
    /// Inputs:
    ///  - DependencyDetail where one required property is set to various non-null edge-case strings.
    /// Expected:
    ///  - No exception is thrown.
    /// </summary>
    [TestCaseSource(nameof(NonNullStringValueCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Validate_NonNullEdgeValuesForRequiredProperties_DoesNotThrow(string targetProperty, string value)
    {
        // Arrange
        var sut = new DependencyDetail
        {
            Name = "name",
            Version = "1.0.0",
            Commit = "sha",
            RepoUri = "https://repo"
        };

        switch (targetProperty)
        {
            case nameof(DependencyDetail.Version):
                sut.Version = value;
                break;
            case nameof(DependencyDetail.Name):
                sut.Name = value;
                break;
            case nameof(DependencyDetail.Commit):
                sut.Commit = value;
                break;
            case nameof(DependencyDetail.RepoUri):
                sut.RepoUri = value;
                break;
        }

        // Act
        Action act = () => sut.Validate();

        // Assert
        act.Should().NotThrow();
    }

    private static IEnumerable<TestCaseData> NonNullStringValueCases()
    {
        var properties = new[] { nameof(DependencyDetail.Version), nameof(DependencyDetail.Name), nameof(DependencyDetail.Commit), nameof(DependencyDetail.RepoUri) };
        var values = new[]
        {
                "",
                " ",
                "line1\nline2",
                new string('a', 5000)
            };

        foreach (var prop in properties)
        {
            foreach (var val in values)
            {
                yield return new TestCaseData(prop, val);
            }
        }
    }
}
