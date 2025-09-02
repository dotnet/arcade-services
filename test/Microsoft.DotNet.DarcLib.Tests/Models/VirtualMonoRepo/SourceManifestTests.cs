// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo.UnitTests;

public class SourceManifestTests
{
    /// <summary>
    /// Validates that ToJson returns an indented, camelCase JSON with expected repositories and submodules content.
    /// This parameterized test covers:
    /// - Empty manifest (no repositories/submodules).
    /// - Repository entries with barId = null and boundary values (int.MinValue, 0, int.MaxValue).
    /// - Submodule entries with various string values (including empty, whitespace, long, and special characters).
    /// Expected: JSON contains "repositories" and "submodules" arrays with matching entries and indented formatting.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(ToJson_Scenarios))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ToJson_VariousStates_ProducesExpectedJson(
        List<RepoUpdate> repoUpdates,
        List<SubUpdate> subUpdates)
    {
        // Arrange
        var sut = SourceManifest.FromJson("{}");

        foreach (var ru in repoUpdates)
        {
            sut.UpdateVersion(ru.Path, ru.RemoteUri, ru.CommitSha, ru.BarId);
        }

        foreach (var su in subUpdates)
        {
            var subMock = new Mock<ISourceComponent>(MockBehavior.Strict);
            subMock.SetupGet(x => x.Path).Returns(su.Path);
            subMock.SetupGet(x => x.RemoteUri).Returns(su.RemoteUri);
            subMock.SetupGet(x => x.CommitSha).Returns(su.CommitSha);

            sut.UpdateSubmodule(subMock.Object);
        }

        // Act
        var json = sut.ToJson();

        // Assert
        json.Should().NotBeNullOrWhiteSpace();
        json.Should().Contain(Environment.NewLine); // WriteIndented = true

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("repositories", out var reposProp).Should().BeTrue();
        reposProp.ValueKind.Should().Be(JsonValueKind.Array);
        reposProp.GetArrayLength().Should().Be(repoUpdates.Count);

        root.TryGetProperty("submodules", out var subsProp).Should().BeTrue();
        subsProp.ValueKind.Should().Be(JsonValueKind.Array);
        subsProp.GetArrayLength().Should().Be(subUpdates.Count);

        foreach (var expectedRepo in repoUpdates)
        {
            var found = FindByPath(reposProp, expectedRepo.Path);
            found.HasValue.Should().BeTrue();

            var repoElem = found.Value;
            GetString(repoElem, "path").Should().Be(expectedRepo.Path);
            GetString(repoElem, "remoteUri").Should().Be(expectedRepo.RemoteUri);
            GetString(repoElem, "commitSha").Should().Be(expectedRepo.CommitSha);

            if (expectedRepo.BarId.HasValue)
            {
                repoElem.TryGetProperty("barId", out var barIdProp).Should().BeTrue();
                barIdProp.GetInt32().Should().Be(expectedRepo.BarId.Value);
            }
        }

        foreach (var expectedSub in subUpdates)
        {
            var found = FindByPath(subsProp, expectedSub.Path);
            found.HasValue.Should().BeTrue();

            var subElem = found.Value;
            GetString(subElem, "path").Should().Be(expectedSub.Path);
            GetString(subElem, "remoteUri").Should().Be(expectedSub.RemoteUri);
            GetString(subElem, "commitSha").Should().Be(expectedSub.CommitSha);
        }
    }

    private static (bool HasValue, JsonElement Value) FindByPath(JsonElement array, string path)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String && p.GetString() == path)
            {
                return (true, item);
            }
        }

        return (false, default);
    }

    private static string GetString(JsonElement obj, string propertyName)
    {
        obj.TryGetProperty(propertyName, out var prop).Should().BeTrue();
        prop.ValueKind.Should().Be(JsonValueKind.String);
        return prop.GetString();
    }

    public static IEnumerable ToJson_Scenarios()
    {
        // Empty manifest
        yield return new TestCaseData(
            new List<RepoUpdate>(),
            new List<SubUpdate>())
        .SetName("ToJson_EmptyManifest_ProducesEmptyArrays");

        // Single repository with null barId
        yield return new TestCaseData(
            new List<RepoUpdate>
            {
                    new RepoUpdate("repo-A", "https://example.com/a.git", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null)
            },
            new List<SubUpdate>())
        .SetName("ToJson_SingleRepository_NullBarId_ContainsExpectedFields");

        // Single repository with boundary barIds
        yield return new TestCaseData(
            new List<RepoUpdate>
            {
                    new RepoUpdate("r-min", "https://x/y.git", "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef", int.MinValue),
            },
            new List<SubUpdate>())
        .SetName("ToJson_SingleRepository_BarIdMinValue");

        yield return new TestCaseData(
            new List<RepoUpdate>
            {
                    new RepoUpdate("r-zero", "ssh://host/repo", "0000000000000000000000000000000000000000", 0),
            },
            new List<SubUpdate>())
        .SetName("ToJson_SingleRepository_BarIdZero");

        yield return new TestCaseData(
            new List<RepoUpdate>
            {
                    new RepoUpdate("r-max", "file:///C:/repo", "ffffffffffffffffffffffffffffffffffffffff", int.MaxValue),
            },
            new List<SubUpdate>())
        .SetName("ToJson_SingleRepository_BarIdMaxValue");

        // Single submodule with special and whitespace values
        yield return new TestCaseData(
            new List<RepoUpdate>(),
            new List<SubUpdate>
            {
                    new SubUpdate("repo/sub 1", " git@github.com:org/repo.git ", "abc123\t\n\r"),
            })
        .SetName("ToJson_SingleSubmodule_SpecialAndWhitespaceValues");

        // Mixed: repo + submodule with long and special values
        var longName = new string('x', 1024);
        yield return new TestCaseData(
            new List<RepoUpdate>
            {
                    new RepoUpdate(longName, "https://example.org/r.git?param=1&x=%20", "1234567890abcdef1234567890abcdef12345678", 42)
            },
            new List<SubUpdate>
            {
                    new SubUpdate($"{longName}/sub", "ssh://user@host:22/~/r", "beadbeadbeadbeadbeadbeadbeadbeadbeadbead")
            })
        .SetName("ToJson_Mixed_LongNamesAndSpecialCharacters");
    }

    public class RepoUpdate
    {
        public RepoUpdate(string path, string remoteUri, string commitSha, int? barId)
        {
            Path = path;
            RemoteUri = remoteUri;
            CommitSha = commitSha;
            BarId = barId;
        }

        public string Path { get; }
        public string RemoteUri { get; }
        public string CommitSha { get; }
        public int? BarId { get; }
    }

    public class SubUpdate
    {
        public SubUpdate(string path, string remoteUri, string commitSha)
        {
            Path = path;
            RemoteUri = remoteUri;
            CommitSha = commitSha;
        }

        public string Path { get; }
        public string RemoteUri { get; }
        public string CommitSha { get; }
    }

    /// <summary>
    /// Verifies that RemoveSubmodule removes an existing submodule when a submodule with the same Path is passed.
    /// Input variations include: normal path, whitespace-only path, empty string path, and special-character path.
    /// Expected: The matching submodule is removed (count decreases by 1), the removed path is no longer present,
    /// and repositories remain unchanged.
    /// </summary>
    [TestCase("src/A")]
    [TestCase("   ")]
    [TestCase("")]
    [TestCase("src/special/#%&! path")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void RemoveSubmodule_ExistingPath_RemovesMatching(string pathToRemove)
    {
        // Arrange
        var repositories = new List<RepositoryRecord>
            {
                new RepositoryRecord("repo/one", "https://example.org/r1.git", "sha1", 1),
                new RepositoryRecord("repo/two", "https://example.org/r2.git", "sha2", null),
            };

        var submodules = new List<SubmoduleRecord>
            {
                new SubmoduleRecord("src/A", "https://example.org/a.git", "a1"),
                new SubmoduleRecord("src/B", "https://example.org/b.git", "b1"),
                new SubmoduleRecord("   ", "https://example.org/ws.git", "w1"),
                new SubmoduleRecord("", "https://example.org/empty.git", "e1"),
                new SubmoduleRecord("src/special/#%&! path", "https://example.org/s.git", "s1"),
            };

        var sut = new SourceManifest(repositories, submodules);
        var originalRepoCount = sut.Repositories.Count;
        var originalSubCount = sut.Submodules.Count;
        var input = new SubmoduleRecord(pathToRemove, "https://different.example.org/ignored.git", "ignored-sha");

        // Act
        sut.RemoveSubmodule(input);

        // Assert
        sut.Submodules.Count.Should().Be(originalSubCount - 1);
        sut.Submodules.Any(s => s.Path == pathToRemove).Should().BeFalse();
        sut.Repositories.Count.Should().Be(originalRepoCount);
    }

    /// <summary>
    /// Verifies that RemoveSubmodule does nothing when the provided submodule's Path does not exist in the manifest.
    /// Input variations include: unknown path, case-mismatched path, long path, and different whitespace.
    /// Expected: No removal occurs (count unchanged) and all original submodule paths remain present.
    /// </summary>
    [TestCase("src/C")]
    [TestCase("src/a")] // case mismatch with "src/A"
    [TestCase("this/is/a/very/long/path/that/does/not/exist/in/the/submodules/and/should/not/remove/anything")]
    [TestCase(" \t")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void RemoveSubmodule_NonExistingPath_DoesNothing(string nonExistingPath)
    {
        // Arrange
        var repositories = new List<RepositoryRecord>
            {
                new RepositoryRecord("repo/one", "https://example.org/r1.git", "sha1", 1),
                new RepositoryRecord("repo/two", "https://example.org/r2.git", "sha2", null),
            };

        var submodules = new List<SubmoduleRecord>
            {
                new SubmoduleRecord("src/A", "https://example.org/a.git", "a1"),
                new SubmoduleRecord("src/B", "https://example.org/b.git", "b1"),
                new SubmoduleRecord("   ", "https://example.org/ws.git", "w1"),
                new SubmoduleRecord("", "https://example.org/empty.git", "e1"),
                new SubmoduleRecord("src/special/#%&! path", "https://example.org/s.git", "s1"),
            };

        var expectedPaths = submodules.Select(s => s.Path).ToList();

        var sut = new SourceManifest(repositories, submodules);
        var originalRepoCount = sut.Repositories.Count;
        var originalSubCount = sut.Submodules.Count;
        var input = new SubmoduleRecord(nonExistingPath, "https://irrelevant.example.org/ignored.git", "ignored-sha");

        // Act
        sut.RemoveSubmodule(input);

        // Assert
        sut.Submodules.Count.Should().Be(originalSubCount);
        sut.Repositories.Count.Should().Be(originalRepoCount);
        foreach (var expectedPath in expectedPaths)
        {
            sut.Submodules.Any(s => s.Path == expectedPath).Should().BeTrue();
        }
    }

    /// <summary>
    /// Verifies that RemoveSubmodule behaves correctly with an empty submodule collection.
    /// Input: any path when Submodules is empty.
    /// Expected: No exception and submodule collection remains empty.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void RemoveSubmodule_EmptySubmodules_KeepsEmpty()
    {
        // Arrange
        var repositories = new List<RepositoryRecord>
            {
                new RepositoryRecord("repo/one", "https://example.org/r1.git", "sha1", 1),
            };

        var submodules = new List<SubmoduleRecord>(); // empty
        var sut = new SourceManifest(repositories, submodules);
        var input = new SubmoduleRecord("any/path", "https://example.org/x.git", "x1");

        // Act
        sut.RemoveSubmodule(input);

        // Assert
        sut.Submodules.Count.Should().Be(0);
        sut.Repositories.Count.Should().Be(1);
    }

    /// <summary>
    /// Verifies that when updating an existing repository with a null barId, the existing BarId is preserved
    /// while CommitSha and RemoteUri are updated.
    /// Inputs:
    /// - repository: fixed path "repo-A"
    /// - uri: "new://uri"
    /// - sha: "new-sha"
    /// - barId: null
    /// Expected:
    /// - Existing record's RemoteUri and CommitSha updated to the new values
    /// - Existing BarId remains unchanged (preserved from the original record)
    /// - Repository count remains unchanged
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void UpdateVersion_ExistingRepositoryWithNullBarId_DoesNotOverrideBarId()
    {
        // Arrange
        var initial = new[]
        {
                new RepositoryRecord("repo-A", "old://uri", "old-sha", 123)
            };
        var sut = new SourceManifest(initial, Array.Empty<SubmoduleRecord>());

        // Act
        sut.UpdateVersion("repo-A", "new://uri", "new-sha", null);

        // Assert
        var record = sut.GetRepositoryRecord("repo-A");
        record.RemoteUri.Should().Be("new://uri");
        record.CommitSha.Should().Be("new-sha");
        record.BarId.Should().Be(123);

        sut.Repositories.Count.Should().Be(1);
    }

    /// <summary>
    /// Ensures that updating an existing repository with a non-null barId overwrites BarId
    /// and updates both RemoteUri and CommitSha.
    /// Inputs:
    /// - repository: fixed path "repo-B"
    /// - uri: "updated://uri"
    /// - sha: "updated-sha"
    /// - barId: parameterized across extremes and boundary values
    /// Expected:
    /// - Existing record has RemoteUri and CommitSha updated
    /// - BarId is set to the provided barId argument
    /// - Repository count remains unchanged
    /// </summary>
    [TestCaseSource(nameof(UpdateVersion_ExistingRepositoryWithBarId_UpdatesAllFields_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void UpdateVersion_ExistingRepositoryWithBarId_UpdatesAllFields(int barId)
    {
        // Arrange
        var initial = new[]
        {
                new RepositoryRecord("repo-B", "initial://uri", "initial-sha", 11)
            };
        var sut = new SourceManifest(initial, Array.Empty<SubmoduleRecord>());

        // Act
        sut.UpdateVersion("repo-B", "updated://uri", "updated-sha", barId);

        // Assert
        var record = sut.GetRepositoryRecord("repo-B");
        record.RemoteUri.Should().Be("updated://uri");
        record.CommitSha.Should().Be("updated-sha");
        record.BarId.Should().Be(barId);

        sut.Repositories.Count.Should().Be(1);
    }

    /// <summary>
    /// Validates that when updating a non-existing repository, a new RepositoryRecord is added
    /// with the provided path, uri, sha, and barId.
    /// Inputs (parameterized):
    /// - repository: includes empty, whitespace, long, and special-character paths
    /// - uri: includes empty, whitespace, long, and special-character URIs
    /// - sha: includes empty, whitespace, long, and special-character SHAs
    /// - barId: null and boundary integer values
    /// Expected:
    /// - Exactly one repository exists afterward with the exact provided values
    /// </summary>
    [TestCaseSource(nameof(UpdateVersion_NewRepository_AddsRecordWithAllValues_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void UpdateVersion_NewRepository_AddsRecordWithAllValues(string repository, string uri, string sha, int? barId)
    {
        // Arrange
        var sut = new SourceManifest(Array.Empty<RepositoryRecord>(), Array.Empty<SubmoduleRecord>());

        // Act
        sut.UpdateVersion(repository, uri, sha, barId);

        // Assert
        sut.Repositories.Count.Should().Be(1);

        var record = sut.GetRepositoryRecord(repository);
        record.Path.Should().Be(repository);
        record.RemoteUri.Should().Be(uri);
        record.CommitSha.Should().Be(sha);
        record.BarId.Should().Be(barId);
    }

    private static IEnumerable<TestCaseData> UpdateVersion_ExistingRepositoryWithBarId_UpdatesAllFields_Cases()
    {
        yield return new TestCaseData(int.MinValue).SetName("BarId=int.MinValue");
        yield return new TestCaseData(-1).SetName("BarId=-1");
        yield return new TestCaseData(0).SetName("BarId=0");
        yield return new TestCaseData(1).SetName("BarId=1");
        yield return new TestCaseData(int.MaxValue).SetName("BarId=int.MaxValue");
    }

    private static IEnumerable<TestCaseData> UpdateVersion_NewRepository_AddsRecordWithAllValues_Cases()
    {
        var longRepo = new string('r', 1024);
        var longUri = "https://example.org/" + new string('u', 2048) + ".git";
        var longSha = new string('s', 2048);

        yield return new TestCaseData("repo-1", "https://example.org/repo.git", "abc123", 0)
            .SetName("NewRepo_NormalValues_BarIdZero");

        yield return new TestCaseData(string.Empty, string.Empty, string.Empty, null)
            .SetName("NewRepo_EmptyStrings_BarIdNull");

        yield return new TestCaseData("   ", "   ", "   ", int.MinValue)
            .SetName("NewRepo_Whitespace_BarIdMinValue");

        yield return new TestCaseData(longRepo, longUri, longSha, int.MaxValue)
            .SetName("NewRepo_VeryLongStrings_BarIdMaxValue");

        yield return new TestCaseData("spécïål/路径\\🙈", "ssh://git@example.com:22/repo.git?x=y#frag", "deadbeef!@#$%^&*()_+-=[]{}|;':,./<>?", -42)
            .SetName("NewRepo_SpecialCharacters_BarIdNegative");
    }

    private enum ExpectedKind
    {
        None,
        Repository,
        Submodule
    }

    private static RepositoryRecord Repo(string path) => new RepositoryRecord(path, "u", "s", null);

    private static SubmoduleRecord Sub(string path) => new SubmoduleRecord(path, "u", "s");

    public static IEnumerable<TestCaseData> TryGetRepoVersion_Cases()
    {
        // 1) Repository match, case-insensitive
        yield return new TestCaseData(
            "SRC/REPO",
            new List<RepositoryRecord> { Repo("src/repo") },
            new List<SubmoduleRecord>(),
            true,
            ExpectedKind.Repository,
            0
        ).SetName("TryGetRepoVersion_RepositoryMatch_CaseInsensitive_ReturnsRepository");

        // 2) Submodule match when repository does not contain it
        yield return new TestCaseData(
            "sub/mod",
            new List<RepositoryRecord>(),
            new List<SubmoduleRecord> { Sub("sub/Mod") },
            true,
            ExpectedKind.Submodule,
            0
        ).SetName("TryGetRepoVersion_SubmoduleMatch_WhenNotInRepositories_ReturnsSubmodule");

        // 3) Both contain same mapping; repository should be preferred
        yield return new TestCaseData(
            "DUP",
            new List<RepositoryRecord> { Repo("dup") },
            new List<SubmoduleRecord> { Sub("dup") },
            true,
            ExpectedKind.Repository,
            0
        ).SetName("TryGetRepoVersion_BothContainName_PrefersRepository");

        // 4) No match anywhere -> false and null
        yield return new TestCaseData(
            "missing",
            new List<RepositoryRecord> { Repo("r1") },
            new List<SubmoduleRecord> { Sub("s1") },
            false,
            ExpectedKind.None,
            -1
        ).SetName("TryGetRepoVersion_NotFound_ReturnsFalseAndNull");

        // 5) Empty path mapping: exact empty path in repositories
        yield return new TestCaseData(
            "",
            new List<RepositoryRecord> { Repo("") },
            new List<SubmoduleRecord>(),
            true,
            ExpectedKind.Repository,
            0
        ).SetName("TryGetRepoVersion_EmptyStringMatchesEmptyPathInRepositories");

        // 6) Whitespace-only mapping: exact whitespace path in submodules
        yield return new TestCaseData(
            "   ",
            new List<RepositoryRecord>(),
            new List<SubmoduleRecord> { Sub("   ") },
            true,
            ExpectedKind.Submodule,
            0
        ).SetName("TryGetRepoVersion_WhitespaceOnlyMatchesWhitespacePathInSubmodules");

        // 7) Very long path with case-insensitive matching
        var longLower = new string('a', 1024);
        var longUpper = longLower.ToUpperInvariant();
        yield return new TestCaseData(
            longUpper,
            new List<RepositoryRecord> { Repo(longLower) },
            new List<SubmoduleRecord>(),
            true,
            ExpectedKind.Repository,
            0
        ).SetName("TryGetRepoVersion_VeryLongPath_CaseInsensitiveMatchInRepositories");
    }

    private enum Scenario
    {
        RepoOnly,
        SubmoduleOnly,
        BothRepositoryAndSubmodule
    }

    /// <summary>
    /// Validates that GetRepoVersion throws an exception with a descriptive message when the mapping is not present.
    /// Inputs: mappingName covering empty string, whitespace-only, and a very long/special-character string.
    /// Expected: Throws Exception with message "No manifest record named {mappingName} found".
    /// </summary>
    [TestCaseSource(nameof(GetRepoVersion_NotFound_MappingNames))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetRepoVersion_NotFound_ThrowsWithDescriptiveMessage(string mappingName)
    {
        // Arrange
        var repositories = new List<RepositoryRecord>
            {
                new RepositoryRecord("Existing-Repo", "https://example.org/repo.git", "12345678", 0)
            };
        var submodules = new List<SubmoduleRecord>
            {
                new SubmoduleRecord("Existing-Sub", "https://example.org/sub.git", "abcdef01")
            };
        var sut = new SourceManifest(repositories, submodules);
        var expectedMessage = $"No manifest record named {mappingName} found";

        // Act
        Exception caught = null;
        try
        {
            ((ISourceManifest)sut).GetRepoVersion(mappingName);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Assert
        caught.Should().NotBeNull();
        caught.Message.Should().Be(expectedMessage);
    }

    private static IEnumerable GetRepoVersion_NotFound_MappingNames()
    {
        yield return string.Empty;
        yield return "   ";
        yield return new string('x', 1024) + "!@#$%^&*()_+[]{}|;':,./<>?\r\n\t";
    }

    /// <summary>
    /// Ensures GetRepositoryRecord returns the exact RepositoryRecord whose Path equals the provided mappingName.
    /// Cases include: normal name, empty string, whitespace-only, very long string, and special/control characters.
    /// Expected: The returned record matches the seeded record (Path, RemoteUri, CommitSha, BarId).
    /// </summary>
    [TestCaseSource(nameof(GetRepositoryRecord_ExistingPath_ReturnsRecord_Cases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetRepositoryRecord_ExistingPath_ReturnsRecord(string mappingName, string remoteUri, string commitSha, int? barId)
    {
        // Arrange
        var repositories = new List<RepositoryRecord>
            {
                new RepositoryRecord(mappingName, remoteUri, commitSha, barId),
                new RepositoryRecord("other", "https://example.org/other.git", "0000000", null),
            };
        var submodules = new List<SubmoduleRecord>();

        var sut = new SourceManifest(repositories, submodules);

        // Act
        var result = sut.GetRepositoryRecord(mappingName);

        // Assert
        result.Should().NotBeNull();
        result.Path.Should().Be(mappingName);
        result.RemoteUri.Should().Be(remoteUri);
        result.CommitSha.Should().Be(commitSha);
        result.BarId.Should().Be(barId);
    }

    /// <summary>
    /// Verifies GetRepositoryRecord throws an exception when no repository record
    /// with the exact (case-sensitive) mappingName exists.
    /// Cases include: non-existent name, case-mismatch with existing record, and very long non-existent name.
    /// Expected: Exception with message "No repository record named {mappingName} found".
    /// </summary>
    [TestCaseSource(nameof(GetRepositoryRecord_NotFound_Throws_Cases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetRepositoryRecord_NotFound_Throws(string queryName)
    {
        // Arrange
        var repositories = new List<RepositoryRecord>
            {
                new RepositoryRecord("repo", "https://example.org/repo.git", "abcdef1", 1),
                new RepositoryRecord("other", "https://example.org/other.git", "abcdef2", null),
            };
        var sut = new SourceManifest(repositories, new List<SubmoduleRecord>());

        // Act
        Action act = () => sut.GetRepositoryRecord(queryName);

        // Assert
        act.Should()
           .Throw<Exception>()
           .WithMessage($"No repository record named {queryName} found");
    }

    private static IEnumerable<TestCaseData> GetRepositoryRecord_ExistingPath_ReturnsRecord_Cases()
    {
        yield return new TestCaseData(
            "repo",
            "https://example.org/repo.git",
            "abc123def",
            42);

        yield return new TestCaseData(
            string.Empty,
            string.Empty,
            string.Empty,
            null);

        yield return new TestCaseData(
            "   ",
            "ssh://git@example.org/some path.git",
            "ffffff0",
            0);

        yield return new TestCaseData(
            new string('a', 4096),
            "https://example.org/long.git",
            "1234567",
            int.MaxValue);

        yield return new TestCaseData(
            "sp€c!al\r\n\t*?|<>",
            "file:///c:/path/with special.chars",
            "deadbeef",
            -1);
    }

    private static IEnumerable<TestCaseData> GetRepositoryRecord_NotFound_Throws_Cases()
    {
        yield return new TestCaseData("unknown");
        yield return new TestCaseData("REPO"); // case mismatch against "repo"
        yield return new TestCaseData(new string('x', 2048));
    }

    /// <summary>
    /// Verifies that Refresh replaces in-memory collections with empty sets when the specified path does not exist.
    /// Inputs: Various non-existent path values (empty string, whitespace, and a guaranteed unique non-existent file path).
    /// Expected: After Refresh, both Repositories and Submodules are empty, indicating previous state was replaced.
    /// </summary>
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("GENERATE_UNIQUE_PATH", true)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Refresh_NonExistingPath_ReplacesCollectionsWithEmpty(string pathInput, bool generateUniquePath)
    {
        // Arrange
        var initialRepos = new List<RepositoryRecord>
            {
                new RepositoryRecord("old/repo", "https://example.com/old.git", "oldsha", 1),
            };

        var initialSubmodules = new List<SubmoduleRecord>
            {
                new SubmoduleRecord("old/sub", "https://example.com/sub.git", "subsha"),
            };

        var sut = new SourceManifest(initialRepos, initialSubmodules);

        var path = generateUniquePath
            ? Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "source-manifest.json")
            : pathInput;

        // Act
        sut.Refresh(path);

        // Assert
        sut.Repositories.Count.Should().Be(0);
        sut.Submodules.Count.Should().Be(0);
    }

    /// <summary>
    /// Ensures Refresh loads content from an existing file and replaces current collections (not appends).
    /// Inputs: A temporary file containing a JSON-serialized SourceManifest with specific repository and submodule entries.
    /// Expected: Repositories/Submodules are replaced with those from the file, and prior items are not present.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Refresh_ExistingFile_ReplacesCollectionsWithDeserializedContent()
    {
        // Arrange
        var oldRepos = new List<RepositoryRecord>
            {
                new RepositoryRecord("old/repo", "https://example.com/old.git", "oldsha", 7),
            };
        var oldSubs = new List<SubmoduleRecord>
            {
                new SubmoduleRecord("old/sub", "https://example.com/sub.git", "subsha"),
            };
        var sut = new SourceManifest(oldRepos, oldSubs);

        var newRepos = new List<RepositoryRecord>
            {
                new RepositoryRecord("new/repo", "https://example.com/new.git", "abc123", 42),
            };
        var newSubs = new List<SubmoduleRecord>
            {
                new SubmoduleRecord("new/sub", "https://example.com/newsub.git", "def456"),
            };

        var newContentManifest = new SourceManifest(newRepos, newSubs);
        var json = newContentManifest.ToJson();

        var tempDir = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "source-manifest.json");
        File.WriteAllText(filePath, json);

        try
        {
            // Act
            sut.Refresh(filePath);

            // Assert
            sut.Repositories.Count.Should().Be(1);
            sut.Submodules.Count.Should().Be(1);

            sut.Repositories.Any(r => r.Path == "new/repo").Should().BeTrue();
            sut.Submodules.Any(s => s.Path == "new/sub").Should().BeTrue();

            sut.Repositories.Any(r => r.Path == "old/repo").Should().BeFalse();
            sut.Submodules.Any(s => s.Path == "old/sub").Should().BeFalse();
        }
        finally
        {
            try { File.Delete(filePath); } catch { /* best effort cleanup */ }
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    /// <summary>
    /// Provides inputs for testing the Repositories property behavior:
    /// - Empty repositories: ensures empty, read-only collection behavior.
    /// - Single repository: ensures single item is exposed as IVersionedSourceComponent.
    /// - Multiple repositories with duplicates and unsorted order: ensures distinct-by-path and sorted by path.
    /// </summary>
    private static IEnumerable<TestCaseData> RepositoriesCases()
    {
        // Empty repositories
        yield return new TestCaseData(
            new List<RepositoryRecord>(),
            new List<SubmoduleRecord>(),
            Array.Empty<string>())
            .SetName("Repositories_EmptyInput_ReturnsEmptyCollection");

        // Single repository
        yield return new TestCaseData(
            new List<RepositoryRecord>
            {
                    new RepositoryRecord(path: "a/repo", remoteUri: "https://example.com/a.git", commitSha: "aaaaaaaa", barId: 0)
            },
            new List<SubmoduleRecord>(),
            new[] { "a/repo" })
            .SetName("Repositories_SingleItem_ReturnsSingleElement");

        // Multiple repositories, unsorted, with duplicate path to verify distinct + sorted by Path
        yield return new TestCaseData(
            new List<RepositoryRecord>
            {
                    new RepositoryRecord(path: "b/repo", remoteUri: "https://example.com/b.git", commitSha: "bbbbbbbb", barId: int.MinValue),
                    new RepositoryRecord(path: "a/repo", remoteUri: "https://example.com/a.git", commitSha: "aaaaaaaa", barId: int.MaxValue),
                    // duplicate path - should be deduplicated by the underlying SortedSet based on Path comparison
                    new RepositoryRecord(path: "a/repo", remoteUri: "https://example.com/a2.git", commitSha: "cccccccc", barId: null),
            },
            new List<SubmoduleRecord>
            {
                    new SubmoduleRecord(path: "s/mod", remoteUri: "https://example.com/s.git", commitSha: "dddddddd")
            },
            new[] { "a/repo", "b/repo" })
            .SetName("Repositories_UnsortedWithDuplicates_ReturnsDistinctSortedByPath");
    }

    /// <summary>
    /// Validates that SourceManifest.Repositories:
    /// - Exposes a read-only collection of IVersionedSourceComponent
    /// - Contains exactly the expected set of distinct repository paths
    /// - Is sorted by the repository Path (according to ManifestRecord.CompareTo)
    /// </summary>
    [Test]
    [TestCaseSource(nameof(RepositoriesCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Repositories_VariousInputs_ReturnsDistinctSortedReadOnlyCollection(
        IEnumerable<RepositoryRecord> repositories,
        IEnumerable<SubmoduleRecord> submodules,
        string[] expectedPathsInOrder)
    {
        // Arrange
        var manifest = new SourceManifest(repositories, submodules);

        // Act
        IReadOnlyCollection<IVersionedSourceComponent> result = manifest.Repositories;
        var actualPaths = result.Select(r => r.Path).ToArray();

        // Assert
        result.Count.Should().Be(expectedPathsInOrder.Length);
        actualPaths.Should().Equal(expectedPathsInOrder);

        // Also ensure all items are IVersionedSourceComponent (already via static typing)
        // and that collection cannot be modified through the exposed interface.
        // Attempting to cast to a mutable generic collection should fail.
        (result as ICollection<IVersionedSourceComponent>).Should().BeNull();

        // The non-generic ICollection interface is allowed by SortedSet, but it does not allow adding items,
        // so we simply assert it exists without providing mutation capability through the exposed type.
        (result as ICollection).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that the Submodules property exposes the submodule collection supplied at construction.
    /// Test cases:
    /// - Empty input collection -> Submodules is empty.
    /// - Single item -> Submodules contains exactly that item.
    /// - Multiple items -> Submodules contains all items.
    /// - Duplicates by Path -> Submodules de-duplicates (SortedSet behavior) and contains only unique paths.
    /// Expected: Submodules is non-null and contains the expected number of items with expected paths.
    /// </summary>
    [TestCaseSource(nameof(SubmoduleScenarios))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Submodules_InitializedWithVariousCollections_ExposesExpectedItems(
        IEnumerable<SubmoduleRecord> inputSubmodules,
        string[] expectedPaths)
    {
        // Arrange
        var repositories = Array.Empty<RepositoryRecord>();
        var sut = new SourceManifest(repositories, inputSubmodules);

        // Act
        var result = sut.Submodules;

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().Be(expectedPaths.Length);
        var actualPaths = result.Select(s => s.Path).ToArray();
        actualPaths.Should().BeEquivalentTo(expectedPaths);
    }

    /// <summary>
    /// Ensures that the Submodules property reflects updates made to an existing submodule record.
    /// Input: One submodule initialized, then updated with a new SHA and Remote URI via UpdateSubmodule.
    /// Expected: Submodules contains the submodule with updated CommitSha and RemoteUri.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Submodules_AfterUpdatingExistingSubmodule_ReflectsUpdatedValues()
    {
        // Arrange
        var path = "src/moduleA";
        var original = new SubmoduleRecord(path, "https://example.org/original.git", "aaaaaaaa");
        var sut = new SourceManifest(Array.Empty<RepositoryRecord>(), new[] { original });

        // Act
        var updated = new SubmoduleRecord(path, "https://example.org/updated.git", "bbbbbbbb");
        sut.UpdateSubmodule(updated);
        var result = sut.Submodules.Single();

        // Assert
        result.Path.Should().Be(path);
        result.RemoteUri.Should().Be("https://example.org/updated.git");
        result.CommitSha.Should().Be("bbbbbbbb");
    }

    private static IEnumerable<TestCaseData> SubmoduleScenarios()
    {
        // Empty
        yield return new TestCaseData(
            Enumerable.Empty<SubmoduleRecord>(),
            Array.Empty<string>())
            .SetName("Empty collection -> Submodules is empty");

        // Single item
        var s1 = new SubmoduleRecord("a/module", "https://example.com/a.git", "1111111");
        yield return new TestCaseData(
            new[] { s1 },
            new[] { "a/module" })
            .SetName("Single item -> Submodules exposes that item");

        // Multiple items
        var s2 = new SubmoduleRecord("b/module", "https://example.com/b.git", "2222222");
        var s3 = new SubmoduleRecord("c/module", "https://example.com/c.git", "3333333");
        yield return new TestCaseData(
            new[] { s3, s1, s2 },
            new[] { "a/module", "b/module", "c/module" })
            .SetName("Multiple items -> Submodules exposes all items");

        // Duplicates by Path (identical records) -> SortedSet de-duplicates
        var dup1 = new SubmoduleRecord("dup/module", "https://example.com/dup.git", "4444444");
        var dup2 = new SubmoduleRecord("dup/module", "https://example.com/dup.git", "4444444");
        yield return new TestCaseData(
            new[] { dup1, dup2 },
            new[] { "dup/module" })
            .SetName("Duplicate paths -> Submodules contains unique path only");
    }

    /// <summary>
    /// Verifies that constructing SourceManifest with empty or single-item collections succeeds and
    /// the public collections reflect the provided content.
    /// Inputs:
    ///  - repositoriesCount and submodulesCount: 0 or 1.
    /// Expected:
    ///  - No exception thrown.
    ///  - Repositories/Submodules counts match inputs.
    ///  - When count == 1, that single record is present with the expected Path.
    /// </summary>
    [Test]
    [TestCase(0, 0)]
    [TestCase(1, 0)]
    [TestCase(0, 1)]
    [TestCase(1, 1)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_EmptyOrSingleItemCollections_ConstructsAndExposesCollections(int repositoriesCount, int submodulesCount)
    {
        // Arrange
        IEnumerable<RepositoryRecord> repositories = CreateRepositoryRecords(repositoriesCount);
        IEnumerable<SubmoduleRecord> submodules = CreateSubmoduleRecords(submodulesCount);

        // Act
        SourceManifest manifest = new SourceManifest(repositories, submodules);

        // Assert
        manifest.Repositories.Should().NotBeNull();
        manifest.Submodules.Should().NotBeNull();

        manifest.Repositories.Should().HaveCount(repositoriesCount);
        manifest.Submodules.Should().HaveCount(submodulesCount);

        if (repositoriesCount == 1)
        {
            manifest.Repositories.Should().ContainSingle(r => r.Path == "repo/0");
        }

        if (submodulesCount == 1)
        {
            manifest.Submodules.Should().ContainSingle(s => s.Path == "sub/0");
        }
    }

    /// <summary>
    /// Ensures that constructing SourceManifest with multiple repository and/or submodule records
    /// throws due to SortedSet requiring a compatible comparer for RepositoryRecord/SubmoduleRecord.
    /// Inputs:
    ///  - repositoriesCount/submodulesCount with at least one of them greater than 1.
    /// Expected:
    ///  - InvalidOperationException is thrown during construction.
    /// </summary>
    [Test]
    [TestCase(2, 0)]
    [TestCase(0, 2)]
    [TestCase(2, 1)]
    [TestCase(1, 2)]
    [TestCase(2, 2)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_MultipleRepositoriesOrSubmodules_ThrowsInvalidOperationException(int repositoriesCount, int submodulesCount)
    {
        // Arrange
        IEnumerable<RepositoryRecord> repositories = CreateRepositoryRecords(repositoriesCount);
        IEnumerable<SubmoduleRecord> submodules = CreateSubmoduleRecords(submodulesCount);

        // Act
        Action act = () => new SourceManifest(repositories, submodules);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Validates that even when duplicates exist (same Path) in repositories or submodules,
    /// construction fails due to missing compatible comparer for SortedSet element type.
    /// Inputs:
    ///  - duplicatesInRepositories: when true, two repository records with identical Path; otherwise, duplicates in submodules.
    /// Expected:
    ///  - InvalidOperationException is thrown during construction.
    /// </summary>
    [Test]
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_DuplicatePaths_ThrowsInvalidOperationException(bool duplicatesInRepositories)
    {
        // Arrange
        IEnumerable<RepositoryRecord> repositories = duplicatesInRepositories
            ? new[]
            {
                    new RepositoryRecord("dup/path", "https://example.org/repo.git", "sha-1", 1),
                    new RepositoryRecord("dup/path", "https://example.org/repo.git", "sha-2", 2),
            }
            : CreateRepositoryRecords(0);

        IEnumerable<SubmoduleRecord> submodules = duplicatesInRepositories
            ? CreateSubmoduleRecords(0)
            : new[]
            {
                    new SubmoduleRecord("dup/sub", "https://example.org/sub.git", "sha-a"),
                    new SubmoduleRecord("dup/sub", "https://example.org/sub.git", "sha-b"),
            };

        // Act
        Action act = () => new SourceManifest(repositories, submodules);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    // Helper methods for creating test data (kept inside the test class)

    private static IEnumerable<RepositoryRecord> CreateRepositoryRecords(int count)
    {
        List<RepositoryRecord> items = new List<RepositoryRecord>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            string path = $"repo/{i}";
            string uri = $"https://example.org/repo{i}.git";
            string sha = $"sha-{i:D4}";
            int? barId = i;
            items.Add(new RepositoryRecord(path, uri, sha, barId));
        }

        return items;
    }

    private static IEnumerable<SubmoduleRecord> CreateSubmoduleRecords(int count)
    {
        List<SubmoduleRecord> items = new List<SubmoduleRecord>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            string path = $"sub/{i}";
            string uri = $"https://example.org/sub{i}.git";
            string sha = $"sha-sub-{i:D4}";
            items.Add(new SubmoduleRecord(path, uri, sha));
        }

        return items;
    }

    /// <summary>
    /// Verifies that when a repository already exists (matched by Path), UpdateVersion:
    ///  - Always updates RemoteUri and CommitSha.
    ///  - Updates BarId only when a non-null barId is provided; otherwise, keeps the original BarId.
    /// Inputs:
    ///  - initialBarId: starting BarId in the manifest for the repo.
    ///  - newBarId: value passed to UpdateVersion (nullable).
    /// Expected:
    ///  - BarId becomes expectedBarId; RemoteUri and CommitSha reflect the new values.
    /// </summary>
    [TestCaseSource(nameof(UpdateExistingRepositoryCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void UpdateVersion_ExistingRepository_UpdatesUriShaAndConditionallyBarId(int? initialBarId, int? newBarId, int? expectedBarId)
    {
        // Arrange
        var existingRepoPath = "repo-A";
        var untouchedRepoPath = "repo-B";

        var initial = new List<RepositoryRecord>
            {
                new RepositoryRecord(existingRepoPath, "old-uri", "old-sha", initialBarId),
                new RepositoryRecord(untouchedRepoPath, "uri-b", "sha-b", 42)
            };

        var manifest = new SourceManifest(initial, Enumerable.Empty<SubmoduleRecord>());

        var updatedUri = "https://new.example/repo.git";
        var updatedSha = "new-sha-value";

        // Act
        manifest.UpdateVersion(existingRepoPath, updatedUri, updatedSha, newBarId);

        // Assert
        manifest.Repositories.Count.Should().Be(2);

        var updated = manifest.Repositories.OfType<RepositoryRecord>().Single(r => r.Path == existingRepoPath);
        updated.RemoteUri.Should().Be(updatedUri);
        updated.CommitSha.Should().Be(updatedSha);
        updated.BarId.Should().Be(expectedBarId);

        // Ensure the other repository is not modified
        var untouched = manifest.Repositories.OfType<RepositoryRecord>().Single(r => r.Path == untouchedRepoPath);
        untouched.RemoteUri.Should().Be("uri-b");
        untouched.CommitSha.Should().Be("sha-b");
        untouched.BarId.Should().Be(42);
    }

    /// <summary>
    /// Ensures that when the target repository doesn't exist in the manifest, UpdateVersion
    /// creates a new RepositoryRecord with the provided Path, RemoteUri, CommitSha, and BarId.
    /// Inputs:
    ///  - repository path strings, including empty, whitespace, very long, special chars, and path-like values.
    ///  - uri and sha strings with edge cases.
    ///  - barId covering null, 0, negative, and int extremes.
    /// Expected:
    ///  - A new repository is added with properties exactly matching the inputs.
    /// </summary>
    [TestCaseSource(nameof(AddNewRepositoryCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void UpdateVersion_NonExistingRepository_AddsNewRecord(string repository, string uri, string sha, int? barId)
    {
        // Arrange
        var existing = new List<RepositoryRecord>
            {
                new RepositoryRecord("existing-only", "e-uri", "e-sha", 7)
            };

        var manifest = new SourceManifest(existing, Enumerable.Empty<SubmoduleRecord>());
        var initialCount = manifest.Repositories.Count;

        // Act
        manifest.UpdateVersion(repository, uri, sha, barId);

        // Assert
        manifest.Repositories.Count.Should().Be(initialCount + 1);

        var added = manifest.Repositories.OfType<RepositoryRecord>().Single(r => r.Path == repository);
        added.RemoteUri.Should().Be(uri);
        added.CommitSha.Should().Be(sha);
        added.BarId.Should().Be(barId);

        // Ensure the pre-existing record remains intact
        var untouched = manifest.Repositories.OfType<RepositoryRecord>().Single(r => r.Path == "existing-only");
        untouched.RemoteUri.Should().Be("e-uri");
        untouched.CommitSha.Should().Be("e-sha");
        untouched.BarId.Should().Be(7);
    }

    private static IEnumerable<TestCaseData> UpdateExistingRepositoryCases()
    {
        yield return new TestCaseData(10, null, 10).SetName("ExistingRepo_BarId_RemainsWhenNullUpdate");
        yield return new TestCaseData(null, 20, 20).SetName("ExistingRepo_BarId_SetFromNullToValue");
        yield return new TestCaseData(5, -1, -1).SetName("ExistingRepo_BarId_UpdatesToNegative");
        yield return new TestCaseData(-7, 0, 0).SetName("ExistingRepo_BarId_UpdatesToZero");
        yield return new TestCaseData(int.MaxValue, int.MinValue, int.MinValue).SetName("ExistingRepo_BarId_UpdatesToMinValue");
    }

    private static IEnumerable<TestCaseData> AddNewRepositoryCases()
    {
        var longStr = new string('x', 2048);

        yield return new TestCaseData("", "", "", null).SetName("AddNew_EmptyStrings_NullBarId");
        yield return new TestCaseData(" ", "  ", "\t \n", 0).SetName("AddNew_WhitespaceStrings_ZeroBarId");
        yield return new TestCaseData(@"C:\path\to\repo", "ssh://git@host:repo.git", "deadbeef$%^&*", -1).SetName("AddNew_PathLikeAndSpecialChars_NegativeBarId");
        yield return new TestCaseData("🙂/🚀", "https://example.com/x?query=1&name=ä", longStr, int.MaxValue).SetName("AddNew_UnicodeAndVeryLongSha_MaxBarId");
        yield return new TestCaseData(longStr, "file:///tmp/repo", "sha-123", int.MinValue).SetName("AddNew_VeryLongRepository_MinBarId");
    }

    /// <summary>
    /// Ensures that when the specified repository exists:
    /// - The repository record with an exact path match is removed.
    /// - Only submodules whose paths start with "repository + '/'" are removed.
    /// Inputs:
    ///  - repository = "src/abc"
    ///  - Repositories: ["src/abc", "src/xyz"]
    ///  - Submodules: ["src/abc/mod1", "src/abcde/modX", "src/abc/mod2/nested", "src/abc", "src/xyz/mod"]
    /// Expected:
    ///  - Repositories: ["src/xyz"]
    ///  - Submodules: ["src/abcde/modX", "src/abc", "src/xyz/mod"]
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RemoveRepository_ExistingRepository_RemovesRepoAndOnlySubmodulesWithSlashPrefix()
    {
        // Arrange
        var repositories = new[]
        {
                new RepositoryRecord("src/abc", "https://example/abc.git", "sha-abc", 1),
                new RepositoryRecord("src/xyz", "https://example/xyz.git", "sha-xyz", 2),
            };

        var submodules = new[]
        {
                new SubmoduleRecord("src/abc/mod1", "https://example/mod1.git", "sha-m1"),       // should be removed
                new SubmoduleRecord("src/abcde/modX", "https://example/modX.git", "sha-mx"),     // should stay (prefix but no slash after "src/abc")
                new SubmoduleRecord("src/abc/mod2/nested", "https://example/mod2.git", "sha-m2"),// should be removed
                new SubmoduleRecord("src/abc", "https://example/abc.git", "sha-abc-sm"),         // should stay (no trailing slash)
                new SubmoduleRecord("src/xyz/mod", "https://example/xyzmod.git", "sha-xyzm"),    // should stay
            };

        var manifest = new SourceManifest(repositories, submodules);

        // Act
        manifest.RemoveRepository("src/abc");

        // Assert
        var repoPaths = manifest.Repositories.Select(r => r.Path);
        repoPaths.Should().BeEquivalentTo(new[] { "src/xyz" });

        var smPaths = manifest.Submodules.Select(s => s.Path);
        smPaths.Should().BeEquivalentTo(new[] { "src/abcde/modX", "src/abc", "src/xyz/mod" });
    }

    /// <summary>
    /// Verifies that when the specified repository does not exist:
    /// - No repository records are removed.
    /// - Submodules starting with "repository + '/'" are still removed as per the implementation.
    /// Inputs:
    ///  - repository = "repoX"
    ///  - Repositories: ["r1", "r2"]
    ///  - Submodules: ["repoX/a", "repo1/a", "repoXA/b", "repoX"]
    /// Expected:
    ///  - Repositories: ["r1", "r2"]
    ///  - Submodules: ["repo1/a", "repoXA/b", "repoX"]
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RemoveRepository_RepositoryNotFound_RemovesOnlyMatchingSubmodules()
    {
        // Arrange
        var repositories = new[]
        {
                new RepositoryRecord("r1", "https://example/r1.git", "sha-r1", 11),
                new RepositoryRecord("r2", "https://example/r2.git", "sha-r2", 22),
            };

        var submodules = new[]
        {
                new SubmoduleRecord("repoX/a", "https://example/repoX-a.git", "sha-xa"), // should be removed
                new SubmoduleRecord("repo1/a", "https://example/repo1-a.git", "sha-1a"), // should stay
                new SubmoduleRecord("repoXA/b", "https://example/repoXA-b.git", "sha-xb"),// should stay (prefix without slash)
                new SubmoduleRecord("repoX", "https://example/repoX.git", "sha-x"),       // should stay (no trailing slash)
            };

        var manifest = new SourceManifest(repositories, submodules);

        // Act
        manifest.RemoveRepository("repoX");

        // Assert
        var repoPaths = manifest.Repositories.Select(r => r.Path);
        repoPaths.Should().BeEquivalentTo(new[] { "r1", "r2" });

        var smPaths = manifest.Submodules.Select(s => s.Path);
        smPaths.Should().BeEquivalentTo(new[] { "repo1/a", "repoXA/b", "repoX" });
    }

    /// <summary>
    /// Ensures trailing slash in the repository parameter prevents both repository match and submodule removal
    /// (since the predicate checks for "repository + '/'").
    /// Inputs:
    ///  - repository = "src/abc/"
    ///  - Repositories: ["src/abc", "src/xyz"]
    ///  - Submodules: ["src/abc/mod1"]
    /// Expected:
    ///  - Repositories: ["src/abc", "src/xyz"] (unchanged)
    ///  - Submodules: ["src/abc/mod1"] (unchanged)
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RemoveRepository_TrailingSlashInput_NoRemovalsOccur()
    {
        // Arrange
        var repositories = new[]
        {
                new RepositoryRecord("src/abc", "https://example/abc.git", "sha-abc", 1),
                new RepositoryRecord("src/xyz", "https://example/xyz.git", "sha-xyz", 2),
            };

        var submodules = new[]
        {
                new SubmoduleRecord("src/abc/mod1", "https://example/mod1.git", "sha-m1"),
            };

        var manifest = new SourceManifest(repositories, submodules);

        // Act
        manifest.RemoveRepository("src/abc/");

        // Assert
        var repoPaths = manifest.Repositories.Select(r => r.Path);
        repoPaths.Should().BeEquivalentTo(new[] { "src/abc", "src/xyz" });

        var smPaths = manifest.Submodules.Select(s => s.Path);
        smPaths.Should().BeEquivalentTo(new[] { "src/abc/mod1" });
    }

    /// <summary>
    /// Validates behavior for empty repository string:
    /// - No repository matches an empty string.
    /// - Submodules are removed only if their path starts with "/".
    /// Inputs:
    ///  - repository = ""
    ///  - Repositories: ["a", "b"]
    ///  - Submodules: ["/root/a", "a/b", " b/c", "root/d"]
    /// Expected:
    ///  - Repositories: ["a", "b"] (unchanged)
    ///  - Submodules: ["a/b", " b/c", "root/d"] ("/root/a" removed)
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RemoveRepository_EmptyString_RemovesOnlyLeadingSlashSubmodules()
    {
        // Arrange
        var repositories = new[]
        {
                new RepositoryRecord("a", "https://example/a.git", "sha-a", 1),
                new RepositoryRecord("b", "https://example/b.git", "sha-b", 2),
            };

        var submodules = new[]
        {
                new SubmoduleRecord("/root/a", "https://example/root-a.git", "sha-ra"), // removed (starts with "/")
                new SubmoduleRecord("a/b", "https://example/a-b.git", "sha-ab"),        // stays
                new SubmoduleRecord(" b/c", "https://example/space-b.git", "sha-bc"),   // stays (" /" doesn't match "/")
                new SubmoduleRecord("root/d", "https://example/root-d.git", "sha-rd"),  // stays
            };

        var manifest = new SourceManifest(repositories, submodules);

        // Act
        manifest.RemoveRepository(string.Empty);

        // Assert
        var repoPaths = manifest.Repositories.Select(r => r.Path);
        repoPaths.Should().BeEquivalentTo(new[] { "a", "b" });

        var smPaths = manifest.Submodules.Select(s => s.Path);
        smPaths.Should().BeEquivalentTo(new[] { "a/b", " b/c", "root/d" });
    }

    /// <summary>
    /// Verifies that RemoveSubmodule removes a submodule only when the provided submodule's Path
    /// exactly matches an existing SubmoduleRecord.Path (case-sensitive).
    /// Inputs:
    ///  - Initial manifest with a single SubmoduleRecord having a specific Path.
    ///  - An ISourceComponent mock with varying Path values (exact match, case-different, empty, whitespace, prefix-only, special chars, very long).
    /// Expected:
    ///  - When Path matches exactly: the submodule is removed (count becomes 0, no element with that Path remains).
    ///  - When Path does not match exactly: no removal occurs (count stays 1, element with that Path remains).
    /// </summary>
    [TestCaseSource(nameof(RemoveSubmodule_PathVariants_Data))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void RemoveSubmodule_PathVariants_RemovesOnlyWhenExactMatch(string existingPath, string inputPath, bool expectedRemoved)
    {
        // Arrange
        var existing = new SubmoduleRecord(existingPath, "https://example.org/repo.git", "abcdef1234567890");
        var manifest = new SourceManifest([], new[] { existing });

        var submoduleMock = new Mock<ISourceComponent>(MockBehavior.Strict);
        submoduleMock.SetupGet(s => s.Path).Returns(inputPath);

        // Act
        manifest.RemoveSubmodule(submoduleMock.Object);

        // Assert
        var remainingHasExistingPath = manifest.Submodules.Any(s => s.Path == existingPath);
        if (expectedRemoved)
        {
            remainingHasExistingPath.Should().BeFalse();
            manifest.Submodules.Count.Should().Be(0);
        }
        else
        {
            remainingHasExistingPath.Should().BeTrue();
            manifest.Submodules.Count.Should().Be(1);
        }
    }

    /// <summary>
    /// Ensures RemoveSubmodule is a no-op when the manifest contains no submodules.
    /// Inputs:
    ///  - Empty manifest (no submodules).
    ///  - An ISourceComponent with any Path value.
    /// Expected:
    ///  - No exception thrown and submodule collection remains empty.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void RemoveSubmodule_EmptyCollection_NoChange()
    {
        // Arrange
        var manifest = new SourceManifest([], []);
        var submoduleMock = new Mock<ISourceComponent>(MockBehavior.Strict);
        submoduleMock.SetupGet(s => s.Path).Returns("any/path");

        // Act
        manifest.RemoveSubmodule(submoduleMock.Object);

        // Assert
        manifest.Submodules.Count.Should().Be(0);
        manifest.Submodules.Any().Should().BeFalse();
    }

    private static IEnumerable<TestCaseData> RemoveSubmodule_PathVariants_Data()
    {
        yield return new TestCaseData("src/module", "src/module", true)
            .SetName("RemoveSubmodule_WhenPathMatchesExactly_RemovesRecord");

        yield return new TestCaseData("src/module", "SRC/MODULE", false)
            .SetName("RemoveSubmodule_WhenPathDiffersByCase_DoesNotRemove");

        yield return new TestCaseData("", "", true)
            .SetName("RemoveSubmodule_WhenEmptyPathMatchesExactly_RemovesRecord");

        yield return new TestCaseData("   ", "   ", true)
            .SetName("RemoveSubmodule_WhenWhitespacePathMatchesExactly_RemovesRecord");

        yield return new TestCaseData("a/b", "a/b/c", false)
            .SetName("RemoveSubmodule_WhenInputIsPrefixOnly_DoesNotRemove");

        yield return new TestCaseData("weird!@#$%^&*()[]{}", "weird!@#$%^&*()[]{}", true)
            .SetName("RemoveSubmodule_WhenPathHasSpecialCharactersAndMatches_RemovesRecord");

        var longPath = new string('a', 1024) + "/submodule";
        yield return new TestCaseData(longPath, longPath, true)
            .SetName("RemoveSubmodule_WhenVeryLongPathMatchesExactly_RemovesRecord");
    }

    /// <summary>
    /// Ensures that calling UpdateSubmodule with a submodule whose Path matches an existing record updates
    /// the existing record's RemoteUri and CommitSha without changing the collection size.
    /// Inputs:
    ///  - Existing SubmoduleRecord with path "src/mod", remote "https://old/repo.git", sha "old-sha".
    ///  - ISourceComponent with same Path "src/mod" but new remote and sha.
    /// Expected:
    ///  - Submodules count remains 1.
    ///  - Existing record's RemoteUri and CommitSha are updated to the new values; Path remains unchanged.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void UpdateSubmodule_ExistingPath_UpdatesRemoteUriAndCommitSha()
    {
        // Arrange
        var existing = new SubmoduleRecord("src/mod", "https://old/repo.git", "old-sha");
        var manifest = new SourceManifest(
            repositories: Array.Empty<RepositoryRecord>(),
            submodules: new[] { existing });

        var newUri = "https://new/repo.git";
        var newSha = "new-sha-123";

        var submoduleMock = new Mock<ISourceComponent>(MockBehavior.Strict);
        submoduleMock.SetupGet(s => s.Path).Returns("src/mod");
        submoduleMock.SetupGet(s => s.RemoteUri).Returns(newUri);
        submoduleMock.SetupGet(s => s.CommitSha).Returns(newSha);

        // Act
        manifest.UpdateSubmodule(submoduleMock.Object);

        // Assert
        manifest.Submodules.Should().HaveCount(1);
        existing.Path.Should().Be("src/mod");
        existing.RemoteUri.Should().Be(newUri);
        existing.CommitSha.Should().Be(newSha);
    }

    /// <summary>
    /// Verifies that when no submodule with the given Path exists, UpdateSubmodule adds a new record
    /// populated from the ISourceComponent parameter.
    /// Inputs:
    ///  - Empty SourceManifest.Submodules.
    ///  - ISourceComponent with varied Path/RemoteUri/CommitSha values including empty, whitespace, and special characters.
    /// Expected:
    ///  - One SubmoduleRecord is added having the provided Path, RemoteUri, and CommitSha.
    /// </summary>
    [TestCase("src/mod", "https://host/repo.git", "abc123", TestName = "UpdateSubmodule_NotExisting_AddsNewRecord_NormalValues")]
    [TestCase("", "", "", TestName = "UpdateSubmodule_NotExisting_AddsNewRecord_EmptyStrings")]
    [TestCase("   ", " \t ", " \n ", TestName = "UpdateSubmodule_NotExisting_AddsNewRecord_Whitespace")]
    [TestCase("path/with/unicode/π/д", "ssh://git@github.com:org/repo.git", "deadbeef1234567890", TestName = "UpdateSubmodule_NotExisting_AddsNewRecord_UnicodeAndSsh")]
    [TestCase(@"C:\weird\path|with*chars?", "https://host/repo.git", "0000000000000000000000000000000000000000", TestName = "UpdateSubmodule_NotExisting_AddsNewRecord_WindowsPathAndSpecialChars")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void UpdateSubmodule_NotExisting_AddsNewRecord(string path, string uri, string sha)
    {
        // Arrange
        var manifest = new SourceManifest(
            repositories: Array.Empty<RepositoryRecord>(),
            submodules: Array.Empty<SubmoduleRecord>());

        var submoduleMock = new Mock<ISourceComponent>(MockBehavior.Strict);
        submoduleMock.SetupGet(s => s.Path).Returns(path);
        submoduleMock.SetupGet(s => s.RemoteUri).Returns(uri);
        submoduleMock.SetupGet(s => s.CommitSha).Returns(sha);

        // Act
        manifest.UpdateSubmodule(submoduleMock.Object);

        // Assert
        manifest.Submodules.Should().HaveCount(1);
        var added = manifest.Submodules.Single();
        added.Path.Should().Be(path);
        added.RemoteUri.Should().Be(uri);
        added.CommitSha.Should().Be(sha);
    }

    /// <summary>
    /// Ensures UpdateSubmodule handles very long strings for Path, RemoteUri, and CommitSha without error,
    /// and correctly adds a new submodule record with those values.
    /// Inputs:
    ///  - Empty Submodules.
    ///  - ISourceComponent with very long path, uri, and sha strings (length ~ 4k each).
    /// Expected:
    ///  - One SubmoduleRecord is added matching the input values exactly.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void UpdateSubmodule_NotExisting_AddsNewRecord_WithVeryLongValues()
    {
        // Arrange
        var longPath = new string('p', 4096);
        var longUri = "https://host/" + new string('u', 4096);
        var longSha = new string('a', 4096);

        var manifest = new SourceManifest(
            repositories: Array.Empty<RepositoryRecord>(),
            submodules: Array.Empty<SubmoduleRecord>());

        var submoduleMock = new Mock<ISourceComponent>(MockBehavior.Strict);
        submoduleMock.SetupGet(s => s.Path).Returns(longPath);
        submoduleMock.SetupGet(s => s.RemoteUri).Returns(longUri);
        submoduleMock.SetupGet(s => s.CommitSha).Returns(longSha);

        // Act
        manifest.UpdateSubmodule(submoduleMock.Object);

        // Assert
        manifest.Submodules.Should().HaveCount(1);
        var added = manifest.Submodules.Single();
        added.Path.Should().Be(longPath);
        added.RemoteUri.Should().Be(longUri);
        added.CommitSha.Should().Be(longSha);
    }

    /// <summary>
    /// Ensures ToJson returns a JSON object containing camel-cased properties
    /// "repositories" and "submodules" and that both are empty arrays when
    /// the manifest is constructed with empty collections.
    /// Inputs:
    ///  - SourceManifest with no repositories and no submodules.
    /// Expected:
    ///  - JSON has "repositories": [] and "submodules": [].
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ToJson_EmptyCollections_ContainsEmptyArraysAndCamelCasedPropertyNames()
    {
        // Arrange
        var repositories = new List<RepositoryRecord>();
        var submodules = new List<SubmoduleRecord>();
        var manifest = new SourceManifest(repositories, submodules);

        // Act
        var json = manifest.ToJson();

        // Assert
        json.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Object);

        root.TryGetProperty("repositories", out var repositoriesElement).Should().BeTrue();
        repositoriesElement.ValueKind.Should().Be(JsonValueKind.Array);
        repositoriesElement.GetArrayLength().Should().Be(0);

        root.TryGetProperty("submodules", out var submodulesElement).Should().BeTrue();
        submodulesElement.ValueKind.Should().Be(JsonValueKind.Array);
        submodulesElement.GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// Validates that ToJson serializes submodule records into the "submodules" array with camel-cased
    /// property names and preserves exact string values, including special characters.
    /// Inputs:
    ///  - One submodule record with varied path/uri/sha values (provided by data source).
    /// Expected:
    ///  - JSON includes "submodules" with one element whose "path", "remoteUri", and "commitSha"
    ///    match the original strings exactly.
    /// </summary>
    [TestCaseSource(nameof(SubmoduleStringCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ToJson_SubmodulesSerialized_CamelCaseAndValuesRoundTrip(string path, string remoteUri, string commitSha)
    {
        // Arrange
        var repositories = new List<RepositoryRecord>();
        var submodules = new List<SubmoduleRecord>
            {
                new SubmoduleRecord(path, remoteUri, commitSha)
            };
        var manifest = new SourceManifest(repositories, submodules);

        // Act
        var json = manifest.ToJson();

        // Assert
        json.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("submodules", out var submodulesElement).Should().BeTrue();
        submodulesElement.ValueKind.Should().Be(JsonValueKind.Array);
        submodulesElement.GetArrayLength().Should().Be(1);

        var sub = submodulesElement[0];
        sub.ValueKind.Should().Be(JsonValueKind.Object);

        sub.TryGetProperty("path", out var pathProp).Should().BeTrue();
        pathProp.GetString().Should().Be(path);

        sub.TryGetProperty("remoteUri", out var remoteUriProp).Should().BeTrue();
        remoteUriProp.GetString().Should().Be(remoteUri);

        sub.TryGetProperty("commitSha", out var commitShaProp).Should().BeTrue();
        commitShaProp.GetString().Should().Be(commitSha);
    }

    private static IEnumerable SubmoduleStringCases()
    {
        // normal
        yield return new TestCaseData("src/modules/m1", "https://github.com/org/repo", "abcdef1234567890");

        // empty strings
        yield return new TestCaseData("", "", "");

        // whitespace variants (including escapes)
        yield return new TestCaseData("  ", " \t", " \r\n");

        // special characters and unicode
        yield return new TestCaseData(
            "p@th/with spåces/ß",
            "ssh://git@github.com:org/repo.git#branch?x=1&y=2",
            "abcDEF123_-$:+/\\\"'\n\r\t");

        // very long strings
        var longStr = new string('a', 2048);
        yield return new TestCaseData(longStr, longStr + "/path", longStr + "_sha");
    }

    /// <summary>
    /// Verifies that Refresh loads repositories and submodules from the provided valid JSON file
    /// and replaces the current collections.
    /// Inputs:
    ///  - An instance initialized with an existing submodule (and no repositories).
    ///  - A temporary file containing JSON with one repository and one submodule (different from the initial one).
    /// Expected:
    ///  - Repositories becomes a single-item collection with the repository from the file.
    ///  - Submodules becomes a single-item collection with the submodule from the file.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Refresh_ValidManifestFile_UpdatesRepositoriesAndSubmodules()
    {
        // Arrange
        var initialSubmodules = new[] { new SubmoduleRecord("old/sub", "https://old.example/sub", "oldsha") };
        var manifest = new SourceManifest(Array.Empty<RepositoryRecord>(), initialSubmodules);

        var tempFile = Path.Combine(Path.GetTempPath(), $"vmr-sm-{Guid.NewGuid():N}.json");
        var json = CreateManifestJson(
            repoEntries: new[] { ("repo/x", "https://example.org/repo/x", "abc123") },
            submoduleEntries: new[] { ("sub/new", "https://example.org/sub/new", "def456") });
        File.WriteAllText(tempFile, json);

        try
        {
            // Act
            manifest.Refresh(tempFile);

            // Assert
            manifest.Repositories.Count.Should().Be(1);
            manifest.Repositories.First().Path.Should().Be("repo/x");

            manifest.Submodules.Count.Should().Be(1);
            manifest.Submodules.First().Path.Should().Be("sub/new");
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    /// <summary>
    /// Ensures that when the provided path does not exist or is effectively invalid (e.g., whitespace),
    /// Refresh replaces the current collections with empty ones.
    /// Inputs:
    ///  - An instance initialized with a non-empty submodule collection.
    ///  - A path that does not refer to an existing file (or whitespace).
    /// Expected:
    ///  - Both Repositories and Submodules become empty.
    /// </summary>
    [TestCaseSource(nameof(NonExistingPaths))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Refresh_NonExistingOrWhitespacePath_EmptiesCollections(string path)
    {
        // Arrange
        var initialSubmodules = new[] { new SubmoduleRecord("keep/me", "https://example.org/keep", "sha-keep") };
        var manifest = new SourceManifest(Array.Empty<RepositoryRecord>(), initialSubmodules);

        // Act
        manifest.Refresh(path);

        // Assert
        manifest.Repositories.Count.Should().Be(0);
        manifest.Submodules.Count.Should().Be(0);
    }

    /// <summary>
    /// Validates that invalid JSON content causes Refresh to throw a JsonException (propagated from deserialization).
    /// Inputs:
    ///  - A temporary file containing malformed JSON.
    /// Expected:
    ///  - A JsonException is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Refresh_InvalidJson_ThrowsJsonException()
    {
        // Arrange
        var manifest = new SourceManifest(Array.Empty<RepositoryRecord>(), Array.Empty<SubmoduleRecord>());
        var tempFile = Path.Combine(Path.GetTempPath(), $"vmr-sm-mal-{Guid.NewGuid():N}.json");
        File.WriteAllText(tempFile, "{ invalid-json: ,,");

        try
        {
            // Act
            Action act = () => manifest.Refresh(tempFile);

            // Assert
            act.Should().Throw<JsonException>();
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    private static IEnumerable<object[]> NonExistingPaths()
    {
        yield return new object[] { Path.Combine(Path.GetTempPath(), $"vmr-sm-missing-{Guid.NewGuid():N}.json") };
        yield return new object[] { "   " };
    }

    private static string CreateManifestJson(
        IEnumerable<(string path, string uri, string sha)> repoEntries,
        IEnumerable<(string path, string uri, string sha)> submoduleEntries)
    {
        // Build minimal JSON aligning with expected wrapper structure and camelCase naming.
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append("\"repositories\":[");
        bool first = true;
        foreach (var r in repoEntries)
        {
            if (!first) sb.Append(",");
            sb.Append("{");
            sb.AppendFormat("\"path\":\"{0}\",\"remoteUri\":\"{1}\",\"commitSha\":\"{2}\",\"barId\":123", Escape(r.path), Escape(r.uri), Escape(r.sha));
            sb.Append("}");
            first = false;
        }
        sb.Append("],");
        sb.Append("\"submodules\":[");
        first = true;
        foreach (var s in submoduleEntries)
        {
            if (!first) sb.Append(",");
            sb.Append("{");
            sb.AppendFormat("\"path\":\"{0}\",\"remoteUri\":\"{1}\",\"commitSha\":\"{2}\"", Escape(s.path), Escape(s.uri), Escape(s.sha));
            sb.Append("}");
            first = false;
        }
        sb.Append("]");
        sb.Append("}");
        return sb.ToString();
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
            // no-op for cleanup
        }
    }

    /// <summary>
    /// Verifies that TryGetRepoVersion returns true and the out parameter is the exact RepositoryRecord instance
    /// when a repository path matches the provided mapping name ignoring case.
    /// Inputs:
    ///  - SourceManifest with a single RepositoryRecord at "repo/A".
    ///  - mappingName "REPO/a" (different casing).
    /// Expected:
    ///  - Method returns true.
    ///  - Out parameter is the same RepositoryRecord instance in the manifest.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void TryGetRepoVersion_RepositoryMatch_IgnoresCaseAndReturnsRepositoryInstance()
    {
        // Arrange
        var repo = new RepositoryRecord(path: "repo/A", remoteUri: "https://example/repoA", commitSha: "abc123", barId: 42);
        var manifest = new SourceManifest(new List<RepositoryRecord> { repo }, new List<SubmoduleRecord>());

        // Act
        var found = manifest.TryGetRepoVersion("REPO/a", out var version);

        // Assert
        found.Should().BeTrue();
        version.Should().BeSameAs(repo);
    }

    /// <summary>
    /// Verifies that TryGetRepoVersion returns true and the out parameter is the exact SubmoduleRecord instance
    /// when a submodule path matches the provided mapping name ignoring case, and there is no repository match.
    /// Inputs:
    ///  - SourceManifest with a single SubmoduleRecord at "src/sub/module".
    ///  - mappingName "SRC/SUB/MODULE" (different casing).
    /// Expected:
    ///  - Method returns true.
    ///  - Out parameter is the same SubmoduleRecord instance in the manifest.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void TryGetRepoVersion_SubmoduleMatch_IgnoresCaseAndReturnsSubmoduleInstance()
    {
        // Arrange
        var sub = new SubmoduleRecord(path: "src/sub/module", remoteUri: "https://example/sub", commitSha: "def456");
        var manifest = new SourceManifest(new List<RepositoryRecord>(), new List<SubmoduleRecord> { sub });

        // Act
        var found = manifest.TryGetRepoVersion("SRC/SUB/MODULE", out var version);

        // Assert
        found.Should().BeTrue();
        version.Should().BeSameAs(sub);
    }

    /// <summary>
    /// Ensures repository precedence over submodules when both contain a record with the same path.
    /// Inputs:
    ///  - SourceManifest with RepositoryRecord and SubmoduleRecord both at path "shared/path".
    ///  - mappingName "shared/path".
    /// Expected:
    ///  - Method returns true.
    ///  - Out parameter references the RepositoryRecord (repositories are searched first).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void TryGetRepoVersion_BothRepoAndSubmoduleWithSamePath_PrefersRepository()
    {
        // Arrange
        var repo = new RepositoryRecord(path: "shared/path", remoteUri: "https://example/repo", commitSha: "aaa111", barId: 7);
        var sub = new SubmoduleRecord(path: "shared/path", remoteUri: "https://example/sub", commitSha: "bbb222");
        var manifest = new SourceManifest(new List<RepositoryRecord> { repo }, new List<SubmoduleRecord> { sub });

        // Act
        var found = manifest.TryGetRepoVersion("shared/path", out var version);

        // Assert
        found.Should().BeTrue();
        version.Should().BeSameAs(repo);
    }

    /// <summary>
    /// Validates that TryGetRepoVersion returns false and sets the out parameter to null when
    /// no repository or submodule path matches the provided mapping name.
    /// Inputs:
    ///  - SourceManifest with repository "repo/A" and submodule "src/B".
    ///  - mappingName values that do not match any path (empty, whitespace, long, special chars, trailing space, unrelated).
    /// Expected:
    ///  - Method returns false.
    ///  - Out parameter is null.
    /// </summary>
    [TestCaseSource(nameof(InvalidMappingNames))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void TryGetRepoVersion_NoMatch_ReturnsFalseAndNull(string mappingName)
    {
        // Arrange
        var repo = new RepositoryRecord(path: "repo/A", remoteUri: "https://example/repoA", commitSha: "abc123", barId: null);
        var sub = new SubmoduleRecord(path: "src/B", remoteUri: "https://example/subB", commitSha: "def456");
        var manifest = new SourceManifest(new List<RepositoryRecord> { repo }, new List<SubmoduleRecord> { sub });

        // Act
        var found = manifest.TryGetRepoVersion(mappingName, out var version);

        // Assert
        found.Should().BeFalse();
        version.Should().BeNull();
    }

    private static IEnumerable<string> InvalidMappingNames()
    {
        yield return "";
        yield return " ";
        yield return "notfound";
        yield return "repo/A "; // trailing space - should not match
        yield return new string('x', 2048);
        yield return "weird-π/路径/🚀";
    }

    /// <summary>
    /// Verifies that when the mapping name matches a repository record (case-insensitively),
    /// the exact repository instance is returned.
    /// Inputs:
    ///  - SourceManifest with repositories containing "src/Repo-A".
    ///  - mappingName variants differing by case.
    /// Expected:
    ///  - Returns the same repository instance stored in the manifest.
    /// </summary>
    [TestCase("src/Repo-A")]
    [TestCase("SRC/repo-a")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetRepoVersion_MappingExistsInRepositories_ReturnsRepositoryRecord(string mappingName)
    {
        // Arrange
        var repoA = new RepositoryRecord("src/Repo-A", "https://example.com/repo-a.git", "sha-aaa", 1);
        var repoB = new RepositoryRecord("src/Repo-B", "https://example.com/repo-b.git", "sha-bbb", 2);
        var subX = new SubmoduleRecord("sub/Module-X", "https://example.com/module-x.git", "sha-xxx");
        var manifest = new SourceManifest(new[] { repoA, repoB }, new[] { subX });

        // Act
        var result = manifest.GetRepoVersion(mappingName);

        // Assert
        result.Should().BeSameAs(repoA);
    }

    /// <summary>
    /// Verifies that when the mapping name matches only a submodule record (case-insensitively),
    /// the exact submodule instance is returned.
    /// Inputs:
    ///  - SourceManifest with submodule "sub/Module-Y" and repositories that don't match.
    ///  - mappingName "sub/module-y" (different casing).
    /// Expected:
    ///  - Returns the same submodule instance stored in the manifest.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetRepoVersion_MappingExistsInSubmodules_ReturnsSubmoduleRecord()
    {
        // Arrange
        var repoA = new RepositoryRecord("src/Repo-A", "https://example.com/repo-a.git", "sha-aaa", 1);
        var subY = new SubmoduleRecord("sub/Module-Y", "https://example.com/module-y.git", "sha-yyy");
        var manifest = new SourceManifest(new[] { repoA }, new[] { subY });

        // Act
        var result = manifest.GetRepoVersion("sub/module-y");

        // Assert
        result.Should().BeSameAs(subY);
    }

    /// <summary>
    /// Ensures that when both a repository and submodule share the same mapping name,
    /// the repository record takes precedence, per TryGetRepoVersion implementation.
    /// Inputs:
    ///  - SourceManifest where both collections contain a record with Path "dup/Same".
    /// Expected:
    ///  - Returns the repository instance, not the submodule.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetRepoVersion_RepositoryTakesPrecedenceOverSubmodule_ReturnsRepositoryRecord()
    {
        // Arrange
        var repoDup = new RepositoryRecord("dup/Same", "https://example.com/repo-dup.git", "sha-repo", 123);
        var subDup = new SubmoduleRecord("dup/Same", "https://example.com/sub-dup.git", "sha-sub");
        var manifest = new SourceManifest(new[] { repoDup }, new[] { subDup });

        // Act
        var result = manifest.GetRepoVersion("dup/same");

        // Assert
        result.Should().BeSameAs(repoDup);
    }

    /// <summary>
    /// Validates that when the mapping name does not exist in either repositories or submodules,
    /// GetRepoVersion throws an Exception with a descriptive message.
    /// Inputs:
    ///  - Various non-matching mappingName values (empty, whitespace, long, special characters, typical not-found).
    /// Expected:
    ///  - Throws Exception with message: "No manifest record named {mappingName} found".
    /// </summary>
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("not-found")]
    [TestCase("invalid?name*")]
    [TestCase("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetRepoVersion_MissingMapping_ThrowsWithHelpfulMessage(string mappingName)
    {
        // Arrange
        var repoA = new RepositoryRecord("src/Repo-A", "https://example.com/repo-a.git", "sha-aaa", 1);
        var subX = new SubmoduleRecord("sub/Module-X", "https://example.com/module-x.git", "sha-xxx");
        var manifest = new SourceManifest(new[] { repoA }, new[] { subX });

        // Act
        Action act = () => manifest.GetRepoVersion(mappingName);

        // Assert
        act.Should().Throw<Exception>()
            .WithMessage($"No manifest record named {mappingName} found");
    }

    /// <summary>
    /// Ensures that when the given path is missing or invalid for File.Exists,
    /// FromFile returns a SourceManifest with empty Repositories and Submodules without throwing.
    /// Inputs:
    ///  - Various path strings that will result in File.Exists(path) == false.
    /// Expected:
    ///  - A non-null SourceManifest instance whose Repositories and Submodules are empty.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(MissingPathCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void FromFile_PathDoesNotExistOrInvalid_ReturnsEmptyManifest(string path)
    {
        // Arrange
        // (no additional setup required)

        // Act
        var manifest = SourceManifest.FromFile(path);

        // Assert
        manifest.Should().NotBeNull();
        manifest.Repositories.Should().BeEmpty();
        manifest.Submodules.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that when the file exists and contains valid JSON in the expected shape,
    /// FromFile parses and returns a SourceManifest reflecting the repositories and submodules.
    /// Inputs:
    ///  - A temp file with JSON containing one repository and one submodule.
    /// Expected:
    ///  - Repositories.Count == 1 and Submodules.Count == 1.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void FromFile_ExistingValidJson_ParsesRepositoriesAndSubmodules()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"sm_valid_{Guid.NewGuid():N}.json");
        var json = @"{
  ""repositories"": [
    { ""path"": ""repo/a"", ""remoteUri"": ""https://example.com/a"", ""commitSha"": ""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"", ""barId"": 42 }
  ],
  ""submodules"": [
    { ""path"": ""sub/one"", ""remoteUri"": ""https://example.com/s"", ""commitSha"": ""bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"" }
  ]
}";
        File.WriteAllText(tempFile, json);

        try
        {
            // Act
            var manifest = SourceManifest.FromFile(tempFile);

            // Assert
            manifest.Should().NotBeNull();
            manifest.Repositories.Should().HaveCount(1);
            manifest.Submodules.Should().HaveCount(1);
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

    /// <summary>
    /// Ensures that when the file exists but contains invalid JSON,
    /// FromFile propagates the deserialization exception.
    /// Inputs:
    ///  - A temp file with non-JSON content.
    /// Expected:
    ///  - A JsonException is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void FromFile_ExistingInvalidJson_ThrowsJsonException()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"sm_invalid_{Guid.NewGuid():N}.json");
        File.WriteAllText(tempFile, "not json at all");

        try
        {
            // Act
            Action act = () => SourceManifest.FromFile(tempFile);

            // Assert
            act.Should().Throw<JsonException>();
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

    private static IEnumerable MissingPathCases()
    {
        yield return string.Empty; // File.Exists("") == false
        yield return "   ";        // File.Exists("   ") == false
        yield return Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.json"); // non-existent absolute path
    }

    /// <summary>
    /// Ensures that an empty JSON object is deserialized into a SourceManifest with empty
    /// Repositories and Submodules collections.
    /// Inputs:
    ///  - json: "{}"
    /// Expected:
    ///  - Non-null SourceManifest instance.
    ///  - Repositories and Submodules are empty.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void FromJson_EmptyObject_ReturnsManifestWithEmptyCollections()
    {
        // Arrange
        var json = "{}";

        // Act
        var manifest = SourceManifest.FromJson(json);

        // Assert
        manifest.Should().NotBeNull();
        manifest.Repositories.Should().BeEmpty();
        manifest.Submodules.Should().BeEmpty();
    }

    /// <summary>
    /// Validates that property name case-insensitivity and trailing commas are honored during deserialization.
    /// Inputs:
    ///  - json: with "Repositories" and "SubModules" (different casing) and a trailing comma.
    /// Expected:
    ///  - Non-null SourceManifest instance.
    ///  - Repositories and Submodules are empty.
    ///  - No exceptions thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void FromJson_EmptyArraysWithTrailingCommasAndCaseInsensitivity_ParsesSuccessfully()
    {
        // Arrange
        var json = "{ \"Repositories\": [], \"SubModules\": [], }";

        // Act
        var manifest = SourceManifest.FromJson(json);

        // Assert
        manifest.Should().NotBeNull();
        manifest.Repositories.Should().BeEmpty();
        manifest.Submodules.Should().BeEmpty();
    }

    /// <summary>
    /// Ensures that when the JSON input is the literal "null", the method throws a specific Exception
    /// as implemented by the null-coalescing throw in the method.
    /// Inputs:
    ///  - json: "null"
    /// Expected:
    ///  - Throws Exception with message "Failed to deserialize source-manifest.json".
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void FromJson_NullLiteral_ThrowsExpectedException()
    {
        // Arrange
        var json = "null";

        // Act
        Action act = () => SourceManifest.FromJson(json);

        // Assert
        act.Should().Throw<Exception>()
           .WithMessage("Failed to deserialize source-manifest.json");
    }

    /// <summary>
    /// Verifies that invalid JSON inputs (empty, whitespace, or non-JSON text) result in JsonException
    /// thrown by the underlying System.Text.Json deserializer.
    /// Inputs:
    ///  - json: "", "   ", "not json"
    /// Expected:
    ///  - Throws JsonException.
    /// </summary>
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not json")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void FromJson_InvalidJson_ThrowsJsonException(string json)
    {
        // Arrange
        // (json provided by TestCase)

        // Act
        Action act = () => SourceManifest.FromJson(json);

        // Assert
        act.Should().Throw<JsonException>();
    }

    /// <summary>
    /// Verifies that ToWrapper returns a wrapper whose Repositories and Submodules
    /// point to the SourceManifest's internal collections so that mutations through the wrapper
    /// are visible via the SourceManifest's public read-only views.
    /// Inputs:
    ///  - A SourceManifest constructed with non-empty repository and submodule collections.
    /// Expected:
    ///  - Wrapper collections contain equivalent items to the SourceManifest's collections.
    ///  - Adding new items through the wrapper reflects in SourceManifest.Repositories/Submodules (reference sharing).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ToWrapper_NonEmptyCollections_PreservesContentsAndSharesReferences()
    {
        // Arrange
        var initialRepositories = new List<RepositoryRecord>
            {
                new RepositoryRecord("src/repo-a", "https://example.org/repo-a.git", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 1),
                new RepositoryRecord("src/repo-b", "https://example.org/repo-b.git", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", null),
            };

        var initialSubmodules = new List<SubmoduleRecord>
            {
                new SubmoduleRecord("sub/one", "https://example.org/sub-one.git", "1111111111111111111111111111111111111111"),
                new SubmoduleRecord("sub/two", "https://example.org/sub-two.git", "2222222222222222222222222222222222222222"),
            };

        var manifest = new SourceManifest(initialRepositories, initialSubmodules);

        // Act
        var wrapper = SourceManifest.ToWrapper(manifest);

        // Assert
        // - Wrapper contains equivalent items to manifest (by Path) for both collections
        wrapper.Repositories.Select(r => r.Path)
            .Should().BeEquivalentTo(manifest.Repositories.Select(m => m.Path));
        wrapper.Submodules.Select(s => s.Path)
            .Should().BeEquivalentTo(manifest.Submodules.Select(m => m.Path));

        // - Mutations via wrapper are reflected in manifest's read-only views (shared references)
        var newRepo = new RepositoryRecord("src/repo-c", "https://example.org/repo-c.git", "cccccccccccccccccccccccccccccccccccccccc", int.MaxValue);
        var newSub = new SubmoduleRecord("sub/three", "https://example.org/sub-three.git", "3333333333333333333333333333333333333333");

        wrapper.Repositories.Add(newRepo);
        wrapper.Submodules.Add(newSub);

        manifest.Repositories.Any(m => m.Path == newRepo.Path).Should().BeTrue();
        manifest.Submodules.Any(m => m.Path == newSub.Path).Should().BeTrue();
    }

    /// <summary>
    /// Ensures that ToWrapper works for empty collections and that the returned wrapper
    /// shares references to the SourceManifest's internal collections (mutations are visible).
    /// Inputs:
    ///  - A SourceManifest created with empty repository and submodule collections.
    /// Expected:
    ///  - Wrapper collections are empty initially.
    ///  - Adding items through the wrapper makes them visible through SourceManifest.Repositories/Submodules.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ToWrapper_EmptyCollections_WrapperStartsEmptyAndMutationsVisible()
    {
        // Arrange
        var manifest = new SourceManifest(new List<RepositoryRecord>(), new List<SubmoduleRecord>());

        // Act
        var wrapper = SourceManifest.ToWrapper(manifest);

        // Assert
        wrapper.Repositories.Should().BeEmpty();
        wrapper.Submodules.Should().BeEmpty();

        var addedRepo = new RepositoryRecord("src/empty-repo", "https://example.org/empty-repo.git", "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", 0);
        var addedSub = new SubmoduleRecord("sub/empty", "https://example.org/empty-sub.git", "ffffffffffffffffffffffffffffffffffffffff");

        wrapper.Repositories.Add(addedRepo);
        wrapper.Submodules.Add(addedSub);

        manifest.Repositories.Any(m => m.Path == "src/empty-repo").Should().BeTrue();
        manifest.Submodules.Any(m => m.Path == "sub/empty").Should().BeTrue();
    }

    /// <summary>
    /// Verifies that when the repositories collection is empty (even if a submodule with the same path exists),
    /// GetVersion returns null.
    /// Inputs:
    ///  - repositories: empty
    ///  - submodules: one submodule with the queried path
    ///  - repository path queried: "vmr/src/mod-only"
    /// Expected:
    ///  - Result is null because only repositories are considered by GetVersion.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetVersion_MatchingSubmoduleOnly_ReturnsNull()
    {
        // Arrange
        var repositories = new List<RepositoryRecord>();
        var submodules = new List<SubmoduleRecord>
            {
                new SubmoduleRecord("vmr/src/mod-only", "https://example.com/repo.git", "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef")
            };
        var manifest = new SourceManifest(repositories, submodules);

        // Act
        var result = manifest.GetVersion("vmr/src/mod-only");

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Ensures that GetVersion performs an exact, case-sensitive match on repository Path
    /// and returns a non-null VmrDependencyVersion only when the queried repository path exactly matches
    /// the existing repository record Path.
    /// Inputs:
    ///  - One repository record (Path varies per test case).
    ///  - Query path (varies per test case).
    /// Expected:
    ///  - Non-null when exact match; otherwise null.
    /// Edge cases covered:
    ///  - Empty string, whitespace-only string, very long string, Unicode and special-character paths,
    ///    case differences, and trailing/leading character differences.
    /// </summary>
    [TestCaseSource(nameof(GetVersion_ExactMatch_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetVersion_VariousRepositoryPathInputs_ReturnsVersionOnlyOnExactMatch(string recordPath, string queryPath, bool expectedFound)
    {
        // Arrange
        var repositories = new List<RepositoryRecord>
            {
                new RepositoryRecord(
                    recordPath,
                    "https://example.com/repo.git",
                    "0123456789abcdef0123456789abcdef01234567",
                    42)
            };
        var manifest = new SourceManifest(repositories, new List<SubmoduleRecord>());

        // Act
        var result = manifest.GetVersion(queryPath);

        // Assert
        if (expectedFound)
        {
            result.Should().NotBeNull().And.BeOfType<VmrDependencyVersion>();
        }
        else
        {
            result.Should().BeNull();
        }
    }

    private static IEnumerable<TestCaseData> GetVersion_ExactMatch_Cases()
    {
        // Normal exact match
        yield return new TestCaseData("vmr/src/repo-a", "vmr/src/repo-a", true)
            .SetName("ExactMatch_NormalPath_ReturnsVersion");

        // Case sensitivity: different case should not match
        yield return new TestCaseData("vmr/src/repo-b", "VMR/SRC/REPO-B", false)
            .SetName("DifferentCase_Path_ReturnsNull");

        // Trailing char difference
        yield return new TestCaseData("vmr/src/repo-c", "vmr/src/repo-c/", false)
            .SetName("TrailingSlashDifference_ReturnsNull");

        // Leading/trailing whitespace difference
        yield return new TestCaseData("vmr/src/repo-d", " vmr/src/repo-d ", false)
            .SetName("LeadingTrailingWhitespaceDifference_ReturnsNull");

        // Empty string path exact match
        yield return new TestCaseData(string.Empty, string.Empty, true)
            .SetName("EmptyString_ExactMatch_ReturnsVersion");

        // Whitespaces exact match
        yield return new TestCaseData("   ", "   ", true)
            .SetName("WhitespaceOnly_ExactMatch_ReturnsVersion");

        // Special characters path exact match
        yield return new TestCaseData("~!@#$%^&()_+|{}:\"<>?-=[]\\;,./", "~!@#$%^&()_+|{}:\"<>?-=[]\\;,./", true)
            .SetName("SpecialCharacters_ExactMatch_ReturnsVersion");

        // Unicode path exact match
        yield return new TestCaseData("répô/路径/репо", "répô/路径/репо", true)
            .SetName("Unicode_ExactMatch_ReturnsVersion");

        // Very long path exact match
        var longSegment = new string('a', 1024);
        var longPath = $"{longSegment}/{longSegment}/{longSegment}";
        yield return new TestCaseData(longPath, longPath, true)
            .SetName("VeryLongPath_ExactMatch_ReturnsVersion");
    }

    /// <summary>
    /// Ensures that when a repository record with an exactly matching Path exists in the manifest,
    /// the method returns that record instance.
    /// Inputs:
    ///  - A SourceManifest containing a single RepositoryRecord whose Path equals the provided mapping name.
    ///  - A variety of path forms (empty, whitespace, long, special characters) to validate exact matching behavior.
    /// Expected:
    ///  - The method returns the same RepositoryRecord instance (reference equality).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(ValidPaths))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetRepositoryRecord_WhenMatchingPathExists_ReturnsRecord(string path)
    {
        // Arrange
        var repo = new RepositoryRecord(path, "https://example.org/repo.git", "abcd1234", null);
        var manifest = new SourceManifest(new[] { repo }, Array.Empty<SubmoduleRecord>());

        // Act
        var result = manifest.GetRepositoryRecord(path);

        // Assert
        result.Should().BeSameAs(repo);
    }

    /// <summary>
    /// Validates that when no repository record with the given mapping name exists,
    /// the method throws an Exception with a clear message.
    /// Inputs:
    ///  - An empty SourceManifest (no repositories).
    ///  - A variety of mapping names including empty, whitespace, and special characters.
    /// Expected:
    ///  - Throws Exception with message: "No repository record named {mappingName} found".
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(InvalidMappingNames))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetRepositoryRecord_WhenNoMatchingRecord_ThrowsWithHelpfulMessage(string mappingName)
    {
        // Arrange
        var manifest = new SourceManifest(Array.Empty<RepositoryRecord>(), Array.Empty<SubmoduleRecord>());

        // Act
        Action act = () => manifest.GetRepositoryRecord(mappingName);

        // Assert
        act.Should().Throw<Exception>().WithMessage($"No repository record named {mappingName} found");
    }

    private static IEnumerable<string> ValidPaths()
    {
        yield return "repo";
        yield return "Repo"; // case-sensitive identity
        yield return ""; // empty path
        yield return "  "; // whitespace-only
        yield return new string('a', 1024); // very long
        yield return "path/with/slash";
        yield return "with:colon*and?invalid|chars";
        yield return "répô🚀"; // unicode
        yield return "\t\n"; // control characters
    }

}

/// <summary>
/// Placeholder tests for ISourceManifest.RemoveRepository(string repository).
/// The implementation bodies are not available in the provided scope, making it impossible to validate behavior.
/// Guidance:
/// - Replace the [Ignore] attribute and instantiate a concrete implementation (e.g., SourceManifest) when accessible.
/// - Seed it with repositories, call RemoveRepository with the provided input, and assert:
///   * When input matches an existing repository mapping name, it is removed from Repositories.
///   * When input does not match any existing repository, state remains unchanged and no exception is thrown (unless specified).
///   * Validate exception behavior for null/invalid inputs if defined by the implementation.
/// </summary>
public class ISourceManifestTests
{
    /// <summary>
    /// Pending test for RemoveRepository covering a variety of input strings:
    /// - null, empty, whitespace-only
    /// - typical names, paths, special and Unicode characters
    /// - very long values, control characters
    /// Expected (to be confirmed once implementation is available):
    /// - Either the repository is removed (if exists) or appropriate exception/validation occurs.
    /// </summary>
    /// <param name="repository">Repository mapping name to remove.</param>
    [Test]
    [Category("auto-generated")]
    [Ignore("Implementation not available in provided scope. Replace with real instance and assert resulting state/behavior.")]
    [TestCaseSource(nameof(RemoveRepository_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RemoveRepository_VariousInputs_PendingBehavior(string repository)
    {
        // Arrange
        // TODO: Instantiate a concrete ISourceManifest (e.g., new SourceManifest(...)) with seeded repositories.

        // Act
        // TODO: Call RemoveRepository(repository) on the concrete instance.

        // Assert
        // TODO: Assert expected behavior:
        // - If repository corresponds to an existing mapping name, ensure it is removed from Repositories.
        // - If not existing, confirm state unchanged or expected exception, per implementation contract.
        // - Validate exception behavior for null/invalid inputs if defined by the implementation.
    }

    private static IEnumerable<TestCaseData> RemoveRepository_Cases()
    {
        yield return new TestCaseData(null).SetName("Null");
        yield return new TestCaseData(string.Empty).SetName("Empty");
        yield return new TestCaseData(" ").SetName("Whitespace_Space");
        yield return new TestCaseData("\t\r\n").SetName("Whitespace_TabsAndNewLines");
        yield return new TestCaseData("repo").SetName("SimpleName");
        yield return new TestCaseData("owner/repo").SetName("OwnerSlashRepo");
        yield return new TestCaseData("UPPER_lower-123").SetName("AlphaNumericAndDashes");
        yield return new TestCaseData(new string('a', 4096)).SetName("VeryLongName_4096");
        yield return new TestCaseData(@"special!@#$%^&*()_+-={}[]|\:;""'<>,.?/~`").SetName("SpecialCharacters");
        yield return new TestCaseData("unicode-汉字-🚀").SetName("UnicodeCharacters");
        yield return new TestCaseData("contains\0null").SetName("ContainsNullChar");
        yield return new TestCaseData(@"relative\path\..\repo").SetName("RelativeLikePath");
    }
}

/// <summary>
/// Tests for SourceManifestWrapper.ToSourceManifest.
/// Since SourceManifestWrapper is internal, tests exercise the method via SourceManifest.FromJson,
/// which internally deserializes to SourceManifestWrapper and then calls ToSourceManifest.
/// </summary>
[TestFixture]
public class SourceManifestWrapperTests
{
    /// <summary>
    /// Verifies that ToSourceManifest correctly maps wrapper collections into a SourceManifest
    /// by round-tripping through SourceManifest.ToJson and SourceManifest.FromJson.
    /// Inputs:
    ///  - Parameterized sets of repositories and submodules (including edge values).
    /// Expected:
    ///  - The round-tripped SourceManifest contains equivalent repositories and submodules
    ///    (ignoring ordering), proving ToSourceManifest mapped the collections correctly.
    /// </summary>
    [TestCaseSource(nameof(RoundTripCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ToSourceManifest_RoundTrip_PreservesCollections(
        IEnumerable<RepositoryRecord> repositories,
        IEnumerable<SubmoduleRecord> submodules)
    {
        // Arrange
        var original = new SourceManifest(repositories, submodules);

        // Act
        var json = original.ToJson();
        var roundTripped = SourceManifest.FromJson(json);

        // Assert
        var actualRepos = roundTripped.Repositories
            .OfType<RepositoryRecord>()
            .Select(r => new { r.Path, r.RemoteUri, r.CommitSha, r.BarId })
            .ToList();
        var expectedRepos = repositories
            .Select(r => new { r.Path, r.RemoteUri, r.CommitSha, r.BarId })
            .ToList();

        actualRepos.Should().BeEquivalentTo(expectedRepos);

        var actualSubmodules = roundTripped.Submodules
            .OfType<SubmoduleRecord>()
            .Select(s => new { s.Path, s.RemoteUri, s.CommitSha })
            .ToList();
        var expectedSubmodules = submodules
            .Select(s => new { s.Path, s.RemoteUri, s.CommitSha })
            .ToList();

        actualSubmodules.Should().BeEquivalentTo(expectedSubmodules);
    }

    /// <summary>
    /// Ensures that when JSON is missing both "repositories" and "submodules" properties,
    /// the wrapper defaults to empty collections and ToSourceManifest returns an empty manifest.
    /// Inputs:
    ///  - Raw JSON "{}" (no arrays present).
    /// Expected:
    ///  - SourceManifest has empty Repositories and Submodules.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ToSourceManifest_FromJsonWithMissingCollections_YieldsEmptySets()
    {
        // Arrange
        var json = "{}";

        // Act
        var manifest = SourceManifest.FromJson(json);

        // Assert
        manifest.Repositories.Should().BeEmpty();
        manifest.Submodules.Should().BeEmpty();
    }

    /// <summary>
    /// Validates behavior when JSON explicitly sets "repositories" and "submodules" to null.
    /// This results in the wrapper having null collections; ToSourceManifest then receives null enumerables.
    /// Inputs:
    ///  - Raw JSON with "repositories": null and "submodules": null.
    /// Expected:
    ///  - An exception is thrown during conversion to SourceManifest (invalid/null collections).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ToSourceManifest_FromJsonWithNullCollections_Throws()
    {
        // Arrange
        var json = "{ \"repositories\": null, \"submodules\": null }";

        // Act
        Action act = () => SourceManifest.FromJson(json);

        // Assert
        act.Should().Throw<Exception>();
    }

    // Test case source providing diverse repository and submodule sets, including edge values.
    private static IEnumerable<TestCaseData> RoundTripCases()
    {
        // Empty collections
        yield return new TestCaseData(
            new List<RepositoryRecord>(),
            new List<SubmoduleRecord>())
            .SetName("ToSourceManifest_RoundTrip_WithEmptyCollections_YieldsEmpty");

        // Mixed repositories/submodules with edge-case values
        yield return new TestCaseData(
            new List<RepositoryRecord>
            {
                    // null BAR ID
                    new RepositoryRecord(
                        "repo-a",
                        "https://example.com/a",
                        "0000000000000000000000000000000000000001",
                        null),

                    // Max int BAR ID, special chars in path/uri
                    new RepositoryRecord(
                        "org/repo-b with space & symbols!@#",
                        "ssh://git@host:2222/org/repo-b.git",
                        "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF",
                        int.MaxValue),
            },
            new List<SubmoduleRecord>
            {
                    new SubmoduleRecord(
                        "submods/path-1",
                        "https://example.com/sub1",
                        "1111111111111111111111111111111111111111"),
                    new SubmoduleRecord(
                        "nested/submods/深い/путь",
                        "https://example.com/sub2?param=value&other=%20",
                        "abcdefabcdefabcdefabcdefabcdefabcdefabcd"),
            })
            .SetName("ToSourceManifest_RoundTrip_WithRepositoriesAndSubmodules_PreservesItems");
    }
}
