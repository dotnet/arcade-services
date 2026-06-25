// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

/// <summary>
/// Unit tests for <see cref="CodeflowSourceDiffVerifier.ForwardFlowMatchesSourceDiffAsync"/>. The git/VMR
/// interactions are mocked so each test drives the public verifier with hand-crafted
/// "source repo" and "VMR PR" diffs and asserts the verdict.
/// </summary>
[TestFixture]
internal class CodeflowSourceDiffVerifierTests
{
    private const string MappingName = "product-repo1";
    private const string SourceRepoUri = "https://github.com/dotnet/product-repo1";
    private const string VmrUri = "https://github.com/dotnet/dotnet";
    private const string OldSha = "aaaaaaa";
    private const string NewSha = "bbbbbbb";
    private const string VmrTargetBranch = "main";
    private const string VmrHeadBranch = "pr-branch";

    // VmrInfo.GetRelativeRepoSourcesPath(MappingName) == "src/product-repo1".
    private const string SrcMappingPath = "src/" + MappingName;
    private const string VmrPrefix = SrcMappingPath + "/";

    private Mock<IVmrCloneManager> _vmrCloneManager = null!;
    private Mock<IRepositoryCloneManager> _cloneManager = null!;
    private Mock<IVmrDependencyTracker> _dependencyTracker = null!;
    private Mock<ISourceManifest> _sourceManifest = null!;
    private Mock<ILocalGitRepo> _sourceRepo = null!;
    private Mock<ILocalGitRepo> _vmr = null!;
    private CodeflowSourceDiffVerifier _verifier = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepo = new Mock<ILocalGitRepo>();
        _vmr = new Mock<ILocalGitRepo>();

        var mapping = new SourceMapping(
            Name: MappingName,
            DefaultRemote: SourceRepoUri,
            DefaultRef: "main",
            Include: [],
            Exclude: [],
            DisableSynchronization: false);

        _dependencyTracker = new Mock<IVmrDependencyTracker>();
        _dependencyTracker.Setup(t => t.GetMapping(MappingName)).Returns(mapping);

        _sourceManifest = new Mock<ISourceManifest>();
        _sourceManifest.Setup(m => m.Submodules).Returns([]);

        _vmrCloneManager = new Mock<IVmrCloneManager>();
        _vmrCloneManager
            .Setup(m => m.PrepareVmrAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _vmr.Object);

        _cloneManager = new Mock<IRepositoryCloneManager>();
        _cloneManager
            .Setup(m => m.PrepareCloneAsync(
                It.IsAny<SourceMapping>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _sourceRepo.Object);

        _verifier = new CodeflowSourceDiffVerifier(
            _vmrCloneManager.Object,
            _cloneManager.Object,
            _dependencyTracker.Object,
            _sourceManifest.Object,
            NullLogger<CodeflowSourceDiffVerifier>.Instance);
    }

    private Task<bool> VerifyAsync() => _verifier.ForwardFlowMatchesSourceDiffAsync(
        SourceRepoUri,
        VmrUri,
        MappingName,
        OldSha,
        NewSha,
        VmrTargetBranch,
        VmrHeadBranch,
        CancellationToken.None);

    /// <summary>
    /// Configures a mocked repo's "git diff" calls. <paramref name="nameOnlyOutput"/> is returned for the
    /// "diff --name-only" call (the changed file listing); <paramref name="fileDiffs"/> maps the file argument
    /// of a "diff -U0 ... -- &lt;file&gt;" call to that file's zero-context diff text.
    /// </summary>
    private static void SetupDiffs(
        Mock<ILocalGitRepo> repo,
        string nameOnlyOutput,
        IReadOnlyDictionary<string, string> fileDiffs)
    {
        repo.Setup(r => r.ExecuteGitCommand(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string[] args, CancellationToken _) =>
            {
                var output = args.Contains("--name-only")
                    ? nameOnlyOutput
                    : fileDiffs.GetValueOrDefault(args[^1], string.Empty);

                return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = output });
            });
    }

    /// <summary>
    /// Builds a realistic "git diff -U0" output for a single-line change of <paramref name="path"/>.
    /// The removed/added content lines are passed verbatim (already including any leading characters of the
    /// actual file content, e.g. "-- old" for a SQL comment).
    /// </summary>
    private static string SingleLineDiff(string path, string removedLine, string addedLine) => string.Join('\n',
    [
        $"diff --git a/{path} b/{path}",
        "index 1111111..2222222 100644",
        $"--- a/{path}",
        $"+++ b/{path}",
        "@@ -1 +1 @@",
        "-" + removedLine,
        "+" + addedLine,
    ]);

    /// <summary>
    /// Builds a "git diff -U0" output for a file changed in two separate places. With zero context each
    /// change region is its own hunk, so this exercises multi-hunk parsing.
    /// </summary>
    private static string TwoHunkDiff(
        string path,
        (string Removed, string Added) firstHunk,
        (string Removed, string Added) secondHunk) => string.Join('\n',
    [
        $"diff --git a/{path} b/{path}",
        "index 1111111..3333333 100644",
        $"--- a/{path}",
        $"+++ b/{path}",
        "@@ -1 +1 @@",
        "-" + firstHunk.Removed,
        "+" + firstHunk.Added,
        "@@ -5 +5 @@",
        "-" + secondHunk.Removed,
        "+" + secondHunk.Added,
    ]);

    [Test]
    public async Task ForwardFlowMatchesSourceDiffAsync_PrMatchesSourceDiff_ReturnsTrue()
    {
        // Arrange: the source repo and the VMR PR change the same file in the same way.
        SetupDiffs(_sourceRepo, "data.txt", new Dictionary<string, string>
        {
            ["data.txt"] = SingleLineDiff("data.txt", "old content", "new content"),
        });
        SetupDiffs(_vmr, VmrPrefix + "data.txt", new Dictionary<string, string>
        {
            [VmrPrefix + "data.txt"] = SingleLineDiff(VmrPrefix + "data.txt", "old content", "new content"),
        });

        // Act
        var result = await VerifyAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public async Task ForwardFlowMatchesSourceDiffAsync_PrChangesFileNotInSourceDiff_ReturnsFalse()
    {
        // Arrange: the PR touches an extra file the source diff never changed.
        SetupDiffs(_sourceRepo, "data.txt", new Dictionary<string, string>
        {
            ["data.txt"] = SingleLineDiff("data.txt", "old content", "new content"),
        });
        SetupDiffs(_vmr, string.Join('\n', VmrPrefix + "data.txt", VmrPrefix + "extra.txt"), new Dictionary<string, string>());

        // Act
        var result = await VerifyAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public async Task ForwardFlowMatchesSourceDiffAsync_PrChangesFileDifferently_ReturnsFalse()
    {
        // Arrange: the same file is changed in both, but to different content.
        SetupDiffs(_sourceRepo, "data.txt", new Dictionary<string, string>
        {
            ["data.txt"] = SingleLineDiff("data.txt", "old content", "new content"),
        });
        SetupDiffs(_vmr, VmrPrefix + "data.txt", new Dictionary<string, string>
        {
            [VmrPrefix + "data.txt"] = SingleLineDiff(VmrPrefix + "data.txt", "old content", "tampered content"),
        });

        // Act
        var result = await VerifyAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public async Task ForwardFlowMatchesSourceDiffAsync_SourceOnlyChangeAlreadyReconciledInVmr_ReturnsTrue()
    {
        // Arrange: the source changed two files but the PR only changed one. The other (readme.md) is a
        // legitimate no-op because the VMR copy already holds the source's new content.
        SetupDiffs(_sourceRepo, string.Join('\n', "data.txt", "readme.md"), new Dictionary<string, string>
        {
            ["data.txt"] = SingleLineDiff("data.txt", "old content", "new content"),
        });
        SetupDiffs(_vmr, VmrPrefix + "data.txt", new Dictionary<string, string>
        {
            [VmrPrefix + "data.txt"] = SingleLineDiff(VmrPrefix + "data.txt", "old content", "new content"),
        });

        _sourceRepo.Setup(r => r.GetFileFromGitAsync("readme.md", NewSha, null)).ReturnsAsync("identical readme");
        _vmr.Setup(r => r.GetFileFromGitAsync(VmrPrefix + "readme.md", VmrHeadBranch, null)).ReturnsAsync("identical readme");

        // Act
        var result = await VerifyAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public async Task ForwardFlowMatchesSourceDiffAsync_PrTampersWithDashDashCommentLine_ReturnsFalse()
    {
        // Arrange: a SQL file whose comment line is changed.
        //   Source repo:  "-- old"       -> "-- new"
        //   VMR PR:       "-- DIFFERENT" -> "-- new"
        // The PR did NOT faithfully reproduce the source change (it removed a different line), so the
        // verifier must reject the PR. In a -U0 diff the removed content lines render as "--- old" /
        // "--- DIFFERENT", which share a prefix with the "--- " file header; GetChangeLines must still
        // tell them apart (it parses hunks structurally) so the tampering is detected.
        SetupDiffs(_sourceRepo, "schema.sql", new Dictionary<string, string>
        {
            ["schema.sql"] = SingleLineDiff("schema.sql", "-- old", "-- new"),
        });
        SetupDiffs(_vmr, VmrPrefix + "schema.sql", new Dictionary<string, string>
        {
            [VmrPrefix + "schema.sql"] = SingleLineDiff(VmrPrefix + "schema.sql", "-- DIFFERENT", "-- new"),
        });

        // Act
        var result = await VerifyAsync();

        // Assert
        result.Should().BeFalse("the PR removed a different comment line than the source diff and must be rejected");
    }

    [Test]
    public async Task ForwardFlowMatchesSourceDiffAsync_MultiHunkFileWithMatchingChanges_ReturnsTrue()
    {
        // Arrange: a file changed in two separate places (two hunks) identically in source and PR.
        SetupDiffs(_sourceRepo, "data.txt", new Dictionary<string, string>
        {
            ["data.txt"] = TwoHunkDiff("data.txt", ("first old", "first new"), ("second old", "second new")),
        });
        SetupDiffs(_vmr, VmrPrefix + "data.txt", new Dictionary<string, string>
        {
            [VmrPrefix + "data.txt"] = TwoHunkDiff(VmrPrefix + "data.txt", ("first old", "first new"), ("second old", "second new")),
        });

        // Act
        var result = await VerifyAsync();

        // Assert
        result.Should().BeTrue();
    }
}
