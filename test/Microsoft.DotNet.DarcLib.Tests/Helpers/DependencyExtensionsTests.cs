// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;


namespace Microsoft.DotNet.DarcLib.Helpers.UnitTests;

public class DependencyExtensionsTests
{
    private const string ArcadeName = "Microsoft.DotNet.Arcade.Sdk";

    /// <summary>
    /// Validates that when the sequence is empty or contains no updates whose 'To' dependency
    /// Name equals Microsoft.DotNet.Arcade.Sdk (case-insensitive), the method returns null.
    /// Inputs:
    ///  - updates: either an empty sequence or a sequence with non-matching 'To' names.
    /// Expected:
    ///  - Result is null.
    /// </summary>
    [TestCaseSource(nameof(NoArcadeMatchCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetArcadeUpdate_EmptyOrNoMatch_ReturnsNull(IEnumerable<DependencyUpdate> updates)
    {
        // Arrange
        // (updates provided by TestCaseSource)

        // Act
        var result = updates.GetArcadeUpdate();

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Ensures that matching is performed against the 'To' dependency only and is case-insensitive.
    /// Inputs:
    ///  - A single update where 'To.Name' is a case-mixed version of Microsoft.DotNet.Arcade.Sdk.
    /// Expected:
    ///  - The exact 'To' DependencyDetail instance is returned.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetArcadeUpdate_CaseInsensitiveMatchOnTo_ReturnsMatchingTo()
    {
        // Arrange
        var expected = new DependencyDetail { Name = "mIcRoSoFt.DoTnEt.ArCaDe.SdK" };
        var updates = new[]
        {
                new DependencyUpdate
                {
                    From = new DependencyDetail { Name = "SomethingElse" },
                    To = expected
                }
            };

        // Act
        var result = updates.GetArcadeUpdate();

        // Assert
        result.Should().BeSameAs(expected);
    }

    /// <summary>
    /// Verifies that when only the 'From' dependency matches Microsoft.DotNet.Arcade.Sdk and 'To' does not,
    /// no match is returned because the method only considers 'To'.
    /// Inputs:
    ///  - A single update where From.Name == Microsoft.DotNet.Arcade.Sdk and To.Name != Microsoft.DotNet.Arcade.Sdk.
    /// Expected:
    ///  - Result is null.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetArcadeUpdate_FromMatchesOnly_ReturnsNull()
    {
        // Arrange
        var updates = new[]
        {
                new DependencyUpdate
                {
                    From = new DependencyDetail { Name = ArcadeName },
                    To = new DependencyDetail { Name = "Not." + ArcadeName }
                }
            };

        // Act
        var result = updates.GetArcadeUpdate();

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Ensures the first matching 'To' dependency is returned when multiple updates match.
    /// Inputs:
    ///  - Two updates where both 'To.Name' values equal Microsoft.DotNet.Arcade.Sdk.
    /// Expected:
    ///  - The 'To' from the first update in enumeration order is returned.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetArcadeUpdate_MultipleMatches_ReturnsFirstInSequence()
    {
        // Arrange
        var first = new DependencyDetail { Name = ArcadeName };
        var second = new DependencyDetail { Name = ArcadeName };

        var updates = new[]
        {
                new DependencyUpdate { From = new DependencyDetail { Name = "X" }, To = first },
                new DependencyUpdate { From = new DependencyDetail { Name = "Y" }, To = second }
            };

        // Act
        var result = updates.GetArcadeUpdate();

        // Assert
        result.Should().BeSameAs(first);
    }

    private static IEnumerable<TestCaseData> NoArcadeMatchCases()
    {
        yield return new TestCaseData(Array.Empty<DependencyUpdate>())
            .SetName("GetArcadeUpdate_Empty_ReturnsNull");

        yield return new TestCaseData(new[]
        {
                new DependencyUpdate { From = new DependencyDetail { Name = "A" }, To = new DependencyDetail { Name = "B" } },
                new DependencyUpdate { From = new DependencyDetail { Name = "C" }, To = new DependencyDetail { Name = "D" } },
            }).SetName("GetArcadeUpdate_NoMatch_ReturnsNull");
    }
}
