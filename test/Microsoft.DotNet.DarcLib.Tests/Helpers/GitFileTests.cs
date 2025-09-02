// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Helpers.UnitTests;

public class GitFileTests
{
    /// <summary>
    /// Validates that the XmlDocument-based constructor:
    ///  - Formats XML content with indentation and without XML declaration,
    ///  - Normalizes content to ensure it ends with a newline,
    ///  - Assigns default Mode, Operation, and Utf8 ContentEncoding,
    ///  - Preserves the provided file path.
    /// Inputs:
    ///  - Various XML payloads including declaration, whitespace, and self-closing elements.
    /// Expected:
    ///  - Content starts with the root element, contains no XML declaration,
    ///    ends with a single newline, and default properties are set correctly.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(ValidXmlCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GitFile_WithXmlDocument_FormatsContentAndAssignsDefaults(string filePath, string xml)
    {
        // Arrange
        var doc = new XmlDocument();
        doc.LoadXml(xml);

        // Act
        var sut = new GitFile(filePath, doc);

        // Assert
        sut.FilePath.Should().Be(filePath);
        sut.ContentEncoding.Should().Be(ContentEncoding.Utf8);
        sut.Mode.Should().Be("100644");
        sut.Operation.Should().Be(GitFileOperation.Add);

        sut.Content.Should().NotBeNullOrEmpty();
        sut.Content.Should().StartWith("<root");
        sut.Content.Should().NotContain("<?xml");
        sut.Content.Should().EndWith("\n");
    }

    /// <summary>
    /// Ensures that passing a null XmlDocument to the XmlDocument-based constructor
    /// results in a NullReferenceException, since the implementation dereferences the parameter.
    /// Inputs:
    ///  - filePath: any non-null string.
    ///  - xmlDocument: null.
    /// Expected:
    ///  - Throws NullReferenceException.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GitFile_NullXmlDocument_ThrowsNullReferenceException()
    {
        // Arrange
        Action act = () => new GitFile("file.xml", (XmlDocument)null);

        // Act & Assert
        act.Should().Throw<NullReferenceException>();
    }

    private static IEnumerable<TestCaseData> ValidXmlCases()
    {
        yield return new TestCaseData(
            "a/b.xml",
            "<?xml version=\"1.0\"?><root><child id=\"1\">text</child><b>y</b></root>")
            .SetName("NestedElementsWithDeclaration");

        yield return new TestCaseData(
            "C:\\temp\\f.xml",
            "<root>  <a/> <b> x </b></root>")
            .SetName("MixedWhitespace");

        yield return new TestCaseData(
            "relative.xml",
            "<root/>")
            .SetName("SelfClosingRoot");
    }

    /// <summary>
    /// Verifies newline normalization and trailing newline enforcement for various content inputs.
    /// Inputs:
    ///  - inputContent: content containing empty, no newline, Environment.NewLine, existing LF, multiple LFs, or very long text.
    /// Expected:
    ///  - Content replaces Environment.NewLine with '\n' (platform-agnostic) and ensures the content ends with a newline,
    ///    without adding an extra newline if one is already present.
    ///  - Other properties are initialized to defaults (Utf8, "100644", Add) and Metadata remains null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(ContentNormalizationCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_ContentVariants_NormalizesAndEnsuresTrailingNewline(string inputContent, string expectedContent)
    {
        // Arrange
        var path = "any/path.txt";

        // Act
        var file = new GitFile(path, inputContent);

        // Assert
        file.Content.Should().Be(expectedContent);
        file.FilePath.Should().Be(path);
        file.ContentEncoding.Should().Be(ContentEncoding.Utf8);
        file.Mode.Should().Be("100644");
        file.Operation.Should().Be(GitFileOperation.Add);
        file.Metadata.Should().BeNull();
    }

    private static IEnumerable ContentNormalizationCases()
    {
        yield return new TestCaseData("", "\n").SetName("EmptyString_ToSingleLf");
        yield return new TestCaseData("hello", "hello\n").SetName("NoNewlines_AppendsLf");
        yield return new TestCaseData("line1" + Environment.NewLine + "line2", "line1\nline2\n").SetName("EnvironmentNewLine_ReplacedWithLfAndAppends");
        yield return new TestCaseData("already\n", "already\n").SetName("AlreadyLfTerminated_NoChange");
        yield return new TestCaseData("double\n\n", "double\n\n").SetName("MultipleTrailingLfs_Preserved");
        yield return new TestCaseData(new string('x', 10000), new string('x', 10000) + "\n").SetName("VeryLongString_AppendsLf");
    }

    /// <summary>
    /// Ensures that the constructor assigns the provided file path verbatim and initializes defaults.
    /// Inputs:
    ///  - filePath: various path formats including empty, whitespace, Windows and relative paths.
    /// Expected:
    ///  - FilePath equals the provided value.
    ///  - Content is the input "x" with a trailing newline appended.
    ///  - ContentEncoding is Utf8; Mode is "100644"; Operation is Add; Metadata is null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("path/to/file.txt")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("C:\\a\\b.txt")]
    [TestCase("./rel/../path")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_FilePathVariants_AssignsPropertiesAndDefaults(string filePath)
    {
        // Arrange
        var content = "x";

        // Act
        var file = new GitFile(filePath, content);

        // Assert
        file.FilePath.Should().Be(filePath);
        file.Content.Should().Be("x\n");
        file.ContentEncoding.Should().Be(ContentEncoding.Utf8);
        file.Mode.Should().Be("100644");
        file.Operation.Should().Be(GitFileOperation.Add);
        file.Metadata.Should().BeNull();
    }

    /// <summary>
    /// Validates that the JObject+metadata constructor:
    ///  - Serializes the JSON using Formatting.Indented,
    ///  - Normalizes line endings to '\n' and appends a trailing newline,
    ///  - Sets default properties (ContentEncoding = Utf8, Mode = "100644", Operation = Add),
    ///  - Assigns the provided metadata dictionary by reference.
    /// Inputs:
    ///  - Various filePath values.
    ///  - A non-empty metadata dictionary.
    /// Expected:
    ///  - FilePath equals input.
    ///  - Content equals indented JSON with normalized '\n' line endings and a final '\n'.
    ///  - ContentEncoding == Utf8, Mode == "100644", Operation == Add.
    ///  - Metadata reference is the same instance as provided.
    /// </summary>
    [TestCase("file.json")]
    [TestCase("")]
    [TestCase("C:\\repo\\sub\\file.json")]
    [TestCase("relative/path/to/file.json")]
    [TestCase("inv*alid?:<>|/path.json")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_WithJObjectAndMetadata_SetsContentMetadataAndDefaults(string filePath)
    {
        // Arrange
        var json = new JObject
        {
            ["level1"] = new JObject
            {
                ["name"] = "value",
                ["num"] = 42
            },
            ["arr"] = new JArray(1, 2, 3)
        };

        var expectedContent = json.ToString(Formatting.Indented).Replace(Environment.NewLine, "\n");
        if (!expectedContent.EndsWith("\n"))
        {
            expectedContent += "\n";
        }

        var metadata = new Dictionary<GitFileMetadataName, string>
            {
                { GitFileMetadataName.ToolsDotNetUpdate, "7.0.100" },
                { GitFileMetadataName.SdkVersionUpdate, "8.0.200" }
            };

        // Act
        var gitFile = new GitFile(filePath, json, metadata);

        // Assert
        gitFile.FilePath.Should().Be(filePath);
        gitFile.Content.Should().Be(expectedContent);
        gitFile.ContentEncoding.Should().Be(ContentEncoding.Utf8);
        gitFile.Mode.Should().Be("100644");
        gitFile.Operation.Should().Be(GitFileOperation.Add);
        gitFile.Metadata.Should().BeSameAs(metadata);
    }

    /// <summary>
    /// Verifies that the constructor normalizes Environment.NewLine to '\n' and ensures exactly one trailing newline.
    /// Inputs:
    ///  - Various content strings including no newline, lone '\n', Environment.NewLine in the middle, and trailing Environment.NewLine.
    /// Expected:
    ///  - Content replaces Environment.NewLine with '\n' and ends with a single '\n' (no duplication).
    ///  - Mode defaults to "100644" and Operation defaults to Add when not specified.
    /// </summary>
    [Test]
    [TestCase("hello", "hello\n")]
    [TestCase("hello\n", "hello\n")]
    [TestCaseSource(nameof(NewLineNormalizationCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GitFile_Ctor_NormalizesNewlinesAndEnsuresSingleTrailingNewline(string inputContent, string expectedContent)
    {
        // Arrange
        var filePath = "path/to/file.txt";
        var encoding = ContentEncoding.Utf8;

        // Act
        var gitFile = new GitFile(filePath, inputContent, encoding);

        // Assert
        gitFile.FilePath.Should().Be(filePath);
        gitFile.Content.Should().Be(expectedContent);
        gitFile.ContentEncoding.Should().Be(encoding);
        gitFile.Mode.Should().Be("100644");
        gitFile.Operation.Should().Be(GitFileOperation.Add);
    }

    /// <summary>
    /// Ensures that explicitly provided ContentEncoding, Mode, and Operation are assigned verbatim.
    /// Inputs:
    ///  - Encoding (Utf8/Base64), Mode ("100755" or "100644"), Operation (Add/Delete).
    /// Expected:
    ///  - Properties ContentEncoding, Mode, and Operation match the provided parameters exactly.
    /// </summary>
    [Test]
    [TestCase(ContentEncoding.Utf8, "100755", GitFileOperation.Delete)]
    [TestCase(ContentEncoding.Base64, "100644", GitFileOperation.Add)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GitFile_Ctor_AssignsProvidedValues_PropertiesMatchParameters(ContentEncoding encoding, string mode, GitFileOperation operation)
    {
        // Arrange
        var filePath = "repo/file.sh";
        var inputContent = "echo hi\n"; // already ends with '\n' so no extra added

        // Act
        var gitFile = new GitFile(filePath, inputContent, encoding, mode, operation);

        // Assert
        gitFile.FilePath.Should().Be(filePath);
        gitFile.Content.Should().Be("echo hi\n");
        gitFile.ContentEncoding.Should().Be(encoding);
        gitFile.Mode.Should().Be(mode);
        gitFile.Operation.Should().Be(operation);
    }

    /// <summary>
    /// Validates that when optional parameters are omitted, Mode defaults to "100644" and Operation defaults to Add.
    /// Inputs:
    ///  - filePath: typical relative path.
    ///  - content: string without a trailing newline.
    ///  - contentEncoding: Utf8.
    /// Expected:
    ///  - Mode == "100644", Operation == Add, Content ends with a single '\n'.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GitFile_Ctor_DefaultsAppliedWhenOptionalArgsOmitted()
    {
        // Arrange
        var filePath = "relative/path.txt";
        var inputContent = "no trailing newline";
        var encoding = ContentEncoding.Utf8;

        // Act
        var gitFile = new GitFile(filePath, inputContent, encoding);

        // Assert
        gitFile.Mode.Should().Be("100644");
        gitFile.Operation.Should().Be(GitFileOperation.Add);
        gitFile.Content.Should().Be("no trailing newline\n");
    }

    /// <summary>
    /// Confirms that FilePath is assigned verbatim for a variety of path-like strings, including empty, whitespace, typical, Windows-style, long, and special-character cases.
    /// Inputs:
    ///  - Various filePath strings from the provided cases.
    /// Expected:
    ///  - FilePath equals the input value with no alterations.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(FilePathCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GitFile_FilePath_AssignedVerbatim_ForVariousStrings(string filePath)
    {
        // Arrange
        var inputContent = "x";
        var encoding = ContentEncoding.Utf8;

        // Act
        var gitFile = new GitFile(filePath, inputContent, encoding);

        // Assert
        gitFile.FilePath.Should().Be(filePath);
        gitFile.Content.Should().Be("x\n");
        gitFile.Mode.Should().Be("100644");
        gitFile.Operation.Should().Be(GitFileOperation.Add);
    }

    // TestCase sources

    private static IEnumerable NewLineNormalizationCases()
    {
        yield return new TestCaseData("hello" + Environment.NewLine + "world", "hello\nworld\n");
        yield return new TestCaseData("hello" + Environment.NewLine, "hello\n");
    }

    private static IEnumerable FilePathCases()
    {
        yield return "";
        yield return "   ";
        yield return "dir/file.txt";
        yield return "C:\\path with spaces\\file.txt";
        yield return new string('a', 1024);
        yield return "weird<>:\"|?*.txt";
    }

    /// <summary>
    /// Verifies that all files under any '.git/objects' directories beneath the provided root path
    /// have their attributes reset to Normal and are no longer read-only.
    /// Inputs:
    ///  - A temporary directory containing multiple nested '.git/objects' trees with read-only files.
    ///  - Additional read-only files outside '.git/objects' to ensure they are not modified.
    /// Expected:
    ///  - Files under '.git/objects' are not read-only and have Normal attributes.
    ///  - Files outside '.git/objects' remain read-only (unchanged).
    /// </summary>
    [NUnit.Framework.Test]
    [NUnit.Framework.Author("Code Testing Agent v0.3.0")]
    [NUnit.Framework.Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void MakeGitFilesDeletable_ReadOnlyFilesUnderGitObjects_AreClearedRecursively()
    {
        // Arrange
        var root = CreateTempDirectory();
        try
        {
            var repo1Objects = Directory.CreateDirectory(Path.Combine(root, "repo1", GitFile.GitDirectory, "objects")).FullName;
            var repo2ObjectsNested = Directory.CreateDirectory(Path.Combine(root, "a", "b", "c", "repo2", GitFile.GitDirectory, "objects", "aa", "bb")).FullName;

            var gitObjFile1 = CreateReadOnlyFile(Path.Combine(repo1Objects, "file1.txt"), "x");
            var gitObjFile2 = CreateReadOnlyFile(Path.Combine(repo2ObjectsNested, "file2.bin"), "y");

            // Files that should NOT be modified
            var outsideGitObjects = CreateReadOnlyFile(Path.Combine(root, "outside", "objects", "outside.txt"), "z");
            var insideGitButNotObjects = CreateReadOnlyFile(Path.Combine(root, "repo1", GitFile.GitDirectory, "other.txt"), "not-in-objects");

            // Act
            GitFile.MakeGitFilesDeletable(root);

            // Assert
            var fi1 = new FileInfo(gitObjFile1);
            var fi2 = new FileInfo(gitObjFile2);
            fi1.Attributes.Should().Be(FileAttributes.Normal);
            fi2.Attributes.Should().Be(FileAttributes.Normal);
            fi1.IsReadOnly.Should().BeFalse();
            fi2.IsReadOnly.Should().BeFalse();

            var fiOutside = new FileInfo(outsideGitObjects);
            var fiInsideGitNotObjects = new FileInfo(insideGitButNotObjects);
            fiOutside.IsReadOnly.Should().BeTrue();
            fiOutside.Attributes.Should().Be(FileAttributes.ReadOnly);
            fiInsideGitNotObjects.IsReadOnly.Should().BeTrue();
            fiInsideGitNotObjects.Attributes.Should().Be(FileAttributes.ReadOnly);
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    /// <summary>
    /// Ensures that when the provided path exists but contains no '.git' directories,
    /// the method completes without throwing and does not alter unrelated files.
    /// Inputs:
    ///  - A temporary directory with read-only files not under any '.git/objects'.
    /// Expected:
    ///  - No exception is thrown.
    ///  - The unrelated read-only file remains unchanged.
    /// </summary>
    [NUnit.Framework.Test]
    [NUnit.Framework.Author("Code Testing Agent v0.3.0")]
    [NUnit.Framework.Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void MakeGitFilesDeletable_NoGitDirectories_DoesNotThrowAndDoesNotChangeUnrelatedFiles()
    {
        // Arrange
        var root = CreateTempDirectory();
        try
        {
            var unrelated = CreateReadOnlyFile(Path.Combine(root, "some", "path", "unrelated.txt"), "content");

            // Act
            NUnit.Framework.Assert.DoesNotThrow(() => GitFile.MakeGitFilesDeletable(root));

            // Assert
            var fi = new FileInfo(unrelated);
            fi.Exists.Should().BeTrue();
            fi.IsReadOnly.Should().BeTrue();
            fi.Attributes.Should().Be(FileAttributes.ReadOnly);
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    /// <summary>
    /// Verifies that when a '.git/objects' directory exists but contains no files,
    /// the method completes successfully without attempting modifications.
    /// Inputs:
    ///  - A temporary directory with an empty '.git/objects' directory.
    /// Expected:
    ///  - No exception is thrown and no changes are needed.
    /// </summary>
    [NUnit.Framework.Test]
    [NUnit.Framework.Author("Code Testing Agent v0.3.0")]
    [NUnit.Framework.Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void MakeGitFilesDeletable_EmptyObjectsDirectory_DoesNotThrow()
    {
        // Arrange
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "repo", GitFile.GitDirectory, "objects"));

            // Act / Assert
            NUnit.Framework.Assert.DoesNotThrow(() => GitFile.MakeGitFilesDeletable(root));
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    /// <summary>
    /// Ensures that if a '.git' directory exists without the required 'objects' subdirectory,
    /// the method fails when attempting to enumerate files from the non-existent 'objects' path.
    /// Inputs:
    ///  - A temporary directory containing a '.git' directory but no 'objects' subdirectory.
    /// Expected:
    ///  - DirectoryNotFoundException is thrown by Directory.GetFiles for the missing 'objects' path.
    /// </summary>
    [NUnit.Framework.Test]
    [NUnit.Framework.Author("Code Testing Agent v0.3.0")]
    [NUnit.Framework.Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void MakeGitFilesDeletable_GitDirectoryWithoutObjects_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "repo", GitFile.GitDirectory));

            // Act
            Action act = () => GitFile.MakeGitFilesDeletable(root);

            // Assert
            act.Should().Throw<DirectoryNotFoundException>();
        }
        finally
        {
            CleanupDirectory(root);
        }
    }

    /// <summary>
    /// Validates that a non-existent root directory results in a DirectoryNotFoundException,
    /// as the method attempts to enumerate directories from the provided path.
    /// Inputs:
    ///  - A random path that does not exist on disk.
    /// Expected:
    ///  - DirectoryNotFoundException is thrown.
    /// </summary>
    [NUnit.Framework.Test]
    [NUnit.Framework.Author("Code Testing Agent v0.3.0")]
    [NUnit.Framework.Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void MakeGitFilesDeletable_NonExistentPath_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        // Act
        Action act = () => GitFile.MakeGitFilesDeletable(missing);

        // Assert
        act.Should().Throw<DirectoryNotFoundException>();
    }

    /// <summary>
    /// Verifies that invalid path inputs (empty or whitespace-only) trigger an ArgumentException
    /// from the underlying Directory.GetDirectories API.
    /// Inputs:
    ///  - Empty string, whitespace-only string variations.
    /// Expected:
    ///  - ArgumentException is thrown.
    /// </summary>
    [NUnit.Framework.TestCase("")]
    [NUnit.Framework.TestCase(" ")]
    [NUnit.Framework.TestCase(" \t\r\n")]
    [NUnit.Framework.Author("Code Testing Agent v0.3.0")]
    [NUnit.Framework.Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void MakeGitFilesDeletable_InvalidPath_ThrowsArgumentException(string invalidPath)
    {
        // Arrange
        // (invalidPath provided via TestCase)

        // Act
        Action act = () => GitFile.MakeGitFilesDeletable(invalidPath);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "GitFileTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateReadOnlyFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, content);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        return path;
    }

    private static void CleanupDirectory(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        fi.Attributes = FileAttributes.Normal;
                        fi.IsReadOnly = false;
                    }
                    catch
                    {
                        // best-effort cleanup
                    }
                }
                Directory.Delete(root, true);
            }
        }
        catch
        {
            // swallow cleanup exceptions
        }
    }
}
