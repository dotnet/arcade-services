// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Maestro.Common;
using Maestro.Common.Telemetry;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;

/// <summary>
/// Tests for <see cref="LocalGitClient.ResolveConflict"/>.
///
/// Each test creates a fresh real git repository in a temp directory and crafts
/// the unmerged index entries (stages 1/2/3) using git plumbing
/// (<c>git hash-object</c> + <c>git update-index --index-info</c>) to produce
/// each of the documented unmerged status codes:
///
///   DD - both deleted
///   AU - added by us
///   UD - modified by us, deleted by them
///   UA - added by them
///   DU - deleted by us, modified by them
///   AA - both added
///   UU - both modified
///
/// We verify that <c>git status --porcelain=v1</c> reports the expected code,
/// then run <c>ResolveConflict</c> with both <c>ours: true</c> and
/// <c>ours: false</c>, and assert the resulting index/working-tree state.
/// </summary>
[TestFixture]
public class LocalGitClientResolveConflictTests
{
    private const string ConflictFile = "conflict.txt";
    private const string BaseContent = "base-content\n";
    private const string OursContent = "ours-content\n";
    private const string TheirsContent = "theirs-content\n";

    private string _repoPath = null!;
    private IProcessManager _processManager = null!;
    private LocalGitClient _gitClient = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), $"darclib-resolveconflict-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_repoPath);

        _processManager = new ProcessManager(NullLogger<ProcessManager>.Instance, "git");
        _gitClient = new LocalGitClient(
            new RemoteTokenProvider(),
            new NoTelemetryRecorder(),
            _processManager,
            new FileSystem(),
            NullLogger<LocalGitClient>.Instance);

        // Initialize the repo and make an initial commit so HEAD exists.
        (await _processManager.ExecuteGit(_repoPath, "init", "-b", "main"))
            .ThrowIfFailed("git init failed");
        (await _processManager.ExecuteGit(_repoPath, "config", "user.email", "test@example.com"))
            .ThrowIfFailed("git config user.email failed");
        (await _processManager.ExecuteGit(_repoPath, "config", "user.name", "Test"))
            .ThrowIfFailed("git config user.name failed");
        (await _processManager.ExecuteGit(_repoPath, "config", "commit.gpgsign", "false"))
            .ThrowIfFailed("git config commit.gpgsign failed");
        // Disable line-ending conversion so blob contents on disk match exactly
        // (otherwise on Windows `git checkout --ours/--theirs` converts LF -> CRLF).
        (await _processManager.ExecuteGit(_repoPath, "config", "core.autocrlf", "false"))
            .ThrowIfFailed("git config core.autocrlf failed");

        var readmePath = Path.Combine(_repoPath, "README.md");
        await File.WriteAllTextAsync(readmePath, "test\n");
        (await _processManager.ExecuteGit(_repoPath, "add", "README.md"))
            .ThrowIfFailed("git add failed");
        (await _processManager.ExecuteGit(_repoPath, "commit", "-m", "initial"))
            .ThrowIfFailed("git commit failed");
    }

    [TearDown]
    public void TearDown()
    {
        if (string.IsNullOrEmpty(_repoPath) || !Directory.Exists(_repoPath))
        {
            return;
        }

        try
        {
            // .git contains read-only files (pack files, etc.) - clear attributes before delete.
            var directory = new DirectoryInfo(_repoPath);
            foreach (var file in directory.GetFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }
            Directory.Delete(_repoPath, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors - tests can leave behind temp dirs occasionally.
        }
    }

    // The conflict-marker mush a real merge would leave on disk in any UU/AA-
    // shaped conflict. The exact contents don't matter for the SUT - we just
    // need *some* file there to mimic what `git merge` produces.
    private static string ConflictMarkerContent =>
        "<<<<<<<\n" + OursContent + "=======\n" + TheirsContent + ">>>>>>>\n";

    // === DD: both deleted ===

    [Test]
    public Task ResolveConflict_DD_Ours_RemovesFile() => VerifyDdAsync(ours: true);

    [Test]
    public Task ResolveConflict_DD_Theirs_RemovesFile() => VerifyDdAsync(ours: false);

    private async Task VerifyDdAsync(bool ours)
    {
        await SetUpConflictAsync(
            expectedStatusCode: "DD",
            baseStage: BaseContent,
            // Both branches deleted the file - no ours, no theirs, no working-tree file.
            oursStage: null,
            theirsStage: null,
            workingTree: null);

        await _gitClient.ResolveConflict(_repoPath, ConflictFile, ours);

        await AssertResultAsync(expectFile: false, expectedContent: null);
    }

    // === AU: added by us only ===

    [Test]
    public Task ResolveConflict_AU_Ours_KeepsOurContent()
        => VerifyAuAsync(ours: true, expectFile: true, expectedContent: OursContent);

    [Test]
    public Task ResolveConflict_AU_Theirs_RemovesFile()
        => VerifyAuAsync(ours: false, expectFile: false, expectedContent: null);

    private async Task VerifyAuAsync(bool ours, bool expectFile, string? expectedContent)
    {
        await SetUpConflictAsync(
            expectedStatusCode: "AU",
            baseStage: null,
            oursStage: OursContent,
            theirsStage: null,
            // Our side added the file, so git leaves our content in the working tree.
            workingTree: OursContent);

        await _gitClient.ResolveConflict(_repoPath, ConflictFile, ours);

        await AssertResultAsync(expectFile, expectedContent);
    }

    // === UA: added by them only ===

    [Test]
    public Task ResolveConflict_UA_Ours_RemovesFile()
        => VerifyUaAsync(ours: true, expectFile: false, expectedContent: null);

    [Test]
    public Task ResolveConflict_UA_Theirs_KeepsTheirContent()
        => VerifyUaAsync(ours: false, expectFile: true, expectedContent: TheirsContent);

    private async Task VerifyUaAsync(bool ours, bool expectFile, string? expectedContent)
    {
        await SetUpConflictAsync(
            expectedStatusCode: "UA",
            baseStage: null,
            oursStage: null,
            theirsStage: TheirsContent,
            // Their side added the file, so git leaves their content in the working tree.
            workingTree: TheirsContent);

        await _gitClient.ResolveConflict(_repoPath, ConflictFile, ours);

        await AssertResultAsync(expectFile, expectedContent);
    }

    // === UD: modified by us, deleted by them ===

    [Test]
    public Task ResolveConflict_UD_Ours_KeepsOurContent()
        => VerifyUdAsync(ours: true, expectFile: true, expectedContent: OursContent);

    [Test]
    public Task ResolveConflict_UD_Theirs_RemovesFile()
        => VerifyUdAsync(ours: false, expectFile: false, expectedContent: null);

    private async Task VerifyUdAsync(bool ours, bool expectFile, string? expectedContent)
    {
        await SetUpConflictAsync(
            expectedStatusCode: "UD",
            baseStage: BaseContent,
            oursStage: OursContent,
            theirsStage: null,
            // We modified it, they deleted it - git keeps our version on disk.
            workingTree: OursContent);

        await _gitClient.ResolveConflict(_repoPath, ConflictFile, ours);

        await AssertResultAsync(expectFile, expectedContent);
    }

    // === DU: deleted by us, modified by them ===

    [Test]
    public Task ResolveConflict_DU_Ours_RemovesFile()
        => VerifyDuAsync(ours: true, expectFile: false, expectedContent: null);

    [Test]
    public Task ResolveConflict_DU_Theirs_KeepsTheirContent()
        => VerifyDuAsync(ours: false, expectFile: true, expectedContent: TheirsContent);

    private async Task VerifyDuAsync(bool ours, bool expectFile, string? expectedContent)
    {
        await SetUpConflictAsync(
            expectedStatusCode: "DU",
            baseStage: BaseContent,
            oursStage: null,
            theirsStage: TheirsContent,
            // We deleted it, they modified it - git restores their version on disk.
            workingTree: TheirsContent);

        await _gitClient.ResolveConflict(_repoPath, ConflictFile, ours);

        await AssertResultAsync(expectFile, expectedContent);
    }

    // === UU: both modified ===

    [Test]
    public Task ResolveConflict_UU_Ours_KeepsOurContent()
        => VerifyUuAsync(ours: true, expectedContent: OursContent);

    [Test]
    public Task ResolveConflict_UU_Theirs_KeepsTheirContent()
        => VerifyUuAsync(ours: false, expectedContent: TheirsContent);

    private async Task VerifyUuAsync(bool ours, string expectedContent)
    {
        await SetUpConflictAsync(
            expectedStatusCode: "UU",
            baseStage: BaseContent,
            oursStage: OursContent,
            theirsStage: TheirsContent,
            workingTree: ConflictMarkerContent);

        await _gitClient.ResolveConflict(_repoPath, ConflictFile, ours);

        await AssertResultAsync(expectFile: true, expectedContent);
    }

    // === AA: both added ===

    [Test]
    public Task ResolveConflict_AA_Ours_KeepsOurContent()
        => VerifyAaAsync(ours: true, expectedContent: OursContent);

    [Test]
    public Task ResolveConflict_AA_Theirs_KeepsTheirContent()
        => VerifyAaAsync(ours: false, expectedContent: TheirsContent);

    private async Task VerifyAaAsync(bool ours, string expectedContent)
    {
        await SetUpConflictAsync(
            expectedStatusCode: "AA",
            // No base - both sides independently added a new file at this path.
            baseStage: null,
            oursStage: OursContent,
            theirsStage: TheirsContent,
            workingTree: ConflictMarkerContent);

        await _gitClient.ResolveConflict(_repoPath, ConflictFile, ours);

        await AssertResultAsync(expectFile: true, expectedContent);
    }

    // === Helpers ===

    /// <summary>
    /// Builds an unmerged index entry for <see cref="ConflictFile"/> with the
    /// requested per-stage contents and working-tree content, then verifies
    /// <c>git status --porcelain=v1</c> reports <paramref name="expectedStatusCode"/>.
    ///
    /// Each stage parameter (base / ours / theirs) installs a blob at stage
    /// 1 / 2 / 3 respectively, or omits that stage when null. The working-tree
    /// parameter writes <see cref="ConflictFile"/> on disk, or leaves it
    /// missing when null. Together this lets each test declare exactly the
    /// shape of unmerged entry it wants without hand-crafting
    /// <c>update-index --index-info</c> lines.
    /// </summary>
    private async Task SetUpConflictAsync(
        string expectedStatusCode,
        string? baseStage,
        string? oursStage,
        string? theirsStage,
        string? workingTree)
    {
        var indexInfo = new StringBuilder();
        if (baseStage is not null)
        {
            indexInfo.Append($"100644 {await CreateBlobAsync(baseStage)} 1\t{ConflictFile}\n");
        }
        if (oursStage is not null)
        {
            indexInfo.Append($"100644 {await CreateBlobAsync(oursStage)} 2\t{ConflictFile}\n");
        }
        if (theirsStage is not null)
        {
            indexInfo.Append($"100644 {await CreateBlobAsync(theirsStage)} 3\t{ConflictFile}\n");
        }

        var updateIndex = await _processManager.ExecuteGit(
            _repoPath,
            ["update-index", "--index-info"],
            standardInput: indexInfo.ToString());
        updateIndex.ThrowIfFailed("git update-index --index-info failed");

        var diskPath = Path.Combine(_repoPath, ConflictFile);
        if (workingTree is not null)
        {
            await File.WriteAllTextAsync(diskPath, workingTree);
        }
        else if (File.Exists(diskPath))
        {
            File.Delete(diskPath);
        }

        await AssertStatusCodeAsync(expectedStatusCode);
    }

    /// <summary>
    /// Creates a blob in the object database with the given content (without
    /// touching the working tree's <see cref="ConflictFile"/>) and returns its
    /// SHA - the address we use to install that blob at a specific stage in
    /// the index via <c>update-index --index-info</c>.
    /// </summary>
    private async Task<string> CreateBlobAsync(string content)
    {
        var tempPath = Path.Combine(_repoPath, $".hash-tmp-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(tempPath, content);
        try
        {
            var result = await _processManager.ExecuteGit(_repoPath, "hash-object", "-w", tempPath);
            result.ThrowIfFailed("git hash-object failed");
            return result.StandardOutput.Trim();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private async Task AssertStatusCodeAsync(string expectedCode)
    {
        var status = await _processManager.ExecuteGit(_repoPath, "status", "--porcelain=v1", "--", ConflictFile);
        status.ThrowIfFailed("git status failed");

        var line = status.GetOutputLines().FirstOrDefault();
        line.Should().NotBeNullOrEmpty($"expected an unmerged status entry for {ConflictFile}");
        line.Substring(0, 2).Should().Be(expectedCode);
    }

    private async Task AssertResultAsync(bool expectFile, string? expectedContent)
    {
        var ls = await _processManager.ExecuteGit(_repoPath, "ls-files", "--stage", "--", ConflictFile);
        ls.ThrowIfFailed("git ls-files failed");
        var stageLines = ls.GetOutputLines();

        var fileOnDisk = Path.Combine(_repoPath, ConflictFile);

        if (expectFile)
        {
            stageLines.Should().HaveCount(1, "the conflict should be resolved with a single staged entry");
            var stageLine = stageLines.First();
            stageLine.Should().StartWith("100644");
            // Stage column is the third whitespace-separated token before the tab-separated path.
            stageLine.Should().Contain(" 0\t", "the entry should be at stage 0 (merged)");

            File.Exists(fileOnDisk).Should().BeTrue();
            (await File.ReadAllTextAsync(fileOnDisk)).Should().Be(expectedContent);
        }
        else
        {
            stageLines.Should().BeEmpty("the file should have been removed from the index");
            File.Exists(fileOnDisk).Should().BeFalse("the file should have been removed from the working tree");
        }
    }
}
