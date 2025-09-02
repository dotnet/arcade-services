// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Maestro;
using Maestro.Common;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet;
using NuGet.Versioning;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;


public class LocalTests
{
    private static IEnumerable<TestCaseData> AddDependencyAsync_Cases()
    {
        yield return new TestCaseData(
            BuildDependency("Package.A", "1.0.0", "https://repo/a", "abc123"),
            null,
            "/tmp/repo")
            .SetName("AddDependencyAsync_NullRelativeBasePath_UsesRepoRootDir");

        yield return new TestCaseData(
            BuildDependency("Microsoft.DotNet.Arcade.Sdk", "9.9.9", "https://github.com/dotnet/arcade", "deadbeef"),
            new UnixPath("src"),
            "/home/user/repo")
            .SetName("AddDependencyAsync_WithRelativeBasePath_ForwardsUnixPath");

        yield return new TestCaseData(
            BuildDependency("Toolset.X", "2.3.4-beta", "https://example.com/toolsetx", "cafebabe"),
            new UnixPath("./sub/dir"),
            "C:/dev/repo")
            .SetName("AddDependencyAsync_WithNestedRelativeBasePath_ForwardsNestedPath");
    }

    private static DependencyDetail BuildDependency(string name, string version, string repoUri, string commit)
    {
        return new DependencyDetail
        {
            Name = name,
            Version = version,
            RepoUri = repoUri,
            Commit = commit,
            Pinned = false,
            SkipProperty = false,
            Type = DependencyType.Product
        };
    }

    /// <summary>
    /// Verifies that Local.RemoveDependencyAsync delegates to DependencyFileManager.RemoveDependencyAsync
    /// with the provided dependencyName and relativeBasePath, and uses the overrideRootPath as repo root.
    /// Inputs:
    ///  - Various dependencyName values including null/empty/whitespace/special characters/long string.
    ///  - relativeBasePath as null, empty, ".", and nested paths.
    /// Expected:
    ///  - DependencyFileManager.RemoveDependencyAsync is invoked with:
    ///      - dependencyName as provided,
    ///      - repoUri equal to overrideRootPath,
    ///      - branch null,
    ///      - relativeBasePath passed through unchanged.
    /// Notes:
    ///  - Ignored: Local constructs concrete internal dependencies (_fileManager, _gitClient) that cannot be replaced or mocked.
    ///    To enable this test, refactor Local to accept an IDependencyFileManager via constructor injection
    ///    or provide an overload/factory to substitute dependencies in tests. Then:
    ///      1) Inject a mock IDependencyFileManager.
    ///      2) Call sut.RemoveDependencyAsync(dependencyName, relativeBasePath).
    ///      3) Verify the mock received RemoveDependencyAsync with expected arguments.
    /// </summary>
    [TestCase(null, null, Description = "Null dependency name; no relative base path.")]
    [TestCase("", null, Description = "Empty dependency name; no relative base path.")]
    [TestCase(" ", null, Description = "Whitespace dependency name; no relative base path.")]
    [TestCase("Package.X", null, Description = "Valid name; no relative base path.")]
    [TestCase("Package.X", ".", Description = "Valid name; current directory relative base path.")]
    [TestCase("Pkg/with/slash", "nested/path", Description = "Name with slash; nested relative path.")]
    [TestCase("NameWith:Colon*", "", Description = "Name with special characters; empty relative path.")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "src/arcade", Description = "Very long name; specific relative path.")]
    [Ignore("Design prevents isolation: Local internally constructs concrete dependencies that cannot be mocked. See test XML comments for refactoring guidance.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task RemoveDependencyAsync_DelegatesToDependencyFileManager_WithExpectedArguments(string dependencyName, string relativeBasePathText)
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var overrideRootPath = "/tmp/override-root-path";

        var sut = new Local(tokenProvider.Object, logger.Object, overrideRootPath);

        UnixPath relativeBasePath = relativeBasePathText == null ? null : new UnixPath(relativeBasePathText);

        // Act
        // NOTE: This call would attempt to perform real file operations via the concrete DependencyFileManager/LocalLibGit2Client.
        // Once Local is refactored for DI, replace with a mock verification as described in the XML comments above.
        await sut.RemoveDependencyAsync(dependencyName, relativeBasePath);

        // Assert
        // Validation is not performed in this ignored test.
        // After refactoring for DI, verify that:
        // _fileManager.RemoveDependencyAsync(dependencyName, overrideRootPath, null, relativeBasePath) was called exactly once.
    }

    /// <summary>
    /// Ensures UpdateDependenciesAsync can be invoked with no Arcade dependency present and that
    /// the method proceeds to update dependency files and commit.
    /// Inputs:
    ///  - dependencies: empty list (no Arcade item).
    ///  - remoteFactory: mock (unused).
    ///  - gitRepoFactory: mock (unused).
    ///  - barClient: mock (unused).
    /// Expected:
    ///  - Method completes without throwing.
    /// Notes:
    ///  - Ignored because Local tightly constructs DependencyFileManager and LocalLibGit2Client internally,
    ///    preventing proper mocking of file system and Git operations, which would attempt real IO.
    ///    To enable this test, refactor Local to accept these collaborators via DI.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Ignore("Cannot execute without refactoring Local to inject DependencyFileManager/ILocalLibGit2Client. This test documents expected behavior.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task UpdateDependenciesAsync_NoArcadeDependency_CommitsFilesAndDoesNotFetchEngCommon()
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        string tempRepo = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var dependencies = new List<DependencyDetail>(); // No Arcade item
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var gitRepoFactory = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);

        var sut = new Local(tokenProvider.Object, logger.Object, tempRepo);

        // Act
        await sut.UpdateDependenciesAsync(dependencies, remoteFactory.Object, gitRepoFactory.Object, barClient.Object);

        // Assert
        Assert.Inconclusive("Executed as documentation-only. Replace Ignore with proper DI-enabled execution once Local is refactored.");
    }

    private static IEnumerable NameFilterCases()
    {
        yield return new TestCaseData(null);
        yield return new TestCaseData(string.Empty);
        yield return new TestCaseData("Alpha");
        yield return new TestCaseData("alpha");
        yield return new TestCaseData("GAMMA");
        yield return new TestCaseData(" ");
        yield return new TestCaseData("\t");
        yield return new TestCaseData("name-with-special-çhår😊");
        yield return new TestCaseData(new string('a', 1024));
    }

    /// <summary>
    /// Verifies that Local.Checkout delegates to the underlying git client with the repository root path,
    /// provided commit (including edge-case strings), and force flag.
    /// Inputs:
    ///  - commit: empty, whitespace, control whitespace, typical branch/ref, tag, SHA, special characters, very long string.
    ///  - force: both true and false.
    /// Expected:
    ///  - _gitClient.Checkout(_repoRootDir.Value, commit, force) is invoked once with the same arguments.
    /// Notes:
    ///  - Ignored because Local constructs its own ILocalLibGit2Client internally, which cannot be mocked without refactoring.
    ///    After refactoring to allow injecting ILocalLibGit2Client, replace the comments in Act/Assert with real Moq verification.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Ignore("Local constructs its own ILocalLibGit2Client internally; cannot mock to verify delegation without refactoring.")]
    [TestCaseSource(nameof(Checkout_CommitAndForce_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Checkout_DelegatesToGitClientWithRepoRoot(string commit, bool force)
    {
        // Arrange
        var tokenProviderMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);
        var overrideRootPath = Path.Combine(Path.GetTempPath(), "repo-root");
        var local = new Local(tokenProviderMock.Object, loggerMock.Object, overrideRootPath);

        // Act
        // local.Checkout(commit, force);

        // Assert
        // After refactor to inject ILocalLibGit2Client:
        // mockGitClient.Verify(m => m.Checkout(overrideRootPath, commit, force), Times.Once);
    }

    private static IEnumerable<TestCaseData> Checkout_CommitAndForce_Cases()
    {
        yield return new TestCaseData("", false).SetName("Checkout_EmptyStringCommit_ForceFalse_Delegates");
        yield return new TestCaseData("", true).SetName("Checkout_EmptyStringCommit_ForceTrue_Delegates");
        yield return new TestCaseData(" ", false).SetName("Checkout_WhitespaceCommit_ForceFalse_Delegates");
        yield return new TestCaseData("\t\n", true).SetName("Checkout_ControlWhitespaceCommit_ForceTrue_Delegates");
        yield return new TestCaseData("main", false).SetName("Checkout_BranchMain_ForceFalse_Delegates");
        yield return new TestCaseData("refs/heads/release/1.0", true).SetName("Checkout_RefHeadsRelease_ForceTrue_Delegates");
        yield return new TestCaseData("v1.0.0", false).SetName("Checkout_TagVersion_ForceFalse_Delegates");
        yield return new TestCaseData("feature/with/slashes", true).SetName("Checkout_BranchWithSlashes_ForceTrue_Delegates");
        yield return new TestCaseData("1234567890abcdef1234567890abcdef12345678", false).SetName("Checkout_40CharSha_ForceFalse_Delegates");
        yield return new TestCaseData("name_with-special.chars@!", true).SetName("Checkout_SpecialCharacters_ForceTrue_Delegates");
        yield return new TestCaseData(new string('x', 4096), false).SetName("Checkout_VeryLongCommit_ForceFalse_Delegates");
    }

    /// <summary>
    /// Guidance-only placeholder illustrating the intended verification once Local allows injection of ILocalLibGit2Client.
    /// Inputs:
    ///  - commit: "main"
    ///  - force: true
    /// Expected:
    ///  - Verify _gitClient.Checkout(overrideRootPath, "main", true) is called exactly once.
    /// Notes:
    ///  - Ignored until Local is refactored to accept an ILocalLibGit2Client via constructor or factory.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Ignore("Design requires refactoring to inject ILocalLibGit2Client; cannot verify without DI.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Checkout_WhenDesignAllowsInjectingGitClient_VerificationExample()
    {
        // Arrange
        var tokenProviderMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);
        var overrideRootPath = Path.Combine(Path.GetTempPath(), "repo-root");
        var local = new Local(tokenProviderMock.Object, loggerMock.Object, overrideRootPath);

        // Act
        // local.Checkout("main", force: true);

        // Assert
        // mockGitClient.Verify(m => m.Checkout(overrideRootPath, "main", true), Times.Once);
    }

    /// <summary>
    /// Verifies that AddRemoteIfMissingAsync delegates to the underlying git client to:
    ///  - Add the remote if not present, and
    ///  - Update/fetch the remote using the returned remote name,
    /// and returns that remote name.
    /// Inputs:
    ///  - Various repoDir and repoUrl values including null, empty, whitespace, normal paths/URLs, and values with spaces.
    /// Expected:
    ///  - The method should forward inputs to the git client and return the remote name from AddRemoteIfMissingAsync.
    /// Notes:
    ///  - This test is marked inconclusive because Local constructs its own ILocalLibGit2Client internally.
    ///    Without a constructor overload or seam to inject a mocked ILocalLibGit2Client, we cannot verify interactions.
    ///    To enable testing, add a constructor that accepts an ILocalLibGit2Client (or an abstraction) and use it in the method.
    /// </summary>
    [TestCase(null, null)]
    [TestCase("", "")]
    [TestCase(" ", " ")]
    [TestCase("C:\\repo", "https://example.com/repo.git")]
    [TestCase("/tmp/repo", "ssh://git@github.com/dotnet/arcade.git")]
    [TestCase("C:\\path with spaces\\repo", "https://example.com/repo with spaces.git")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task AddRemoteIfMissingAsync_VariousInputs_CallsGitClientAndReturnsRemoteName(string repoDir, string repoUrl)
    {
        // Arrange
        var tokenProviderMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        // Provide an override root path to avoid accessing _gitClient.GetRootDirAsync() in the constructor.
        var local = new Local(tokenProviderMock.Object, loggerMock.Object, overrideRootPath: "C:\\repo");

        // Act & Assert
        // Partial test: Cannot arrange mocks for _gitClient as it is created internally and is not injectable.
        // Next steps for maintainers:
        //  1) Add a constructor overload to Local that accepts an ILocalLibGit2Client and assigns it to _gitClient.
        //  2) In tests, mock ILocalLibGit2Client to:
        //     - Return a known remote name from AddRemoteIfMissingAsync(repoDir, repoUrl, CancellationToken.None)
        //     - Verify UpdateRemoteAsync(repoDir, returnedRemoteName, CancellationToken.None) is called once
        //     - Verify the method returns the expected remote name.
        Assert.Inconclusive("Cannot test Local.AddRemoteIfMissingAsync: _gitClient is not injectable. Add a constructor overload that accepts ILocalLibGit2Client to enable mocking.");
        await Task.CompletedTask;
    }

    private static IEnumerable<TestCaseData> Constructor_OverrideRootPath_Cases()
    {
        yield return new TestCaseData(null).SetName("Constructor_NullOverrideRootPath_InstanceCreated");
        yield return new TestCaseData(string.Empty).SetName("Constructor_EmptyOverrideRootPath_InstanceCreated");
        yield return new TestCaseData(" \t\r\n").SetName("Constructor_WhitespaceOverrideRootPath_InstanceCreated");
        yield return new TestCaseData(".").SetName("Constructor_CurrentDirectoryPath_InstanceCreated");
        yield return new TestCaseData("..").SetName("Constructor_ParentDirectoryPath_InstanceCreated");
        yield return new TestCaseData("C:\\repo").SetName("Constructor_WindowsAbsolutePath_InstanceCreated");
        yield return new TestCaseData("/tmp/repo").SetName("Constructor_UnixAbsolutePath_InstanceCreated");
        yield return new TestCaseData("C:\\path with spaces\\repo").SetName("Constructor_PathWithSpaces_InstanceCreated");
        yield return new TestCaseData("C:\\inv<ali>|d*?:\\pa\"th").SetName("Constructor_PathWithInvalidCharacters_InstanceCreated");
        yield return new TestCaseData("C:\\путь\\репо").SetName("Constructor_UnicodePath_InstanceCreated");
        yield return new TestCaseData(new string('a', 4096)).SetName("Constructor_VeryLongPath_InstanceCreated");
    }

    /// <summary>
    /// Ensures the Local constructor can create an instance for a variety of overrideRootPath inputs
    /// without eagerly evaluating repository root resolution or performing IO.
    /// Inputs:
    ///  - overrideRootPath: null, empty, whitespace, ".", "..", absolute Windows/Unix paths,
    ///    paths with spaces, invalid characters, unicode, and very long strings.
    /// Expected:
    ///  - The constructor completes without throwing and returns a non-null instance.
    /// </summary>
    [TestCaseSource(nameof(Constructor_OverrideRootPath_Cases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_VariousOverrideRootPaths_InstanceCreated(string overrideRootPath)
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        // Act
        var sut = new Local(tokenProvider.Object, logger.Object, overrideRootPath);

        // Assert
        // Intentionally avoiding explicit assertions per framework constraints; construction without exception is success.
        _ = sut;
    }

    /// <summary>
    /// Verifies that Local.AddDependencyAsync delegates to DependencyFileManager.AddDependencyAsync
    /// with the provided dependency and relativeBasePath, and uses the overrideRootPath as repo root.
    /// Inputs:
    ///  - dependency: various combinations including null, empty/whitespace fields, special characters, and long strings.
    ///  - relativeBasePath: null, empty, ".", and nested paths.
    /// Expected:
    ///  - DependencyFileManager.AddDependencyAsync is invoked with:
    ///      - dependency as provided,
    ///      - repoUri equal to overrideRootPath,
    ///      - branch null,
    ///      - relativeBasePath passed through unchanged.
    /// Notes:
    ///  - Ignored: Local constructs concrete internal dependencies (_fileManager, _gitClient) that cannot be replaced or mocked.
    ///    To enable this test, refactor Local to accept an IDependencyFileManager via constructor injection
    ///    or provide an overload/factory to substitute dependencies in tests. Then:
    ///      1) Inject a mock IDependencyFileManager.
    ///      2) Call sut.AddDependencyAsync(dependency, relativeBasePath).
    ///      3) Verify the mock received AddDependencyAsync(dependency, overrideRootPath, null, relativeBasePath, It.IsAny<bool>(), It.IsAny<bool?>()).
    /// </summary>
    [TestCaseSource(nameof(AddDependencyAsync_Cases))]
    [Ignore("Design prevents isolation: Local internally constructs concrete dependencies that cannot be mocked. See test XML comments for refactoring guidance.")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task AddDependencyAsync_DelegatesToDependencyFileManager_WithExpectedArguments(
        string name,
        string version,
        string repoUri,
        string commit,
        string relativeBasePathText,
        bool dependencyIsNull)
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var overrideRootPath = "/tmp/override-root-path";

        var sut = new Local(tokenProvider.Object, logger.Object, overrideRootPath);

        UnixPath relativeBasePath = relativeBasePathText == null ? null : new UnixPath(relativeBasePathText);
        DependencyDetail dependency = dependencyIsNull ? null : BuildDependency(name, version, repoUri, commit);

        // Act
        // NOTE: This call would attempt to perform real file and git operations via the concrete DependencyFileManager/LocalLibGit2Client.
        // Once Local is refactored for DI, replace this with mock verification as described in the XML comments above.
        await sut.AddDependencyAsync(dependency, relativeBasePath);

        // Assert
        Assert.Inconclusive("Documentation-only: Replace Ignore and verify mock interaction after refactoring Local for DI.");
    }

    /// <summary>
    /// Verifies that name filtering in GetDependenciesAsync is applied as:
    /// - If name is null/empty, all dependencies are returned.
    /// - Otherwise, only dependencies whose Name equals the provided name (case-insensitive) are returned.
    /// Inputs:
    ///  - name: null, empty, whitespace, mixed-case match, special characters, very long string, and non-existing.
    /// Expected:
    ///  - Correct subset of dependencies is returned based on case-insensitive equality; null/empty yields all.
    /// Notes:
    ///  - Ignored because Local constructs concrete collaborators internally, preventing mock-based isolation of file IO.
    ///    After refactoring Local to accept an IDependencyFileManager, set up ParseVersionDetailsXmlAsync to return
    ///    a VersionDetails with known Dependencies and assert the returned list matches the expected subset.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Ignore("Design prevents isolation: Local internally constructs concrete collaborators (DependencyFileManager/LocalLibGit2Client). Refactor to inject abstractions and unignore.")]
    [TestCaseSource(nameof(NameFilterCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetDependenciesAsync_NameFilter_AppliesCaseInsensitiveAndNullMeansAll(string name)
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        string overrideRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var sut = new Local(tokenProvider.Object, logger.Object, overrideRootPath);

        // Act
        var result = await sut.GetDependenciesAsync(name, includePinned: true);

        // Assert
        // After refactoring for DI, assert that:
        // - When name is null/empty/whitespace: result is equivalent to versionDetails.Dependencies.
        // - Otherwise: result contains only items where d.Name.Equals(name, StringComparison.OrdinalIgnoreCase).
        // Example (post-refactor):
        // result.Should().BeEquivalentTo(expectedDependencies);
    }

    /// <summary>
    /// Ensures the includePinned flag is passed through to the underlying parser via DependencyFileManager.ParseVersionDetailsXmlAsync,
    /// affecting which dependencies are included.
    /// Inputs:
    ///  - includePinned: true and false.
    /// Expected:
    ///  - When true: pinned dependencies are included.
    ///  - When false: pinned dependencies are excluded.
    /// Notes:
    ///  - Ignored because we cannot inject a mocked IDependencyFileManager to verify the includePinned parameter or control return data.
    ///    After refactoring Local to accept IDependencyFileManager, set up ParseVersionDetailsXmlAsync(_repoRootDir.Value, null, includePinned)
    ///    to return appropriate VersionDetails for both cases and assert the results differ as expected.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Category("auto-generated")]
    [Ignore("Cannot verify without DI seam for DependencyFileManager. Refactor Local to inject IDependencyFileManager and unignore.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetDependenciesAsync_IncludePinned_PassesThroughToParser(bool includePinned)
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        string overrideRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var sut = new Local(tokenProvider.Object, logger.Object, overrideRootPath);

        // Act
        var result = await sut.GetDependenciesAsync(name: null, includePinned: includePinned);

        // Assert
        // After refactoring for DI, verify that ParseVersionDetailsXmlAsync was called with includePinned == provided flag
        // and that the returned dependencies reflect inclusion/exclusion of pinned entries accordingly.
        // Example (post-refactor):
        // mockFileManager.Verify(m => m.ParseVersionDetailsXmlAsync(overrideRootPath, null, includePinned, null), Times.Once);
        // result.Should().BeEquivalentTo(expected);
    }

    /// <summary>
    /// Ensures that if the underlying git client throws during AddRemoteIfMissingAsync or UpdateRemoteAsync,
    /// the exception propagates to the caller.
    /// Inputs:
    ///  - repoDir: typical path.
    ///  - repoUrl: typical URL.
    /// Expected:
    ///  - The thrown exception bubbles up unchanged.
    /// Notes:
    ///  - Ignored because we cannot configure the internally constructed git client to throw without DI.
    ///    After refactoring Local to accept ILocalLibGit2Client:
    ///      1) Setup AddRemoteIfMissingAsync to throw or UpdateRemoteAsync to throw.
    ///      2) Assert that the same exception is observed by the caller.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Ignore("Cannot simulate exceptions without injecting a mock ILocalLibGit2Client. Refactor Local for DI to enable this test.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void AddRemoteIfMissingAsync_WhenGitClientThrows_ExceptionIsPropagated()
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var sut = new Local(tokenProvider.Object, logger.Object, overrideRootPath: "/tmp/dummy");

        // Act & Assert
        // After refactor, replace with:
        //   Func<Task> act = () => sut.AddRemoteIfMissingAsync("C:\\repo", "https://example/repo.git");
        //   await act.Should().ThrowAsync<InvalidOperationException>();
        Assert.Inconclusive("Replace with exception verification once Local allows injecting ILocalLibGit2Client.");
    }
}



/// <summary>
/// Targeted tests for Local.UpdateDependenciesAsync. These tests are intentionally marked ignored
/// because Local constructs concrete collaborators internally (DependencyFileManager, LocalLibGit2Client),
/// preventing isolation and safe execution without real file system and git operations.
/// Guidance in each test explains how to proceed after refactoring Local for DI.
/// </summary>
public class LocalUpdateDependenciesAsyncTests
{
    /// <summary>
    /// Ensures UpdateDependenciesAsync can be invoked with no Arcade dependency present and that
    /// the method would proceed to update dependency files and commit.
    /// Inputs:
    ///  - dependencies: empty list (no Arcade item).
    ///  - remoteFactory: mock (unused).
    ///  - gitRepoFactory: mock (unused).
    ///  - barClient: mock (unused).
    /// Expected:
    ///  - Method completes without throwing.
    /// Notes:
    ///  - Ignored because Local tightly constructs DependencyFileManager and LocalLibGit2Client internally,
    ///    preventing proper mocking of file system and Git operations, which would attempt real IO.
    ///    To enable this test, refactor Local to accept these collaborators via DI.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Ignore("Cannot execute without refactoring Local to inject DependencyFileManager/ILocalLibGit2Client. This test documents expected behavior.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task UpdateDependenciesAsync_NoArcadeDependency_CommitsFilesAndDoesNotFetchEngCommon()
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        string tempRepo = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var dependencies = new List<DependencyDetail>(); // No Arcade item
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var gitRepoFactory = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);

        var sut = new Local(tokenProvider.Object, logger.Object, tempRepo);

        // Act
        await sut.UpdateDependenciesAsync(dependencies, remoteFactory.Object, gitRepoFactory.Object, barClient.Object);

        // Assert
        Assert.Inconclusive("Executed as documentation-only. Replace Ignore with proper DI-enabled execution once Local is refactored.");
    }

    /// <summary>
    /// Documents expected behavior when an Arcade dependency is present:
    ///  - For VMR (repoIsVmr == true), file paths from eng/common returned under 'src/arcade/' are stripped of that prefix.
    ///  - For non-VMR (repoIsVmr == false), paths are left unchanged.
    ///  - Files present locally but not in Arcade's eng/common at the specified SHA are scheduled for deletion.
    /// Inputs:
    ///  - dependencies: includes one DependencyDetail with Name == DependencyFileManager.ArcadeSdkPackageName.
    ///  - remoteFactory: mock IRemote returned; its GetCommonScriptFilesAsync returns eng/common files.
    ///  - gitRepoFactory: mock (used when Local creates a new DependencyFileManager internally).
    ///  - barClient: mock used by AssetLocationResolver.
    /// Expected:
    ///  - Files to update contain Arcade eng/common files (with VMR prefix stripped when applicable)
    ///    and deletions for local-only eng/common files.
    /// Notes:
    ///  - Ignored because Local constructs its own DependencyFileManager and performs real IO via _fileManager/_gitClient.
    ///    After refactoring Local to inject these dependencies:
    ///      1) Mock the inner DependencyFileManager.ReadToolsDotnetVersionAsync to throw DependencyFileNotFoundException
    ///         to simulate non-VMR and return a value otherwise to simulate VMR.
    ///      2) Mock IRemoteFactory.CreateRemoteAsync and IRemote.GetCommonScriptFilesAsync to return the desired files.
    ///      3) Mock internal _fileManager.UpdateDependencyFiles to return a GitFileContentContainer with initial files.
    ///      4) Verify CommitFilesAsync receives expected transformed additions and deletions.
    /// </summary>
    [TestCase(true, Description = "VMR: src/arcade/ prefix should be stripped from returned file paths.")]
    [TestCase(false, Description = "Non-VMR: returned file paths should remain unchanged.")]
    [Category("auto-generated")]
    [Ignore("Design prevents isolation. Refactor Local to inject file manager and git client to execute this test.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task UpdateDependenciesAsync_ArcadeDependency_EngCommonFilesHandledPerVmrMode(bool simulateVmr)
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        string tempRepo = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var arcade = BuildArcadeDependency(
            repoUri: "https://github.com/dotnet/arcade",
            commit: "0123456789abcdef0123456789abcdef01234567");

        var dependencies = new List<DependencyDetail> { arcade };

        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);
        // Example eng/common files from Arcade. In VMR mode, they come under src/arcade/ prefix.
        var remoteFiles = new List<GitFile>
        {
            new GitFile(simulateVmr ? "src/arcade/eng/common/tools.ps1" : "eng/common/tools.ps1", "echo 1", ContentEncoding.Utf8),
            new GitFile(simulateVmr ? "src/arcade/eng/common/tools.sh"  : "eng/common/tools.sh",  "echo 2", ContentEncoding.Utf8),
        };

        remoteMock
            .Setup(m => m.GetCommonScriptFilesAsync(arcade.RepoUri, arcade.Commit, simulateVmr ? VmrInfo.ArcadeRepoDir : null))
            .ReturnsAsync(remoteFiles);

        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        remoteFactory
            .Setup(f => f.CreateRemoteAsync(arcade.RepoUri))
            .ReturnsAsync(remoteMock.Object);

        var gitRepoFactory = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);

        var sut = new Local(tokenProvider.Object, logger.Object, tempRepo);

        // Act
        await sut.UpdateDependenciesAsync(dependencies, remoteFactory.Object, gitRepoFactory.Object, barClient.Object);

        // Assert
        Assert.Inconclusive("After DI refactor, verify CommitFilesAsync receives appropriately transformed eng/common files and deletions.");
    }

    /// <summary>
    /// Documents that when the remote returns a "Not Found" error during eng/common retrieval,
    /// Local swallows the exception via a filter and logs a warning, continuing the update flow.
    /// Inputs:
    ///  - dependencies: includes Arcade dependency.
    ///  - failOn: "create-remote" (CreateRemoteAsync throws) or "get-files" (GetCommonScriptFilesAsync throws).
    /// Expected:
    ///  - Warning is logged via ILogger.LogWarning and method continues to commit dependency file updates.
    /// Notes:
    ///  - Ignored pending DI refactor to inject and verify:
    ///      - ILogger.LogWarning called with the expected message.
    ///      - _gitClient.CommitFilesAsync invoked regardless of the Not Found error.
    /// </summary>
    [TestCase("create-remote")]
    [TestCase("get-files")]
    [Category("auto-generated")]
    [Ignore("Requires DI to verify logger interaction and avoid real IO. See XML comments for enablement steps.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task UpdateDependenciesAsync_RemoteReturnsNotFound_LogsWarningAndContinues(string failOn)
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        string tempRepo = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var arcade = BuildArcadeDependency(
            repoUri: "https://github.com/dotnet/arcade",
            commit: "0123456789abcdef0123456789abcdef01234567");

        var dependencies = new List<DependencyDetail> { arcade };

        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);

        if (failOn == "create-remote")
        {
            remoteFactory
                .Setup(f => f.CreateRemoteAsync(arcade.RepoUri))
                .ThrowsAsync(new Exception("Not Found"));
        }
        else
        {
            remoteFactory
                .Setup(f => f.CreateRemoteAsync(arcade.RepoUri))
                .ReturnsAsync(remoteMock.Object);

            remoteMock
                .Setup(m => m.GetCommonScriptFilesAsync(arcade.RepoUri, arcade.Commit, VmrInfo.ArcadeRepoDir))
                .ThrowsAsync(new Exception("Not Found"));
        }

        var gitRepoFactory = new Mock<IGitRepoFactory>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);

        var sut = new Local(tokenProvider.Object, logger.Object, tempRepo);

        // Act
        await sut.UpdateDependenciesAsync(dependencies, remoteFactory.Object, gitRepoFactory.Object, barClient.Object);

        // Assert
        Assert.Inconclusive("After DI refactor, verify LogWarning was called and CommitFilesAsync still executed.");
    }

    private static DependencyDetail BuildArcadeDependency(string repoUri, string commit)
    {
        return new DependencyDetail
        {
            Name = DependencyFileManager.ArcadeSdkPackageName,
            Version = "1.0.0",
            RepoUri = repoUri,
            Commit = commit,
            Pinned = false,
            SkipProperty = false,
            Type = DependencyType.Product
        };
    }
}



/// <summary>
/// Tests focused on the Local.Verify method behavior and delegation.
/// </summary>
public class LocalVerifyTests
{
}
