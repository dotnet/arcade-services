// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LibGit2Sharp;
using Maestro;
using Maestro.Common;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.Services;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.UnitTests;


public class LocalLibGit2ClientTests
{
    /// <summary>
    /// Ensures that the LocalLibGit2Client constructor completes successfully and returns a non-null instance
    /// when provided with valid (non-null) dependencies.
    /// Inputs:
    ///  - Non-null mocks for IRemoteTokenProvider, ITelemetryRecorder, IProcessManager, IFileSystem, and ILogger.
    /// Expected:
    ///  - No exception is thrown and the created instance is not null and is assignable to LocalGitClient.
    /// </summary>
    [TestCase(MockBehavior.Loose)]
    [TestCase(MockBehavior.Strict)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_WithValidDependencies_CreatesInstance(MockBehavior behavior)
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(behavior);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(behavior);
        var processManager = new Mock<IProcessManager>(behavior);
        var fileSystem = new Mock<IFileSystem>(behavior);
        var logger = new Mock<ILogger>(behavior);

        // Act
        var client = new LocalLibGit2Client(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Assert
        client.Should().NotBeNull();
        client.Should().BeAssignableTo<LocalGitClient>();
    }

    /// <summary>
    /// Verifies that the LocalLibGit2Client constructor completes successfully with valid dependencies
    /// without needing to inspect private fields. This ensures the object is created and is of the expected type.
    /// Inputs:
    ///  - Valid mocks for all constructor parameters.
    /// Expected:
    ///  - No exception is thrown.
    ///  - The created instance is not null and is assignable to LocalGitClient.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_InternalFields_AreInitializedAsExpected_Skipped()
    {
        // Arrange
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Strict);

        // Act
        var client = new LocalLibGit2Client(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Assert
        client.Should().NotBeNull();
        client.Should().BeAssignableTo<LocalGitClient>();
    }

    /// <summary>
    /// Verifies that when commit is null, Checkout resolves the repository's default commit/branch
    /// and does not clean untracked files when force is false.
    /// Inputs:
    ///  - A repository with an initial commit and an untracked file.
    ///  - commit = null, force = false.
    /// Expected:
    ///  - No exception is thrown.
    ///  - The untracked file remains (no cleanup).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Checkout_NullCommitForceFalse_DoesNotCleanUntrackedFiles()
    {
        // Arrange
        var (client, repoPath, tempDir) = CreateClientAndRepoWithInitialCommit();
        var untrackedPath = Path.Combine(repoPath, "untracked.txt");
        File.WriteAllText(untrackedPath, "temp");

        try
        {
            // Act
            client.Checkout(repoPath, null, false);

            // Assert
            File.Exists(untrackedPath).Should().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Verifies that when force is true, Checkout triggers repository cleanup and
    /// deletes untracked files.
    /// Inputs:
    ///  - A repository with an initial commit and an untracked file.
    ///  - commit = null (uses default), force = true.
    /// Expected:
    ///  - No exception is thrown.
    ///  - The untracked file is deleted by cleanup.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Checkout_NullCommitForceTrue_CleansUntrackedFiles()
    {
        // Arrange
        var (client, repoPath, tempDir) = CreateClientAndRepoWithInitialCommit();
        var untrackedPath = Path.Combine(repoPath, "untracked.txt");
        File.WriteAllText(untrackedPath, "temp");

        try
        {
            // Act
            client.Checkout(repoPath, null, true);

            // Assert
            File.Exists(untrackedPath).Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Ensures that supplying a non-existent commit/treeish causes Checkout to throw
    /// a wrapping Exception with the expected message format.
    /// Inputs:
    ///  - A valid repository path.
    ///  - commit = "this-does-not-exist", force = false.
    /// Expected:
    ///  - An Exception is thrown.
    ///  - The exception message contains "Something went wrong when checking out {commit} in {repoPath}".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Checkout_InvalidCommit_ThrowsWrappedException()
    {
        // Arrange
        var (client, repoPath, tempDir) = CreateClientAndRepoWithInitialCommit();
        var invalidCommit = "this-does-not-exist";

        try
        {
            // Act
            Exception caught = null;
            try
            {
                client.Checkout(repoPath, invalidCommit, false);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Assert
            caught.Should().NotBeNull();
            caught.Message.Should().Contain($"Something went wrong when checking out {invalidCommit} in {repoPath}");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static (LocalLibGit2Client client, string repoPath, string tempDir) CreateClientAndRepoWithInitialCommit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        // Initialize repository and create an initial commit
        Repository.Init(tempDir);
        using (var repo = new Repository(tempDir))
        {
            var filePath = Path.Combine(tempDir, "file.txt");
            File.WriteAllText(filePath, "content");
            Commands.Stage(repo, "*");
            var author = new Signature("tester", "tester@example.com", DateTimeOffset.Now);
            repo.Commit("initial", author, author);
        }

        // Create LocalLibGit2Client with mocked dependencies
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Loose);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Loose);
        var processManager = new Mock<IProcessManager>(MockBehavior.Loose);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var client = new LocalLibGit2Client(
            remoteTokenProvider.Object,
            telemetry.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        return (client, tempDir, tempDir);
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                // Best-effort cleanup; ignore IO errors from files locked by libgit2
                Directory.Delete(dir, true);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    /// <summary>
    /// Provides diverse string inputs to exercise edge and boundary conditions for repoPath, branchName, and remoteUrl.
    /// Inputs:
    ///  - Paths with spaces/special chars, empty strings, long strings, and different URL schemes.
    /// Expected:
    ///  - Method should attempt push without argument validation exceptions; actual behaviors require integration with LibGit2Sharp.
    /// Notes:
    ///  - Test is ignored until an integration environment (real git repo and remote) can be set up or code refactored for testability.
    /// </summary>
    public static IEnumerable<TestCaseData> Push_InputEdgeCases =>
        new[]
        {
                new TestCaseData(
                    Path.Combine(Path.GetTempPath(), "repo-with-spaces path"),
                    "main",
                    "https://example.com/owner/repo.git"
                ).SetName("StandardHttps_WithSpacesInRepoPath"),
                new TestCaseData(
                    "",
                    "feature/äüß",
                    "ssh://git@example.com:22/owner/repo.git"
                ).SetName("EmptyRepoPath_SSHUrl_UnicodeBranch"),
                new TestCaseData(
                    new string('a', 256),
                    "release/1.0",
                    "https://example.com/" + new string('p', 300) + ".git?query=1#frag"
                ).SetName("VeryLongRepoPath_UrlWithQueryAndFragment"),
                new TestCaseData(
                    ".",
                    " hotfix ",
                    "file:///tmp/repo.git"
                ).SetName("RelativeRepoPath_WhitespacePaddedBranch_FileUrl"),
        };

    /// <summary>
    /// Verifies that when path is null or empty, LsTreeAsync enumerates the repository root tree
    /// and returns items with leading slash in the Path field (due to $"{path}/{t.Path}" formatting).
    /// Inputs:
    ///  - A repository with one file at root and one subdirectory containing a file.
    ///  - path argument: null or empty string.
    /// Expected:
    ///  - Two items returned (one blob for the root file, one tree for the subdirectory).
    ///  - Each item's Path begins with a leading slash (e.g., "/root.txt", "/dir1").
    /// </summary>
    [Test]
    [TestCase(null)]
    [TestCase("")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task LsTreeAsync_NullOrEmptyPath_EnumeratesRootWithLeadingSlashPaths(string pathArgument)
    {
        // Arrange
        var (repoPath, commitSha) = RepoBuilder.CreateRepoWithStructure();

        var client = new LocalLibGit2Client(
            new Mock<IRemoteTokenProvider>(MockBehavior.Strict).Object,
            new Mock<ITelemetryRecorder>(MockBehavior.Strict).Object,
            new Mock<IProcessManager>(MockBehavior.Strict).Object,
            new Mock<IFileSystem>(MockBehavior.Strict).Object,
            new Mock<ILogger>(MockBehavior.Strict).Object);

        try
        {
            // Act
            var items = await client.LsTreeAsync(repoPath, commitSha, pathArgument);

            // Assert
            items.Should().HaveCount(2);
            items.Should().ContainSingle(x => x.Path == "/root.txt" && x.Type == "blob");
            items.Should().ContainSingle(x => x.Path == "/dir1" && x.Type == "tree");
        }
        finally
        {
            RepoBuilder.TryDeleteDirectory(repoPath);
        }
    }

    /// <summary>
    /// Verifies that when a valid subdirectory path is provided, LsTreeAsync enumerates its children
    /// and returns items with paths prefixed by the provided directory (no leading slash).
    /// Inputs:
    ///  - A repository containing dir1 with inner.txt (blob) and sub (tree).
    ///  - path argument: "dir1".
    /// Expected:
    ///  - Two items returned: "dir1/inner.txt" (blob) and "dir1/sub" (tree).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task LsTreeAsync_SubdirectoryPath_EnumeratesChildrenWithRelativePaths()
    {
        // Arrange
        var (repoPath, commitSha) = RepoBuilder.CreateRepoWithStructure();

        var client = new LocalLibGit2Client(
            new Mock<IRemoteTokenProvider>(MockBehavior.Strict).Object,
            new Mock<ITelemetryRecorder>(MockBehavior.Strict).Object,
            new Mock<IProcessManager>(MockBehavior.Strict).Object,
            new Mock<IFileSystem>(MockBehavior.Strict).Object,
            new Mock<ILogger>(MockBehavior.Strict).Object);

        try
        {
            // Act
            var items = await client.LsTreeAsync(repoPath, commitSha, "dir1");

            // Assert
            items.Should().HaveCount(2);
            items.Should().ContainSingle(x => x.Path == "dir1/inner.txt" && x.Type == "blob");
            items.Should().ContainSingle(x => x.Path == "dir1/sub" && x.Type == "tree");
        }
        finally
        {
            RepoBuilder.TryDeleteDirectory(repoPath);
        }
    }

    /// <summary>
    /// Ensures that providing a non-existent path causes a DirectoryNotFoundException.
    /// Inputs:
    ///  - A valid repository and commit.
    ///  - path argument: "missing" which does not exist.
    /// Expected:
    ///  - DirectoryNotFoundException is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task LsTreeAsync_PathNotFound_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var (repoPath, commitSha) = RepoBuilder.CreateRepoWithStructure();

        var client = new LocalLibGit2Client(
            new Mock<IRemoteTokenProvider>(MockBehavior.Strict).Object,
            new Mock<ITelemetryRecorder>(MockBehavior.Strict).Object,
            new Mock<IProcessManager>(MockBehavior.Strict).Object,
            new Mock<IFileSystem>(MockBehavior.Strict).Object,
            new Mock<ILogger>(MockBehavior.Strict).Object);

        try
        {
            // Act
            Func<Task> act = async () => await client.LsTreeAsync(repoPath, commitSha, "missing");

            // Assert
            await act.Should().ThrowAsync<DirectoryNotFoundException>();
        }
        finally
        {
            RepoBuilder.TryDeleteDirectory(repoPath);
        }
    }

    /// <summary>
    /// Ensures that when the provided path points to a file rather than a directory, an ArgumentException is thrown.
    /// Inputs:
    ///  - A valid repository with a file at root named "root.txt".
    ///  - path argument: "root.txt".
    /// Expected:
    ///  - ArgumentException is thrown with a message indicating path is not a directory.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task LsTreeAsync_PathIsFile_ThrowsArgumentException()
    {
        // Arrange
        var (repoPath, commitSha) = RepoBuilder.CreateRepoWithStructure();

        var client = new LocalLibGit2Client(
            new Mock<IRemoteTokenProvider>(MockBehavior.Strict).Object,
            new Mock<ITelemetryRecorder>(MockBehavior.Strict).Object,
            new Mock<IProcessManager>(MockBehavior.Strict).Object,
            new Mock<IFileSystem>(MockBehavior.Strict).Object,
            new Mock<ILogger>(MockBehavior.Strict).Object);

        try
        {
            // Act
            Func<Task> act = async () => await client.LsTreeAsync(repoPath, commitSha, "root.txt");

            // Assert
            await act.Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            RepoBuilder.TryDeleteDirectory(repoPath);
        }
    }

    /// <summary>
    /// Ensures that an invalid gitRef causes an ArgumentException before any path resolution is attempted.
    /// Inputs:
    ///  - A valid repository (but the provided gitRef does not exist in the repository).
    ///  - gitRef argument: "non-existent-ref".
    /// Expected:
    ///  - ArgumentException is thrown indicating the reference couldn't be found.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task LsTreeAsync_InvalidGitRef_ThrowsArgumentException()
    {
        // Arrange
        var (repoPath, _) = RepoBuilder.CreateRepoWithStructure();

        var client = new LocalLibGit2Client(
            new Mock<IRemoteTokenProvider>(MockBehavior.Strict).Object,
            new Mock<ITelemetryRecorder>(MockBehavior.Strict).Object,
            new Mock<IProcessManager>(MockBehavior.Strict).Object,
            new Mock<IFileSystem>(MockBehavior.Strict).Object,
            new Mock<ILogger>(MockBehavior.Strict).Object);

        try
        {
            // Act
            Func<Task> act = async () => await client.LsTreeAsync(repoPath, "non-existent-ref", null);

            // Assert
            await act.Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            RepoBuilder.TryDeleteDirectory(repoPath);
        }
    }

    /// <summary>
    /// Validates cache-hit behavior: a first call at root will cache subtree SHAs using leading-slash paths,
    /// enabling a subsequent call with a leading-slash path (e.g., "/dir1") to resolve via cache.
    /// Inputs:
    ///  - First call: path = null (root enumeration, caches "/dir1").
    ///  - Second call: path = "/dir1".
    /// Expected:
    ///  - Second call returns items under dir1 and their Path values begin with "/dir1".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task LsTreeAsync_CacheHitWithLeadingSlashPath_ReturnsSubtreeItems()
    {
        // Arrange
        var (repoPath, commitSha) = RepoBuilder.CreateRepoWithStructure();

        var client = new LocalLibGit2Client(
            new Mock<IRemoteTokenProvider>(MockBehavior.Strict).Object,
            new Mock<ITelemetryRecorder>(MockBehavior.Strict).Object,
            new Mock<IProcessManager>(MockBehavior.Strict).Object,
            new Mock<IFileSystem>(MockBehavior.Strict).Object,
            new Mock<ILogger>(MockBehavior.Strict).Object);

        try
        {
            // Warm-up to populate cache entries such as "/dir1"
            var rootItems = await client.LsTreeAsync(repoPath, commitSha, null);
            rootItems.Should().ContainSingle(x => x.Path == "/dir1" && x.Type == "tree");

            // Act
            var items = await client.LsTreeAsync(repoPath, commitSha, "/dir1");

            // Assert
            items.Should().HaveCount(2);
            items.Should().ContainSingle(x => x.Path == "/dir1/inner.txt" && x.Type == "blob");
            items.Should().ContainSingle(x => x.Path == "/dir1/sub" && x.Type == "tree");
        }
        finally
        {
            RepoBuilder.TryDeleteDirectory(repoPath);
        }
    }

    private static class RepoBuilder
    {
        public static (string repoPath, string commitSha) CreateRepoWithStructure()
        {
            var repoPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repoPath);

            Repository.Init(repoPath);

            using (var repo = new Repository(repoPath))
            {
                // Create directory structure
                Directory.CreateDirectory(Path.Combine(repoPath, "dir1", "sub"));

                // Create files
                File.WriteAllText(Path.Combine(repoPath, "root.txt"), "root content");
                File.WriteAllText(Path.Combine(repoPath, "dir1", "inner.txt"), "inner content");
                File.WriteAllText(Path.Combine(repoPath, "dir1", "sub", "deep.txt"), "deep content");

                // Stage and commit
                Commands.Stage(repo, "*");

                var author = new Signature("Test", "test@example.com", DateTimeOffset.UtcNow);
                var commit = repo.Commit("Initial commit", author, author);

                return (repoPath, commit.Sha);
            }
        }

        public static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    // Make all files writable to ensure deletion
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var attrs = File.GetAttributes(file);
                            if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                            {
                                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                            }
                        }
                        catch
                        {
                            // best-effort
                        }
                    }

                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    /// <summary>
    /// Verifies that adding a Base64-encoded file results in a decoded file being written
    /// under the repository root, and no exception is thrown.
    /// Inputs:
    ///  - A real initialized repository path.
    ///  - filesToCommit with one GitFile using ContentEncoding.Base64 and a nested relative path.
    ///  - Various branch and commitMessage strings (including empty and whitespace).
    /// Expected:
    ///  - No exception.
    ///  - The target file is created under repoPath with decoded content.
    /// Notes:
    ///  - Sets Environment.CurrentDirectory to repoPath to satisfy Directory.GetParent(file.FilePath) usage in implementation.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(AddFile_InputCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CommitFilesAsync_AddBase64File_WritesDecodedContentAndCreatesFile(string branch, string commitMessage, string relativePath, string base64Content, string expectedContentSubstring)
    {
        // Arrange
        var (client, repoPath, tempDir) = CreateClientAndRepoWithInitialCommit();
        var originalCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = repoPath;

        var targetPath = Path.Combine(repoPath, relativePath);
        var files = new List<GitFile>
        {
            new GitFile(relativePath, base64Content, ContentEncoding.Base64)
        };

        try
        {
            // Act
            await client.CommitFilesAsync(files, repoPath, branch, commitMessage);

            // Assert
            Assert.That(File.Exists(targetPath), Is.True, "File should be created under the repository path.");
            var written = File.ReadAllText(targetPath);
            Assert.That(written.Contains(expectedContentSubstring), Is.True, "Decoded content should be present in the written file.");
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Ensures that when a file path without a parent directory is provided for an Add operation,
    /// the method throws a DarcException wrapping the original error.
    /// Inputs:
    ///  - A valid repository.
    ///  - filesToCommit with one GitFile path like "file.txt" (no parent directory).
    /// Expected:
    ///  - DarcException is thrown with a message containing "Something went wrong when checking out {repoPath} in {repoPath}".
    ///  - InnerException message contains "Cannot find parent directory of file.txt.".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CommitFilesAsync_AddFileWithoutParentDirectory_ThrowsWrappedDarcExceptionAsync()
    {
        // Arrange
        var (client, repoPath, tempDir) = CreateClientAndRepoWithInitialCommit();
        var originalCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = repoPath;

        var files = new List<GitFile>
        {
            new GitFile("file.txt", "content", ContentEncoding.Utf8)
        };

        try
        {
            // Act
            Exception caught = null;
            try
            {
                await client.CommitFilesAsync(files, repoPath, "branch", "message");
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That(caught, Is.Not.Null);
            Assert.That(caught, Is.TypeOf<DarcException>());
            Assert.That(caught.Message.Contains($"Something went wrong when checking out {repoPath} in {repoPath}"), Is.True);
            Assert.That(caught.InnerException, Is.Not.Null);
            Assert.That(caught.InnerException.Message.Contains("Cannot find parent directory of file.txt."), Is.True);
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Validates that Delete operation checks file existence using an unrooted path and thus fails to delete
    /// when the current working directory differs from repoPath (potential bug).
    /// Inputs:
    ///  - A valid repository with a file under repoPath/dir/del.txt present.
    ///  - Current working directory set to a different location.
    ///  - filesToCommit containing a Delete operation for "dir/del.txt".
    /// Expected:
    ///  - No exception.
    ///  - The file remains on disk (not deleted) due to path resolution against CWD instead of repoPath.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CommitFilesAsync_DeleteOperation_WithDifferentWorkingDirectory_DoesNotDeleteFileAsync()
    {
        // Arrange
        var (client, repoPath, tempDir) = CreateClientAndRepoWithInitialCommit();
        var differentDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "cwd-" + Guid.NewGuid().ToString("N"))).FullName;
        var originalCwd = Environment.CurrentDirectory;

        var relPath = Path.Combine("dir", "del.txt");
        var fullPath = Path.Combine(repoPath, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, "to-be-deleted");

        var files = new List<GitFile>
        {
            new GitFile(relPath, "", ContentEncoding.Utf8, mode: "100644", operation: GitFileOperation.Delete)
        };

        try
        {
            Environment.CurrentDirectory = differentDir;

            // Act
            await client.CommitFilesAsync(files, repoPath, "branch", "message");

            // Assert
            Assert.That(File.Exists(fullPath), Is.True, "File should not be deleted because File.Exists/Delete use unrooted path.");
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            TryDeleteDirectory(differentDir);
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Ensures that an unknown ContentEncoding causes a DarcException which is then wrapped
    /// by the outer catch into another DarcException with the standard message.
    /// Inputs:
    ///  - A valid repository.
    ///  - filesToCommit with ContentEncoding outside the defined enum values.
    /// Expected:
    ///  - Outer DarcException message contains "Something went wrong when checking out {repoPath} in {repoPath}".
    ///  - InnerException is DarcException with "Unknown file content encoding ..." message.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CommitFilesAsync_UnknownContentEncoding_ThrowsWrappedDarcExceptionAsync()
    {
        // Arrange
        var (client, repoPath, tempDir) = CreateClientAndRepoWithInitialCommit();
        var originalCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = repoPath;

        var relPath = Path.Combine("dir", "unknown.txt");
        var invalidEncoding = (ContentEncoding)123;
        var files = new List<GitFile>
        {
            new GitFile(relPath, "ignored", invalidEncoding)
        };

        try
        {
            // Act
            Exception caught = null;
            try
            {
                await client.CommitFilesAsync(files, repoPath, "branch", "message");
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That(caught, Is.Not.Null);
            Assert.That(caught, Is.TypeOf<DarcException>());
            Assert.That(caught.Message.Contains($"Something went wrong when checking out {repoPath} in {repoPath}"), Is.True);
            Assert.That(caught.InnerException, Is.Not.Null);
            Assert.That(caught.InnerException, Is.TypeOf<DarcException>());
            Assert.That(caught.InnerException.Message.Contains("Unknown file content encoding"), Is.True);
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Verifies that passing an empty file list results in no action and no exceptions.
    /// Inputs:
    ///  - A valid repository.
    ///  - filesToCommit: empty list.
    /// Expected:
    ///  - No exception is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CommitFilesAsync_EmptyFileList_NoExceptionsAsync()
    {
        // Arrange
        var (client, repoPath, tempDir) = CreateClientAndRepoWithInitialCommit();
        var files = new List<GitFile>();

        try
        {
            // Act
            await client.CommitFilesAsync(files, repoPath, "branch", "message");

            // Assert
            Assert.Pass("No exceptions thrown for empty list.");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    public static IEnumerable<TestCaseData> AddFile_InputCases()
    {
        yield return new TestCaseData("main", "initial commit", Path.Combine("dir1", "new.txt"), "SGVsbG8=", "Hell");
        yield return new TestCaseData("", " ", Path.Combine("a b", "c.txt"), "V29ybGQh", "World");
        yield return new TestCaseData("feature/x", "message with ü", Path.Combine("spécial", "f.txt"), "U29tZUNvbnRlbnQ=", "SomeContent");
    }

    /// <summary>
    /// Verifies that when commit is null, Checkout resolves the repository's default commit/branch
    /// and conditionally cleans untracked files based on the 'force' flag.
    /// Inputs:
    ///  - A repository with an initial commit and an untracked file.
    ///  - commit = null.
    ///  - force parameter is varied (false/true).
    /// Expected:
    ///  - When force = false => untracked file remains.
    ///  - When force = true  => untracked file is deleted by cleanup.
    /// </summary>
    [Test]
    [TestCase(false, true, TestName = "Checkout_NullCommit_ForceFalse_DoesNotCleanUntrackedFiles")]
    [TestCase(true, false, TestName = "Checkout_NullCommit_ForceTrue_CleansUntrackedFiles")]
    [Category("Checkout")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Checkout_NullCommit_CleansUntrackedFiles_AccordingToForce(bool force, bool expectedUntrackedExists)
    {
        // Arrange
        var (client, repoPath, tempDir) = CreateClientAndRepoWithInitialCommit();
        var untrackedPath = Path.Combine(repoPath, "untracked.txt");
        File.WriteAllText(untrackedPath, "temp");

        try
        {
            // Act
            client.Checkout(repoPath, null, force);

            // Assert
            File.Exists(untrackedPath).Should().Be(expectedUntrackedExists);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Validates that invalid repository paths (non-existent, empty, whitespace) result in a wrapped exception.
    /// Inputs:
    ///  - repoPath values: "", " ", non-existent directory path.
    ///  - commit: "any".
    /// Expected:
    ///  - An Exception is thrown with a message containing both the commit and repoPath values.
    /// </summary>
    [Test]
    [Category("Checkout")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Checkout_InvalidRepoPath_ThrowsWrappedException()
    {
        // Arrange
        string[] invalidPaths =
        {
            "",
            " ",
            Path.Combine(Path.GetTempPath(), "darc-tests", Guid.NewGuid().ToString("N"))
        };

        foreach (var invalidRepoPath in invalidPaths)
        {
            var client = CreateClient();

            // Act
            Exception caught = null;
            try
            {
                client.Checkout(invalidRepoPath, "any", false);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Assert
            caught.Should().NotBeNull();
            caught.Message.Should().Contain($"Something went wrong when checking out any in {invalidRepoPath}");
        }
    }

    private static LocalLibGit2Client CreateClient()
    {
        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Loose);
        var telemetryRecorder = new Mock<ITelemetryRecorder>(MockBehavior.Loose);
        var processManager = new Mock<IProcessManager>(MockBehavior.Loose);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        return new LocalLibGit2Client(
            remoteTokenProvider.Object,
            telemetryRecorder.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);
    }

    /// <summary>
    /// Partial test: Validates that supplying an explicit Identity is supported by Push.
    /// Inputs:
    ///  - Non-null LibGit2Sharp.Identity passed to Push.
    /// Expected:
    ///  - Method should use the provided identity when creating RepositoryOptions.
    /// Notes:
    ///  - Requires integration to verify that commits and push metadata reflect the provided identity.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Ignore("Requires integration setup to verify that the provided Identity is honored.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task Push_WithExplicitIdentity_BehavesAsDocumented()
    {
        // Arrange
        var repoPath = "/tmp/repo";
        var branchName = "main";
        var remoteUrl = "https://example.com/owner/repo.git";
        var identity = new Identity("some-user", "some-user@example.com");

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        remoteTokenProvider
            .Setup(m => m.GetTokenForRepository(remoteUrl))
            .Returns("token-value");

        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Loose);
        var processManager = new Mock<IProcessManager>(MockBehavior.Loose);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var client = new LocalLibGit2Client(
            remoteTokenProvider.Object,
            telemetry.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act
        await client.Push(repoPath, branchName, remoteUrl, identity);

        // Assert
        // Integration assertions to be added upon enabling this test.
    }

    /// <summary>
    /// Partial test: Documents expected error behavior when the specified branch does not exist.
    /// Inputs:
    ///  - Valid git repository path but branchName that is not present.
    /// Expected:
    ///  - Exception with message "No branch {branchName} found in repo. {repo.Info.Path}" is thrown before push.
    /// Notes:
    ///  - Requires an initialized repository at repoPath to verify.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Ignore("Requires real repository with missing branch to validate exception behavior.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Push_InvalidBranch_ThrowsExpectedException_Skipped()
    {
        // Arrange
        var repoPath = "/tmp/repo";
        var missingBranch = "this-branch-does-not-exist";
        var remoteUrl = "https://example.com/owner/repo.git";

        var remoteTokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Loose);
        var telemetry = new Mock<ITelemetryRecorder>(MockBehavior.Loose);
        var processManager = new Mock<IProcessManager>(MockBehavior.Loose);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var client = new LocalLibGit2Client(
            remoteTokenProvider.Object,
            telemetry.Object,
            processManager.Object,
            fileSystem.Object,
            logger.Object);

        // Act & Assert
        // To enable:
        //  1) Initialize a real repository at repoPath without the missingBranch.
        //  2) Remove Ignore and assert that calling Push throws the documented exception.
        Assert.Pass("Enable with integration setup and convert to exception assertion.");
    }
}
