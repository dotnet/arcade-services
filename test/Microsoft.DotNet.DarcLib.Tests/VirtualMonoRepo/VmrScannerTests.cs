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
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class VmrScannerTests
{
    /// <summary>
    /// Verifies that the constructor assigns all provided dependencies to the corresponding protected fields.
    /// Inputs:
    ///  - Non-null instances of IVmrDependencyTracker, IProcessManager, IVmrInfo, and ILogger<VmrScanner>.
    /// Expected:
    ///  - The backing fields hold the exact same instances as provided to the constructor.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_ValidDependencies_AssignsFields()
    {
        // Arrange
        var dependencyTrackerMock = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrScanner>>(MockBehavior.Strict);

        // Act
        var scanner = new TestableVmrScanner(
            dependencyTrackerMock.Object,
            processManagerMock.Object,
            vmrInfoMock.Object,
            loggerMock.Object);

        // Assert
        scanner.DependencyTracker.Should().BeSameAs(dependencyTrackerMock.Object);
        scanner.ProcessManager.Should().BeSameAs(processManagerMock.Object);
        scanner.VmrInfo.Should().BeSameAs(vmrInfoMock.Object);
        scanner.Logger.Should().BeSameAs(loggerMock.Object);
    }

    /// <summary>
    /// Ensures the constructor always creates a new FileSystem instance for internal use.
    /// Inputs:
    ///  - Valid non-null dependencies.
    /// Expected:
    ///  - The _fileSystem field is non-null and is of concrete type FileSystem.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_Always_CreatesNewFileSystem()
    {
        // Arrange
        var dependencyTrackerMock = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Strict);
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrScanner>>(MockBehavior.Strict);

        // Act
        var scanner = new TestableVmrScanner(
            dependencyTrackerMock.Object,
            processManagerMock.Object,
            vmrInfoMock.Object,
            loggerMock.Object);

        // Assert
        scanner.FileSystem.Should().NotBeNull();
        scanner.FileSystem.Should().BeOfType<FileSystem>();
    }

    private sealed class TestableVmrScanner : VmrScanner
    {
        public TestableVmrScanner(
            IVmrDependencyTracker dependencyTracker,
            IProcessManager processManager,
            IVmrInfo vmrInfo,
            ILogger<VmrScanner> logger)
            : base(dependencyTracker, processManager, vmrInfo, logger)
        {
        }

        public IVmrDependencyTracker DependencyTracker => _dependencyTracker;
        public IProcessManager ProcessManager => _processManager;
        public IVmrInfo VmrInfo => _vmrInfo;
        public ILogger<VmrScanner> Logger => _logger;
        public IFileSystem FileSystem => _fileSystem;

        protected override string ScanType => "Test";

        protected override Task<IEnumerable<string>> ScanSubRepository(
            SourceMapping sourceMapping,
            string baselineFilePath,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        }

        protected override Task<IEnumerable<string>> ScanBaseRepository(
            string baselineFilePath,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        }
    }

    /// <summary>
    /// Ensures that when there are no mappings, only the base repository scan is executed,
    /// and the returned file list is sorted ascending.
    /// Inputs:
    ///  - Mappings: empty.
    ///  - Base scan returns unsorted file names.
    /// Expected:
    ///  - Sub-repository scan is never invoked.
    ///  - Result contains only base scan files in ascending order.
    ///  - Dependency tracker metadata is refreshed exactly once.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ScanVmr_NoMappings_OnlyBaseScanResultsSortedAsync()
    {
        // Arrange
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        dependencyTracker.Setup(m => m.RefreshMetadataAsync(null)).Returns(Task.CompletedTask);
        dependencyTracker.SetupGet(m => m.Mappings).Returns(Array.Empty<SourceMapping>());

        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrScanner>>(MockBehavior.Loose);

        var baseReturned = new[] { "b.txt", "A.txt", "a.txt" };
        var testScanner = new TestableVmrScanner(
            dependencyTracker.Object,
            processManager.Object,
            vmrInfo.Object,
            logger.Object,
            scanType: "test",
            subRepoFunc: (mapping, baseline, ct) => Task.FromResult<IEnumerable<string>>(Array.Empty<string>()),
            baseRepoFunc: (baseline, ct) => Task.FromResult<IEnumerable<string>>(baseReturned));

        // Act
        var result = await testScanner.ScanVmr(baselineFilePath: null, cancellationToken: CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(new[] { "A.txt", "a.txt", "b.txt" }, opt => opt.WithStrictOrdering());
        dependencyTracker.Verify(m => m.RefreshMetadataAsync(null), Times.Once);
    }

    /// <summary>
    /// Verifies that only mappings with Exclude rules are scanned, and that results from all tasks
    /// (including base repository) are aggregated, allow duplicates, and are returned sorted.
    /// Inputs:
    ///  - Mappings: m1 (Exclude count 1), m2 (Exclude count 0), m3 (Exclude count 2).
    ///  - Sub scans for m1 => ["3","1"], m3 => ["2","1"]; base scan => ["2"].
    /// Expected:
    ///  - Sub scan invoked for m1 and m3, not for m2.
    ///  - Final list equals ["1","1","2","2","3"] (sorted, duplicates preserved).
    ///  - Metadata refresh invoked once.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ScanVmr_FilterMappings_AggregatesAndSorts_WithDuplicatesAsync()
    {
        // Arrange
        var m1 = new SourceMapping("m1", "remote", "ref", Array.Empty<string>(), new[] { "ex" }, false);
        var m2 = new SourceMapping("m2", "remote", "ref", Array.Empty<string>(), Array.Empty<string>(), false);
        var m3 = new SourceMapping("m3", "remote", "ref", Array.Empty<string>(), new[] { "ex1", "ex2" }, false);

        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        dependencyTracker.Setup(m => m.RefreshMetadataAsync(null)).Returns(Task.CompletedTask);
        dependencyTracker.SetupGet(m => m.Mappings).Returns(new[] { m1, m2, m3 });

        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrScanner>>(MockBehavior.Loose);

        var invokedSubMappings = new List<string>();
        Func<SourceMapping, string, CancellationToken, Task<IEnumerable<string>>> subRepo = (mapping, baseline, ct) =>
        {
            invokedSubMappings.Add(mapping.Name);
            if (mapping.Name == "m1")
            {
                return Task.FromResult<IEnumerable<string>>(new[] { "3", "1" });
            }
            if (mapping.Name == "m3")
            {
                return Task.FromResult<IEnumerable<string>>(new[] { "2", "1" });
            }
            // If m2 is ever called, return a recognizable marker
            return Task.FromResult<IEnumerable<string>>(new[] { "should-not-be-produced" });
        };

        Func<string, CancellationToken, Task<IEnumerable<string>>> baseRepo = (baseline, ct)
            => Task.FromResult<IEnumerable<string>>(new[] { "2" });

        var testScanner = new TestableVmrScanner(
            dependencyTracker.Object,
            processManager.Object,
            vmrInfo.Object,
            logger.Object,
            scanType: "test",
            subRepoFunc: subRepo,
            baseRepoFunc: baseRepo);

        // Act
        var result = await testScanner.ScanVmr(baselineFilePath: "ignored", cancellationToken: CancellationToken.None);

        // Assert
        invokedSubMappings.Should().BeEquivalentTo(new[] { "m1", "m3" }, opt => opt.WithoutStrictOrdering());
        result.Should().BeEquivalentTo(new[] { "1", "1", "2", "2", "3" }, opt => opt.WithStrictOrdering());
        dependencyTracker.Verify(m => m.RefreshMetadataAsync(null), Times.Once);
    }

    /// <summary>
    /// Validates that the baselineFilePath argument is forwarded unchanged to both sub-repository and base-repository scans.
    /// Inputs:
    ///  - baselineFilePath: null, empty, whitespace, long string, path with special chars, control chars.
    ///  - One mapping with Exclude rules to ensure both sub and base scans execute.
    /// Expected:
    ///  - Each scan receives the exact same baselineFilePath value that was passed to ScanVmr.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("this/is/a/very/very/very/very/very/very/long/baseline/file/path/that/should/be/forwarded/correctly.txt")]
    [TestCase("C:\\prj\\weird<>:\\path")]
    [TestCase("\t\n")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task ScanVmr_BaselineIsForwarded_ToAllScansAsync(string baseline)
    {
        // Arrange
        var mapping = new SourceMapping("repo", "remote", "ref", Array.Empty<string>(), new[] { "x" }, false);

        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        dependencyTracker.Setup(m => m.RefreshMetadataAsync(null)).Returns(Task.CompletedTask);
        dependencyTracker.SetupGet(m => m.Mappings).Returns(new[] { mapping });

        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrScanner>>(MockBehavior.Loose);

        var forwardedBaselines = new List<string>();
        var nullForwardCount = 0;

        Func<SourceMapping, string, CancellationToken, Task<IEnumerable<string>>> subRepo = (m, b, ct) =>
        {
            if (b == null) nullForwardCount++; else forwardedBaselines.Add(b);
            return Task.FromResult<IEnumerable<string>>(new[] { "sub.txt" });
        };

        Func<string, CancellationToken, Task<IEnumerable<string>>> baseRepo = (b, ct) =>
        {
            if (b == null) nullForwardCount++; else forwardedBaselines.Add(b);
            return Task.FromResult<IEnumerable<string>>(new[] { "base.txt" });
        };

        var testScanner = new TestableVmrScanner(
            dependencyTracker.Object,
            processManager.Object,
            vmrInfo.Object,
            logger.Object,
            scanType: "test",
            subRepoFunc: subRepo,
            baseRepoFunc: baseRepo);

        var token = new CancellationTokenSource().Token;

        // Act
        var result = await testScanner.ScanVmr(baseline, token);

        // Assert
        result.Should().BeEquivalentTo(new[] { "base.txt", "sub.txt" }, opt => opt.WithStrictOrdering());
        if (baseline == null)
        {
            nullForwardCount.Should().Be(2);
        }
        else
        {
            forwardedBaselines.Should().BeEquivalentTo(new[] { baseline, baseline }, opt => opt.WithStrictOrdering());
        }

        dependencyTracker.Verify(m => m.RefreshMetadataAsync(null), Times.Once);
    }

    /// <summary>
    /// Validates behavior when repoName is null (scanning base VMR):
    /// Inputs:
    ///  - Baseline containing global '*' rules, non-src rules, src/* rules, whitespace-only lines, and full-line/inline comments.
    /// Expected:
    ///  - Includes lines starting with '*' and non-src lines.
    ///  - Excludes lines under src/*.
    ///  - Strips inline comments and trims trailing spaces before '#'.
    ///  - Transforms comment-only lines into an empty path, resulting in ":(exclude)".
    ///  - Preserves whitespace-only lines as ":(exclude)   " (spaces retained).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetExclusionFilters_NullRepoName_IncludesGlobalAndNonSrcRulesAndStripsInlineCommentsAsync()
    {
        // Arrange
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrScanner>>(MockBehavior.Loose);

        var sut = new TestableVmrScanner(dependencyTracker.Object, processManager.Object, vmrInfo.Object, logger.Object);

        string content =
            "*global-1   # comment" + Environment.NewLine +
            "src/arcade/file1.txt" + Environment.NewLine +
            "docs/readme.md # trailing" + Environment.NewLine +
            "# full line comment" + Environment.NewLine +
            "   " + Environment.NewLine +
            "tools/#myfile" + Environment.NewLine +
            "*another" + Environment.NewLine +
            "src/runtime/another.txt";

        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".baseline");
        File.WriteAllText(tempPath, content);

        try
        {
            // Act
            var result = (await sut.InvokeGetExclusionFiltersAsync(null, tempPath)).ToList();

            // Assert
            var expected = new List<string>
                {
                    ":(exclude)*global-1",
                    ":(exclude)docs/readme.md",
                    ":(exclude)",
                    ":(exclude)   ",
                    ":(exclude)tools/",
                    ":(exclude)*another",
                };

            result.Should().BeEquivalentTo(expected);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// Ensures that when a specific repoName is provided, only global '*' rules and rules prefixed with "src/{repoName}"
    /// are included while other lines (other repos, non-src, whitespace-only, and full-line comments) are excluded.
    /// Inputs:
    ///  - repoName = "arcade"
    ///  - Baseline containing '*' rules, src/arcade, src/runtime, non-src, whitespace-only, and comment-only lines.
    /// Expected:
    ///  - Output includes ":(exclude)*..." for '*' lines and ":(exclude)src/arcade/..." for matching repo lines with inline comments removed.
    ///  - All other lines are excluded.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetExclusionFilters_SpecificRepoName_IncludesOnlyStarAndRepoPrefixedRulesAsync()
    {
        // Arrange
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrScanner>>(MockBehavior.Loose);

        var sut = new TestableVmrScanner(dependencyTracker.Object, processManager.Object, vmrInfo.Object, logger.Object);

        string content =
            "*global-1" + Environment.NewLine +
            "src/arcade/a.txt #  comment" + Environment.NewLine +
            "src/runtime/b.txt" + Environment.NewLine +
            "docs/guide.md" + Environment.NewLine +
            "   " + Environment.NewLine +
            "# comment" + Environment.NewLine +
            "*global-2";

        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".baseline");
        File.WriteAllText(tempPath, content);

        try
        {
            // Act
            var result = (await sut.InvokeGetExclusionFiltersAsync("arcade", tempPath)).ToList();

            // Assert
            var expected = new List<string>
                {
                    ":(exclude)*global-1",
                    ":(exclude)src/arcade/a.txt",
                    ":(exclude)*global-2",
                };

            result.Should().BeEquivalentTo(expected);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// Verifies that an empty baseline file yields an empty list of exclusion filters.
    /// Inputs:
    ///  - Empty baseline file content.
    /// Expected:
    ///  - No results (empty collection).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetExclusionFilters_EmptyBaseline_ProducesEmptyExclusionListAsync()
    {
        // Arrange
        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrScanner>>(MockBehavior.Loose);

        var sut = new TestableVmrScanner(dependencyTracker.Object, processManager.Object, vmrInfo.Object, logger.Object);

        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".baseline");
        File.WriteAllText(tempPath, string.Empty);

        try
        {
            // Act
            var result = (await sut.InvokeGetExclusionFiltersAsync("arcade", tempPath)).ToList();

            // Assert
            result.Should().BeEquivalentTo(Array.Empty<string>());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

}
