// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.Helpers;


[TestFixture]
public class LocalPathTests
{
    [Test]
    public void UnixStylePathsCombineWell()
    {
        var path1 = new UnixPath("src/");
        var path2 = new UnixPath("/some/path/foo.jpg");

        path1.Path.Should().Be("src/");
        path2.Path.Should().Be("/some/path/foo.jpg");
        (path1 / path2).Path.Should().Be("src/some/path/foo.jpg");
        (path2 / path1).Path.Should().Be("/some/path/foo.jpg/src/");
        (path1 / "/something/else").Path.Should().Be("src/something/else");
        ("/something/else" / path1).Path.Should().Be("/something/else/src/");
        (path1 / "something\\else").Path.Should().Be("src/something/else");
        new UnixPath("something\\else").Path.Should().Be("something/else");
    }

    [Test]
    public void WindowsStylePathsCombineWell()
    {
        var path1 = new WindowsPath("D:\\foo\\bar");
        var path2 = new WindowsPath("some/path/foo.jpg");

        path1.Path.Should().Be("D:\\foo\\bar");
        path2.Path.Should().Be("some\\path\\foo.jpg");
        (path1 / path2).Path.Should().Be("D:\\foo\\bar\\some\\path\\foo.jpg");
        (path2 / path1).Path.Should().Be("some\\path\\foo.jpg\\D:\\foo\\bar");
        (path1 / "/something/else").Path.Should().Be("D:\\foo\\bar\\something\\else");
        ("something/else" / path1).Path.Should().Be("something\\else\\D:\\foo\\bar");
    }

    [Test]
    public void NativeStylePathsCombineWell()
    {
        var path1 = new NativePath("foo\\bar\\");
        var path2 = new NativePath("some/path/foo.jpg");

        (path1 / path2).Path.Should().Be(
            Path.Combine(
                path1.Path.Replace('\\', Path.DirectorySeparatorChar),
                path2.Path.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// Provides a diverse set of path strings to validate Length handling, including:
    /// - empty, dot, whitespace
    /// - mixed separators
    /// - unicode and surrogate pairs
    /// - long strings
    /// </summary>
    private static IEnumerable<string> SamplePaths()
    {
        yield return string.Empty;
        yield return ".";
        yield return " ";
        yield return "a";
        yield return "a/b\\c";
        yield return "📁/子/файл.txt";
        yield return new string('x', 10_000);
    }

    /// <summary>
    /// Verifies that Length reflects the number of characters in the path string for UnixPath.
    /// Inputs include empty, whitespace-only, mixed separators, unicode, and long strings.
    /// Expected: Length equals input.Length since normalization only replaces separators without changing count.
    /// </summary>
    [TestCaseSource(nameof(SamplePaths))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Length_UnixPath_VariousInputs_ReturnsUnderlyingStringLength(string input)
    {
        // Arrange
        var path = new UnixPath(input);

        // Act
        var length = path.Length;

        // Assert
        length.Should().Be(input.Length);
    }

    /// <summary>
    /// Verifies that Length reflects the number of characters in the path string for WindowsPath.
    /// Inputs include empty, whitespace-only, mixed separators, unicode, and long strings.
    /// Expected: Length equals input.Length since normalization only replaces separators without changing count.
    /// </summary>
    [TestCaseSource(nameof(SamplePaths))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Length_WindowsPath_VariousInputs_ReturnsUnderlyingStringLength(string input)
    {
        // Arrange
        var path = new WindowsPath(input);

        // Act
        var length = path.Length;

        // Assert
        length.Should().Be(input.Length);
    }

    /// <summary>
    /// Verifies that Length reflects the number of characters in the path string for NativePath.
    /// Inputs include empty, whitespace-only, mixed separators, unicode, and long strings.
    /// Expected: Length equals input.Length since normalization only replaces separators without changing count.
    /// </summary>
    [TestCaseSource(nameof(SamplePaths))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Length_NativePath_VariousInputs_ReturnsUnderlyingStringLength(string input)
    {
        // Arrange
        var path = new NativePath(input);

        // Act
        var length = path.Length;

        // Assert
        length.Should().Be(input.Length);
    }

    /// <summary>
    /// Validates that the 2-parameter constructor normalizes the input path by invoking the overridden NormalizePath,
    /// proving it delegates to the 3-parameter constructor with normalizePath = true.
    /// Inputs vary across empty, whitespace, dot, mixed separators, control chars, unicode, invalid filename chars, and unusual separators.
    /// Expected: Path (and ToString) equals the normalized value returned by the override.
    /// </summary>
    [TestCase("", '/')]
    [TestCase(" ", '\\')]
    [TestCase(".", 'X')]
    [TestCase(@".\..", '/')]
    [TestCase(@"C:\foo/bar\baz", '/')]
    [TestCase("\t\r\n", 'a')]
    [TestCase("αβγ/测试", '\\')]
    [TestCase("file:name?*<>|", '\\')]
    [TestCase("a", '\0')]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void LocalPathConstructor_TwoParameters_NormalizesPath(string input, char separator)
    {
        // Arrange
        // (No extra arrangement required.)

        // Act
        var sut = new TestableLocalPath(input, separator);

        // Assert
        var expected = TestableLocalPath.ExpectedNormalized(input);
        sut.Path.Should().Be(expected);
        sut.ToString().Should().Be(expected);
    }

    /// <summary>
    /// Ensures that extremely long input paths are still normalized by the 2-parameter constructor without throwing.
    /// Input: 10,000-character path composed of 'a'.
    /// Expected: Path equals the normalized value returned by the override.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void LocalPathConstructor_TwoParameters_WithVeryLongPath_NormalizesCorrectly()
    {
        // Arrange
        var longPath = new string('a', 10_000);
        var separator = '/';

        // Act
        var sut = new TestableLocalPath(longPath, separator);

        // Assert
        var expected = TestableLocalPath.ExpectedNormalized(longPath);
        sut.Path.Should().Be(expected);
        sut.ToString().Should().Be(expected);
    }

    private sealed class TestableLocalPath : LocalPath
    {
        private readonly char _sep;

        public TestableLocalPath(string path, char separator) : base(path, separator)
        {
            _sep = separator;
        }

        private TestableLocalPath(string path, char separator, bool normalize) : base(path, separator, normalize)
        {
            _sep = separator;
        }

        protected override LocalPath CreateMergedPath(string path) => new TestableLocalPath(path, _sep, false);

        protected override string NormalizePath(string s) => NormalizeImpl(s);

        private static string NormalizeImpl(string s) => "[norm]" + s;

        public static string ExpectedNormalized(string s) => NormalizeImpl(s);
    }

    /// <summary>
    /// Verifies that when normalizePath is true, the constructor normalizes the input path
    /// by invoking NormalizePath exactly once and stores the normalized value in Path.
    /// Inputs cover empty, whitespace, mixed separators, control characters, and long strings.
    /// Expected: Path equals the value returned by the normalizer and NormalizePath is called once.
    /// </summary>
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("a/b\\c")]
    [TestCase("C:\\dir\\file")]
    [TestCase("..//./")]
    [TestCase("subdir\nname\twith\u0001control")]
    [TestCase("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void LocalPath_Ctor_NormalizeTrue_CallsNormalizeAndSetsPath(string rawPath)
    {
        // Arrange
        Func<string, string> normalizer = s => $"norm({s})";

        // Act
        var sut = new TestLocalPath(rawPath, '/', true, normalizer);

        // Assert
        sut.NormalizeCalls.Should().Be(1);
        sut.Path.Should().Be($"norm({rawPath})");
    }

    /// <summary>
    /// Verifies that when normalizePath is false, the constructor does not invoke NormalizePath
    /// and stores the input path as-is in Path.
    /// Inputs cover empty, whitespace, mixed separators, control characters, and long strings.
    /// Expected: Path equals the raw input and NormalizePath is not called.
    /// </summary>
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("a/b\\c")]
    [TestCase("C:\\dir\\file")]
    [TestCase("..//./")]
    [TestCase("subdir\nname\twith\u0001control")]
    [TestCase("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void LocalPath_Ctor_NormalizeFalse_DoesNotCallNormalizeAndKeepsPath(string rawPath)
    {
        // Arrange
        Func<string, string> normalizer = s => $"norm({s})";

        // Act
        var sut = new TestLocalPath(rawPath, '\\', false, normalizer);

        // Assert
        sut.NormalizeCalls.Should().Be(0);
        sut.Path.Should().Be(rawPath);
    }

    /// <summary>
    /// Ensures that the separator provided to the constructor is stored and honored by Combine,
    /// validating that the constructor correctly sets the internal separator used by Combine logic.
    /// Inputs: different separators and combinations of trailing/leading separator presence on left/right.
    /// Expected: Combined result follows the rules:
    ///  - none: left + sep + right
    ///  - one:  left + right
    ///  - both: left + right without the leading sep of right
    /// </summary>
    [TestCase('/', false, false)]
    [TestCase('/', true, false)]
    [TestCase('/', false, true)]
    [TestCase('/', true, true)]
    [TestCase('\\', false, false)]
    [TestCase('\\', true, false)]
    [TestCase('\\', false, true)]
    [TestCase('\\', true, true)]
    [TestCase('|', false, false)]
    [TestCase('|', true, false)]
    [TestCase('|', false, true)]
    [TestCase('|', true, true)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void LocalPath_Ctor_SetsSeparator_CombineHonorsSeparator(char separator, bool leftEndsWithSep, bool rightStartsWithSep)
    {
        // Arrange
        var sut = new TestLocalPath(string.Empty, separator, false, s => s);
        var left = leftEndsWithSep ? $"a{separator}" : "a";
        var right = rightStartsWithSep ? $"{separator}b" : "b";

        var slashCount = (leftEndsWithSep ? 1 : 0) + (rightStartsWithSep ? 1 : 0);
        string expected = slashCount switch
        {
            0 => left + separator + right,
            1 => left + right,
            2 => left + right.Substring(1),
            _ => throw new InvalidOperationException("Unexpected combination.")
        };

        // Act
        var combined = sut.CombineUsingSeparator(left, right);

        // Assert
        combined.Should().Be(expected);
    }

    private sealed class TestLocalPath : LocalPath
    {
        private readonly Func<string, string> _normalizer;
        private readonly char _sep;

        public int NormalizeCalls { get; private set; }

        public TestLocalPath(string path, char separator, bool normalizePath, Func<string, string> normalizer)
            : base(path, separator, normalizePath)
        {
            _normalizer = normalizer;
            _sep = separator;
        }

        protected override LocalPath CreateMergedPath(string path)
            => new TestLocalPath(path, _sep, false, _normalizer);

        protected override string NormalizePath(string s)
        {
            NormalizeCalls++;
            return _normalizer(s);
        }

        public string CombineUsingSeparator(string left, string right) => Combine(left, right);
    }

    /// <summary>
    /// Verifies that ToString() returns the internal Path value as-is when normalization is disabled,
    /// and returns the normalized value when normalization is enabled.
    /// Inputs:
    ///  - normalize: whether the LocalPath constructor requests normalization.
    /// Expected:
    ///  - normalize == false: ToString() returns the original input string unchanged.
    ///  - normalize == true: ToString() returns the normalized version (upper-cased by the test implementation).
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ToString_NormalizationFlag_ReturnsExpectedPathString(bool normalize)
    {
        // Arrange
        var original = "a/b\\c 123_ä-测试";
        var sut = new TestLocalPath(original, '/', normalize);
        var expected = normalize ? original.ToUpperInvariant() : original;

        // Act
        var result = sut.ToString();

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that when normalization is disabled, LocalPath.GetHashCode returns the same value
    /// as string.GetHashCode computed on the raw input Path.
    /// Inputs:
    ///  - Various raw path strings with different characters and formats.
    /// Expected:
    ///  - GetHashCode() equals input.GetHashCode().
    /// </summary>
    [TestCase("", '/')]
    [TestCase(".", '\\')]
    [TestCase("abc", '/')]
    [TestCase("a/b\\c", '/')]
    [TestCase("αβγ", '/')]
    [TestCase("   ", '/')]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetHashCode_RawPath_ReturnsStringHashCode(string input, char separator)
    {
        // Arrange
        var subject = new TestLocalPath(input, separator, normalize: false, normalizer: s => s);
        var expected = input.GetHashCode();

        // Act
        var actual = subject.GetHashCode();

        // Assert
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Ensures that when normalization is enabled, LocalPath.GetHashCode uses the normalized Path value.
    /// Inputs:
    ///  - Raw path containing mixed separators.
    ///  - Target separator for normalization.
    /// Expected:
    ///  - GetHashCode() equals normalizedPath.GetHashCode().
    /// </summary>
    [TestCase("a\\b/c", '/', "a/b/c")]
    [TestCase("a\\b\\c", '/', "a/b/c")]
    [TestCase("a/b/c", '\\', "a\\b\\c")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetHashCode_NormalizationApplied_ReturnsNormalizedStringHashCode(string input, char separator, string normalizedExpected)
    {
        // Arrange
        Func<string, string> normalizer = s => s.Replace('\\', separator).Replace('/', separator);
        var subject = new TestLocalPath(input, separator, normalize: true, normalizer: normalizer);
        var expected = normalizedExpected.GetHashCode();

        // Act
        var actual = subject.GetHashCode();

        // Assert
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Validates that repeated calls to GetHashCode are consistent for the same instance.
    /// Inputs:
    ///  - A stable path string.
    /// Expected:
    ///  - Multiple GetHashCode() invocations return the same value.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetHashCode_ConsistentAcrossCalls_ReturnsSameValue()
    {
        // Arrange
        var input = "C:\\temp\\file.txt";
        var subject = new TestLocalPath(input, '\\', normalize: false, normalizer: s => s);

        // Act
        var first = subject.GetHashCode();
        var second = subject.GetHashCode();

        // Assert
        second.Should().Be(first);
    }

    /// <summary>
    /// Verifies that LocalPath.GetHashCode returns exactly the same hash code as the underlying Path string.
    /// Inputs:
    ///  - A variety of path strings including empty, whitespace, UNIX/Windows styles, unicode, control chars, and long strings.
    /// Expected:
    ///  - The returned hash code equals path.GetHashCode() for each input.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(GetPathCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetHashCode_ReturnsStringHashCodeOfPath(string input)
    {
        // Arrange
        var sut = new TestLocalPath(input, normalize: false);

        // Act
        var actual = sut.GetHashCode();

        // Assert
        var expected = input.GetHashCode();
        actual.Should().Be(expected);
    }

    private static System.Collections.Generic.IEnumerable<string> GetPathCases()
    {
        yield return string.Empty;
        yield return " ";
        yield return "   ";
        yield return ".";
        yield return "..";
        yield return "a";
        yield return "a/b";
        yield return "a\\b";
        yield return "C:\\";
        yield return "C:\\folder\\file.txt";
        yield return "/usr/local/bin";
        yield return new string('a', 1024);
        yield return "name-with-unicode-π-漢字";
        yield return "specials:*?\"<>|";
        yield return "line\nbreak\tand\0null";
    }

}

