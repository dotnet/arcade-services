// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Helpers.UnitTests;

public class VersionFilesTests
{
    /// <summary>
    /// Validates that GetVersionPropsPackageVersionElementName removes '.' and '-' from the dependency name
    /// and appends the VersionPropsVersionElementSuffix.
    /// Inputs:
    ///  - Various dependency names including dots, hyphens, empty, whitespace, mixed characters, and Unicode.
    /// Expected:
    ///  - Output equals the sanitized dependency name (without '.' and '-') concatenated with VersionPropsVersionElementSuffix.
    /// </summary>
    /// <param name="dependencyName">The input dependency name to sanitize.</param>
    /// <param name="expectedSanitizedBase">The expected sanitized base after removing '.' and '-'.</param>
    [Test]
    [Category("auto-generated")]
    [TestCase("Package.Name-Version", "PackageNameVersion")]
    [TestCase(".-", "")]
    [TestCase("", "")]
    [TestCase("   my_pkg  ", "   my_pkg  ")]
    [TestCase("co.m-plex.-name--v1.0", "complexnamev10")]
    [TestCase("你好-世界.テスト", "你好世界テスト")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetVersionPropsPackageVersionElementName_VariousNames_SanitizedAndSuffixAppended(string dependencyName, string expectedSanitizedBase)
    {
        // Arrange
        var expected = expectedSanitizedBase + VersionDetailsParser.VersionPropsVersionElementSuffix;

        // Act
        var actual = VersionFiles.GetVersionPropsPackageVersionElementName(dependencyName);

        // Assert
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Validates that dots ('.') and hyphens ('-') are removed from the dependency name
    /// and the alternate version element suffix is appended.
    /// Inputs:
    ///  - Various dependency names containing '.', '-', empty string, whitespace/control characters, and underscores.
    /// Expected:
    ///  - The returned string equals the input with '.' and '-' removed, followed by VersionPropsAlternateVersionElementSuffix.
    /// </summary>
    [TestCase("Package.Name-Alpha", "PackageNameAlpha", TestName = "GetVersionPropsAlternatePackageVersionElementName_CommonInput_RemovesSeparatorsAndAppendsSuffix")]
    [TestCase("", "", TestName = "GetVersionPropsAlternatePackageVersionElementName_EmptyString_ReturnsOnlySuffix")]
    [TestCase("-.-", "", TestName = "GetVersionPropsAlternatePackageVersionElementName_OnlySeparators_ReturnsOnlySuffix")]
    [TestCase(" \t\r\n-.- ", " \t\r\n ", TestName = "GetVersionPropsAlternatePackageVersionElementName_WhitespaceAndSeparators_WhitespacePreservedSeparatorsRemoved")]
    [TestCase("pkg_with_underscore.and-dots", "pkg_with_underscoreanddots", TestName = "GetVersionPropsAlternatePackageVersionElementName_UnderscorePreserved_DotsAndHyphensRemoved")]
    [TestCase("a--b..c", "abc", TestName = "GetVersionPropsAlternatePackageVersionElementName_MultipleAdjacentSeparators_AllRemoved")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetVersionPropsAlternatePackageVersionElementName_CommonInputs_RemovesSeparatorsAndAppendsSuffix(string dependencyName, string expectedBase)
    {
        // Arrange
        var suffix = VersionDetailsParser.VersionPropsAlternateVersionElementSuffix;

        // Act
        var result = VersionFiles.GetVersionPropsAlternatePackageVersionElementName(dependencyName);

        // Assert
        var expected = expectedBase + suffix;
        result.Should().Be(expected);
    }

    /// <summary>
    /// Ensures that very long dependency names are processed correctly without truncation
    /// by removing '.' and '-' and appending the alternate version element suffix.
    /// Inputs:
    ///  - A long dependency name constructed by repeating a pattern containing '.', '-'.
    /// Expected:
    ///  - Output equals the long input with '.' and '-' removed, followed by the suffix.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetVersionPropsAlternatePackageVersionElementName_LongInput_RemovesSeparatorsAndAppendsSuffix()
    {
        // Arrange
        var sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("ab.-cd-.-ef.");
        }
        var dependencyName = sb.ToString();
        var suffix = VersionDetailsParser.VersionPropsAlternateVersionElementSuffix;
        var expectedBase = dependencyName.Replace(".", string.Empty).Replace("-", string.Empty);

        // Act
        var result = VersionFiles.GetVersionPropsAlternatePackageVersionElementName(dependencyName);

        // Assert
        var expected = expectedBase + suffix;
        result.Should().Be(expected);
    }

    /// <summary>
    /// Ensures CalculateGlobalJsonElementName returns its input unchanged for a broad set of string cases.
    /// Inputs:
    ///  - Various representative strings: empty, whitespace-only, mixed casing, special characters,
    ///    path-like strings, control characters, Unicode, and very long strings.
    /// Expected:
    ///  - The returned value equals the input string exactly (no transformation).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(CalculateGlobalJsonElementName_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void CalculateGlobalJsonElementName_VariousInputs_ReturnsUnchanged(string input)
    {
        // Arrange
        var dependencyName = input;

        // Act
        var actual = VersionFiles.CalculateGlobalJsonElementName(dependencyName);

        // Assert
        actual.Should().Be(dependencyName);
    }

    public static IEnumerable<TestCaseData> CalculateGlobalJsonElementName_Cases()
    {
        yield return new TestCaseData(string.Empty).SetName("CalculateGlobalJsonElementName_EmptyString_ReturnsEmpty");
        yield return new TestCaseData(" ").SetName("CalculateGlobalJsonElementName_SingleSpace_ReturnsSame");
        yield return new TestCaseData("\t").SetName("CalculateGlobalJsonElementName_Tab_ReturnsSame");
        yield return new TestCaseData("\r\n").SetName("CalculateGlobalJsonElementName_NewLine_ReturnsSame");
        yield return new TestCaseData("sdk").SetName("CalculateGlobalJsonElementName_SimpleLowercase_ReturnsSame");
        yield return new TestCaseData("SDK").SetName("CalculateGlobalJsonElementName_SimpleUppercase_ReturnsSame");
        yield return new TestCaseData("name_with-dash.and.dot").SetName("CalculateGlobalJsonElementName_MixedSeparators_ReturnsSame");
        yield return new TestCaseData("path/with/slash").SetName("CalculateGlobalJsonElementName_ForwardSlashes_ReturnsSame");
        yield return new TestCaseData("path\\with\\backslash").SetName("CalculateGlobalJsonElementName_Backslashes_ReturnsSame");
        yield return new TestCaseData("abc\0def").SetName("CalculateGlobalJsonElementName_ContainsNullChar_ReturnsSame");
        yield return new TestCaseData("🤖-Бета-版本").SetName("CalculateGlobalJsonElementName_UnicodeAndEmoji_ReturnsSame");

        var longString = new string('a', 2048);
        yield return new TestCaseData(longString).SetName("CalculateGlobalJsonElementName_VeryLongString_ReturnsSame");
    }

    /// <summary>
    /// Provides a variety of string inputs to validate that the method does not transform the input.
    /// Includes: empty, whitespace-only, control/special characters, unicode, hyphen/dot usage, and very long strings.
    /// </summary>
    public static IEnumerable<string> CalculateDotnetToolsJsonElementName_Inputs()
    {
        yield return string.Empty;
        yield return " ";
        yield return "\t\n";
        yield return "tool";
        yield return "Package.Name";
        yield return "Pkg-Name";
        yield return "name_with_underscores";
        yield return "λ-工具-नाम";
        yield return "with\"quotes\"and\\slashes/and{braces}:,[]";
        yield return "\0bell\u0007control";
        yield return new string('a', 1024);
    }

    /// <summary>
    /// Ensures that CalculateDotnetToolsJsonElementName returns the input string unchanged.
    /// Inputs:
    ///  - Various dependencyName strings including empty, whitespace-only, special/control characters, unicode, and long values.
    /// Expected:
    ///  - The returned value is exactly the same instance and content as the input string (no transformations).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(CalculateDotnetToolsJsonElementName_Inputs))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void CalculateDotnetToolsJsonElementName_VariousStrings_ReturnsInputUnchanged(string dependencyName)
    {
        // Arrange

        // Act
        var result = VersionFiles.CalculateDotnetToolsJsonElementName(dependencyName);

        // Assert
        result.Should().BeSameAs(dependencyName);
        result.Should().Be(dependencyName);
    }
}
