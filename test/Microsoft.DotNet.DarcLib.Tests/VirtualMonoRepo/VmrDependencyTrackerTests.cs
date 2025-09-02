// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class VmrDependencyTrackerTests
{
    /// <summary>
    /// Ensures TryGetMapping throws an exception if source mappings were not initialized.
    /// Input: Any mapping name when _mappings is null (default after construction).
    /// Expected: Exception with message "Source mappings have not been initialized."
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void TryGetMapping_MappingsNotInitialized_ThrowsException()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Loose);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var parser = new Mock<ISourceMappingParser>(MockBehavior.Loose);
        var manifest = new Mock<Models.VirtualMonoRepo.ISourceManifest>(MockBehavior.Loose);
        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var sut = new VmrDependencyTracker(
            vmrInfo.Object,
            fileSystem.Object,
            parser.Object,
            manifest.Object,
            logger.Object);

        // Act
        Action act = () => sut.TryGetMapping("anything", out var _);

        // Assert
        act.Should().Throw<Exception>()
            .WithMessage("Source mappings have not been initialized.");
    }

    /// <summary>
    /// Verifies TryGetMapping returns expected boolean and mapping for a variety of input names.
    /// Inputs: A set of names including exact case, different casing, empty, whitespace, special chars, and long string.
    /// Expected: True with correct mapping for case-insensitive matches; otherwise false with null mapping.
    /// </summary>
    [TestCaseSource(nameof(TryGetMapping_NameCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryGetMapping_NameVariants_ReturnsExpected(string inputName, bool expectedFound)
    {
        // Arrange
        var mappings = new List<SourceMapping>
            {
                new SourceMapping(
                    "RepoA",
                    "https://example.com/repoA.git",
                    "main",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false),
                new SourceMapping(
                    "Other",
                    "https://example.com/other.git",
                    "main",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false),
            };

        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupGet(x => x.SourceManifestPath).Returns("manifest.json");

        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);

        var parser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        parser.Setup(x => x.ParseMappings("path.json"))
              .ReturnsAsync(mappings);

        var manifest = new Mock<Models.VirtualMonoRepo.ISourceManifest>(MockBehavior.Strict);
        manifest.Setup(x => x.Refresh("manifest.json"));

        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var sut = new VmrDependencyTracker(
            vmrInfo.Object,
            fileSystem.Object,
            parser.Object,
            manifest.Object,
            logger.Object);

        await sut.RefreshMetadataAsync("path.json");

        // Act
        var result = sut.TryGetMapping(inputName, out var mapping);

        // Assert
        result.Should().Be(expectedFound);
        if (expectedFound)
        {
            mapping.Should().NotBeNull();
            mapping.Name.Should().Be("RepoA");
        }
        else
        {
            mapping.Should().BeNull();
        }
    }

    /// <summary>
    /// Ensures TryGetMapping can successfully locate a mapping whose name is an empty string.
    /// Input: Empty string when mappings contain an entry with empty Name.
    /// Expected: True with the mapping whose Name is empty.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryGetMapping_EmptyNameMapping_ReturnsTrueForEmptyInput()
    {
        // Arrange
        var mappings = new List<SourceMapping>
            {
                new SourceMapping(
                    "",
                    "https://example.com/empty.git",
                    "main",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false),
                new SourceMapping(
                    "NonEmpty",
                    "https://example.com/nonempty.git",
                    "main",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false),
            };

        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupGet(x => x.SourceManifestPath).Returns("manifest.json");

        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);

        var parser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        parser.Setup(x => x.ParseMappings("path.json"))
              .ReturnsAsync(mappings);

        var manifest = new Mock<Models.VirtualMonoRepo.ISourceManifest>(MockBehavior.Strict);
        manifest.Setup(x => x.Refresh("manifest.json"));

        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var sut = new VmrDependencyTracker(
            vmrInfo.Object,
            fileSystem.Object,
            parser.Object,
            manifest.Object,
            logger.Object);

        await sut.RefreshMetadataAsync("path.json");

        // Act
        var found = sut.TryGetMapping(string.Empty, out var mapping);

        // Assert
        found.Should().BeTrue();
        mapping.Should().NotBeNull();
        mapping.Name.Should().Be(string.Empty);
    }

    private static IEnumerable TryGetMapping_NameCases()
    {
        yield return new TestCaseData("RepoA", true).SetName("ExactCaseMatch_ReturnsTrue");
        yield return new TestCaseData("repoa", true).SetName("DifferentCaseMatch_ReturnsTrue");
        yield return new TestCaseData("unknown", false).SetName("UnknownName_ReturnsFalse");
        yield return new TestCaseData("", false).SetName("EmptyString_NoMatchingEmptyMapping_ReturnsFalse");
        yield return new TestCaseData("   ", false).SetName("WhitespaceOnly_ReturnsFalse");
        yield return new TestCaseData("#$%^&*()", false).SetName("SpecialCharacters_ReturnsFalse");
        yield return new TestCaseData(new string('x', 2048), false).SetName("VeryLongString_ReturnsFalse");
    }

    /// <summary>
    /// Verifies that GetMapping returns the correct mapping when the name is present.
    /// The mapping name comparison must be case-insensitive.
    /// Expects the method to return the mapping without throwing.
    /// </summary>
    /// <param name="inputName">Variant of the existing source mapping name differing by case.</param>
    [TestCase("Repo")]
    [TestCase("repo")]
    [TestCase("REPO")]
    [TestCase("RePo")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetMapping_NameExistsDifferentCasing_ReturnsMapping(string inputName)
    {
        // Arrange
        var expected = new SourceMapping(
            name: "Repo",
            defaultRemote: "https://example",
            defaultRef: "main",
            include: new[] { "**/*" },
            exclude: Array.Empty<string>(),
            disableSynchronization: false);

        var mappings = new List<SourceMapping> { expected };
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Loose);
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        const string mappingsPath = "src/source-mappings.json";
        sourceMappingParser.Setup(x => x.ParseMappings(mappingsPath)).ReturnsAsync(mappings);
        vmrInfo.SetupGet(x => x.SourceManifestPath).Returns("manifest.json");

        var sut = new VmrDependencyTracker(
            vmrInfo.Object,
            fileSystem.Object,
            sourceMappingParser.Object,
            sourceManifest.Object,
            logger.Object);

        await sut.RefreshMetadataAsync(mappingsPath);

        // Act
        var result = sut.GetMapping(inputName);

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that GetMapping throws when the requested mapping name does not exist
    /// in the initialized mappings collection.
    /// Expects an Exception with a specific message indicating the missing name.
    /// </summary>
    /// <param name="missingName">A name that is not present among mappings (includes empty and whitespace).</param>
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("unknown")]
    [TestCase("Repo ")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetMapping_NameNotFound_ThrowsException(string missingName)
    {
        // Arrange
        var onlyMapping = new SourceMapping(
            name: "Repo",
            defaultRemote: "https://example",
            defaultRef: "main",
            include: new[] { "**/*" },
            exclude: Array.Empty<string>(),
            disableSynchronization: false);

        var mappings = new List<SourceMapping> { onlyMapping };
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Loose);
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        const string mappingsPath = "src/source-mappings.json";
        sourceMappingParser.Setup(x => x.ParseMappings(mappingsPath)).ReturnsAsync(mappings);
        vmrInfo.SetupGet(x => x.SourceManifestPath).Returns("manifest.json");

        var sut = new VmrDependencyTracker(
            vmrInfo.Object,
            fileSystem.Object,
            sourceMappingParser.Object,
            sourceManifest.Object,
            logger.Object);

        await sut.RefreshMetadataAsync(mappingsPath);

        // Act
        Action act = () => sut.GetMapping(missingName);

        // Assert
        act.Should().Throw<Exception>().WithMessage($"No mapping named {missingName} found");
    }

    /// <summary>
    /// Verifies that GetMapping throws when source mappings have not been initialized
    /// (i.e., RefreshMetadataAsync has not been called to populate mappings).
    /// Expects an Exception indicating that source mappings are not initialized.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetMapping_MappingsNotInitialized_ThrowsInitializationException()
    {
        // Arrange
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Loose);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Loose);
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Loose);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var sut = new VmrDependencyTracker(
            vmrInfo.Object,
            fileSystem.Object,
            sourceMappingParser.Object,
            sourceManifest.Object,
            logger.Object);

        // Act
        Action act = () => sut.GetMapping("Repo");

        // Assert
        act.Should().Throw<Exception>().WithMessage("Source mappings have not been initialized.");
    }

    /// <summary>
    /// Verifies that RefreshMetadataAsync:
    /// - Invokes the source mapping parser with the provided explicit path (including empty/whitespace/special paths).
    /// - Assigns the returned mappings to the Mappings property.
    /// - Calls source manifest refresh with the expected manifest path.
    /// </summary>
    /// <param name="explicitPath">Explicit source mappings path to parse (edge cases included).</param>
    [TestCase("custom.json")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("C:\\path with spaces\\µß\\file.json")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task RefreshMetadataAsync_ExplicitPath_ParsesMappingsAndRefreshesManifest(string explicitPath)
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var manifestPath = "path/to/source-manifest.json";
        vmrInfoMock.SetupGet(v => v.SourceManifestPath).Returns(manifestPath);

        var parsedMappings = new List<SourceMapping>();
        sourceMappingParserMock
            .Setup(p => p.ParseMappings(explicitPath))
            .ReturnsAsync(parsedMappings);

        sourceManifestMock
            .Setup(m => m.Refresh(manifestPath));

        var sut = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            sourceMappingParserMock.Object,
            sourceManifestMock.Object,
            loggerMock.Object);

        // Act
        await sut.RefreshMetadataAsync(explicitPath);

        // Assert
        sourceMappingParserMock.Verify(p => p.ParseMappings(explicitPath), Times.Once);
        sourceManifestMock.Verify(m => m.Refresh(manifestPath), Times.Once);

        sut.Mappings.Should().BeSameAs(parsedMappings);
    }

    /// <summary>
    /// Verifies that RefreshMetadataAsync works with a very long explicit path
    /// and still initializes mappings and refreshes the manifest without throwing.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task RefreshMetadataAsync_ExplicitVeryLongPath_ParsesMappingsAndRefreshesManifest()
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var explicitPath = new string('a', 1024) + ".json";
        var manifestPath = "/manifest/source-manifest.json";

        vmrInfoMock.SetupGet(v => v.SourceManifestPath).Returns(manifestPath);

        var parsedMappings = new List<SourceMapping>();
        sourceMappingParserMock
            .Setup(p => p.ParseMappings(explicitPath))
            .ReturnsAsync(parsedMappings);

        sourceManifestMock
            .Setup(m => m.Refresh(manifestPath));

        var sut = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            sourceMappingParserMock.Object,
            sourceManifestMock.Object,
            loggerMock.Object);

        // Act
        await sut.RefreshMetadataAsync(explicitPath);

        // Assert
        sourceMappingParserMock.Verify(p => p.ParseMappings(explicitPath), Times.Once);
        sourceManifestMock.Verify(m => m.Refresh(manifestPath), Times.Once);
        sut.Mappings.Should().BeSameAs(parsedMappings);
    }

    /// <summary>
    /// Verifies that when parsing mappings fails, RefreshMetadataAsync:
    /// - Propagates the thrown exception.
    /// - Does NOT refresh the source manifest.
    /// - Leaves Mappings uninitialized so accessing it throws the expected error.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task RefreshMetadataAsync_ParseMappingsThrows_PropagatesAndDoesNotRefresh()
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var explicitPath = "broken.json";
        var manifestPath = "/manifest.json";

        vmrInfoMock.SetupGet(v => v.SourceManifestPath).Returns(manifestPath);

        var boom = new Exception("parse failed");
        sourceMappingParserMock
            .Setup(p => p.ParseMappings(explicitPath))
            .ThrowsAsync(boom);

        var sut = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            sourceMappingParserMock.Object,
            sourceManifestMock.Object,
            loggerMock.Object);

        // Act
        var act = new Func<Task>(() => sut.RefreshMetadataAsync(explicitPath));

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("parse failed");
        sourceManifestMock.Verify(m => m.Refresh(It.IsAny<string>()), Times.Never);

        var accessMappings = new Action(() => { var _ = sut.Mappings; });
        accessMappings.Should().Throw<Exception>()
            .WithMessage("Source mappings have not been initialized.");
    }

    /// <summary>
    /// Partial test for null sourceMappingsPath:
    /// Intended to verify that when sourceMappingsPath is null, the default path composed from IVmrInfo.VmrPath is used.
    /// However, IVmrInfo.VmrPath participates in an overloaded path-combining operator (/) whose concrete type cannot be instantiated or mocked here.
    /// Guidance: Provide a real IVmrInfo implementation or an accessible concrete type for VmrPath with the expected (/) operator, then verify ParseMappings was called with the default-computed path.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void RefreshMetadataAsync_NullPath_UsesDefaultSourceMappingsPath_PartialInconclusive()
    {
        Assert.Inconclusive("Cannot reliably validate default path composition without a concrete, instantiable VmrPath type supporting the (/) operator. Provide a concrete IVmrInfo/VmrPath type to complete this test.");
    }

    /// <summary>
    /// Verifies UpdateDependencyVersion forwards all inputs to the source manifest and persists the updated content.
    /// This test parameterizes:
    /// - mappingName: arbitrary repository (including special characters and whitespace-only)
    /// - remoteUri: various forms (empty, whitespace, long, special characters)
    /// - targetRevision: various forms (empty, whitespace, long, special characters)
    /// - barId: null and boundary integer values
    /// Expected: ISourceManifest.UpdateVersion is invoked with the exact values; ToJson is called; IFileSystem.WriteToFile
    /// is invoked with vmrInfo.SourceManifestPath and the content from ToJson. Call order must be UpdateVersion -> ToJson -> WriteToFile.
    /// </summary>
    [TestCaseSource(nameof(UpdateDependencyVersion_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void UpdateDependencyVersion_ForwardsArgumentsAndPersists(
        string mappingName,
        string remoteUri,
        string targetRevision,
        int? barId)
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        // Ensure the SourceManifestPath is read and used
        vmrInfo.SetupGet(v => v.SourceManifestPath).Returns(default);

        // Prepare mapping and update payload
        var mapping = new SourceMapping(
            mappingName,
            "https://example.org/repo.git",
            "main",
            new List<string> { "**/*" },
            new List<string>(),
            false,
            null);

        var update = new VmrDependencyUpdate(
            mapping,
            remoteUri,
            targetRevision,
            null,
            null,
            barId,
            null);

        var expectedJson = $"{{\"repositories\":[\"{mappingName}\"],\"barId\":\"{(barId?.ToString() ?? "null")}\"}}";
        var capturedJson = string.Empty;

        // Enforce call order: UpdateVersion -> ToJson -> WriteToFile
        var sequence = new MockSequence();
        sourceManifest
            .InSequence(sequence)
            .Setup(m => m.UpdateVersion(
                It.Is<string>(s => s == mappingName),
                It.Is<string>(s => s == remoteUri),
                It.Is<string>(s => s == targetRevision),
                It.Is<int?>(b => b == barId)));

        sourceManifest
            .InSequence(sequence)
            .Setup(m => m.ToJson())
            .Returns(expectedJson);

        fileSystem
            .InSequence(sequence)
            .Setup(fs => fs.WriteToFile(
                It.IsAny<string>(),
                It.Is<string>(s => s == expectedJson)))
            .Callback<string, string>((_, content) => capturedJson = content);

        var sut = new VmrDependencyTracker(
            vmrInfo.Object,
            fileSystem.Object,
            sourceMappingParser.Object,
            sourceManifest.Object,
            logger.Object);

        // Act
        sut.UpdateDependencyVersion(update);

        // Assert
        sourceManifest.Verify(m => m.UpdateVersion(mappingName, remoteUri, targetRevision, barId), Times.Once);
        sourceManifest.Verify(m => m.ToJson(), Times.Once);
        vmrInfo.VerifyGet(v => v.SourceManifestPath, Times.Once);
        fileSystem.Verify(fs => fs.WriteToFile(It.IsAny<string>(), expectedJson), Times.Once);

        capturedJson.Should().Be(expectedJson);
    }

    private static IEnumerable<TestCaseData> UpdateDependencyVersion_Cases()
    {
        // Strings for edge scenarios
        var veryLong = new string('x', 1024);
        var special = "https://exámple.org/repo.git?query=ä&k=v#frag";
        var controlChars = "rev-\u0000-\u0001-\t-\n";
        var whitespace = "   ";

        // mappingName, remoteUri, targetRevision, barId
        yield return new TestCaseData("repo", "https://contoso.example/repo.git", "abcdef0123456789", 123)
            .SetName("UpdateDependencyVersion_NormalValues_Persists");

        yield return new TestCaseData("repo-with-dash", "", "v1.2.3", null)
            .SetName("UpdateDependencyVersion_EmptyRemoteUri_Persists");

        yield return new TestCaseData("  whitespace-name  ", whitespace, whitespace, 0)
            .SetName("UpdateDependencyVersion_WhitespaceInputs_Persists");

        yield return new TestCaseData("repo/special", special, "dev-branch", int.MaxValue)
            .SetName("UpdateDependencyVersion_SpecialCharsAndMaxBarId_Persists");

        yield return new TestCaseData("repo:control", "file:///c:/path", controlChars, -42)
            .SetName("UpdateDependencyVersion_ControlCharsAndNegativeBarId_Persists");

        yield return new TestCaseData("repo-long", veryLong, veryLong, 1)
            .SetName("UpdateDependencyVersion_VeryLongStrings_Persists");
    }

    /// <summary>
    /// Verifies that UpdateSubmodules:
    /// - Calls RemoveSubmodule for records with CommitSha == Constants.EmptyGitObject.
    /// - Calls UpdateSubmodule for records with non-empty CommitSha.
    /// - Persists the manifest exactly once via IFileSystem.WriteToFile.
    /// - Does not mutate the provided submodules collection.
    /// Test cases cover:
    /// - Empty list (no ops).
    /// - Single update.
    /// - Single remove.
    /// - Mixed remove/update.
    /// - Duplicates (multiple updates).
    /// </summary>
    /// <param name="submodules">Input submodule records.</param>
    /// <param name="expectedUpdateCalls">Expected count of UpdateSubmodule calls.</param>
    /// <param name="expectedRemoveCalls">Expected count of RemoveSubmodule calls.</param>
    [TestCaseSource(nameof(UpdateSubmodules_Scenarios))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void UpdateSubmodules_VariousInputs_CallsManifestAndPersistsExpected(
        List<SubmoduleRecord> submodules,
        int expectedUpdateCalls,
        int expectedRemoveCalls)
    {
        // Arrange
        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        sourceManifestMock.Setup(x => x.UpdateSubmodule(It.IsAny<SubmoduleRecord>()));
        sourceManifestMock.Setup(x => x.RemoveSubmodule(It.IsAny<SubmoduleRecord>()));
        sourceManifestMock.Setup(x => x.ToJson()).Returns("json");

        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var manifestPath = new NativePath("/vmr/src/source-manifest.json");
        vmrInfoMock.SetupGet(x => x.SourceManifestPath).Returns(manifestPath);

        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystemMock
            .Setup(x => x.WriteToFile(It.IsAny<string>(), It.IsAny<string>()));

        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var sut = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            sourceMappingParserMock.Object,
            sourceManifestMock.Object,
            loggerMock.Object);

        var originalCount = submodules.Count;

        // Act
        sut.UpdateSubmodules(submodules);

        // Assert
        sourceManifestMock.Verify(
            x => x.UpdateSubmodule(It.IsAny<SubmoduleRecord>()),
            Times.Exactly(expectedUpdateCalls));

        sourceManifestMock.Verify(
            x => x.RemoveSubmodule(It.IsAny<SubmoduleRecord>()),
            Times.Exactly(expectedRemoveCalls));

        sourceManifestMock.Verify(x => x.ToJson(), Times.Once);

        fileSystemMock.Verify(
            x => x.WriteToFile(
                It.Is<string>(p => p == manifestPath),
                It.Is<string>(c => c == "json")),
            Times.Once);

        submodules.Count.Should().Be(originalCount);
    }

    private static IEnumerable UpdateSubmodules_Scenarios()
    {
        // Empty list
        yield return new TestCaseData(
            new List<SubmoduleRecord>(),
            0, // expected UpdateSubmodule calls
            0  // expected RemoveSubmodule calls
        ).SetName("UpdateSubmodules_EmptyList_PersistsManifestWithoutChanges");

        // Single update
        yield return new TestCaseData(
            new List<SubmoduleRecord>
            {
                    new SubmoduleRecord("p1", "u1", "commit-1")
            },
            1,
            0
        ).SetName("UpdateSubmodules_SingleUpdate_UpdatesOnceAndPersists");

        // Single remove
        yield return new TestCaseData(
            new List<SubmoduleRecord>
            {
                    new SubmoduleRecord("p2", "u2", Constants.EmptyGitObject)
            },
            0,
            1
        ).SetName("UpdateSubmodules_SingleRemove_RemovesOnceAndPersists");

        // Mixed add/remove
        yield return new TestCaseData(
            new List<SubmoduleRecord>
            {
                    new SubmoduleRecord("p3", "u3", Constants.EmptyGitObject),
                    new SubmoduleRecord("p4", "u4", "commit-2"),
                    new SubmoduleRecord("p5", "u5", Constants.EmptyGitObject),
            },
            1,
            2
        ).SetName("UpdateSubmodules_MixedInput_CallsCorrectManifestMethodsAndPersists");

        // Duplicates (multiple updates)
        var duplicate = new SubmoduleRecord("p6", "u6", "commit-dup");
        yield return new TestCaseData(
            new List<SubmoduleRecord> { duplicate, duplicate },
            2,
            0
        ).SetName("UpdateSubmodules_DuplicateUpdates_UpdatesForEachOccurrenceAndPersists");
    }

    /// <summary>
    /// Verifies that GetDependencyVersion:
    /// - Forwards the SourceMapping.Name value exactly to ISourceManifest.GetVersion
    /// - Returns the exact value provided by the manifest (either a version or null)
    /// 
    /// Inputs:
    /// - Various repository names including empty, whitespace, ASCII, Unicode, special characters, and very long strings.
    /// 
    /// Expected:
    /// - The manifest is queried once with the exact name and the returned value is propagated unchanged.
    /// </summary>
    [TestCaseSource(nameof(GetDependencyVersion_NameVariants_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetDependencyVersion_NameVariants_ForwardsToSourceManifestAndReturnsExpected(string mappingName, string expectedShaOrNull)
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var expectedReturn = expectedShaOrNull == null ? null : new VmrDependencyVersion(expectedShaOrNull);

        sourceManifest
            .Setup(m => m.GetVersion(mappingName))
            .Returns(expectedReturn);

        var sut = new VmrDependencyTracker(
            vmrInfo.Object,
            fileSystem.Object,
            sourceMappingParser.Object,
            sourceManifest.Object,
            logger.Object);

        var mapping = new SourceMapping(
            mappingName,
            "https://example.com/repo.git",
            "main",
            Array.Empty<string>(),
            Array.Empty<string>(),
            false);

        // Act
        var result = sut.GetDependencyVersion(mapping);

        // Assert
        if (expectedReturn == null)
        {
            result.Should().BeNull();
        }
        else
        {
            result.Should().Be(expectedReturn);
        }

        sourceManifest.Verify(m => m.GetVersion(It.Is<string>(s => s == mappingName)), Times.Once);
        sourceManifest.VerifyNoOtherCalls();
    }

    private static IEnumerable GetDependencyVersion_NameVariants_Cases()
    {
        yield return new TestCaseData("", null).SetName("GetDependencyVersion_EmptyName_ReturnsNull");
        yield return new TestCaseData(" ", null).SetName("GetDependencyVersion_WhitespaceName_ReturnsNull");
        yield return new TestCaseData("repo", "deadbeef").SetName("GetDependencyVersion_NormalName_ReturnsVersion");
        yield return new TestCaseData("REPO", "ABCDEF0123456789").SetName("GetDependencyVersion_UppercaseName_ReturnsVersion");
        yield return new TestCaseData("Repo-123_.$", "sha-!@#").SetName("GetDependencyVersion_SpecialCharsName_ReturnsVersion");
        yield return new TestCaseData("路径/仓库", "ユニコード").SetName("GetDependencyVersion_UnicodeName_ReturnsVersion");
        yield return new TestCaseData(new string('a', 2048), null).SetName("GetDependencyVersion_VeryLongName_ReturnsNull");
    }

    /// <summary>
    /// Ensures that accessing the Mappings property before initialization throws an Exception
    /// with a clear message. The property should be unavailable until RefreshMetadataAsync
    /// (or other initialization) populates the internal mappings.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Mappings_WhenNotInitialized_ThrowsExceptionWithMessage()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Loose);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Loose);
        var sourceManifest = new Mock<Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo.ISourceManifest>(MockBehavior.Loose);
        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var tracker = new VmrDependencyTracker(
            vmrInfo.Object,
            fileSystem.Object,
            sourceMappingParser.Object,
            sourceManifest.Object,
            logger.Object);

        // Act
        Action act = () =>
        {
            // Accessing Mappings should throw because _mappings has not been initialized.
            var _ = tracker.Mappings;
        };

        // Assert
        act.Should().Throw<Exception>()
           .WithMessage("Source mappings have not been initialized.");
    }

    /// <summary>
    /// Verifies that after RefreshMetadataAsync initializes mappings, the Mappings property
    /// returns the exact collection provided by the parser. Covers both empty and single-item cases.
    /// </summary>
    /// <param name="expectedMappings">The collection that the parser returns and the property should expose.</param>
    [TestCaseSource(nameof(GetMappingsCollections))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task Mappings_AfterRefresh_ReturnsParsedCollection(IReadOnlyCollection<SourceMapping> expectedMappings)
    {
        // Arrange
        const string sourceMappingsPath = "some/path/source-mappings.json";

        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Loose);
        vmrInfo.SetupGet(v => v.SourceManifestPath).Returns("manifest.json");

        var fileSystem = new Mock<IFileSystem>(MockBehavior.Loose);

        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        sourceMappingParser
            .Setup(p => p.ParseMappings(sourceMappingsPath))
            .ReturnsAsync(expectedMappings);

        var sourceManifest = new Mock<Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo.ISourceManifest>(MockBehavior.Loose);

        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var tracker = new VmrDependencyTracker(
            vmrInfo.Object,
            fileSystem.Object,
            sourceMappingParser.Object,
            sourceManifest.Object,
            logger.Object);

        // Act
        await tracker.RefreshMetadataAsync(sourceMappingsPath);
        var result = tracker.Mappings;

        // Assert
        result.Should().BeSameAs(expectedMappings);
        result.Count.Should().Be(expectedMappings.Count);
    }

    private static IEnumerable<TestCaseData> GetMappingsCollections()
    {
        var empty = new List<SourceMapping>();

        var single = new List<SourceMapping>
            {
                new SourceMapping(
                    "repoA",
                    "https://example.com/repoA.git",
                    "main",
                    new List<string>(),
                    new List<string>(),
                    false,
                    "1.0.0")
            };

        yield return new TestCaseData(empty).SetName("Mappings_AfterRefresh_ReturnsParsedCollection_Empty");
        yield return new TestCaseData(single).SetName("Mappings_AfterRefresh_ReturnsParsedCollection_SingleItem");
    }

    /// <summary>
    /// Ensures the constructor accepts valid non-null dependencies and does not throw.
    /// Inputs:
    ///  - Valid mocks for IVmrInfo, IFileSystem, ISourceMappingParser, ISourceManifest, ILogger<VmrDependencyTracker>.
    /// Expected:
    ///  - No exception is thrown during construction.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict).Object;
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict).Object;
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict).Object;
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Strict).Object;

        // Act
        Action construct = () => new VmrDependencyTracker(vmrInfo, fileSystem, sourceMappingParser, sourceManifest, logger);

        // Assert
        construct.Should().NotThrow();
    }

    /// <summary>
    /// Validates that immediately after construction, source mappings are uninitialized and accessing
    /// the Mappings property throws with a clear message.
    /// Inputs:
    ///  - Valid mocks for IVmrInfo, IFileSystem, ISourceMappingParser, ISourceManifest, ILogger<VmrDependencyTracker>.
    /// Expected:
    ///  - Accessing Mappings throws Exception with the message "Source mappings have not been initialized.".
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_MappingsUnset_AccessingMappingsThrowsWithExpectedMessage()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict).Object;
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict).Object;
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict).Object;
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Strict).Object;

        var sut = new VmrDependencyTracker(vmrInfo, fileSystem, sourceMappingParser, sourceManifest, logger);

        // Act
        Action act = () =>
        {
            // Accessing Mappings before initialization should throw
            var _ = sut.Mappings;
        };

        // Assert
        act.Should()
           .Throw<Exception>()
           .WithMessage("Source mappings have not been initialized.");
    }

    /// <summary>
    /// Verifies TryGetMapping performs a case-insensitive name lookup and returns the matching mapping.
    /// Inputs:
    ///  - Initialized mappings containing a single mapping named "Repo.A".
    ///  - Query name varies by case to ensure StringComparison.InvariantCultureIgnoreCase behavior.
    /// Expected:
    ///  - Method returns true and 'mapping' is not null with Name equal to "Repo.A".
    /// </summary>
    [Test]
    [TestCase("Repo.A")]
    [TestCase("repo.a")]
    [TestCase("REPO.A")]
    [TestCase("RePo.A")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryGetMapping_CaseInsensitiveMatch_ReturnsTrueAndOutMapping(string queryName)
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfoMock.SetupGet(v => v.SourceManifestPath).Returns("manifest.json");

        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);

        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var mappings = new List<SourceMapping>
            {
                new SourceMapping(
                    "Repo.A",
                    "https://remote/a.git",
                    "main",
                    new[] { "src/**" },
                    Array.Empty<string>(),
                    false)
            };
        sourceMappingParserMock
            .Setup(p => p.ParseMappings(It.IsAny<string>()))
            .ReturnsAsync(mappings);

        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        sourceManifestMock.Setup(m => m.Refresh("manifest.json"));

        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var sut = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            sourceMappingParserMock.Object,
            sourceManifestMock.Object,
            loggerMock.Object);

        await sut.RefreshMetadataAsync("custom-source-mappings.json");

        // Act
        var result = sut.TryGetMapping(queryName, out var mapping);

        // Assert
        result.Should().BeTrue();
        mapping.Should().NotBeNull();
        mapping!.Name.Should().Be("Repo.A");

        sourceMappingParserMock.Verify(p => p.ParseMappings("custom-source-mappings.json"), Times.Once);
        sourceManifestMock.Verify(m => m.Refresh("manifest.json"), Times.Once);
    }

    /// <summary>
    /// Ensures TryGetMapping returns false with null mapping when the name does not match any existing mapping.
    /// Inputs:
    ///  - Initialized mappings containing "Repo.A".
    ///  - Query names include empty, whitespace-only, unknown, very long, and special-character strings.
    /// Expected:
    ///  - Method returns false and 'mapping' is null in all cases.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(NotFoundNames))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryGetMapping_NotFoundOrInvalidName_ReturnsFalseAndNullOut(string queryName)
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfoMock.SetupGet(v => v.SourceManifestPath).Returns("manifest.json");

        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);

        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var mappings = new List<SourceMapping>
            {
                new SourceMapping(
                    "Repo.A",
                    "https://remote/a.git",
                    "main",
                    new[] { "src/**" },
                    Array.Empty<string>(),
                    false)
            };
        sourceMappingParserMock
            .Setup(p => p.ParseMappings(It.IsAny<string>()))
            .ReturnsAsync(mappings);

        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        sourceManifestMock.Setup(m => m.Refresh("manifest.json"));

        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var sut = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            sourceMappingParserMock.Object,
            sourceManifestMock.Object,
            loggerMock.Object);

        await sut.RefreshMetadataAsync("custom-source-mappings.json");

        // Act
        var result = sut.TryGetMapping(queryName, out var mapping);

        // Assert
        result.Should().BeFalse();
        mapping.Should().BeNull();

        sourceMappingParserMock.Verify(p => p.ParseMappings("custom-source-mappings.json"), Times.Once);
        sourceManifestMock.Verify(m => m.Refresh("manifest.json"), Times.Once);
    }

    /// <summary>
    /// Validates that TryGetMapping throws when source mappings have not been initialized.
    /// Inputs:
    ///  - A new VmrDependencyTracker instance without calling RefreshMetadataAsync.
    ///  - Any query name (non-null string).
    /// Expected:
    ///  - Throws Exception with the message "Source mappings have not been initialized.".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void TryGetMapping_MappingsNotInitialized_ThrowsInitializationException()
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var sut = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            sourceMappingParserMock.Object,
            sourceManifestMock.Object,
            loggerMock.Object);

        // Act
        Action act = () => sut.TryGetMapping("any", out _);

        // Assert
        act.Should().Throw<Exception>().WithMessage("Source mappings have not been initialized.");
    }

    private static IEnumerable<string> NotFoundNames()
    {
        yield return "";
        yield return " ";
        yield return "unknown";
        yield return "repo.b";
        yield return new string('x', 10000);
        yield return "name-with-\t-\n-\r-specials";
    }

    /// <summary>
    /// Ensures GetMapping throws an Exception with the expected message when no mapping
    /// matches the provided name, despite mappings being initialized.
    /// Inputs:
    ///  - Initialized mappings not containing the requested name.
    ///  - Various non-matching names (empty, whitespace, typical unknown, special chars).
    /// Expected:
    ///  - An Exception is thrown with message "No mapping named {name} found".
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-found")]
    [TestCase("weird/$%^")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetMapping_MappingMissing_ThrowsWithNameInMessage(string missingName)
    {
        // Arrange
        var existing = new SourceMapping(
            "Existing.Repo",
            "https://example/existing",
            "main",
            new List<string> { "**/*" },
            new List<string>(),
            false,
            null);

        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfoMock.SetupGet(v => v.SourceManifestPath).Returns("manifest.json");

        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var parserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        parserMock
            .Setup(p => p.ParseMappings("source-mappings.json"))
            .ReturnsAsync(new List<SourceMapping> { existing });

        var manifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        manifestMock.Setup(m => m.Refresh("manifest.json"));

        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var tracker = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            parserMock.Object,
            manifestMock.Object,
            loggerMock.Object);

        await tracker.RefreshMetadataAsync("source-mappings.json");

        // Act
        Action act = () => tracker.GetMapping(missingName);

        // Assert
        act.Should()
           .Throw<Exception>()
           .WithMessage($"No mapping named {missingName} found");
    }

    /// <summary>
    /// Validates that calling GetMapping before mappings are initialized throws an Exception
    /// indicating that source mappings have not been initialized.
    /// Inputs:
    ///  - Freshly constructed VmrDependencyTracker with no calls to RefreshMetadataAsync.
    ///  - Any mapping name.
    /// Expected:
    ///  - An Exception with message "Source mappings have not been initialized." is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetMapping_BeforeInitialization_ThrowsInitializationException()
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var parserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var manifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var tracker = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            parserMock.Object,
            manifestMock.Object,
            loggerMock.Object);

        // Act
        Action act = () => tracker.GetMapping("anything");

        // Assert
        act.Should()
           .Throw<Exception>()
           .WithMessage("Source mappings have not been initialized.");
    }

    /// <summary>
    /// Ensures that GetDependencyVersion forwards the SourceMapping.Name verbatim to ISourceManifest.GetVersion
    /// and returns the exact VmrDependencyVersion instance provided by the manifest.
    /// Inputs:
    ///  - SourceMapping with varying Name values (empty, whitespace, long, special characters, mixed case).
    /// Expected:
    ///  - ISourceManifest.GetVersion is called exactly once with the same Name.
    ///  - The returned value is the same instance as provided by the manifest.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(MappingNames))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetDependencyVersion_ForwardsNameAndReturnsManifestResult(string mappingName)
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var expected = new VmrDependencyVersion("sha-123");
        sourceManifest
            .Setup(m => m.GetVersion(mappingName))
            .Returns(expected);

        var sut = new VmrDependencyTracker(
            vmrInfo.Object,
            fileSystem.Object,
            sourceMappingParser.Object,
            sourceManifest.Object,
            logger.Object);

        var mapping = new SourceMapping(
            mappingName,
            "https://example.com/repo.git",
            "main",
            new List<string>(),
            new List<string>(),
            false,
            null);

        // Act
        var result = sut.GetDependencyVersion(mapping);

        // Assert
        result.Should().BeSameAs(expected);
        sourceManifest.Verify(m => m.GetVersion(mappingName), Times.Once);
        sourceManifest.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that when ISourceManifest.GetVersion returns null, the method returns null as well.
    /// Inputs:
    ///  - SourceMapping with any valid Name.
    /// Expected:
    ///  - Null is returned and no exception is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetDependencyVersion_WhenManifestReturnsNull_ReturnsNull()
    {
        // Arrange
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var sourceManifest = new Mock<ISourceManifest>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        const string name = "repo-x";
        sourceManifest
            .Setup(m => m.GetVersion(name))
            .Returns((VmrDependencyVersion)null);

        var sut = new VmrDependencyTracker(
            vmrInfo.Object,
            fileSystem.Object,
            sourceMappingParser.Object,
            sourceManifest.Object,
            logger.Object);

        var mapping = new SourceMapping(
            name,
            "https://example.com/repo.git",
            "main",
            new List<string>(),
            new List<string>(),
            false,
            null);

        // Act
        var result = sut.GetDependencyVersion(mapping);

        // Assert
        result.Should().BeNull();
        sourceManifest.Verify(m => m.GetVersion(name), Times.Once);
        sourceManifest.VerifyNoOtherCalls();
    }

    private static IEnumerable<string> MappingNames()
    {
        yield return "";                                // empty
        yield return " ";                               // whitespace
        yield return "repo";                            // simple
        yield return "MiXeDCasE";                       // mixed case
        yield return new string('a', 1024);             // very long
        yield return "name/with\\special:chars?*|<>";   // special characters
    }

    /// <summary>
    /// Verifies that RefreshMetadataAsync propagates the provided sourceMappingsPath (including null and edge cases)
    /// to the ISourceMappingParser.ParseMappings call, and always refreshes the manifest using IVmrInfo.SourceManifestPath.
    /// Inputs:
    ///  - sourceMappingsPath: null, empty, whitespace, relative, or absolute path strings.
    /// Expected:
    ///  - When null: ParseMappings is called with {VmrPath}/{VmrInfo.DefaultRelativeSourceMappingsPath}.
    ///  - When non-null: ParseMappings is called with the exact provided value.
    ///  - ISourceManifest.Refresh is called with IVmrInfo.SourceManifestPath.
    /// </summary>
    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("relative/source-mappings.json")]
    [TestCase("/abs/custom/source-mappings.json")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task RefreshMetadataAsync_PathPropagationAndManifestRefresh_Succeeds(string sourceMappingsPath)
    {
        // Arrange
        var vmrPath = new NativePath("/vmr-root");
        var expectedDefaultMappingsPath = (vmrPath / VmrInfo.DefaultRelativeSourceMappingsPath).ToString();
        var manifestPath = (vmrPath / VmrInfo.DefaultRelativeSourceManifestPath).ToString();

        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfoMock.SetupGet(v => v.VmrPath).Returns(vmrPath);
        vmrInfoMock.SetupGet(v => v.SourceManifestPath).Returns(new NativePath(manifestPath));

        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        sourceManifestMock.Setup(m => m.Refresh(manifestPath));

        var parserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        parserMock
            .Setup(p => p.ParseMappings(It.IsAny<string>()))
            .ReturnsAsync(new List<SourceMapping>());

        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var sut = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            parserMock.Object,
            sourceManifestMock.Object,
            loggerMock.Object);

        var expectedMappingsArg = sourceMappingsPath == null ? expectedDefaultMappingsPath : sourceMappingsPath;

        // Act
        await sut.RefreshMetadataAsync(sourceMappingsPath);

        // Assert
        parserMock.Verify(p => p.ParseMappings(It.Is<string>(s => s == expectedMappingsArg)), Times.Once);
        sourceManifestMock.Verify(m => m.Refresh(It.Is<string>(s => s == manifestPath)), Times.Once);
    }

    /// <summary>
    /// Verifies that when parsing source mappings fails, RefreshMetadataAsync propagates the exception
    /// and does not attempt to refresh the source manifest.
    /// Inputs:
    ///  - sourceMappingsPath: null to trigger default path resolution (behavior is irrelevant once parsing throws).
    /// Expected:
    ///  - An exception is thrown from RefreshMetadataAsync.
    ///  - ISourceManifest.Refresh is never called.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task RefreshMetadataAsync_ParsingFails_ExceptionBubblesAndManifestNotRefreshed()
    {
        // Arrange
        var vmrPath = new NativePath("/vmr-root");
        var manifestPath = (vmrPath / VmrInfo.DefaultRelativeSourceManifestPath).ToString();

        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfoMock.SetupGet(v => v.VmrPath).Returns(vmrPath);
        vmrInfoMock.SetupGet(v => v.SourceManifestPath).Returns(new NativePath(manifestPath));

        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        // No Refresh setup intentionally; must not be called.

        var parserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        parserMock
            .Setup(p => p.ParseMappings(It.IsAny<string>()))
            .ThrowsAsync(new Exception("parse failure"));

        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var sut = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            parserMock.Object,
            sourceManifestMock.Object,
            loggerMock.Object);

        // Act
        Func<Task> act = () => sut.RefreshMetadataAsync(null);

        // Assert
        await act.Should().ThrowAsync<Exception>();
        sourceManifestMock.Verify(m => m.Refresh(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Provides diverse inputs to ensure all fields are forwarded as-is to ISourceManifest.UpdateVersion,
    /// and JSON produced by ISourceManifest.ToJson is persisted via IFileSystem.WriteToFile.
    /// Inputs exercise edge cases for strings and nullable numeric barId.
    /// </summary>
    public static IEnumerable<TestCaseData> UpdateDependencyVersion_ForwardsAllFieldsAndPersistsJson_Cases()
    {
        yield return new TestCaseData(
            "repo",
            "https://example.com/org/repo.git",
            "abc123",
            (int?)123);

        yield return new TestCaseData(
            " ",
            " ",
            "",
            (int?)null);

        yield return new TestCaseData(
            new string('x', 1024),
            "ssh://user@host:2222/path/to/repo?query=1#frag",
            "𝛼βγ\t\n",
            (int?)int.MaxValue);

        yield return new TestCaseData(
            "repo-min",
            "file:///C:/temp/repo",
            "0",
            (int?)int.MinValue);

        yield return new TestCaseData(
            "repo-zero",
            "",
            " ",
            (int?)0);

        yield return new TestCaseData(
            "repo-negative",
            "git@github.com:org/repo.git",
            "deadbeef",
            (int?)(-1));
    }

    /// <summary>
    /// Validates that UpdateDependencyVersion forwards all fields from the provided VmrDependencyUpdate
    /// to ISourceManifest.UpdateVersion and then persists the JSON via IFileSystem.WriteToFile.
    /// Inputs: mappingName, remoteUri, targetRevision, and barId variations (including null, extremes, and special strings).
    /// Expected: ISourceManifest.UpdateVersion called with exact values; IFileSystem.WriteToFile called with content from ISourceManifest.ToJson.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(UpdateDependencyVersion_ForwardsAllFieldsAndPersistsJson_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void UpdateDependencyVersion_ForwardsAllFieldsAndPersistsJson(string mappingName, string remoteUri, string targetRevision, int? barId)
    {
        // Arrange
        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Loose);
        var parserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var mapping = new SourceMapping(
            mappingName,
            defaultRemote: "dr",
            defaultRef: "main",
            Include: new List<string>(),
            Exclude: new List<string>(),
            DisableSynchronization: false,
            Version: null);

        var update = new VmrDependencyUpdate(
            mapping,
            RemoteUri: remoteUri,
            TargetRevision: targetRevision,
            Parent: null,
            OfficialBuildId: null,
            BarId: barId,
            OriginRevision: null);

        const string json = "{ \"manifest\": true }";

        var sequence = new MockSequence();
        sourceManifestMock
            .InSequence(sequence)
            .Setup(m => m.UpdateVersion(mappingName, remoteUri, targetRevision, barId));
        sourceManifestMock
            .InSequence(sequence)
            .Setup(m => m.ToJson())
            .Returns(json);
        fileSystemMock
            .InSequence(sequence)
            .Setup(fs => fs.WriteToFile(It.IsAny<string>(), json));

        var sut = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            parserMock.Object,
            sourceManifestMock.Object,
            loggerMock.Object);

        // Act
        sut.UpdateDependencyVersion(update);

        // Assert
        sourceManifestMock.Verify(m => m.UpdateVersion(mappingName, remoteUri, targetRevision, barId), Times.Once);
        sourceManifestMock.Verify(m => m.ToJson(), Times.Once);
        fileSystemMock.Verify(fs => fs.WriteToFile(It.IsAny<string>(), json), Times.Once);
        sourceManifestMock.VerifyNoOtherCalls();
        fileSystemMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures that if ISourceManifest.ToJson throws, the exception propagates and the file is not written.
    /// Inputs: simple valid update; ISourceManifest.ToJson throws InvalidOperationException.
    /// Expected: InvalidOperationException is thrown; IFileSystem.WriteToFile is not called; UpdateVersion is called once.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void UpdateDependencyVersion_ToJsonThrows_PropagatesAndDoesNotWrite()
    {
        // Arrange
        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Loose);
        var parserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var mapping = new SourceMapping("repo", "dr", "main", new List<string>(), new List<string>(), false, null);
        var update = new VmrDependencyUpdate(mapping, "https://r", "sha", null, null, 1, null);

        var sequence = new MockSequence();
        sourceManifestMock.InSequence(sequence).Setup(m => m.UpdateVersion("repo", "https://r", "sha", 1));
        sourceManifestMock.InSequence(sequence).Setup(m => m.ToJson()).Throws(new InvalidOperationException("boom"));

        var sut = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            parserMock.Object,
            sourceManifestMock.Object,
            loggerMock.Object);

        // Act
        Action act = () => sut.UpdateDependencyVersion(update);

        // Assert
        act.Should().Throw<InvalidOperationException>();
        sourceManifestMock.Verify(m => m.UpdateVersion("repo", "https://r", "sha", 1), Times.Once);
        sourceManifestMock.Verify(m => m.ToJson(), Times.Once);
        fileSystemMock.Verify(fs => fs.WriteToFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        sourceManifestMock.VerifyNoOtherCalls();
        fileSystemMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures that if IFileSystem.WriteToFile throws, the exception propagates
    /// and both UpdateVersion and ToJson were invoked exactly once beforehand.
    /// Inputs: simple valid update; IFileSystem.WriteToFile throws IOException.
    /// Expected: IOException is thrown; UpdateVersion and ToJson were called once each.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void UpdateDependencyVersion_WriteToFileThrows_PropagatesAndPrecedingCallsMade()
    {
        // Arrange
        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Loose);
        var parserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var mapping = new SourceMapping("repo", "dr", "main", new List<string>(), new List<string>(), false, null);
        var update = new VmrDependencyUpdate(mapping, "https://r", "sha", null, null, 42, null);

        const string json = "{\"ok\":true}";

        var sequence = new MockSequence();
        sourceManifestMock.InSequence(sequence).Setup(m => m.UpdateVersion("repo", "https://r", "sha", 42));
        sourceManifestMock.InSequence(sequence).Setup(m => m.ToJson()).Returns(json);
        fileSystemMock.InSequence(sequence).Setup(fs => fs.WriteToFile(It.IsAny<string>(), json)).Throws(new System.IO.IOException("fail"));

        var sut = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            parserMock.Object,
            sourceManifestMock.Object,
            loggerMock.Object);

        // Act
        Action act = () => sut.UpdateDependencyVersion(update);

        // Assert
        act.Should().Throw<System.IO.IOException>();
        sourceManifestMock.Verify(m => m.UpdateVersion("repo", "https://r", "sha", 42), Times.Once);
        sourceManifestMock.Verify(m => m.ToJson(), Times.Once);
        fileSystemMock.Verify(fs => fs.WriteToFile(It.IsAny<string>(), json), Times.Once);
        sourceManifestMock.VerifyNoOtherCalls();
        fileSystemMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures that if ISourceManifest.UpdateVersion throws, the exception propagates
    /// and neither ToJson nor WriteToFile is called.
    /// Inputs: UpdateVersion setup to throw InvalidOperationException.
    /// Expected: InvalidOperationException is thrown; ToJson and WriteToFile are not invoked.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void UpdateDependencyVersion_UpdateVersionThrows_PropagatesAndNoFurtherCalls()
    {
        // Arrange
        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        var fileSystemMock = new Mock<IFileSystem>(MockBehavior.Strict);
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Loose);
        var parserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrDependencyTracker>>(MockBehavior.Loose);

        var mapping = new SourceMapping("repoX", "dr", "main", new List<string>(), new List<string>(), false, null);
        var update = new VmrDependencyUpdate(mapping, "https://rX", "shaX", null, null, null, null);

        sourceManifestMock.Setup(m => m.UpdateVersion("repoX", "https://rX", "shaX", null)).Throws(new InvalidOperationException("uv-fail"));

        var sut = new VmrDependencyTracker(
            vmrInfoMock.Object,
            fileSystemMock.Object,
            parserMock.Object,
            sourceManifestMock.Object,
            loggerMock.Object);

        // Act
        Action act = () => sut.UpdateDependencyVersion(update);

        // Assert
        act.Should().Throw<InvalidOperationException>();
        sourceManifestMock.Verify(m => m.UpdateVersion("repoX", "https://rX", "shaX", null), Times.Once);
        sourceManifestMock.Verify(m => m.ToJson(), Times.Never);
        fileSystemMock.Verify(fs => fs.WriteToFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        sourceManifestMock.VerifyNoOtherCalls();
        fileSystemMock.VerifyNoOtherCalls();
    }
}
