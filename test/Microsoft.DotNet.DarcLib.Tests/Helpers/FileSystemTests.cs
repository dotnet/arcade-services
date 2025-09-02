// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Helpers.UnitTests;

public class FileSystemTests
{
    /// <summary>
    /// Verifies that the DirectorySeparatorChar property returns the platform-specific directory separator character.
    /// Input: No inputs; reads current platform's separator.
    /// Expected: The returned character equals System.IO.Path.DirectorySeparatorChar for the running platform.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DirectorySeparatorChar_OnThisPlatform_MatchesSystemIOPath()
    {
        // Arrange
        var fileSystem = new FileSystem();

        // Act
        var separator = fileSystem.DirectorySeparatorChar;

        // Assert
        separator.Should().Be(Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Verifies that CreateDirectory creates the target directory (including nested directories) when it does not exist,
    /// and does not throw when the directory already exists.
    /// Inputs:
    ///  - depth: Number of nested subdirectories to create relative to a unique temp root.
    ///  - precreate: Whether the target directory is pre-created before invoking CreateDirectory.
    /// Expected:
    ///  - No exception is thrown.
    ///  - The target directory exists after the call.
    /// </summary>
    [TestCaseSource(nameof(CreateDirectory_ValidPaths_TestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CreateDirectory_ValidAndExistingPaths_DirectoryExists(int depth, bool precreate)
    {
        // Arrange
        var fileSystem = new FileSystem();

        var root = Path.Combine(Path.GetTempPath(), "darc-tests", "fs-create", Guid.NewGuid().ToString("N"));
        var path = root;
        for (int i = 0; i < depth; i++)
        {
            path = Path.Combine(path, $"level-{i + 1}");
        }

        if (precreate)
        {
            Directory.CreateDirectory(path);
        }

        try
        {
            // Act
            fileSystem.CreateDirectory(path);

            // Assert
            Directory.Exists(path).Should().BeTrue();
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // Intentionally ignore cleanup exceptions to avoid masking test results.
            }
        }
    }

    /// <summary>
    /// Ensures that CreateDirectory throws an ArgumentException when the path contains a null-character,
    /// which is invalid across platforms.
    /// Inputs:
    ///  - A path containing the '\0' character.
    /// Expected:
    ///  - ArgumentException is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CreateDirectory_PathContainsNullCharacter_ThrowsArgumentException()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var invalidPath = Path.Combine(Path.GetTempPath(), "darc-tests") + "\0invalid";

        // Act
        Action act = () => fileSystem.CreateDirectory(invalidPath);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    private static System.Collections.IEnumerable CreateDirectory_ValidPaths_TestCases()
    {
        yield return new TestCaseData(1, false).SetName("CreateDirectory_CreatesSingleLevel_WhenNotExists");
        yield return new TestCaseData(3, false).SetName("CreateDirectory_CreatesNestedDirectories_WhenNotExists");
        yield return new TestCaseData(2, true).SetName("CreateDirectory_DoesNotThrow_WhenAlreadyExists");
    }

    /// <summary>
    /// Validates that DirectoryExists returns expected boolean outcomes for a variety of path inputs.
    /// Inputs:
    ///  - ".", Temp directory path (expected to exist).
    ///  - Empty string, whitespace-only string, wildcard character, random non-existent path, and an exceedingly long path (all expected to not exist).
    /// Expected:
    ///  - True for existing directories; otherwise false. No exceptions should be thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(DirectoryExists_PathVariants_ReturnsExpected_TestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void DirectoryExists_PathVariants_ReturnsExpected(string path, bool expected)
    {
        // Arrange
        var fileSystem = new FileSystem();

        // Act
        var exists = fileSystem.DirectoryExists(path);

        // Assert
        exists.Should().Be(expected);
    }

    /// <summary>
    /// Ensures that when the provided path points to an existing file (not a directory),
    /// DirectoryExists returns false.
    /// Inputs:
    ///  - Path returned by Path.GetTempFileName(), which is guaranteed to create a new temp file.
    /// Expected:
    ///  - False, because the path refers to a file, not a directory.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void DirectoryExists_PathPointsToExistingFile_ReturnsFalse()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var tempFile = Path.GetTempFileName();

        try
        {
            // Act
            var exists = fileSystem.DirectoryExists(tempFile);

            // Assert
            exists.Should().BeFalse();
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<TestCaseData> DirectoryExists_PathVariants_ReturnsExpected_TestCases()
    {
        var tempPath = Path.GetTempPath();
        var randomNonExistent = Path.Combine(tempPath, "darc-test-" + Guid.NewGuid().ToString("N"));
        var veryLongPath = Path.Combine(tempPath, new string('a', 300));

        yield return new TestCaseData(".", true).SetName("DirectoryExists_PathVariants_ReturnsExpected_CurrentDirectory_ReturnsTrue");
        yield return new TestCaseData(tempPath, true).SetName("DirectoryExists_PathVariants_ReturnsExpected_TempDirectory_ReturnsTrue");

        yield return new TestCaseData(string.Empty, false).SetName("DirectoryExists_PathVariants_ReturnsExpected_Empty_ReturnsFalse");
        yield return new TestCaseData(" ", false).SetName("DirectoryExists_PathVariants_ReturnsExpected_Whitespace_ReturnsFalse");
        yield return new TestCaseData("*", false).SetName("DirectoryExists_PathVariants_ReturnsExpected_Wildcard_ReturnsFalse");
        yield return new TestCaseData(randomNonExistent, false).SetName("DirectoryExists_PathVariants_ReturnsExpected_RandomNonExistent_ReturnsFalse");
        yield return new TestCaseData(veryLongPath, false).SetName("DirectoryExists_PathVariants_ReturnsExpected_TooLongPath_ReturnsFalse");
    }

    /// <summary>
    /// Verifies that FileExists returns true when the specified file path points to an existing file.
    /// Inputs:
    ///  - A valid path to a temporary file created for the test.
    /// Expected:
    ///  - Returns true and does not throw any exception.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void FileExists_ExistingFile_ReturnsTrue()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var tempFile = Path.GetTempFileName();

        try
        {
            // Act
            var exists = fileSystem.FileExists(tempFile);

            // Assert
            exists.Should().BeTrue();
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verifies that FileExists returns false when the provided path points to a directory instead of a file.
    /// Inputs:
    ///  - A valid path to a temporary directory.
    /// Expected:
    ///  - Returns false and does not throw any exception.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void FileExists_PathIsDirectory_ReturnsFalse()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var tempDir = Path.Combine(Path.GetTempPath(), "darc_fs_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Act
            var exists = fileSystem.FileExists(tempDir);

            // Assert
            exists.Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verifies that FileExists returns false for empty or whitespace-only paths.
    /// Inputs:
    ///  - An empty string, single space, or tab character.
    /// Expected:
    ///  - Returns false and does not throw any exception.
    /// </summary>
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\t")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void FileExists_EmptyOrWhitespacePath_ReturnsFalse(string path)
    {
        // Arrange
        var fileSystem = new FileSystem();

        // Act
        var exists = fileSystem.FileExists(path);

        // Assert
        exists.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that FileExists returns false for invalid or wildcard file names within a temporary directory.
    /// Inputs:
    ///  - A set of file names that are invalid on some platforms or are unlikely to exist (e.g., "*", "?", "<", ">", "|"),
    ///    combined with a temporary directory to ensure path isolation.
    /// Expected:
    ///  - Returns false and does not throw any exception.
    /// </summary>
    [TestCase("*")]
    [TestCase("?")]
    [TestCase("<")]
    [TestCase(">")]
    [TestCase("|")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void FileExists_InvalidOrWildcardPathInTempDir_ReturnsFalse(string fileName)
    {
        // Arrange
        var fileSystem = new FileSystem();
        var tempDir = Path.Combine(Path.GetTempPath(), "darc_fs_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, fileName);

        try
        {
            // Act
            var exists = fileSystem.FileExists(path);

            // Assert
            exists.Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Verifies that FileExists returns false for a path that does not exist.
    /// Inputs:
    ///  - A random file name within a newly created temporary directory, ensuring the file does not exist.
    /// Expected:
    ///  - Returns false and does not throw any exception.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void FileExists_NonExistingRandomPath_ReturnsFalse()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var tempDir = Path.Combine(Path.GetTempPath(), "darc_fs_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            // Act
            var exists = fileSystem.FileExists(path);

            // Assert
            exists.Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Ensures that deleting an existing file removes it from disk without throwing.
    /// Inputs:
    ///  - A valid file path to a file that exists.
    /// Expected:
    ///  - No exception is thrown.
    ///  - The file no longer exists after invocation.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void DeleteFile_WhenFileExists_FileIsDeleted()
    {
        // Arrange
        using var temp = new TempDirectory();
        var filePath = Path.Combine(temp.DirectoryPath, "to-delete.txt");
        File.WriteAllText(filePath, "content");
        var sut = new FileSystem();

        // Act
        Action act = () => sut.DeleteFile(filePath);
        act.Should().NotThrow();

        // Assert
        File.Exists(filePath).Should().BeFalse();
    }

    /// <summary>
    /// Validates that deleting a file that does not exist does not throw an exception.
    /// Inputs:
    ///  - A valid file path that does not exist (within an existing directory).
    /// Expected:
    ///  - No exception is thrown.
    ///  - The file still does not exist after invocation.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void DeleteFile_WhenFileDoesNotExist_NoExceptionAndStillMissing()
    {
        // Arrange
        using var temp = new TempDirectory();
        var filePath = Path.Combine(temp.DirectoryPath, "missing.txt");
        var sut = new FileSystem();

        // Act
        Action act = () => sut.DeleteFile(filePath);

        // Assert
        act.Should().NotThrow();
        File.Exists(filePath).Should().BeFalse();
    }

    /// <summary>
    /// Confirms that providing an empty path results in an ArgumentException from the underlying System.IO API.
    /// Inputs:
    ///  - An empty string path ("").
    /// Expected:
    ///  - ArgumentException is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void DeleteFile_WhenPathIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        var sut = new FileSystem();
        var emptyPath = string.Empty;

        // Act
        Action act = () => sut.DeleteFile(emptyPath);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    private sealed class TempDirectory : IDisposable
    {
        public string DirectoryPath { get; }

        public TempDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "darc-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    /// <summary>
    /// Ensures that an empty directory is deleted when recursive is false.
    /// Inputs:
    ///  - path: An existing empty directory.
    ///  - recursive: false.
    /// Expected:
    ///  - No exception is thrown and the directory no longer exists after deletion.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DeleteDirectory_EmptyDirectory_RemovedWithoutException()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var tempRoot = CreateUniqueTempDirectory();

        try
        {
            // Act
            Action act = () => fileSystem.DeleteDirectory(tempRoot, recursive: false);
            act.Should().NotThrow();

            // Assert
            Directory.Exists(tempRoot).Should().BeFalse();
        }
        finally
        {
            // Cleanup in case of unexpected failure
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    /// <summary>
    /// Verifies that deleting a non-empty directory with recursive=false throws IOException and does not remove the directory.
    /// Inputs:
    ///  - path: An existing directory containing a file and a subdirectory.
    ///  - recursive: false.
    /// Expected:
    ///  - IOException is thrown and the directory still exists after the call.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DeleteDirectory_NonEmptyDirectory_RecursiveFalse_ThrowsIOException()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var parent = CreateUniqueTempDirectory();
        var sub = Path.Combine(parent, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(parent, "file.txt"), "content");

        try
        {
            // Act
            Action act = () => fileSystem.DeleteDirectory(parent, recursive: false);

            // Assert
            act.Should().Throw<IOException>();
            Directory.Exists(parent).Should().BeTrue();
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, true);
            }
        }
    }

    /// <summary>
    /// Ensures that deleting a non-empty directory with recursive=true removes the directory and all its contents.
    /// Inputs:
    ///  - path: An existing directory containing nested files and subdirectories.
    ///  - recursive: true.
    /// Expected:
    ///  - No exception is thrown and the directory no longer exists after deletion.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DeleteDirectory_NonEmptyDirectory_RecursiveTrue_RemovesDirectory()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var parent = CreateUniqueTempDirectory();
        var sub1 = Path.Combine(parent, "sub1");
        var sub2 = Path.Combine(sub1, "sub2");
        Directory.CreateDirectory(sub2);
        File.WriteAllText(Path.Combine(parent, "a.txt"), "a");
        File.WriteAllText(Path.Combine(sub1, "b.txt"), "b");
        File.WriteAllText(Path.Combine(sub2, "c.txt"), "c");

        // Act
        Action act = () => fileSystem.DeleteDirectory(parent, recursive: true);

        // Assert
        act.Should().NotThrow();
        Directory.Exists(parent).Should().BeFalse();
    }

    /// <summary>
    /// Validates that attempting to delete a non-existent directory throws DirectoryNotFoundException for both recursive values.
    /// Inputs:
    ///  - path: A unique path that does not exist.
    ///  - recursive: parameterized (true/false).
    /// Expected:
    ///  - DirectoryNotFoundException is thrown.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DeleteDirectory_PathDoesNotExist_ThrowsDirectoryNotFoundException(bool recursive)
    {
        // Arrange
        var fileSystem = new FileSystem();
        var nonExistent = Path.Combine(Path.GetTempPath(), "darc-tests", Guid.NewGuid().ToString("N"));

        // Act
        Action act = () => fileSystem.DeleteDirectory(nonExistent, recursive);

        // Assert
        act.Should().Throw<DirectoryNotFoundException>();
    }

    /// <summary>
    /// Validates that an empty path is rejected and throws ArgumentException for both recursive values.
    /// Inputs:
    ///  - path: "" (empty string).
    ///  - recursive: parameterized (true/false).
    /// Expected:
    ///  - ArgumentException is thrown.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DeleteDirectory_EmptyPath_ThrowsArgumentException(bool recursive)
    {
        // Arrange
        var fileSystem = new FileSystem();
        var emptyPath = string.Empty;

        // Act
        Action act = () => fileSystem.DeleteDirectory(emptyPath, recursive);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    private static string CreateUniqueTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "darc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// Validates that GetDirectoryName returns the same result as System.IO.Path.GetDirectoryName
    /// for a variety of representative, non-null path inputs, including relative, absolute,
    /// mixed separators, roots, UNC-like, trailing separators, and long paths.
    /// Inputs:
    ///  - Non-null strings with diverse path patterns.
    /// Expected:
    ///  - The result exactly equals Path.GetDirectoryName(input) for each input.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(GetDirectoryName_ValidInputCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetDirectoryName_VariousPaths_EqualsBclBehavior(string path)
    {
        // Arrange
        var fileSystem = new FileSystem();

        // Act
        var actual = fileSystem.GetDirectoryName(path);
        var expected = Path.GetDirectoryName(path);

        // Assert
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Ensures null input is handled identically to the BCL behavior.
    /// Inputs:
    ///  - path = null.
    /// Expected:
    ///  - Returns the exact same value as Path.GetDirectoryName(null) (likely null).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetDirectoryName_NullPath_EqualsBclBehavior()
    {
        // Arrange
        var fileSystem = new FileSystem();

        // Act
        var actual = fileSystem.GetDirectoryName(null);
        var expected = Path.GetDirectoryName(null);

        // Assert
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Verifies inputs containing invalid characters are treated the same as the BCL,
    /// including exception type/message if an exception is thrown, or the same result otherwise.
    /// Inputs:
    ///  - A path containing the null character ('\0'), which is invalid across platforms.
    /// Expected:
    ///  - If Path.GetDirectoryName throws, FileSystem.GetDirectoryName throws the same exception type and message.
    ///  - Otherwise, both return equal results.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetDirectoryName_InvalidCharacters_MatchesBclExceptionOrResult()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var invalid = "dir" + '\0' + "file";

        Exception bclEx = null;
        string bclResult = null;
        Exception wrapperEx = null;
        string wrapperResult = null;

        // Act
        try
        {
            bclResult = Path.GetDirectoryName(invalid);
        }
        catch (Exception ex)
        {
            bclEx = ex;
        }

        try
        {
            wrapperResult = fileSystem.GetDirectoryName(invalid);
        }
        catch (Exception ex)
        {
            wrapperEx = ex;
        }

        // Assert
        if (bclEx != null)
        {
            wrapperEx.Should().NotBeNull();
            wrapperEx.GetType().Should().Be(bclEx.GetType());
            wrapperEx.Message.Should().Be(bclEx.Message);
        }
        else
        {
            wrapperEx.Should().BeNull();
            wrapperResult.Should().Be(bclResult);
        }
    }

    // Test case source: diverse, non-null path inputs.
    public static IEnumerable GetDirectoryName_ValidInputCases()
    {
        yield return "";
        yield return " ";
        yield return "file.txt";
        yield return "dir/file.txt";
        yield return "dir\\file.txt";
        yield return "/rooted/file.txt";
        yield return "/";
        yield return "\\";
        yield return "C:\\dir\\file.txt";
        yield return "C:\\";
        yield return "C:";
        yield return "\\\\server\\share\\dir\\file.txt";
        yield return "\\\\server\\share\\";
        yield return "dir/";
        yield return "dir\\";
        yield return "dir.with.dot\\file";
        yield return "dir...\\file";
        yield return new string('a', 5) + "/" + new string('b', 260) + ".txt";
        yield return ".hiddenfile";
        yield return " folder / sub folder / name.ext ";
    }

    /// <summary>
    /// Verifies that when the target directory exists but contains no files,
    /// GetFiles returns an empty array.
    /// Inputs:
    ///  - path: an existing empty directory.
    /// Expected:
    ///  - An empty array is returned; no exception is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFiles_EmptyDirectory_ReturnsEmptyArray()
    {
        // Arrange
        var sut = new FileSystem();
        var tempDir = Path.Combine(Path.GetTempPath(), "fs-tests-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Act
            var result = sut.GetFiles(tempDir);

            // Assert
            result.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Ensures that file paths returned are full paths to all files in the specified directory (non-recursive),
    /// both when the path has and does not have a trailing directory separator.
    /// Inputs:
    ///  - path: an existing directory containing multiple files and a subdirectory with a file.
    ///  - includeTrailingSeparator: whether to append the directory separator to the path.
    /// Expected:
    ///  - Returned array contains only the files in the specified directory (not subdirectories),
    ///    and each entry is the full path to the file.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFiles_DirectoryWithFiles_ReturnsFullPathsAndAllFiles(bool includeTrailingSeparator)
    {
        // Arrange
        var sut = new FileSystem();
        var tempDir = Path.Combine(Path.GetTempPath(), "fs-tests-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var topFile1 = Path.Combine(tempDir, "a.txt");
        var topFile2 = Path.Combine(tempDir, "b.log");
        File.WriteAllText(topFile1, "alpha");
        File.WriteAllText(topFile2, "beta");

        var subDir = Path.Combine(tempDir, "sub");
        Directory.CreateDirectory(subDir);
        var subFile = Path.Combine(subDir, "c.txt");
        File.WriteAllText(subFile, "gamma");

        var queryPath = includeTrailingSeparator ? tempDir + Path.DirectorySeparatorChar : tempDir;

        try
        {
            // Act
            var result = sut.GetFiles(queryPath);

            // Assert
            result.Should().BeEquivalentTo(new[] { topFile1, topFile2 });
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Validates that providing a path to a non-existent directory results in a DirectoryNotFoundException.
    /// Inputs:
    ///  - path: a random, non-existent directory under the system temp path.
    /// Expected:
    ///  - Throws DirectoryNotFoundException.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFiles_NonExistentDirectory_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var sut = new FileSystem();
        var nonExistent = Path.Combine(Path.GetTempPath(), "fs-tests-missing-" + Guid.NewGuid().ToString("N"));

        // Act
        Action act = () => sut.GetFiles(nonExistent);

        // Assert
        act.Should().Throw<DirectoryNotFoundException>();
    }

    /// <summary>
    /// Validates that providing an empty string path results in an ArgumentException.
    /// Inputs:
    ///  - path: string.Empty.
    /// Expected:
    ///  - Throws ArgumentException.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFiles_EmptyPath_ThrowsArgumentException()
    {
        // Arrange
        var sut = new FileSystem();
        var emptyPath = string.Empty;

        // Act
        Action act = () => sut.GetFiles(emptyPath);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies that providing a file path instead of a directory path results in a DirectoryNotFoundException.
    /// Inputs:
    ///  - path: full path to an existing file.
    /// Expected:
    ///  - Throws DirectoryNotFoundException.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFiles_PathPointsToFile_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var sut = new FileSystem();
        var tempDir = Path.Combine(Path.GetTempPath(), "fs-tests-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "single.txt");
        File.WriteAllText(filePath, "content");

        try
        {
            // Act
            Action act = () => sut.GetFiles(filePath);

            // Assert
            act.Should().Throw<DirectoryNotFoundException>();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that when the specified directory exists and contains subdirectories,
    /// the method returns the full paths of all immediate subdirectories.
    /// Inputs:
    ///  - A valid existing directory path containing two subdirectories.
    /// Expected:
    ///  - An array containing the full paths of the two subdirectories (order not guaranteed).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetDirectories_ExistingDirectoryWithSubdirectories_ReturnsFullPaths()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var parentDir = Path.Combine(Path.GetTempPath(), "fs-getdirectories-" + Guid.NewGuid().ToString("N"));
        var subDir1 = Path.Combine(parentDir, "subA");
        var subDir2 = Path.Combine(parentDir, "subB");

        Directory.CreateDirectory(parentDir);
        Directory.CreateDirectory(subDir1);
        Directory.CreateDirectory(subDir2);

        try
        {
            // Act
            var result = fileSystem.GetDirectories(parentDir);

            // Assert
            result.Should().BeEquivalentTo(new[] { subDir1, subDir2 });
        }
        finally
        {
            // Cleanup
            try { Directory.Delete(parentDir, true); } catch { }
        }
    }

    /// <summary>
    /// Verifies that when the specified directory exists but has no subdirectories,
    /// the method returns an empty array.
    /// Inputs:
    ///  - A valid existing directory path with no subdirectories.
    /// Expected:
    ///  - An empty array.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetDirectories_ExistingDirectoryWithoutSubdirectories_ReturnsEmptyArray()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var parentDir = Path.Combine(Path.GetTempPath(), "fs-getdirectories-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parentDir);

        try
        {
            // Act
            var result = fileSystem.GetDirectories(parentDir);

            // Assert
            result.Should().BeEmpty();
        }
        finally
        {
            // Cleanup
            try { Directory.Delete(parentDir, true); } catch { }
        }
    }

    /// <summary>
    /// Ensures that when the specified directory does not exist,
    /// the method throws DirectoryNotFoundException.
    /// Inputs:
    ///  - A non-existent directory path under the temp folder.
    /// Expected:
    ///  - DirectoryNotFoundException is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetDirectories_NonExistingDirectory_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var nonExisting = Path.Combine(Path.GetTempPath(), "fs-getdirectories-" + Guid.NewGuid().ToString("N"));

        // Act
        Action act = () => fileSystem.GetDirectories(nonExisting);

        // Assert
        act.Should().Throw<DirectoryNotFoundException>();
    }

    /// <summary>
    /// Ensures that when an empty string is supplied as the path,
    /// the method throws ArgumentException.
    /// Inputs:
    ///  - An empty string as the path.
    /// Expected:
    ///  - ArgumentException is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetDirectories_EmptyPath_ThrowsArgumentException()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var emptyPath = string.Empty;

        // Act
        Action act = () => fileSystem.GetDirectories(emptyPath);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Validates that GetFileName returns the correct final path segment (or null/empty) according to System.IO.Path.GetFileName semantics.
    /// Inputs covered via cases:
    ///  - null path: expected null.
    ///  - Empty path: expected empty string.
    ///  - Whitespace-only path: expected same input (no trimming).
    ///  - Paths ending with directory or alternate directory separator: expected empty string.
    ///  - Paths with file name and extension: expected full file name with extension.
    ///  - Nested directory paths: expected last segment only.
    ///  - UNC-like path (leading double separators): expected final segment.
    ///  - File name containing invalid characters: expected segment preserved as-is.
    ///  - Very long file name: expected entire long segment.
    /// Expected:
    ///  - The returned string matches the behavior of Path.GetFileName for the given input.
    /// </summary>
    [TestCaseSource(nameof(GetFileNameTestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFileName_VariousInputs_ReturnsExpected(string path, string expected)
    {
        // Arrange
        var fileSystem = new FileSystem();

        // Act
        var result = fileSystem.GetFileName(path);

        // Assert
        result.Should().Be(expected);
    }

    private static IEnumerable<TestCaseData> GetFileNameTestCases()
    {
        var sep = Path.DirectorySeparatorChar;
        var alt = Path.AltDirectorySeparatorChar;

        yield return new TestCaseData(null, null).SetName("GetFileName_NullPath_ReturnsNull");
        yield return new TestCaseData(string.Empty, string.Empty).SetName("GetFileName_EmptyPath_ReturnsEmptyString");
        yield return new TestCaseData("   ", "   ").SetName("GetFileName_WhitespaceOnlyPath_ReturnsInput");
        yield return new TestCaseData("dir" + sep, string.Empty).SetName("GetFileName_PathEndsWithDirectorySeparator_ReturnsEmptyString");
        yield return new TestCaseData("dir" + alt, string.Empty).SetName("GetFileName_PathEndsWithAltDirectorySeparator_ReturnsEmptyString");
        yield return new TestCaseData("dir" + sep + "file.txt", "file.txt").SetName("GetFileName_FileWithExtension_ReturnsFileNameWithExtension");
        yield return new TestCaseData("dir1" + sep + "dir2" + sep + "file", "file").SetName("GetFileName_MultipleDirectories_ReturnsLastSegment");
        yield return new TestCaseData(new string(sep, 2) + "server" + sep + "share" + sep + "file.txt", "file.txt").SetName("GetFileName_UncLikePath_ReturnsFileName");
        yield return new TestCaseData("dir" + sep + "fi<le>.txt", "fi<le>.txt").SetName("GetFileName_InvalidCharactersInFileName_ReturnsSegmentAsIs");

        var longName = new string('a', 260) + ".txt";
        yield return new TestCaseData("dir" + sep + longName, longName).SetName("GetFileName_VeryLongFileName_ReturnsLongSegment");
    }

    /// <summary>
    /// Validates that GetTempFileName creates a zero-byte file in the system temporary directory
    /// and returns a rooted, existing path.
    /// Inputs:
    ///  - No inputs.
    /// Expected:
    ///  - Returned path is non-empty, rooted, starts with Path.GetTempPath().
    ///  - File exists and has length 0.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetTempFileName_CreatesZeroByteFileAndReturnsExistingPath()
    {
        // Arrange
        var sut = new FileSystem();
        var tempDir = Path.GetTempPath();
        string createdPath = null;

        try
        {
            // Act
            createdPath = sut.GetTempFileName();

            // Assert
            createdPath.Should().NotBeNullOrWhiteSpace();
            Path.IsPathRooted(createdPath).Should().BeTrue();
            createdPath.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
            File.Exists(createdPath).Should().BeTrue();

            var info = new FileInfo(createdPath);
            info.Length.Should().Be(0);
        }
        finally
        {
            TryDelete(createdPath);
        }
    }

    /// <summary>
    /// Ensures that multiple invocations of GetTempFileName return unique file paths,
    /// and that each file exists on disk.
    /// Inputs:
    ///  - Invocation counts: 1, 3, 8 (parameterized).
    /// Expected:
    ///  - The number of distinct paths equals the invocation count.
    ///  - All created files exist.
    /// </summary>
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(8)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetTempFileName_MultipleInvocations_ReturnUniqueExistingFiles(int invocationCount)
    {
        // Arrange
        var sut = new FileSystem();
        var paths = new string[invocationCount];

        try
        {
            // Act
            for (int i = 0; i < invocationCount; i++)
            {
                paths[i] = sut.GetTempFileName();
            }

            // Assert
            var distinctCount = paths.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            distinctCount.Should().Be(invocationCount);

            foreach (var path in paths)
            {
                File.Exists(path).Should().BeTrue();
            }
        }
        finally
        {
            TryDelete(paths);
        }
    }

    /// <summary>
    /// Verifies that concurrent invocations of GetTempFileName return unique paths
    /// without collisions, and that each created file exists.
    /// Inputs:
    ///  - 12 concurrent calls.
    /// Expected:
    ///  - All returned paths are unique and the files exist on disk.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetTempFileName_ConcurrentInvocations_ReturnUniquePaths()
    {
        // Arrange
        var sut = new FileSystem();
        const int callCount = 12;
        var paths = new string[callCount];

        try
        {
            // Act
            var tasks = Enumerable.Range(0, callCount)
                .Select(i => Task.Run(() => paths[i] = sut.GetTempFileName()))
                .ToArray();

            await Task.WhenAll(tasks);

            // Assert
            var distinctCount = paths.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            distinctCount.Should().Be(callCount);

            foreach (var path in paths)
            {
                File.Exists(path).Should().BeTrue();
            }
        }
        finally
        {
            TryDelete(paths);
        }
    }

    private static void TryDelete(params string[] paths)
    {
        if (paths == null) return;
        foreach (var p in paths)
        {
            TryDelete(p);
        }
    }

    private static void TryDelete(string path)
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
            // Swallow cleanup exceptions to avoid masking test results.
        }
    }

    /// <summary>
    /// Validates that PathCombine correctly handles various combinations including:
    ///  - Empty strings (returns the other path),
    ///  - Whitespace-only strings (treated as a valid segment and combined),
    ///  - Trailing separators on the first path (no duplicate separators in result),
    ///  - Unicode/special characters (combined as-is),
    ///  - Rooted second path starting with the directory separator (returns the second path).
    /// Tokens:
    ///  - "{sep}" in inputs is replaced with the OS-specific directory separator.
    ///  - "|" in the expected template is replaced with the OS-specific directory separator.
    /// Inputs/Expected:
    ///  - Provided via TestCase attributes; see each case for specifics.
    /// Expected:
    ///  - The combined path string as per System.IO.Path.Combine semantics.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("", "b", "b", TestName = "PathCombine_EmptyFirst_ReturnsSecond")]
    [TestCase("a", "", "a", TestName = "PathCombine_EmptySecond_ReturnsFirst")]
    [TestCase("", "", "", TestName = "PathCombine_BothEmpty_ReturnsEmpty")]
    [TestCase("a", "b", "a|b", TestName = "PathCombine_RelativeSegments_CombinedWithSeparator")]
    [TestCase(" ", "b", " |b", TestName = "PathCombine_WhitespaceFirst_TreatedAsSegment")]
    [TestCase("a{sep}", "b", "a|b", TestName = "PathCombine_FirstEndsWithSeparator_NoDuplicateSeparator")]
    [TestCase("földér", "子", "földér|子", TestName = "PathCombine_UnicodeSegments_Combined")]
    [TestCase("a", "{sep}b", "{sep}b", TestName = "PathCombine_SecondStartsWithSeparator_ReturnsSecond")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void PathCombine_VariousInputs_ExpectedCombination(string path1Token, string path2Token, string expectedTemplate)
    {
        // Arrange
        var fileSystem = new FileSystem();
        var path1 = ReplaceTokens(path1Token);
        var path2 = ReplaceTokens(path2Token);
        var expected = ReplaceExpectedTemplate(expectedTemplate);

        // Act
        var result = fileSystem.PathCombine(path1, path2);

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Ensures that when the second path is an absolute path (rooted with the OS-specific root),
    /// PathCombine returns the second path, ignoring the first.
    /// Inputs:
    ///  - First: "prefix" (relative).
    ///  - Second: Absolute path constructed from the root plus "dest".
    /// Expected:
    ///  - The absolute second path unchanged.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void PathCombine_SecondIsAbsolutePath_ReturnsSecond()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var first = "prefix";
        var root = Path.GetPathRoot(Directory.GetCurrentDirectory());
        var absoluteSecond = Path.Combine(root, "dest");

        // Act
        var result = fileSystem.PathCombine(first, absoluteSecond);

        // Assert
        result.Should().Be(absoluteSecond);
    }

    private static string ReplaceTokens(string token)
    {
        if (token == null)
        {
            return string.Empty;
        }

        var sep = Path.DirectorySeparatorChar.ToString();
        return token.Replace("{sep}", sep);
    }

    private static string ReplaceExpectedTemplate(string template)
    {
        if (template == null)
        {
            return string.Empty;
        }

        var sep = Path.DirectorySeparatorChar.ToString();
        return template.Replace("|", sep).Replace("{sep}", sep);
    }

    /// <summary>
    /// Verifies that when the provided path contains no directory information (e.g., just a filename),
    /// the method throws DirectoryNotFoundException with the expected message.
    /// Inputs:
    ///  - path: A filename without any directory component.
    ///  - content: Any non-null string.
    /// Expected:
    ///  - Throws DirectoryNotFoundException with message "Invalid path {path}".
    /// </summary>
    [Test]
    [TestCase("file.txt")]
    [TestCase("output.log")]
    [TestCase("readme")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void WriteToFile_PathWithoutDirectory_ThrowsDirectoryNotFoundException(string path)
    {
        // Arrange
        var fileSystem = new FileSystem();
        var content = "any content";

        // Act
        Action act = () => fileSystem.WriteToFile(path, content);

        // Assert
        act.Should().Throw<DirectoryNotFoundException>()
           .WithMessage("Invalid path " + path);
    }

    /// <summary>
    /// Ensures that given a valid path containing directory information, the method creates the directory (including parents if needed)
    /// and writes the provided content to the file, overwriting any existing file content.
    /// Inputs:
    ///  - path: A path under a temporary, unique directory.
    ///  - content: Different content variations, including empty and unicode characters.
    /// Expected:
    ///  - The directory is created.
    ///  - The file exists with exact content written.
    /// </summary>
    [Test]
    [TestCase("")]
    [TestCase("Hello, world!")]
    [TestCase("αß漢字\nline2")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void WriteToFile_ValidPath_CreatesDirectoryAndWritesContent(string content)
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "darc-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "sub", "file.txt");
        var directory = Path.GetDirectoryName(path);
        var fileSystem = new FileSystem();

        try
        {
            // Act
            fileSystem.WriteToFile(path, content);

            // Assert
            Directory.Exists(directory).Should().BeTrue();
            File.Exists(path).Should().BeTrue();
            var actual = File.ReadAllText(path);
            actual.Should().Be(content);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); } catch { /* best-effort cleanup */ }
            }
        }
    }

    /// <summary>
    /// Validates that when the target file already exists, the method overwrites it with the new content.
    /// Inputs:
    ///  - path: A valid path with existing file.
    ///  - initialContent: The original file content.
    ///  - newContent: The content to overwrite with.
    /// Expected:
    ///  - After invocation, the file content equals newContent.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void WriteToFile_ExistingFile_OverwritesContent()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "darc-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "sub", "overwrite.txt");
        var directory = Path.GetDirectoryName(path);
        Directory.CreateDirectory(directory);
        var initialContent = "old content";
        File.WriteAllText(path, initialContent);
        var newContent = "new content";
        var fileSystem = new FileSystem();

        try
        {
            // Act
            fileSystem.WriteToFile(path, newContent);

            // Assert
            var actual = File.ReadAllText(path);
            actual.Should().Be(newContent);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); } catch { /* best-effort cleanup */ }
            }
        }
    }

    /// <summary>
    /// Ensures that when the source file exists and the destination file does not exist,
    /// CopyFile creates the destination file with the exact contents of the source.
    /// Inputs:
    ///  - Existing source file path with content.
    ///  - Non-existing destination file path.
    /// Expected:
    ///  - Destination file is created and its content equals the source file content.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CopyFile_SourceExistsAndDestinationDoesNotExist_CopiesFileContent()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var sourcePath = Path.Combine(testDir, "source.txt");
            var destPath = Path.Combine(testDir, "dest.txt");
            var content = "hello world " + Guid.NewGuid();

            File.WriteAllText(sourcePath, content);

            // Act
            fileSystem.CopyFile(sourcePath, destPath);

            // Assert
            File.Exists(destPath).Should().BeTrue();
            File.ReadAllText(destPath).Should().Be(content);
            File.Exists(sourcePath).Should().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(testDir);
        }
    }

    /// <summary>
    /// Verifies that when the destination file already exists and overwrite is not specified (default false),
    /// CopyFile throws an IOException and does not modify the destination content.
    /// Inputs:
    ///  - Existing source file path.
    ///  - Existing destination file path.
    ///  - overwrite not provided (defaults to false).
    /// Expected:
    ///  - IOException is thrown.
    ///  - Destination file content remains unchanged.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CopyFile_DestinationExistsWithoutOverwrite_ThrowsIOException()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var sourcePath = Path.Combine(testDir, "source.txt");
            var destPath = Path.Combine(testDir, "dest.txt");
            var sourceContent = "src-" + Guid.NewGuid();
            var originalDestContent = "dest-" + Guid.NewGuid();

            File.WriteAllText(sourcePath, sourceContent);
            File.WriteAllText(destPath, originalDestContent);

            // Act
            Action act = () => fileSystem.CopyFile(sourcePath, destPath);

            // Assert
            act.Should().Throw<IOException>();
            File.ReadAllText(destPath).Should().Be(originalDestContent);
        }
        finally
        {
            TryDeleteDirectory(testDir);
        }
    }

    /// <summary>
    /// Ensures that when the destination file already exists and overwrite is true,
    /// CopyFile overwrites the destination file with the source file content.
    /// Inputs:
    ///  - Existing source file path with specific content.
    ///  - Existing destination file path with different content.
    ///  - overwrite = true.
    /// Expected:
    ///  - Destination file content equals the source file content after the operation.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CopyFile_DestinationExistsWithOverwriteTrue_OverwritesDestinationContent()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var sourcePath = Path.Combine(testDir, "source.txt");
            var destPath = Path.Combine(testDir, "dest.txt");
            var sourceContent = "updated-content-" + Guid.NewGuid();

            File.WriteAllText(sourcePath, sourceContent);
            File.WriteAllText(destPath, "old-content");

            // Act
            fileSystem.CopyFile(sourcePath, destPath, overwrite: true);

            // Assert
            File.ReadAllText(destPath).Should().Be(sourceContent);
        }
        finally
        {
            TryDeleteDirectory(testDir);
        }
    }

    /// <summary>
    /// Validates that providing a non-existent source file path results in FileNotFoundException.
    /// Inputs:
    ///  - Non-existent source file path.
    ///  - Valid destination file path.
    /// Expected:
    ///  - FileNotFoundException is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CopyFile_SourceDoesNotExist_ThrowsFileNotFoundException()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var sourcePath = Path.Combine(testDir, "missing.txt");
            var destPath = Path.Combine(testDir, "dest.txt");

            // Act
            Action act = () => fileSystem.CopyFile(sourcePath, destPath);

            // Assert
            act.Should().Throw<FileNotFoundException>();
        }
        finally
        {
            TryDeleteDirectory(testDir);
        }
    }

    /// <summary>
    /// Ensures that invalid source paths (empty or whitespace-only) cause an ArgumentException.
    /// Inputs:
    ///  - Invalid source path: empty or whitespace-only.
    ///  - Valid destination path.
    /// Expected:
    ///  - ArgumentException is thrown.
    /// </summary>
    [TestCase("")]
    [TestCase(" ")]
    [TestCase(" \t ")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CopyFile_SourcePathInvalid_ThrowsArgumentException(string invalidSourcePath)
    {
        // Arrange
        var fileSystem = new FileSystem();
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var destPath = Path.Combine(testDir, "dest.txt");

            // Act
            Action act = () => fileSystem.CopyFile(invalidSourcePath, destPath);

            // Assert
            act.Should().Throw<ArgumentException>();
        }
        finally
        {
            TryDeleteDirectory(testDir);
        }
    }

    /// <summary>
    /// Ensures that invalid destination paths (empty or whitespace-only) cause an ArgumentException.
    /// Inputs:
    ///  - Valid source path.
    ///  - Invalid destination path: empty or whitespace-only.
    /// Expected:
    ///  - ArgumentException is thrown.
    /// </summary>
    [TestCase("")]
    [TestCase(" ")]
    [TestCase(" \t ")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CopyFile_DestinationPathInvalid_ThrowsArgumentException(string invalidDestinationPath)
    {
        // Arrange
        var fileSystem = new FileSystem();
        var testDir = CreateUniqueTestDirectory();
        try
        {
            var sourcePath = Path.Combine(testDir, "source.txt");
            File.WriteAllText(sourcePath, "content");

            // Act
            Action act = () => fileSystem.CopyFile(sourcePath, invalidDestinationPath);

            // Assert
            act.Should().Throw<ArgumentException>();
        }
        finally
        {
            TryDeleteDirectory(testDir);
        }
    }

    private static string CreateUniqueTestDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FileSystemTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    /// <summary>
    /// Verifies that GetFileStream with FileMode.Create and FileAccess.ReadWrite:
    /// - returns a non-null stream
    /// - allows both reading and writing
    /// - creates the file on disk.
    /// Inputs:
    ///  - A valid, non-existing file path in a temp directory.
    ///  - mode: Create, access: ReadWrite.
    /// Expected:
    ///  - File is created; stream is readable and writable; contents round-trip successfully.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFileStream_Create_ReadWrite_AllowsReadAndWriteAndCreatesFile()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var tempDir = Path.Combine(Path.GetTempPath(), "FileSystemTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "new.txt");

        try
        {
            // Act
            using (var stream = fileSystem.GetFileStream(filePath, FileMode.Create, FileAccess.ReadWrite))
            {
                // Assert
                stream.Should().NotBeNull();
                stream.CanRead.Should().BeTrue();
                stream.CanWrite.Should().BeTrue();

                var payload = new byte[] { 1, 2, 3, 4 };
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
                stream.Position = 0;

                var buffer = new byte[payload.Length];
                var read = stream.Read(buffer, 0, buffer.Length);

                read.Should().Be(payload.Length);
                buffer.Should().BeEquivalentTo(payload);
            }

            File.Exists(filePath).Should().BeTrue();
            new FileInfo(filePath).Length.Should().Be(4);
        }
        finally
        {
            try { if (File.Exists(filePath)) File.Delete(filePath); } catch { /* ignore cleanup errors */ }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { /* ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Ensures that opening a non-existing file with FileMode.Open throws FileNotFoundException.
    /// Inputs:
    ///  - A valid path that does not exist.
    ///  - mode: Open, access: Read.
    /// Expected:
    ///  - FileNotFoundException is thrown; no file is created as a side effect.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFileStream_OpenNonExisting_ThrowsFileNotFoundException()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var tempDir = Path.Combine(Path.GetTempPath(), "FileSystemTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "does-not-exist.txt");

        Exception captured = null;

        try
        {
            // Act
            try
            {
                using (fileSystem.GetFileStream(filePath, FileMode.Open, FileAccess.Read)) { }
            }
            catch (Exception ex)
            {
                captured = ex;
            }

            // Assert
            captured.Should().NotBeNull();
            captured.Should().BeOfType<FileNotFoundException>();
            File.Exists(filePath).Should().BeFalse();
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { /* ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Validates that an empty path results in an ArgumentException from the underlying FileStream constructor.
    /// Inputs:
    ///  - path: empty string.
    ///  - mode: Open, access: Read.
    /// Expected:
    ///  - ArgumentException is thrown due to invalid path.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFileStream_EmptyPath_ThrowsArgumentException()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var invalidPath = string.Empty;

        Exception captured = null;

        // Act
        try
        {
            using (fileSystem.GetFileStream(invalidPath, FileMode.Open, FileAccess.Read)) { }
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        // Assert
        captured.Should().NotBeNull();
        captured.Should().BeOfType<ArgumentException>();
    }

    /// <summary>
    /// Ensures that invalid enum values for mode or access cause an ArgumentOutOfRangeException.
    /// Inputs:
    ///  - A valid temp file path and an invalid mode OR invalid access.
    /// Expected:
    ///  - ArgumentOutOfRangeException is thrown before any file operation occurs.
    /// </summary>
    [TestCase(999, (int)FileAccess.Read, typeof(ArgumentOutOfRangeException))]   // invalid FileMode
    [TestCase((int)FileMode.Open, 999, typeof(ArgumentOutOfRangeException))]     // invalid FileAccess
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFileStream_InvalidEnumValues_ThrowsArgumentOutOfRangeException(int modeValue, int accessValue, Type expectedExceptionType)
    {
        // Arrange
        var fileSystem = new FileSystem();
        var tempDir = Path.Combine(Path.GetTempPath(), "FileSystemTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "enum-check.txt");

        Exception captured = null;

        try
        {
            // Act
            try
            {
                using (fileSystem.GetFileStream(filePath, (FileMode)modeValue, (FileAccess)accessValue)) { }
            }
            catch (Exception ex)
            {
                captured = ex;
            }

            // Assert
            captured.Should().NotBeNull();
            captured.Should().BeOfType(expectedExceptionType);
        }
        finally
        {
            try { if (File.Exists(filePath)) File.Delete(filePath); } catch { /* ignore cleanup errors */ }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { /* ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Verifies that a read-only stream does not allow writing by checking CanWrite is false.
    /// Inputs:
    ///  - An existing file path.
    ///  - mode: Open, access: Read.
    /// Expected:
    ///  - Returned stream has CanWrite == false and CanRead == true.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFileStream_OpenReadOnly_ReturnsNonWritableStream()
    {
        // Arrange
        var fileSystem = new FileSystem();
        var tempDir = Path.Combine(Path.GetTempPath(), "FileSystemTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "readonly.txt");
        File.WriteAllText(filePath, "content");

        try
        {
            // Act
            using (var stream = fileSystem.GetFileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                // Assert
                stream.Should().NotBeNull();
                stream.CanRead.Should().BeTrue();
                stream.CanWrite.Should().BeFalse();
            }
        }
        finally
        {
            try { if (File.Exists(filePath)) File.Delete(filePath); } catch { /* ignore cleanup errors */ }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { /* ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Verifies that a valid, unique temp path returns a non-null FileInfoWrapper instance and no exception is thrown.
    /// Inputs:
    ///  - A unique path under the system temp directory that does not exist.
    /// Expected:
    ///  - GetFileInfo returns an instance of FileInfoWrapper implementing IFileInfo.
    ///  - The returned instance reports Exists == false.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFileInfo_ValidUniqueTempPath_ReturnsWrapper()
    {
        // Arrange
        var sut = new FileSystem();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tmp");

        // Act
        var info = sut.GetFileInfo(path);

        // Assert
        info.Should().NotBeNull();
        info.Should().BeOfType<FileInfoWrapper>();
        info.Exists.Should().BeFalse();
    }

    /// <summary>
    /// Ensures that empty or whitespace-only paths are rejected.
    /// Inputs:
    ///  - path = "" (empty), " " (space), " \t " (whitespace).
    /// Expected:
    ///  - GetFileInfo throws ArgumentException due to invalid path.
    /// </summary>
    [TestCase("")]
    [TestCase(" ")]
    [TestCase(" \t ")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFileInfo_EmptyOrWhitespace_ThrowsArgumentException(string invalidPath)
    {
        // Arrange
        var sut = new FileSystem();

        // Act
        Action act = () => sut.GetFileInfo(invalidPath);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Validates that control characters (e.g., null char) in the path cause an exception.
    /// Inputs:
    ///  - path containing '\0' (null character).
    /// Expected:
    ///  - GetFileInfo throws ArgumentException indicating illegal characters in path.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFileInfo_PathWithNullChar_ThrowsArgumentException()
    {
        // Arrange
        var sut = new FileSystem();
        var invalidPath = "abc\0def.txt";

        // Act
        Action act = () => sut.GetFileInfo(invalidPath);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Confirms that Unicode and emoji characters in the file name are accepted.
    /// Inputs:
    ///  - A path containing non-ASCII characters and emoji.
    /// Expected:
    ///  - GetFileInfo returns a non-null FileInfoWrapper without throwing, and Exists == false.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFileInfo_UnicodePath_ReturnsWrapper()
    {
        // Arrange
        var sut = new FileSystem();
        var fileName = $"文件_📄_{Guid.NewGuid():N}.txt";
        var path = Path.Combine(Path.GetTempPath(), fileName);

        // Act
        var info = sut.GetFileInfo(path);

        // Assert
        info.Should().NotBeNull();
        info.Should().BeOfType<FileInfoWrapper>();
        info.Exists.Should().BeFalse();
    }

    /// <summary>
    /// Evaluates behavior with a very long path. Depending on platform and runtime settings,
    /// this may either succeed (returning a wrapper) or throw PathTooLongException.
    /// Inputs:
    ///  - A path longer than typical limits.
    /// Expected:
    ///  - Either a FileInfoWrapper instance is returned, or PathTooLongException is thrown.
    /// Notes:
    ///  - This test is tolerant to platform-specific differences and asserts either valid outcome.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetFileInfo_VeryLongPath_ReturnsWrapperOrThrowsPathTooLong()
    {
        // Arrange
        var sut = new FileSystem();
        var longName = new string('a', 600) + ".txt";
        var path = Path.Combine(Path.GetTempPath(), longName);

        // Act
        IFileInfo result = null;
        Exception error = null;
        try
        {
            result = sut.GetFileInfo(path);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        // Assert
        if (error != null)
        {
            error.Should().BeOfType<PathTooLongException>();
        }
        else
        {
            result.Should().NotBeNull();
            result.Should().BeOfType<FileInfoWrapper>();
        }
    }

    /// <summary>
    /// Ensures that when the source directory does not exist, the method throws DirectoryNotFoundException with the expected message,
    /// and the destination directory is not created.
    /// Inputs:
    ///  - sourceDir: a non-existing absolute path under the temp directory.
    ///  - destinationDir: a unique path.
    /// Expected:
    ///  - Throws DirectoryNotFoundException with message "Source directory not found: {fullPath}".
    ///  - Destination directory does not get created.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CopyDirectory_SourceDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var fs = new FileSystem();
        var sourceDir = Path.Combine(Path.GetTempPath(), "darc-tests", Guid.NewGuid().ToString("N"));
        var destinationDir = Path.Combine(Path.GetTempPath(), "darc-tests", Guid.NewGuid().ToString("N"));
        var expectedMessage = $"Source directory not found: {new DirectoryInfo(sourceDir).FullName}";

        // Act
        Action act = () => fs.CopyDirectory(sourceDir, destinationDir, recursive: true);

        // Assert
        act.Should().Throw<DirectoryNotFoundException>().WithMessage(expectedMessage);
        Directory.Exists(destinationDir).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that when copying non-recursively, only files at the top level are copied and subdirectories are not created.
    /// Inputs:
    ///  - sourceDir: contains two top-level files and one subdirectory with a file.
    ///  - destinationDir: does not exist prior to the call.
    ///  - recursive: false.
    /// Expected:
    ///  - Destination directory is created.
    ///  - Only top-level files are copied.
    ///  - No subdirectories exist under the destination directory.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CopyDirectory_NonRecursive_CopiesOnlyTopLevelFiles()
    {
        // Arrange
        var fs = new FileSystem();
        var root = CreateTempRoot();
        var sourceDir = Path.Combine(root, "source");
        var destinationDir = Path.Combine(root, "dest");
        Directory.CreateDirectory(sourceDir);

        var file1 = Path.Combine(sourceDir, "a.txt");
        var file2 = Path.Combine(sourceDir, "b.txt");
        File.WriteAllText(file1, "content-a");
        File.WriteAllText(file2, "content-b");

        var subDir = Path.Combine(sourceDir, "sub");
        Directory.CreateDirectory(subDir);
        var subFile = Path.Combine(subDir, "c.txt");
        File.WriteAllText(subFile, "content-c");

        try
        {
            // Act
            fs.CopyDirectory(sourceDir, destinationDir, recursive: false);

            // Assert
            Directory.Exists(destinationDir).Should().BeTrue();
            var destFiles = Directory.GetFiles(destinationDir).Select(Path.GetFileName).ToArray();
            destFiles.Should().BeEquivalentTo(new[] { "a.txt", "b.txt" });
            Directory.GetDirectories(destinationDir).Length.Should().Be(0);

            File.ReadAllText(Path.Combine(destinationDir, "a.txt")).Should().Be("content-a");
            File.ReadAllText(Path.Combine(destinationDir, "b.txt")).Should().Be("content-b");
            Directory.Exists(Path.Combine(destinationDir, "sub")).Should().BeFalse();
        }
        finally
        {
            SafeDelete(root);
        }
    }

    /// <summary>
    /// Ensures that when copying recursively, all files and subdirectories are copied.
    /// Inputs:
    ///  - sourceDir: contains top-level files and nested subdirectories with files.
    ///  - destinationDir: does not exist prior to the call.
    ///  - recursive: true.
    /// Expected:
    ///  - Entire directory tree is reproduced under destination.
    ///  - All files exist with original content.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CopyDirectory_Recursive_CopiesAllFilesAndSubdirectories()
    {
        // Arrange
        var fs = new FileSystem();
        var root = CreateTempRoot();
        var sourceDir = Path.Combine(root, "source");
        var destinationDir = Path.Combine(root, "dest");
        Directory.CreateDirectory(sourceDir);

        // Top-level files
        var f1 = Path.Combine(sourceDir, "root1.txt");
        var f2 = Path.Combine(sourceDir, "root2.txt");
        File.WriteAllText(f1, "root-1");
        File.WriteAllText(f2, "root-2");

        // Subdirectories and files
        var sub1 = Path.Combine(sourceDir, "sub1");
        var sub2 = Path.Combine(sub1, "sub2");
        Directory.CreateDirectory(sub1);
        Directory.CreateDirectory(sub2);

        var f3 = Path.Combine(sub1, "s1.txt");
        var f4 = Path.Combine(sub2, "s2.txt");
        File.WriteAllText(f3, "sub1-file");
        File.WriteAllText(f4, "sub2-file");

        try
        {
            // Act
            fs.CopyDirectory(sourceDir, destinationDir, recursive: true);

            // Assert
            Directory.Exists(destinationDir).Should().BeTrue();

            // Top-level
            File.Exists(Path.Combine(destinationDir, "root1.txt")).Should().BeTrue();
            File.Exists(Path.Combine(destinationDir, "root2.txt")).Should().BeTrue();
            File.ReadAllText(Path.Combine(destinationDir, "root1.txt")).Should().Be("root-1");
            File.ReadAllText(Path.Combine(destinationDir, "root2.txt")).Should().Be("root-2");

            // Subdirectories
            var destSub1 = Path.Combine(destinationDir, "sub1");
            var destSub2 = Path.Combine(destSub1, "sub2");

            Directory.Exists(destSub1).Should().BeTrue();
            Directory.Exists(destSub2).Should().BeTrue();

            File.Exists(Path.Combine(destSub1, "s1.txt")).Should().BeTrue();
            File.Exists(Path.Combine(destSub2, "s2.txt")).Should().BeTrue();

            File.ReadAllText(Path.Combine(destSub1, "s1.txt")).Should().Be("sub1-file");
            File.ReadAllText(Path.Combine(destSub2, "s2.txt")).Should().Be("sub2-file");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    /// <summary>
    /// Validates that when a destination file already exists, the copy operation fails without overwriting existing content.
    /// Inputs:
    ///  - sourceDir: contains a file "same.txt".
    ///  - destinationDir: pre-created and containing "same.txt" with different content.
    ///  - recursive: either value (irrelevant for top-level collision).
    /// Expected:
    ///  - IOException is thrown due to disallowed overwrite.
    ///  - Destination file content remains unchanged.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CopyDirectory_DestinationHasExistingFile_ThrowsIOExceptionAndDoesNotOverwrite()
    {
        // Arrange
        var fs = new FileSystem();
        var root = CreateTempRoot();
        var sourceDir = Path.Combine(root, "source");
        var destinationDir = Path.Combine(root, "dest");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destinationDir);

        var srcFile = Path.Combine(sourceDir, "same.txt");
        var destFile = Path.Combine(destinationDir, "same.txt");
        File.WriteAllText(srcFile, "from-source");
        File.WriteAllText(destFile, "existing-dest");

        try
        {
            // Act
            Action act = () => fs.CopyDirectory(sourceDir, destinationDir, recursive: false);

            // Assert
            act.Should().Throw<IOException>();
            File.ReadAllText(destFile).Should().Be("existing-dest");
        }
        finally
        {
            SafeDelete(root);
        }
    }

    /// <summary>
    /// Ensures that copying from an empty source directory results in an empty destination directory,
    /// regardless of the recursive flag.
    /// Inputs:
    ///  - sourceDir: empty directory.
    ///  - destinationDir: does not exist prior to the call.
    ///  - recursive: parameterized (false and true).
    /// Expected:
    ///  - Destination directory is created.
    ///  - No files or subdirectories are present in the destination.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CopyDirectory_EmptySource_CreatesEmptyDestination(bool recursive)
    {
        // Arrange
        var fs = new FileSystem();
        var root = CreateTempRoot();
        var sourceDir = Path.Combine(root, "source");
        var destinationDir = Path.Combine(root, "dest");
        Directory.CreateDirectory(sourceDir);

        try
        {
            // Act
            fs.CopyDirectory(sourceDir, destinationDir, recursive);

            // Assert
            Directory.Exists(destinationDir).Should().BeTrue();
            Directory.GetFiles(destinationDir, "*", SearchOption.AllDirectories).Length.Should().Be(0);
            Directory.GetDirectories(destinationDir, "*", SearchOption.AllDirectories).Length.Should().Be(0);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    // Helpers

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "darc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void SafeDelete(string path)
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
            // Intentionally ignored: best-effort cleanup for test artifacts.
        }
    }
}
