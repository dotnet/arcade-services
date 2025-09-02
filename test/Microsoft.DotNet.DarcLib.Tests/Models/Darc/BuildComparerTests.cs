// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Models.Darc.UnitTests;

public class BuildComparerTests
{
    /// <summary>
    /// Verifies that Equals returns true when two Build instances have identical Id values.
    /// Inputs:
    ///  - A pair of Build instances constructed with the same Id, covering boundary values.
    /// Expected:
    ///  - Equals returns true.
    /// </summary>
    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(int.MaxValue)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Equals_SameId_ReturnsTrue(int id)
    {
        // Arrange
        var comparer = new BuildComparer();
        var x = CreateBuild(id);
        var y = CreateBuild(id);

        // Act
        var result = comparer.Equals(x, y);

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that Equals returns false when two Build instances have different Id values.
    /// Inputs:
    ///  - Pairs of differing Ids, including boundary and mixed-sign combinations.
    /// Expected:
    ///  - Equals returns false.
    /// </summary>
    [TestCase(int.MinValue, int.MaxValue)]
    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(-1, 1)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Equals_DifferentId_ReturnsFalse(int xId, int yId)
    {
        // Arrange
        var comparer = new BuildComparer();
        var x = CreateBuild(xId);
        var y = CreateBuild(yId);

        // Act
        var result = comparer.Equals(x, y);

        // Assert
        result.Should().BeFalse();
    }

    private static Build CreateBuild(int id)
    {
        return new Build(
            id: id,
            dateProduced: DateTimeOffset.UtcNow,
            staleness: 0,
            released: false,
            stable: false,
            commit: "commit",
            channels: new List<Channel>(),
            assets: new List<Asset>(),
            dependencies: new List<BuildRef>(),
            incoherencies: new List<BuildIncoherence>());
    }

    /// <summary>
    /// Verifies that GetHashCode returns the build's Id directly.
    /// Inputs:
    ///  - Build instances with a range of Id values including int.MinValue, negative, zero, positive, and int.MaxValue.
    /// Expected:
    ///  - The returned hash code equals the provided Id for each input.
    /// </summary>
    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(int.MaxValue)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetHashCode_IdExtremes_ReturnsIdAsHashCode(int id)
    {
        // Arrange
        var sut = new BuildComparer();
        var build = new Build(
            id: id,
            dateProduced: DateTimeOffset.UnixEpoch,
            staleness: 0,
            released: false,
            stable: false,
            commit: "commit",
            channels: new List<Channel>(),
            assets: new List<Asset>(),
            dependencies: new List<BuildRef>(),
            incoherencies: new List<BuildIncoherence>());

        // Act
        var hash = sut.GetHashCode(build);

        // Assert
        hash.Should().Be(id);
    }

    /// <summary>
    /// Ensures that different Build instances with the same Id produce identical hash codes.
    /// Inputs:
    ///  - Two Build instances constructed separately but sharing the same Id value.
    /// Expected:
    ///  - GetHashCode returns the same value for both instances.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetHashCode_DistinctBuildsWithSameId_ProducesEqualHashCodes()
    {
        // Arrange
        var sut = new BuildComparer();
        const int sharedId = 42;

        var build1 = new Build(
            id: sharedId,
            dateProduced: DateTimeOffset.UnixEpoch,
            staleness: 1,
            released: false,
            stable: false,
            commit: "a",
            channels: new List<Channel>(),
            assets: new List<Asset>(),
            dependencies: new List<BuildRef>(),
            incoherencies: new List<BuildIncoherence>());

        var build2 = new Build(
            id: sharedId,
            dateProduced: DateTimeOffset.UnixEpoch.AddDays(1),
            staleness: 2,
            released: true,
            stable: true,
            commit: "b",
            channels: new List<Channel>(),
            assets: new List<Asset>(),
            dependencies: new List<BuildRef>(),
            incoherencies: new List<BuildIncoherence>());

        // Act
        var hash1 = sut.GetHashCode(build1);
        var hash2 = sut.GetHashCode(build2);

        // Assert
        hash1.Should().Be(hash2);
    }
}
