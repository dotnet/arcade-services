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

public class AssetComparerTests
{
    /// <summary>
    /// Validates AssetComparer.Equals compares Asset instances by Name (case-insensitive) and Version (case-sensitive).
    /// Inputs:
    ///  - Pairs of Asset names and versions covering case differences, whitespace, empty strings, long strings, and special characters.
    /// Expected:
    ///  - True when names are equal ignoring case and versions are exactly equal; otherwise false.
    /// </summary>
    [TestCase("Package.X", "1.2.3", "package.x", "1.2.3", true, TestName = "Equals_NameCaseInsensitive_VersionEqual_True")]
    [TestCase("Package.X", "1.2.3", "package.x", "1.2.4", false, TestName = "Equals_NameCaseInsensitive_VersionDifferent_False")]
    [TestCase("Alpha", "v", "Beta", "v", false, TestName = "Equals_NameDifferent_VersionEqual_False")]
    [TestCase("", "", "", "", true, TestName = "Equals_EmptyNameAndVersion_True")]
    [TestCase("pkg", "1", "pkg ", "1", false, TestName = "Equals_TrailingWhitespaceInName_False")]
    [TestCaseSource(nameof(LongAndSpecialCaseData))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Equals_NameAndVersion_VariousCases_ReturnsExpected(string xName, string xVersion, string yName, string yVersion, bool expected)
    {
        // Arrange
        var x = new Asset(id: 1, buildId: 10, nonShipping: false, name: xName, version: xVersion, locations: new List<AssetLocation>());
        var y = new Asset(id: 2, buildId: 20, nonShipping: true, name: yName, version: yVersion, locations: new List<AssetLocation>());
        var sut = new AssetComparer();

        // Act
        var result = sut.Equals(x, y);

        // Assert
        result.Should().Be(expected);
    }

    private static IEnumerable LongAndSpecialCaseData()
    {
        // Very long names equal ignoring case
        var longUpper = new string('A', 1024);
        var longLower = new string('a', 1024);
        yield return new TestCaseData(longUpper, "9", longLower, "9", true)
            .SetName("Equals_VeryLongNameCaseInsensitive_VersionEqual_True");

        // Names with diacritics equal ignoring case
        yield return new TestCaseData("Päckage", "2.0", "pÄCKAGE", "2.0", true)
            .SetName("Equals_SpecialCharactersCaseInsensitive_VersionEqual_True");
    }

    /// <summary>
    /// Verifies that AssetComparer.Equals(Asset, DependencyDetail) compares:
    /// - Name using StringComparison.OrdinalIgnoreCase.
    /// - Version using an exact, case-sensitive string comparison.
    /// Inputs:
    /// - assetName/depName variations for case-insensitive match or mismatch.
    /// - assetVersion/depVersion variations for exact match or mismatch (including case).
    /// Expected:
    /// - Returns true only when names are equal ignoring case AND versions are exactly equal.
    /// </summary>
    /// <param name="assetName">Asset.Name value.</param>
    /// <param name="depName">DependencyDetail.Name value.</param>
    /// <param name="assetVersion">Asset.Version value.</param>
    /// <param name="depVersion">DependencyDetail.Version value.</param>
    /// <param name="expected">Expected boolean result.</param>
    [TestCase("Package.X", "package.x", "1.2.3", "1.2.3", true)]
    [TestCase("Package.X", "pAcKaGe.X", "1.2.3", "1.2.3.0", false)]
    [TestCase("Package.X", "Different", "1.2.3", "1.2.3", false)]
    [TestCase("Name", "name", "1.0-Preview", "1.0-preview", false)]
    [TestCase("  A  ", "  a  ", "v", "v", true)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Equals_CaseInsensitiveNameAndCaseSensitiveVersion_ReturnsExpected(
        string assetName,
        string depName,
        string assetVersion,
        string depVersion,
        bool expected)
    {
        // Arrange
        var asset = new Asset(
            id: 1,
            buildId: 1,
            nonShipping: false,
            name: assetName,
            version: assetVersion,
            locations: new List<AssetLocation>());

        var dependency = new DependencyDetail
        {
            Name = depName,
            Version = depVersion
        };

        // Act
        var actual = AssetComparer.Equals(asset, dependency);

        // Assert
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that GetHashCode returns the same hash for assets whose Name and Version are identical,
    /// even when other fields (Id, BuildId, NonShipping, Locations) differ.
    /// Inputs:
    ///  - Various (name, version) pairs including empty, whitespace-only, long, and special-character strings.
    /// Expected:
    ///  - Hash codes computed for two different Asset instances with the same Name and Version are equal.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(NameVersionCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetHashCode_SameNameAndVersionAcrossDifferentOtherFields_ReturnsEqualHash(string name, string version)
    {
        // Arrange
        var comparer = new AssetComparer();
        var asset1 = new Asset(
            id: int.MinValue,
            buildId: int.MaxValue,
            nonShipping: false,
            name: name,
            version: version,
            locations: new List<AssetLocation>());

        var asset2 = new Asset(
            id: int.MaxValue,
            buildId: int.MinValue,
            nonShipping: true,
            name: name,
            version: version,
            locations: new List<AssetLocation>());

        // Act
        var hash1 = comparer.GetHashCode(asset1);
        var hash2 = comparer.GetHashCode(asset2);

        // Assert
        hash1.Should().Be(hash2);
    }

    /// <summary>
    /// Ensures that GetHashCode is stable for the same Asset instance across multiple invocations.
    /// Inputs:
    ///  - A single Asset instance with representative Name and Version values.
    /// Expected:
    ///  - Repeated calls to GetHashCode return the same hash value.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetHashCode_SameInstanceMultipleInvocations_ReturnsStableHash()
    {
        // Arrange
        var comparer = new AssetComparer();
        var asset = new Asset(
            id: 1,
            buildId: 2,
            nonShipping: false,
            name: "Package.X",
            version: "1.2.3",
            locations: new List<AssetLocation>());

        // Act
        var hashFirst = comparer.GetHashCode(asset);
        var hashSecond = comparer.GetHashCode(asset);

        // Assert
        hashFirst.Should().Be(hashSecond);
    }

    private static IEnumerable<TestCaseData> NameVersionCases()
    {
        yield return new TestCaseData("", "").SetName("EmptyStrings");
        yield return new TestCaseData(" ", " ").SetName("WhitespaceOnly");
        yield return new TestCaseData("Package.X", "1.2.3").SetName("TypicalValues");
        yield return new TestCaseData(new string('A', 1024), new string('9', 1024)).SetName("VeryLongStrings");
        yield return new TestCaseData("特殊字符-∆✓\t\n\r\0", "ver🚀-β-1.0.0\n").SetName("SpecialAndControlCharacters");
    }
}
