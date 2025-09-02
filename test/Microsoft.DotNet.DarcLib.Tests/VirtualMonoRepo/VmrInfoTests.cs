// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;

/// <summary>
/// Tests for VmrInfo.GetRepoSourcesPath(SourceMapping).
/// Ensures the method returns the VMR root combined with the "src" directory and the mapping's Name,
/// handling various mapping names including empty, whitespace, nested segments, and leading/trailing separators.
/// </summary>
public class VmrInfoTests
{
    /// <summary>
    /// Verifies that GetRepoSourcesPath(SourceMapping) correctly combines VmrPath, "src", and the mapping's Name.
    /// Inputs:
    ///  - A VMR root "vmr-root" and multiple mapping names, including empty, spaces, and nested segments.
    /// Expected:
    ///  - The returned NativePath equals VmrPath / VmrInfo.SourcesDir / mapping.Name without throwing.
    /// </summary>
    [TestCase("alpha")]
    [TestCase("")]
    [TestCase(" nested ")]
    [TestCase("nested/segments")]
    [TestCase("/leading")]
    [TestCase("trailing/")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetRepoSourcesPath_SourceMappingNames_CombinesVmrRootSrcAndName(string mappingName)
    {
        // Arrange
        var vmrRoot = "vmr-root";
        var sut = new VmrInfo(new NativePath(vmrRoot), new NativePath("tmp"));
        var mapping = new SourceMapping(mappingName, "remote", "ref", new List<string>(), new List<string>(), false);

        var expected = sut.VmrPath / VmrInfo.SourcesDir / mapping.Name;

        // Act
        var actual = sut.GetRepoSourcesPath(mapping);

        // Assert
        (actual == expected).Should().BeTrue();
    }

    /// <summary>
    /// Ensures that when the VMR path varies by having or not having a trailing separator (and absolute vs relative),
    /// GetRepoSourcesPath(SourceMapping) still produces a correctly combined path with no duplicate or missing separators.
    /// Inputs:
    ///  - Multiple VMR roots: "root", "root/", "/opt/vmr", "/opt/vmr/"; mapping name "alpha".
    /// Expected:
    ///  - The returned NativePath equals VmrPath / VmrInfo.SourcesDir / "alpha".
    /// </summary>
    [TestCase("root")]
    [TestCase("root/")]
    [TestCase("/opt/vmr")]
    [TestCase("/opt/vmr/")]
    [TestCase("C:\\vmr")]
    [TestCase("C:\\vmr\\")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetRepoSourcesPath_VmrPathVariants_NoDuplicateOrMissingSeparators(string vmrRoot)
    {
        // Arrange
        var sut = new VmrInfo(new NativePath(vmrRoot), new NativePath("tmp"));
        var mapping = new SourceMapping("alpha", "remote", "ref", new List<string>(), new List<string>(), false);
        var expected = sut.VmrPath / VmrInfo.SourcesDir / mapping.Name;

        // Act
        var actual = sut.GetRepoSourcesPath(mapping);

        // Assert
        (actual == expected).Should().BeTrue();
    }

    /// <summary>
    /// Validates that GetRepoSourcesPath(SourceMapping) uses the current value of VmrPath,
    /// not the original constructor value.
    /// Inputs:
    ///  - Initial VMR root "first-root", then VmrPath is updated to "second-root"; mapping name "alpha".
    /// Expected:
    ///  - The returned path is based on "second-root".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetRepoSourcesPath_VmrPathChanged_UsesUpdatedRoot()
    {
        // Arrange
        var sut = new VmrInfo(new NativePath("first-root"), new NativePath("tmp"));
        sut.VmrPath = new NativePath("second-root");
        var mapping = new SourceMapping("alpha", "remote", "ref", new List<string>(), new List<string>(), false);
        var expected = sut.VmrPath / VmrInfo.SourcesDir / mapping.Name;

        // Act
        var actual = sut.GetRepoSourcesPath(mapping);

        // Assert
        (actual == expected).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that setting VmrPath updates the backing field and recalculates SourceManifestPath
    /// using the default relative manifest path. Covers various path shapes (absolute, relative, trailing separators).
    /// </summary>
    /// <param name="newVmrBasePath">The new base path assigned to VmrPath.</param>
    [TestCase("C:\\vmr")]
    [TestCase("C:\\vmr\\")]
    [TestCase("/opt/vmr")]
    [TestCase("/opt/vmr/")]
    [TestCase(".")]
    [TestCase("vmr")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void VmrPath_Set_RecomputesSourceManifestPath(string newVmrBasePath)
    {
        // Arrange
        var initialBase = new NativePath("initial");
        var tmpPath = new NativePath("tmp");
        var sut = new VmrInfo(initialBase, tmpPath);

        var initialExpectedManifest = initialBase / VmrInfo.DefaultRelativeSourceManifestPath;

        // Sanity: initial state is computed from the constructor
        sut.SourceManifestPath.Should().Be(initialExpectedManifest);

        var newBase = new NativePath(newVmrBasePath);
        var expectedAfterSet = newBase / VmrInfo.DefaultRelativeSourceManifestPath;

        // Act
        sut.VmrPath = newBase;

        // Assert
        sut.VmrPath.Should().Be(newBase);
        sut.SourceManifestPath.Should().Be(expectedAfterSet);
        sut.SourceManifestPath.Should().NotBe(initialExpectedManifest);
    }

    /// <summary>
    /// Verifies that setting VmrPath multiple times always reflects the last assigned value
    /// and that SourceManifestPath matches the last base path combined with the default relative manifest path.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void VmrPath_SetMultipleTimes_SourceManifestPathReflectsLastValue()
    {
        // Arrange
        var base0 = new NativePath("root0");
        var tmpPath = new NativePath("tmp");
        var sut = new VmrInfo(base0, tmpPath);

        var base1 = new NativePath("root1");
        var base2 = new NativePath("root2");
        var expectedAfterBase2 = base2 / VmrInfo.DefaultRelativeSourceManifestPath;

        // Act
        sut.VmrPath = base1;
        sut.VmrPath = base2;

        // Assert
        sut.VmrPath.Should().Be(base2);
        sut.SourceManifestPath.Should().Be(expectedAfterBase2);
    }

    /// <summary>
    /// Ensures that the NativePath-based constructor initializes all key properties correctly.
    /// Inputs:
    ///  - vmrPath: diverse path formats (absolute Windows-like, absolute Unix-like, relative with trailing slash).
    ///  - tmpPath: diverse path formats (absolute Windows-like, absolute Unix-like, relative with trailing slash).
    /// Expected:
    ///  - VmrPath equals the provided vmrPath.
    ///  - TmpPath equals the provided tmpPath.
    ///  - SourceManifestPath is computed as vmrPath / "src" / "source-manifest.json".
    ///  - VmrUri equals Constants.DefaultVmrUri.
    /// </summary>
    [Test]
    [TestCase(@"C:/repo/vmr", @"C:/repo/tmp")]
    [TestCase("/opt/vmr", "/var/tmp")]
    [TestCase("relative/vmr/", "relative/tmp/")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Ctor_ValidPaths_PropertiesInitialized(string vmrPathStr, string tmpPathStr)
    {
        // Arrange
        var vmrPath = new NativePath(vmrPathStr);
        var tmpPath = new NativePath(tmpPathStr);

        // Act
        var sut = new VmrInfo(vmrPath, tmpPath);

        // Assert
        sut.VmrPath.Should().Be(vmrPath);
        sut.TmpPath.Should().Be(tmpPath);

        var expectedManifest = vmrPath / VmrInfo.SourcesDir / VmrInfo.SourceManifestFileName;
        sut.SourceManifestPath.Should().Be(expectedManifest);

        sut.VmrUri.Should().Be(Constants.DefaultVmrUri);
    }

    /// <summary>
    /// Verifies that the string-based constructor initializes all key properties correctly.
    /// Inputs:
    ///  - vmrPath and tmpPath variations (relative, absolute-like, with/without trailing separators, empty).
    /// Expected:
    ///  - VmrPath equals new NativePath(vmrPath).
    ///  - TmpPath equals new NativePath(tmpPath).
    ///  - SourceManifestPath equals new NativePath(vmrPath) / VmrInfo.SourcesDir / VmrInfo.SourceManifestFileName.
    ///  - VmrUri equals Constants.DefaultVmrUri.
    /// </summary>
    [TestCase("vmr", "tmp")]
    [TestCase("vmr/", "tmp/")]
    [TestCase("C:\\vmr", "D:\\tmp")]
    [TestCase("C:/vmr", "/tmp")]
    [TestCase("", "")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Ctor_ValidStrings_InitializesPathsAndDefaults(string vmrPathInput, string tmpPathInput)
    {
        // Arrange
        var expectedVmr = new NativePath(vmrPathInput);
        var expectedTmp = new NativePath(tmpPathInput);
        var expectedManifest = expectedVmr / VmrInfo.SourcesDir / VmrInfo.SourceManifestFileName;

        // Act
        var sut = new VmrInfo(vmrPathInput, tmpPathInput);

        // Assert
        sut.VmrPath.Should().Be(expectedVmr);
        sut.TmpPath.Should().Be(expectedTmp);
        sut.SourceManifestPath.Should().Be(expectedManifest);
        sut.VmrUri.Should().Be(Constants.DefaultVmrUri);
    }

    /// <summary>
    /// Ensures GetRepoSourcesPath(SourceMapping) composes the expected NativePath under:
    /// VmrPath / "src" / mapping.Name.
    /// Inputs:
    ///  - vmrPath (various forms including Windows/Unix separators and trailing slashes)
    ///  - mappingName (empty, whitespace, nested segments, special characters)
    /// Expected:
    ///  - The returned NativePath string representation equals the composed expected path.
    ///  - No exceptions are thrown for provided mapping names (since method does not validate).
    /// </summary>
    [TestCase("C:\\vmr", "arcade")]
    [TestCase("C:\\vmr\\", "arcade")]
    [TestCase("/opt/vmr", "repo")]
    [TestCase("/opt/vmr/", "repo")]
    [TestCase("root", "nested/repo")]
    [TestCase("root", "nested\\repo")]
    [TestCase("root", "")]
    [TestCase("root", "   ")]
    [TestCase("root", "inva*lid?name")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetRepoSourcesPath_SourceMapping_ComposesExpectedPath(string vmrPath, string mappingName)
    {
        // Arrange
        var tmpPath = "tmp";
        var sut = new VmrInfo(vmrPath, tmpPath);
        var mapping = new SourceMapping(
            mappingName,
            "remote",
            "ref",
            Array.Empty<string>(),
            Array.Empty<string>(),
            false,
            null);

        // Act
        NativePath actual = sut.GetRepoSourcesPath(mapping);

        // Assert
        var expected = new NativePath(vmrPath) / VmrInfo.SourcesDir / mappingName;
        actual.ToString().Should().Be(expected.ToString());
    }

    /// <summary>
    /// Validates that GetRepoSourcesPath(string) correctly combines VmrPath, the sources directory ("src"),
    /// and the provided mappingName across a variety of edge-case inputs.
    /// Inputs cover: empty string, whitespace, leading/trailing separators, mixed separators, special characters,
    /// relative segments (., ..), and very long strings.
    /// Expected: The returned NativePath equals new NativePath(vmrRoot) / VmrInfo.SourcesDir / mappingName,
    /// with separators normalized according to the current OS.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(MappingNames))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetRepoSourcesPath_VariousMappingNames_CombinesCorrectly(string mappingName)
    {
        // Arrange
        var vmrRoot = "vmrRoot";
        var tmpRoot = "tmpRoot";
        var sut = new VmrInfo(vmrRoot, tmpRoot);

        var expected = new NativePath(vmrRoot) / VmrInfo.SourcesDir / mappingName;

        // Act
        var actual = sut.GetRepoSourcesPath(mappingName);

        // Assert
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Ensures GetRepoSourcesPath(string) uses the current value of VmrPath at call time.
    /// Input: mappingName = "repo"; VmrPath is changed after construction.
    /// Expected: The returned path is combined from the updated VmrPath value, "src", and the mapping name.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetRepoSourcesPath_AfterVmrPathChange_UsesUpdatedRoot()
    {
        // Arrange
        var initialRoot = "oldRoot";
        var updatedRoot = "newRoot";
        var tmpRoot = "tmpRoot";
        var mappingName = "repo";
        var sut = new VmrInfo(initialRoot, tmpRoot);
        sut.VmrPath = new NativePath(updatedRoot);

        var expected = new NativePath(updatedRoot) / VmrInfo.SourcesDir / mappingName;

        // Act
        var actual = sut.GetRepoSourcesPath(mappingName);

        // Assert
        actual.Should().Be(expected);
    }

    private static IEnumerable<string> MappingNames()
    {
        yield return "repo";
        yield return string.Empty;                 // empty
        yield return " ";                          // whitespace
        yield return "/repo";                      // leading forward slash
        yield return "\\repo";                     // leading backslash
        yield return "nested/child";               // forward slash in middle
        yield return "nested\\child";              // backslash in middle
        yield return "name with spaces";           // spaces
        yield return ".";                          // current dir segment
        yield return "..";                         // parent dir segment
        yield return "a:b?c*|<>";                  // special characters (invalid on some FS, but allowed here as string)
        yield return new string('a', 1024);        // very long mapping name
    }

    /// <summary>
    /// Ensures VmrInfo.GetRelativeRepoSourcesPath(SourceMapping) returns a UnixPath rooted under "src"
    /// and normalizes mapping names with various shapes (simple, nested, backslashes, leading slash,
    /// empty, whitespace, and special characters).
    /// Inputs:
    ///  - mappingName: parameterized per test case
    /// Expected:
    ///  - Returned path equals the expected Unix-style path string.
    /// </summary>
    [TestCase("repo", "src/repo")]
    [TestCase("org/repo", "src/org/repo")]
    [TestCase("org\\repo", "src/org/repo")]
    [TestCase("/leading", "src/leading")]
    [TestCase("", "src/")]
    [TestCase(" ", "src/ ")]
    [TestCase("a:b*c?d|e", "src/a:b*c?d|e")]
    [TestCase("repo/", "src/repo/")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetRelativeRepoSourcesPath_SourceMappingName_ComposesUnderSrcUsingUnixSeparators(string mappingName, string expectedPath)
    {
        // Arrange
        SourceMapping mapping = new SourceMapping(
            mappingName,
            "https://example/remote.git",
            "main",
            Array.Empty<string>(),
            Array.Empty<string>(),
            false);

        // Act
        UnixPath actual = VmrInfo.GetRelativeRepoSourcesPath(mapping);

        // Assert
        actual.ToString().Should().Be(expectedPath);
    }

    /// <summary>
    /// Ensures GetRelativeRepoSourcesPath builds a Unix-style relative path under "src" for a variety of inputs,
    /// including leading/trailing slashes, backslashes, whitespace, and nested segments.
    /// Inputs:
    ///  - mappingName: Provided via [TestCase]s (e.g., "repo", "/repo", "repo/", "a/b", "a\\b", "", " ", "../x", "///x")
    /// Expected:
    ///  - The resulting UnixPath equals the expected path rooted at "src", using forward slashes and preserving trailing slashes.
    ///  - The computed path is usable in consumers that accept UnixPath (verified via ILocalGitRepo.GetFileFromGitAsync invocation).
    /// </summary>
    [TestCase("repo", "src/repo")]
    [TestCase("/repo", "src/repo")]
    [TestCase("repo/", "src/repo/")]
    [TestCase("a/b", "src/a/b")]
    [TestCase("a\\b", "src/a/b")]
    [TestCase("", "src/")]
    [TestCase(" ", "src/ ")]
    [TestCase("../x", "src/../x")]
    [TestCase("///x", "src///x")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async System.Threading.Tasks.Task GetRelativeRepoSourcesPath_VariousInputs_BuildsExpectedUnixPath(string mappingName, string expectedPath)
    {
        // Arrange
        var repo = new Mock<ILocalGitRepo>();
        repo.Setup(r => r.GetFileFromGitAsync(It.IsAny<UnixPath>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("content");
        var expectedUnixPath = new UnixPath(expectedPath);
        var expectedFilePath = expectedUnixPath / "file.txt";

        // Act
        var actualBasePath = VmrInfo.GetRelativeRepoSourcesPath(mappingName);
        var actualFilePath = actualBasePath / "file.txt";
        _ = await repo.Object.GetFileFromGitAsync(actualFilePath, "sha", "branch");

        // Assert
        repo.Verify(r => r.GetFileFromGitAsync(expectedFilePath, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    /// <summary>
    /// Verifies that when ThirdPartyNoticesTemplatePath is null or empty,
    /// ThirdPartyNoticesTemplateFullPath is null.
    /// Inputs:
    /// - ThirdPartyNoticesTemplatePath: null or "".
    /// Expected:
    /// - ThirdPartyNoticesTemplateFullPath returns null.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ThirdPartyNoticesTemplateFullPath_NullOrEmpty_ReturnsNull(string templatePath)
    {
        // Arrange
        var vmrBase = "C:/vmr";
        var tmpBase = "C:/tmp";
        var sut = new VmrInfo(vmrBase, tmpBase);
        sut.ThirdPartyNoticesTemplatePath = templatePath;

        // Act
        var result = sut.ThirdPartyNoticesTemplateFullPath;

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies that when ThirdPartyNoticesTemplatePath is non-empty,
    /// ThirdPartyNoticesTemplateFullPath returns the NativePath-combined value with correct
    /// directory separator normalization and slash-collapsing behavior.
    /// Inputs:
    /// - Various base paths (with/without trailing separator).
    /// - Various template paths (with forward/back slashes, leading separators, spaces, unicode).
    /// Expected:
    /// - Full path equals Combine(Normalize(basePath), Normalize(templatePath)) using NativePath logic.
    /// </summary>
    [TestCase("C:/vmr", "file.txt")]
    [TestCase("C:/vmr/", "file.txt")]
    [TestCase("C:/vmr", "dir/sub/file.txt")]
    [TestCase("C:/vmr", "dir\\sub\\file.txt")]
    [TestCase("C:/vmr", "/leading.txt")]
    [TestCase("C:/vmr/", "/leading.txt")]
    [TestCase("C:/vmr", "\\leading.txt")]
    [TestCase("C:/vmr", "//double-leading.txt")]
    [TestCase("C:/vmr/", "//double-leading.txt")]
    [TestCase("C:/vmr", "  ")]
    [TestCase("C:/vmr", "dir/with spaces/🧪-file.txt")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ThirdPartyNoticesTemplateFullPath_ValidTemplate_CombinesWithVmrPathCorrectly(string basePath, string templatePath)
    {
        // Arrange
        var tmpBase = "C:/tmp";
        var sut = new VmrInfo(basePath, tmpBase);
        sut.ThirdPartyNoticesTemplatePath = templatePath;
        var expected = CombineNative(Normalize(basePath), Normalize(templatePath));

        // Act
        var result = sut.ThirdPartyNoticesTemplateFullPath;

        // Assert
        result.Should().Be(expected);
    }

    // Helper: mirrors Microsoft.DotNet.DarcLib.Helpers.NativePath.NormalizePath behavior.
    private static string Normalize(string s)
    {
        return System.IO.Path.DirectorySeparatorChar == '/'
            ? s.Replace('\\', '/')
            : s.Replace('/', '\\');
    }

    // Helper: mirrors Microsoft.DotNet.DarcLib.Helpers.LocalPath.Combine logic.
    private static string CombineNative(string left, string right)
    {
        char sep = System.IO.Path.DirectorySeparatorChar;
        bool leftEnds = left.EndsWith(sep.ToString(), StringComparison.Ordinal);
        bool rightStarts = right.StartsWith(sep.ToString(), StringComparison.Ordinal);
        int slashCount = (leftEnds ? 1 : 0) + (rightStarts ? 1 : 0);

        switch (slashCount)
        {
            case 0: return left + sep + right;
            case 1: return left + right;
            case 2: return left + right.Substring(1);
            default: throw new NotImplementedException();
        }
    }
}
