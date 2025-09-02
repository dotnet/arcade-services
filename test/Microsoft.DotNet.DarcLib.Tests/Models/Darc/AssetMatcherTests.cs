// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions;
using Microsoft.Extensions.FileSystemGlobbing;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;

public class AssetFilterExtensionsTests
{
    /// <summary>
    /// Ensures that when filters are null or empty, GetAssetMatcher returns an IAssetMatcher
    /// that does not exclude any asset name.
    /// Inputs:
    ///  - filters: null and an empty collection.
    /// Expected:
    ///  - Returned matcher is not null.
    ///  - Calling IsExcluded for any name returns false.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(NullOrEmptyFilterCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetAssetMatcher_NullOrEmptyFilters_ReturnsMatcherThatDoesNotExcludeAnyName(IReadOnlyCollection<string> filters)
    {
        // Arrange
        // filters provided by TestCaseSource

        // Act
        var matcher = filters.GetAssetMatcher();

        // Assert
        matcher.Should().NotBeNull();
        matcher.IsExcluded("any").Should().BeFalse();
        matcher.IsExcluded("foo/bar.txt").Should().BeFalse();
    }

    /// <summary>
    /// Verifies that when filters contain include patterns, the returned matcher applies them correctly.
    /// Inputs:
    ///  - filters: { "**/*.txt", "foo/*" }
    ///  - assetName: parameterized.
    /// Expected:
    ///  - IsExcluded returns true when assetName matches any include pattern; otherwise false.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("foo/bar.cs", true)]
    [TestCase("baz/readme.txt", true)]
    [TestCase("baz/image.png", false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetAssetMatcher_WithPatterns_AppliesIncludePatterns(string assetName, bool expectedExcluded)
    {
        // Arrange
        IReadOnlyCollection<string> filters = new List<string> { "**/*.txt", "foo/*" };

        // Act
        var matcher = filters.GetAssetMatcher();
        var result = matcher.IsExcluded(assetName);

        // Assert
        matcher.Should().NotBeNull();
        result.Should().Be(expectedExcluded);
    }

    private static IEnumerable<IReadOnlyCollection<string>> NullOrEmptyFilterCases()
    {
        yield return null;
        yield return Array.Empty<string>();
    }
}


// Intentionally present to align with the convention of having a test class per production class.
// No tests here because only AssetFilterExtensions.GetAssetMatcher is in scope for testing.
public class AssetMatcherTests
{
    /// <summary>
    /// Verifies that when no filters are provided (filters == null),
    /// IsExcluded always returns false regardless of the asset name.
    /// Inputs:
    ///  - filters: null
    ///  - name: various values including null, empty, whitespace, and typical names
    /// Expected:
    ///  - IsExcluded returns false (asset not excluded)
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("file.txt")]
    [TestCase("lib/file.dll")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsExcluded_NullFilters_ReturnsFalse(string name)
    {
        // Arrange
        var matcher = AssetFilterExtensions.GetAssetMatcher((IReadOnlyCollection<string>)null);

        // Act
        var excluded = matcher.IsExcluded(name);

        // Assert
        excluded.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that when an empty filter collection is provided,
    /// IsExcluded always returns false regardless of the asset name.
    /// Inputs:
    ///  - filters: empty collection
    ///  - name: various values including null, empty, whitespace, and typical names
    /// Expected:
    ///  - IsExcluded returns false (asset not excluded)
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("file.txt")]
    [TestCase("lib/file.dll")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsExcluded_EmptyFilters_ReturnsFalse(string name)
    {
        // Arrange
        var filters = new List<string>();
        var matcher = filters.GetAssetMatcher();

        // Act
        var excluded = matcher.IsExcluded(name);

        // Assert
        excluded.Should().BeFalse();
    }

    /// <summary>
    /// Ensures IsExcluded evaluates inclusion patterns using the underlying matcher and
    /// returns true when the pattern matches, otherwise false.
    /// Inputs:
    ///  - filters: globbing include patterns (e.g., *.nupkg, lib/*, **/*.dll)
    ///  - name: asset name to test
    /// Expected:
    ///  - Returned value equals whether the name matches the patterns (true if matched, false otherwise)
    /// </summary>
    [TestCaseSource(nameof(IncludePatternCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsExcluded_WithIncludePatterns_EvaluatesMatchAsExclusion(IEnumerable<string> filters, string name, bool expected)
    {
        // Arrange
        var filterList = new List<string>(filters);
        var matcher = filterList.GetAssetMatcher();

        // Act
        var excluded = matcher.IsExcluded(name);

        // Assert
        excluded.Should().Be(expected);
    }

    private static IEnumerable IncludePatternCases()
    {
        yield return new TestCaseData(new[] { "*.nupkg" }, "package.nupkg", true)
            .SetName("IsExcluded_PatternNupkg_PackageNupkg_ReturnsTrue");
        yield return new TestCaseData(new[] { "*.nupkg" }, "package.zip", false)
            .SetName("IsExcluded_PatternNupkg_PackageZip_ReturnsFalse");
        yield return new TestCaseData(new[] { "lib/*" }, "lib/a.dll", true)
            .SetName("IsExcluded_PatternLibWildcard_LibDll_ReturnsTrue");
        yield return new TestCaseData(new[] { "lib/*" }, "src/a.dll", false)
            .SetName("IsExcluded_PatternLibWildcard_SrcDll_ReturnsFalse");
        yield return new TestCaseData(new[] { "**/*.dll" }, "bin/a.dll", true)
            .SetName("IsExcluded_PatternRecursiveDll_BinDll_ReturnsTrue");
        yield return new TestCaseData(new[] { "**/*.dll" }, "a.txt", false)
            .SetName("IsExcluded_PatternRecursiveDll_TextFile_ReturnsFalse");
    }

    /// <summary>
    /// Validates that when filters are provided (non-null matcher),
    /// calling IsExcluded with a null name throws an ArgumentNullException
    /// due to the underlying globbing matcher requiring a non-null input.
    /// Inputs:
    ///  - filters: ["*"]
    ///  - name: null
    /// Expected:
    ///  - ArgumentNullException is thrown
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsExcluded_WithMatcherAndNullName_ThrowsArgumentNullException()
    {
        // Arrange
        var filters = new List<string> { "*" };
        var matcher = filters.GetAssetMatcher();

        // Act
        Action act = () => matcher.IsExcluded(null);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies the constructor accepts both null and non-null Matcher instances without throwing,
    /// and that the constructed object reflects the provided matcher when evaluating exclusions via IsExcluded.
    /// Inputs:
    ///  - provideMatcher: false (null matcher), true (configured matcher with a pattern exactly equal to 'name').
    ///  - name: the asset name to evaluate.
    /// Expected:
    ///  - Instance is created (not null) in all cases.
    ///  - When matcher is null, IsExcluded(name) returns false.
    ///  - When matcher is configured to include 'name', IsExcluded(name) returns true (proves constructor assigned provided matcher).
    /// </summary>
    [TestCase(false, "any.txt", false)]
    [TestCase(true, "file.txt", true)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_NullVsConfiguredMatcher_ObjectCreatedAndExclusionReflectsMatcher(bool provideMatcher, string name, bool expectedExcluded)
    {
        // Arrange
        Matcher matcher = provideMatcher ? new Matcher() : null;
        if (provideMatcher)
        {
            matcher.AddIncludePatterns(new[] { name });
        }

        // Act
        var sut = new AssetMatcher(matcher);
        var isExcluded = sut.IsExcluded(name);

        // Assert
        sut.Should().NotBeNull();
        isExcluded.Should().Be(expectedExcluded);
    }

    /// <summary>
    /// Ensures IsExcluded returns false when constructed with a null Matcher.
    /// Inputs:
    ///  - matcher: null
    ///  - name: "anything.ext"
    /// Expected:
    ///  - Returns false without throwing.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsExcluded_MatcherNull_ReturnsFalse()
    {
        // Arrange
        var sut = new AssetMatcher(null);
        var name = "anything.ext";

        // Act
        var excluded = sut.IsExcluded(name);

        // Assert
        excluded.Should().BeFalse();
    }

    /// <summary>
    /// Ensures IsExcluded returns false when the Matcher has no include patterns.
    /// Inputs:
    ///  - matcher: new Matcher() with no includes
    ///  - name: various non-null strings
    /// Expected:
    ///  - Returns false for all inputs.
    /// </summary>
    [TestCase("file.txt")]
    [TestCase("a.dll")]
    [TestCase("dir/a.dll")]
    [TestCase(" ")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsExcluded_NoIncludePatterns_ReturnsFalse(string name)
    {
        // Arrange
        var matcher = new Matcher();
        var sut = new AssetMatcher(matcher);

        // Act
        var excluded = sut.IsExcluded(name);

        // Assert
        excluded.Should().BeFalse();
    }

    /// <summary>
    /// Validates matching and non-matching outcomes with common DLL glob patterns.
    /// Inputs:
    ///  - matcher: includes "*.dll" and "**/*.dll"
    ///  - name: parameterized test values
    /// Expected:
    ///  - Returns true for DLL names (including nested paths), false otherwise.
    /// </summary>
    [TestCase("a.dll", true)]
    [TestCase("b.txt", false)]
    [TestCase("sub/inner/a.dll", true)]
    [TestCase("readme", false)]
    [TestCase("weird-😀.dll", true)]
    [TestCase("a.dllx", false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsExcluded_WithDllPatterns_MatchesAccordingToGlobs(string name, bool expected)
    {
        // Arrange
        var matcher = new Matcher();
        matcher.AddInclude("*.dll");
        matcher.AddInclude("**/*.dll");
        var sut = new AssetMatcher(matcher);

        // Act
        var excluded = sut.IsExcluded(name);

        // Assert
        excluded.Should().Be(expected);
    }

    /// <summary>
    /// Ensures very long file names are handled and match as expected when pattern is "*.dll".
    /// Inputs:
    ///  - matcher: includes "*.dll"
    ///  - name: a very long string ending with ".dll"
    /// Expected:
    ///  - Returns true (pattern matches suffix).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsExcluded_WithDllPattern_VeryLongName_ReturnsTrue()
    {
        // Arrange
        var matcher = new Matcher();
        matcher.AddInclude("*.dll");
        var sut = new AssetMatcher(matcher);
        var longBase = new string('a', 2048);
        var name = longBase + ".dll";

        // Act
        var excluded = sut.IsExcluded(name);

        // Assert
        excluded.Should().BeTrue();
    }
}
