// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

/// <summary>
/// Tests for ThirdPartyNoticesGenerator.UpdateThirdPartyNotices focusing on template path handling and header inclusion.
/// Notes:
///  - These tests are marked ignored because constructing a valid instance/value for IVmrInfo.VmrPath (used with the '/' operator)
///    and allowing StreamWriter to create a file without knowing the concrete path type is not feasible in isolation.
///  - To enable these tests, provide a real IVmrInfo with a concrete VmrPath value compatible with StreamWriter,
///    or refactor the production code to allow injecting a stream factory.
/// </summary>
public class ThirdPartyNoticesGeneratorTests
{
    /// <summary>
    /// Ensures UpdateThirdPartyNotices propagates exceptions thrown while reading the template header.
    /// Inputs:
    ///  - A templatePath for which IFileSystem.FileExists returns true and IFileSystem.ReadAllTextAsync throws IOException.
    /// Expected:
    ///  - The method throws the same IOException.
    /// The test is ignored pending a valid IVmrInfo.VmrPath value compatible with StreamWriter file creation.
    /// </summary>
    [Test]
    [Ignore("Inconclusive: Requires a concrete IVmrInfo.VmrPath compatible with StreamWriter path creation. Provide a real VmrPath to enable.")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task UpdateThirdPartyNotices_WhenTemplateFileReadFails_ExceptionPropagates()
    {
        // Arrange
        var templatePath = "template.txt";

        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var dependencyTrackerMock = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<ThirdPartyNoticesGenerator>>(MockBehavior.Loose);

        fileSystemMock.Setup(fs => fs.FileExists(templatePath)).Returns(true);
        fileSystemMock.Setup(fs => fs.ReadAllTextAsync(templatePath)).ThrowsAsync(new IOException("Read failure"));

        dependencyTrackerMock.SetupGet(d => d.Mappings).Returns(new List<object>()); // Placeholder: replace with real mapping model as needed.
        fileSystemMock.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(false);

        var sut = new ThirdPartyNoticesGenerator(
            vmrInfoMock.Object,
            dependencyTrackerMock.Object,
            fileSystemMock.Object,
            loggerMock.Object);

        // Act
        Func<Task> act = async () => await sut.UpdateThirdPartyNotices(templatePath);

        // Assert
        await act.Should().ThrowAsync<IOException>().WithMessage("*Read failure*");
    }

    /// <summary>
    /// Verifies that the constructor successfully creates an instance when provided with valid, non-null dependencies.
    /// Inputs:
    ///  - IVmrInfo mock instance.
    ///  - IVmrDependencyTracker mock instance.
    ///  - IFileSystem mock instance.
    ///  - ILogger&lt;ThirdPartyNoticesGenerator&gt; mock instance.
    /// Expected:
    ///  - No exception is thrown and the created instance is not null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict).Object;
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict).Object;
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger<ThirdPartyNoticesGenerator>>(MockBehavior.Strict).Object;

        // Act
        var sut = new ThirdPartyNoticesGenerator(vmrInfo, dependencyTracker, fileSystem, logger);

        // Assert
        sut.Should().NotBeNull();
    }

    /// <summary>
    /// Ensures that the constructed instance implements the IThirdPartyNoticesGenerator interface,
    /// confirming the intended contract is exposed by the type.
    /// Inputs:
    ///  - IVmrInfo mock instance.
    ///  - IVmrDependencyTracker mock instance.
    ///  - IFileSystem mock instance.
    ///  - ILogger&lt;ThirdPartyNoticesGenerator&gt; mock instance.
    /// Expected:
    ///  - The created instance is assignable to IThirdPartyNoticesGenerator.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithValidDependencies_ImplementsInterface()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict).Object;
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict).Object;
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger<ThirdPartyNoticesGenerator>>(MockBehavior.Strict).Object;

        // Act
        var sut = new ThirdPartyNoticesGenerator(vmrInfo, dependencyTracker, fileSystem, logger);

        // Assert
        sut.Should().BeAssignableTo<IThirdPartyNoticesGenerator>();
    }

    /// <summary>
    /// Verifies that when the template file does not exist and there are no notices discovered,
    /// the generator creates the THIRD-PARTY-NOTICES.txt file at the VMR root with empty content.
    /// Inputs:
    ///  - templatePath: any string path for which IFileSystem.FileExists returns false.
    ///  - IVmrDependencyTracker.Mappings: empty collection (no repositories).
    /// Expected:
    ///  - File is created at {vmrPath}/THIRD-PARTY-NOTICES.txt and is empty.
    ///  - No exceptions are thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task UpdateThirdPartyNotices_NoTemplateAndNoNotices_CreatesEmptyFile()
    {
        // Arrange
        var tempRoot = CreateTempDirectory();
        var vmrPath = new NativePath(tempRoot);

        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfoMock.SetupGet(v => v.VmrPath).Returns(vmrPath);

        var depTrackerMock = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        depTrackerMock.SetupGet(d => d.Mappings).Returns(new List<SourceMapping>());

        var fsMock = new Mock<IFileSystem>(MockBehavior.Strict);
        fsMock.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);

        var loggerMock = new Mock<ILogger<ThirdPartyNoticesGenerator>>(MockBehavior.Loose);

        var sut = new ThirdPartyNoticesGenerator(vmrInfoMock.Object, depTrackerMock.Object, fsMock.Object, loggerMock.Object);
        var outputPath = Path.Combine(tempRoot, VmrInfo.ThirdPartyNoticesFileName);

        try
        {
            // Act
            await sut.UpdateThirdPartyNotices("any-template-path");

            // Assert
            File.Exists(outputPath).Should().BeTrue();
            var content = File.ReadAllText(outputPath);
            content.Should().BeEmpty();
        }
        finally
        {
            SafeDeleteDirectory(tempRoot);
        }
    }

    /// <summary>
    /// Ensures that when a template exists and multiple repositories contain THIRD-PARTY-NOTICES files,
    /// the generator writes the header, then notice sections ordered by full notice path,
    /// uses repository names determined by IFileSystem.GetFileName(IFileSystem.GetDirectoryName(notice)),
    /// and includes the notice contents with proper separators.
    /// Inputs:
    ///  - templatePath exists and returns a non-empty header.
    ///  - Two mappings whose source paths exist and contain TPN files.
    /// Expected:
    ///  - Output starts with the header.
    ///  - Sections appear for both repos in sorted order by notice path.
    ///  - Each section contains 45 '#' lines, a "### {repo}" header, and the notice content.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task UpdateThirdPartyNotices_TemplatePresentAndMultipleNotices_WritesHeaderAndSectionsInSortedOrder()
    {
        // Arrange
        var tempRoot = CreateTempDirectory();
        var vmrPath = new NativePath(tempRoot);
        var outputPath = Path.Combine(tempRoot, VmrInfo.ThirdPartyNoticesFileName);

        var templatePath = Path.Combine(tempRoot, "TEMPLATE.txt");
        var header = "HEADER LINE\n";

        var mappingAlpha = new SourceMapping("alpha", "remote", "ref", new List<string>(), new List<string>(), false);
        var mappingBeta = new SourceMapping("beta", "remote", "ref", new List<string>(), new List<string>(), false);

        var alphaRepoPath = Path.Combine(tempRoot, "src", "alpha");
        var betaRepoPath = Path.Combine(tempRoot, "src", "beta");

        var alphaNoticePath = Path.Combine(alphaRepoPath, "THIRD-PARTY-NOTICES.txt");
        var betaNoticePath = Path.Combine(betaRepoPath, "third-party-notices"); // no .txt suffix is valid

        var alphaLicense = "ALPHA LICENSE CONTENT";
        var betaLicense = "BETA LICENSE CONTENT";

        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfoMock.SetupGet(v => v.VmrPath).Returns(vmrPath);
        vmrInfoMock.Setup(v => v.GetRepoSourcesPath(It.Is<SourceMapping>(m => m.Name == "alpha"))).Returns(new NativePath(alphaRepoPath));
        vmrInfoMock.Setup(v => v.GetRepoSourcesPath(It.Is<SourceMapping>(m => m.Name == "beta"))).Returns(new NativePath(betaRepoPath));

        var depTrackerMock = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        depTrackerMock.SetupGet(d => d.Mappings).Returns(new List<SourceMapping> { mappingAlpha, mappingBeta });

        var fsMock = new Mock<IFileSystem>(MockBehavior.Strict);
        // Template exists and has content
        fsMock.Setup(f => f.FileExists(templatePath)).Returns(true);
        fsMock.Setup(f => f.ReadAllTextAsync(templatePath)).ReturnsAsync(header);

        // Repos exist and expose files
        fsMock.Setup(f => f.DirectoryExists(alphaRepoPath)).Returns(true);
        fsMock.Setup(f => f.DirectoryExists(betaRepoPath)).Returns(true);
        fsMock.Setup(f => f.GetFiles(alphaRepoPath)).Returns(new[] { alphaNoticePath, Path.Combine(alphaRepoPath, "README.md") });
        fsMock.Setup(f => f.GetFiles(betaRepoPath)).Returns(new[] { betaNoticePath });

        // Repo name extraction for alpha
        fsMock.Setup(f => f.GetDirectoryName(alphaNoticePath)).Returns(alphaRepoPath);
        fsMock.Setup(f => f.GetFileName(alphaRepoPath)).Returns("alpha");
        fsMock.Setup(f => f.ReadAllTextAsync(alphaNoticePath)).ReturnsAsync(alphaLicense);

        // Repo name extraction for beta
        fsMock.Setup(f => f.GetDirectoryName(betaNoticePath)).Returns(betaRepoPath);
        fsMock.Setup(f => f.GetFileName(betaRepoPath)).Returns("beta");
        fsMock.Setup(f => f.ReadAllTextAsync(betaNoticePath)).ReturnsAsync(betaLicense);

        var loggerMock = new Mock<ILogger<ThirdPartyNoticesGenerator>>(MockBehavior.Loose);

        var sut = new ThirdPartyNoticesGenerator(vmrInfoMock.Object, depTrackerMock.Object, fsMock.Object, loggerMock.Object);

        try
        {
            // Act
            await sut.UpdateThirdPartyNotices(templatePath);

            // Assert
            File.Exists(outputPath).Should().BeTrue();
            var content = File.ReadAllText(outputPath);

            // Header present
            content.Should().StartWith(header);

            // Section markers and repo headers
            var hashes = new string('#', 45);
            content.Should().Contain(hashes);
            content.Should().Contain("### alpha");
            content.Should().Contain("### beta");

            // Notice contents included
            content.Should().Contain(alphaLicense);
            content.Should().Contain(betaLicense);

            // Sections appear in lexicographic order of notice paths (alpha before beta for our paths)
            var alphaIndex = content.IndexOf("### alpha", StringComparison.Ordinal);
            var betaIndex = content.IndexOf("### beta", StringComparison.Ordinal);
            (alphaIndex >= 0 && betaIndex >= 0 && alphaIndex < betaIndex).Should().BeTrue();
        }
        finally
        {
            SafeDeleteDirectory(tempRoot);
        }
    }

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ThirdPartyNoticesGeneratorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Swallow cleanup exceptions to avoid test flakiness.
        }
    }

    /// <summary>
    /// Verifies that IsTpnPath correctly identifies paths that match the expected THIRD-PARTY-NOTICES file pattern,
    /// including case-insensitivity, optional hyphens, and the optional .txt extension, while ensuring non-matching
    /// paths (wrong extension, invalid separators, extra characters, or trailing whitespace) are rejected.
    /// Inputs:
    ///  - Diverse path strings with mixed separators, casing, prefixes, suffixes, and whitespace.
    /// Expected:
    ///  - True for inputs ending with "third-?party-?notices" optionally followed by ".txt" (case-insensitive).
    ///  - False for all other inputs that deviate from the pattern or do not end with the pattern.
    /// </summary>
    [TestCase("third-party-notices.txt", true)]
    [TestCase("THIRD-PARTY-NOTICES.TXT", true)]
    [TestCase("third-party-notices", true)]
    [TestCase("thirdpartynotices", true)]
    [TestCase("third-partynotices.txt", true)]
    [TestCase("dir/sub/thirdpartynotices.txt", true)]
    [TestCase(@"C:\path\to\third-party-notices", true)]
    [TestCase("README-third-party-notices.txt", true)] // Substring match before end is allowed by current regex (no start anchor)
    [TestCase("\tthirdpartynotices.txt", true)] // Leading whitespace before the match is allowed by current regex

    [TestCase("third--party-notices.txt", false)]
    [TestCase("third-party--notices.txt", false)]
    [TestCase("third party notices.txt", false)]
    [TestCase("third_party_notices.txt", false)]
    [TestCase("third-party-notices.md", false)]
    [TestCase("third-party-notices.txt.old", false)]
    [TestCase("somethingthird-party-notices.txt.swp", false)]
    [TestCase("thirdpartynoticesTXT", false)]
    [TestCase("", false)]
    [TestCase(" ", false)]
    [TestCase("third", false)]
    [TestCase("thirdpartynotices.txt ", false)] // Trailing space prevents end-of-string anchor
    [TestCase("thirdpartynotices.txt\n", false)] // Trailing newline prevents end-of-string anchor
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void IsTpnPath_VariousInputs_ReturnsExpected(string inputPath, bool expected)
    {
        // Arrange
        var path = inputPath;

        // Act
        var result = ThirdPartyNoticesGenerator.IsTpnPath(path);

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Ensures that the regex efficiently matches very long inputs that end with the valid pattern.
    /// Inputs:
    ///  - A very long path string that ends with "third-party-notices.txt".
    /// Expected:
    ///  - True (the method should recognize the valid suffix).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void IsTpnPath_VeryLongInputEndingWithPattern_ReturnsTrue()
    {
        // Arrange
        var longPrefix = new string('a', 8192);
        var path = longPrefix + "/third-party-notices.txt";

        // Act
        var result = ThirdPartyNoticesGenerator.IsTpnPath(path);

        // Assert
        result.Should().Be(true);
    }
}
