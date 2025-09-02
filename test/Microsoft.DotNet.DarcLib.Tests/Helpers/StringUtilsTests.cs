// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Text;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.VisualStudio.Services;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Helpers.UnitTests;

public class StringUtilsTests
{
    /// <summary>
    /// Ensures non-existing or invalid file paths result in "0 B".
    /// Inputs:
    ///  - path: empty string, whitespace-only, and a randomly generated non-existent path under temp.
    /// Expected:
    ///  - The method returns the exact string "0 B" without throwing.
    /// </summary>
    [TestCaseSource(nameof(NonExistingPathCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetHumanReadableFileSize_PathNotExistingOrInvalid_Returns0B(string path)
    {
        // Arrange
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }

        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        try
        {
            // Act
            var result = StringUtils.GetHumanReadableFileSize(path);

            // Assert
            result.Should().Be("0 B");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// Verifies that existing files are reported with correct human-readable sizes,
    /// covering byte, KB, and MB thresholds and rounding behavior.
    /// Inputs:
    ///  - length: file lengths including 0, 1, 1023, 1024, 1536, 1048575 (1MB - 1), 1048576 (1MB), 1572864 (1.5MB).
    /// Expected:
    ///  - Correct formatted strings for each size, e.g., "1 KB" for 1024, "1.5 KB" for 1536,
    ///    "1024 KB" (rounded) for 1048575, "1 MB" for 1048576, "1.5 MB" for 1572864.
    /// </summary>
    [TestCase(0L, "0 B")]
    [TestCase(1L, "1 B")]
    [TestCase(1023L, "1023 B")]
    [TestCase(1024L, "1 KB")]
    [TestCase(1536L, "1.5 KB")]
    [TestCase(1048575L, "1024 KB")] // Edge rounding: 1MB - 1 byte rounds to "1024 KB"
    [TestCase(1048576L, "1 MB")]
    [TestCase(1572864L, "1.5 MB")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetHumanReadableFileSize_ExistingFile_ReturnsExpectedHumanReadable(long length, string expected)
    {
        // Arrange
        string path = TempFileHelper.CreateFileWithLength(length);

        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        try
        {
            // Act
            var result = StringUtils.GetHumanReadableFileSize(path);

            // Assert
            result.Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            TempFileHelper.SafeDelete(path);
        }
    }

    private static IEnumerable NonExistingPathCases()
    {
        yield return new TestCaseData(string.Empty).SetName("EmptyString");
        yield return new TestCaseData("   ").SetName("WhitespaceOnly");
        yield return new TestCaseData(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).SetName("RandomTempNonexistent");
    }

    private class TempFileHelper
    {
        public static string CreateFileWithLength(long length)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tmp");
            using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                fs.SetLength(length);
                fs.Flush(true);
            }
            return path;
        }

        public static void SafeDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Intentionally swallow exceptions during cleanup.
            }
        }
    }

    /// <summary>
    /// Verifies that for a variety of ASCII-only inputs (including empty, whitespace, control characters, and punctuation),
    /// GetXxHash64 returns a deterministic uppercase hexadecimal string of length 16 that matches a reference xxHash64(0) computed over ASCII bytes.
    /// Inputs:
    ///  - ASCII strings from edge cases: "", "a", "abc", whitespace, control chars, punctuation, pangram.
    /// Expected:
    ///  - The output equals the reference hash, is uppercase [0-9A-F], and has length 16.
    /// </summary>
    [TestCaseSource(nameof(AsciiTestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetXxHash64_AsciiInputs_ReturnsExpectedUpperHex(string input)
    {
        // Arrange
        string expected = ComputeXxHash64Ascii(input);

        // Act
        string actual = StringUtils.GetXxHash64(input);

        // Assert
        actual.Should().Be(expected);
        actual.Should().HaveLength(16);
        actual.Should().MatchRegex("^[0-9A-F]+$");
    }

    /// <summary>
    /// Ensures non-ASCII characters are treated as '?' because the implementation uses Encoding.ASCII with fallback.
    /// Inputs:
    ///  - Strings containing non-ASCII characters across different ranges (Latin-1 accents, BMP CJK, emoji/surrogates).
    /// Expected:
    ///  - Hash(input) equals Hash(ASCII-sanitized input where every non-ASCII char is replaced by '?').
    /// </summary>
    [TestCaseSource(nameof(NonAsciiTestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetXxHash64_NonAsciiInputs_TreatedAsQuestionMark(string input)
    {
        // Arrange
        string asciiFallback = ReplaceNonAsciiWithQuestionMark(input);
        string expected = ComputeXxHash64Ascii(asciiFallback);

        // Act
        string actual = StringUtils.GetXxHash64(input);

        // Assert
        actual.Should().Be(expected);
        actual.Should().HaveLength(16);
        actual.Should().MatchRegex("^[0-9A-F]+$");
    }

    /// <summary>
    /// Validates determinism: invoking GetXxHash64 multiple times with the same input yields the same result.
    /// Inputs:
    ///  - Representative values including ASCII, mixed case, non-ASCII, and long strings.
    /// Expected:
    ///  - Two successive calls produce identical hashes.
    /// </summary>
    [TestCaseSource(nameof(DeterminismTestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetXxHash64_SameInput_Deterministic(string input)
    {
        // Arrange
        // (no additional arrangement required)

        // Act
        string first = StringUtils.GetXxHash64(input);
        string second = StringUtils.GetXxHash64(input);

        // Assert
        first.Should().Be(second);
        first.Should().HaveLength(16);
        first.Should().MatchRegex("^[0-9A-F]+$");
    }

    /// <summary>
    /// Verifies case sensitivity: different ASCII casing should produce different outputs.
    /// Inputs:
    ///  - "abc" and "ABC".
    /// Expected:
    ///  - Hashes are not equal.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetXxHash64_DifferentCase_ProducesDifferentHash()
    {
        // Arrange
        string lower = "abc";
        string upper = "ABC";

        // Act
        string lowerHash = StringUtils.GetXxHash64(lower);
        string upperHash = StringUtils.GetXxHash64(upper);

        // Assert
        lowerHash.Should().NotBe(upperHash);
        lowerHash.Should().HaveLength(16);
        upperHash.Should().HaveLength(16);
    }

    // ----------------------
    // Test case sources
    // ----------------------

    public static IEnumerable AsciiTestCases
    {
        get
        {
            yield return new TestCaseData(string.Empty);
            yield return new TestCaseData("a");
            yield return new TestCaseData("abc");
            yield return new TestCaseData("hello");
            yield return new TestCaseData("   ");
            yield return new TestCaseData("\r\n\t");
            yield return new TestCaseData("!@#$%^&*()_+-=~`[]{}|;:,.<>/");
            yield return new TestCaseData("The quick brown fox jumps over the lazy dog");
        }
    }

    public static IEnumerable NonAsciiTestCases
    {
        get
        {
            yield return new TestCaseData("café");
            yield return new TestCaseData("mañana");
            yield return new TestCaseData("中文");
            yield return new TestCaseData("π");
            yield return new TestCaseData("😀");
            yield return new TestCaseData("a😀b");
            yield return new TestCaseData("é");
            yield return new TestCaseData("𝄞"); // U+1D11E MUSICAL SYMBOL G CLEF (surrogate pair)
        }
    }

    public static IEnumerable DeterminismTestCases
    {
        get
        {
            yield return new TestCaseData("abc");
            yield return new TestCaseData("ABC");
            yield return new TestCaseData("café");
            yield return new TestCaseData("😀 emoji sequence 😀😀");
            yield return new TestCaseData(new string('x', 10000));
        }
    }

    // ----------------------
    // Helpers
    // ----------------------

    private static string ComputeXxHash64Ascii(string s)
    {
        var hasher = new XxHash64(0);
        byte[] bytes = Encoding.ASCII.GetBytes(s);
        hasher.Append(bytes);
        byte[] hashBytes = hasher.GetCurrentHash();
        return Convert.ToHexString(hashBytes);
    }

    private static string ReplaceNonAsciiWithQuestionMark(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            sb.Append(c <= 0x7F ? c : '?');
        }
        return sb.ToString();
    }

    public static IEnumerable<TestCaseData> IsValidLongCommitShaCases()
    {
        yield return new TestCaseData(new string('a', 40), true).SetName("Exactly40Chars_AllLowerHex_ReturnsTrue");
        yield return new TestCaseData(new string('A', 40), true).SetName("Exactly40Chars_AllUpperHex_ReturnsTrue");
        yield return new TestCaseData("0123456789abcdef0123456789abcdef01234567", true).SetName("Exactly40Chars_MixedDigitsAndHex_ReturnsTrue");

        yield return new TestCaseData(string.Empty, false).SetName("EmptyString_ReturnsFalse");
        yield return new TestCaseData("   ", false).SetName("WhitespaceOnly_ReturnsFalse");
        yield return new TestCaseData(new string('a', 39), false).SetName("Length39_ReturnsFalse");
        yield return new TestCaseData(new string('a', 41), false).SetName("Length41_ReturnsFalse");
        yield return new TestCaseData(new string('a', 100), false).SetName("Length100_ReturnsFalse");

        yield return new TestCaseData("0123456789abcdef0123456789abcdef0123456g", false).SetName("Exactly40_WithLowerG_ReturnsFalse");
        yield return new TestCaseData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFZ", false).SetName("Exactly40_WithUpperZ_ReturnsFalse");
        yield return new TestCaseData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-", false).SetName("Exactly40_WithDash_ReturnsFalse");
        yield return new TestCaseData(new string(' ', 40), false).SetName("Exactly40_Spaces_ReturnsFalse");
        yield return new TestCaseData(new string('a', 20) + "\n" + new string('a', 19), false).SetName("ContainsNewlineWithin40_ReturnsFalse");
        yield return new TestCaseData(new string('a', 39) + "á", false).SetName("ContainsUnicodeNonHex_ReturnsFalse");
    }

    /// <summary>
    /// Validates StringUtils.IsValidLongCommitSha for a comprehensive set of inputs:
    /// - Edge lengths (empty, 39, 40, 41, very long), whitespace-only, and newline.
    /// - Valid hex-only strings with exactly 40 characters in lower/upper/mixed forms.
    /// - Exactly 40-character strings containing invalid characters (g, Z, '-', unicode).
    /// Expects true only for exactly-40-character hex strings; false otherwise.
    /// </summary>
    [TestCaseSource(nameof(IsValidLongCommitShaCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void IsValidLongCommitSha_VariousInputs_ReturnsExpected(string input, bool expected)
    {
        // Arrange
        // Input provided by TestCaseSource.

        // Act
        bool result = StringUtils.IsValidLongCommitSha(input);

        // Assert
        result.Should().Be(expected);
    }
}
