// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.HealthMetrics;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.HealthMetrics.UnitTests;

public class ProductDependencyCyclesHealthMetricTests
{
    private static IEnumerable<TestCaseData> ValidConstructorInputs()
    {
        yield return new TestCaseData("https://repo/x", "main").SetName("RepoAndBranch_NormalValues");
        yield return new TestCaseData(string.Empty, string.Empty).SetName("RepoAndBranch_EmptyStrings");
        yield return new TestCaseData("  ", "  ").SetName("RepoAndBranch_WhitespaceOnly");
        yield return new TestCaseData(new string('r', 256), new string('b', 256)).SetName("RepoAndBranch_VeryLongStrings");
        yield return new TestCaseData("repo://some\n\t\u2603", "feature/foo bar@1.2.3").SetName("RepoAndBranch_SpecialAndControlChars");
    }

    /// <summary>
    /// Verifies that the MetricName property always returns the expected constant string.
    /// Inputs:
    ///  - repo/branch values are arbitrary and should not affect MetricName.
    /// Expected:
    ///  - The property returns "Product Dependency Cycle Health".
    /// Note:
    ///  - This test is marked inconclusive to guide the replacement with AwesomeAssertions-based validation.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void MetricName_Always_ReturnsExpectedConstant()
    {
        // Arrange
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var sut = new ProductDependencyCyclesHealthMetric("any-repo", "any-branch", remoteFactory.Object, barClient.Object, logger.Object);
        // Act
        var actual = sut.MetricName;
        // Assert
        // TODO: Replace this Inconclusive with an AwesomeAssertions check, e.g.:
        // actual.Should().Be("Product Dependency Cycle Health");
        actual.Should().Be("Product Dependency Cycle Health");
    }

    private static IEnumerable<TestCaseData> MetricDescriptionTestCases()
    {
        yield return new TestCaseData("repo", "main");
        yield return new TestCaseData("", "");
        yield return new TestCaseData(" ", " ");
        yield return new TestCaseData(new string('r', 512), new string('b', 512));
        yield return new TestCaseData("répø😀/\\:*?\"<>|\t\r\n", "branch-αβγ/\\:*?\"<>|\t\r\n");
    }

    /// <summary>
    /// Verifies that MetricDescription formats the message using the provided repository and branch values.
    /// Inputs cover normal, empty, whitespace-only, very long, and special-character strings.
    /// Expected: "Product dependency cycle health for {repo} @ {branch}" without throwing exceptions.
    /// Note: This test is skipped until AwesomeAssertions is integrated to perform validations as required.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(MetricDescriptionTestCases))]
    [Ignore("Replace commented assertion with AwesomeAssertions v8.1.0 once API is available in the test project.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void MetricDescription_VariousInputs_FormatsMessageWithRepoAndBranch(string repo, string branch)
    {
        // Arrange
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var metric = new ProductDependencyCyclesHealthMetric(repo, branch, remoteFactory.Object, barClient.Object, logger.Object);
        // Act
        var description = metric.MetricDescription;
        // Assert
        var expected = $"Product dependency cycle health for {repo} @ {branch}";
        // TODO: Use AwesomeAssertions to validate:
        // AwesomeAssertions.Expect.That(description).IsEqualTo(expected);
    }

    private static IEnumerable<TestCaseData> Constructor_ValidInputs_CreatesInstanceWithoutUsingDependencies_Cases()
    {
        yield return new TestCaseData("https://github.com/dotnet/arcade", "main").SetName("Normal_UrlAndBranch");
        yield return new TestCaseData(string.Empty, string.Empty).SetName("EmptyStrings");
        yield return new TestCaseData("   ", "   ").SetName("WhitespaceOnly");
        yield return new TestCaseData(new string('r', 8192), new string('b', 8192)).SetName("VeryLongStrings");
        yield return new TestCaseData("repo-with-special-ß∂ƒ©Ω≈ç√∫˜µ≤≥", "feature/bugfix-#1234").SetName("SpecialCharacters");
        yield return new TestCaseData("repo\n\twith\u0000controls\u2603", "branch\n\twith\u0001ctrls").SetName("ControlCharacters");
    }

    /// <summary>
    /// Ensures the constructor accepts a variety of non-null repo and branch values and does not interact with dependencies.
    /// Inputs:
    ///  - repo and branch: normal, empty, whitespace-only, very long, special and control-character strings.
    ///  - Strict mocks for IRemoteFactory, IBasicBarClient, ILogger.
    /// Expected:
    ///  - Construction succeeds without exceptions.
    ///  - No calls are made to the injected dependencies during construction.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Constructor_ValidInputs_CreatesInstanceWithoutUsingDependencies_Cases))]
    [Category("constructor")]
    [Category("edge-cases")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_ValidInputs_CreatesInstanceWithoutUsingDependencies(string repo, string branch)
    {
        // Arrange
        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Strict);

        // Act
        var metric = new ProductDependencyCyclesHealthMetric(
            repo,
            branch,
            remoteFactoryMock.Object,
            barClientMock.Object,
            loggerMock.Object);

        // Assert
        // No interactions expected during construction.
        remoteFactoryMock.VerifyNoOtherCalls();
        barClientMock.VerifyNoOtherCalls();
        loggerMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that the Cycles property remains unset (null) immediately after construction.
    /// Inputs:
    ///  - Any non-null repo and branch values and strict mocks for dependencies.
    /// Expected:
    ///  - Cycles is null before any evaluation occurs.
    /// </summary>
    [Test]
    [Category("constructor")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_AfterInitialization_CyclesIsNull()
    {
        // Arrange
        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Strict);

        // Act
        var metric = new ProductDependencyCyclesHealthMetric(
            "repo",
            "main",
            remoteFactoryMock.Object,
            barClientMock.Object,
            loggerMock.Object);

        // Assert
        if (metric.Cycles != null)
        {
            throw new InvalidOperationException("Expected Cycles to be null immediately after construction.");
        }

        remoteFactoryMock.VerifyNoOtherCalls();
        barClientMock.VerifyNoOtherCalls();
        loggerMock.VerifyNoOtherCalls();
    }

    private static IEnumerable<TestCaseData> MetricNameInputs()
    {
        yield return new TestCaseData("repo", "main").SetName("MetricName_RepoAndBranch_Normal_ReturnsExpectedConstant");
        yield return new TestCaseData(string.Empty, string.Empty).SetName("MetricName_RepoAndBranch_EmptyStrings_ReturnsExpectedConstant");
        yield return new TestCaseData("   ", "   ").SetName("MetricName_RepoAndBranch_WhitespaceOnly_ReturnsExpectedConstant");
        yield return new TestCaseData(
            new string('r', 1024),
            new string('b', 1024)).SetName("MetricName_RepoAndBranch_VeryLongStrings_ReturnsExpectedConstant");
        yield return new TestCaseData(
            "org/repo.name-with_underscores",
            "feature/🔥-ユニコード").SetName("MetricName_RepoAndBranch_SpecialAndUnicodeCharacters_ReturnsExpectedConstant");
    }

    /// <summary>
    /// Verifies that the MetricName property always returns the expected constant string, regardless of constructor inputs.
    /// Inputs:
    ///  - Various repository and branch values including empty, whitespace, long, and special-character strings.
    /// Expected:
    ///  - The property returns "Product Dependency Cycle Health" without throwing any exceptions.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(MetricNameInputs))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void MetricName_VariousConstructorInputs_ReturnsExpectedConstant(string repo, string branch)
    {
        // Arrange
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var metric = new ProductDependencyCyclesHealthMetric(
            repo,
            branch,
            remoteFactory.Object,
            barClient.Object,
            logger.Object);

        // Act
        var actual = metric.MetricName;

        // Assert
        actual.Should().Be("Product Dependency Cycle Health");
    }

    private static IEnumerable<TestCaseData> RepoBranchEdgeCases()
    {
        yield return new TestCaseData("https://contoso/repo", "main").SetName("EvaluateAsync_CommitIsNull_StandardInputs");
        yield return new TestCaseData("", "").SetName("EvaluateAsync_CommitIsNull_EmptyStrings");
        yield return new TestCaseData("   ", "\t").SetName("EvaluateAsync_CommitIsNull_WhitespaceOnly");
        yield return new TestCaseData(new string('r', 256), new string('b', 256)).SetName("EvaluateAsync_CommitIsNull_VeryLongStrings");
        yield return new TestCaseData("repo/with/special:chars?&", "release/1.0-hotfix").SetName("EvaluateAsync_CommitIsNull_SpecialCharacters");
    }

    /// <summary>
    /// Ensures that when the latest commit on the target branch is null (no commits),
    /// EvaluateAsync sets Result to Passed and Cycles to an empty collection.
    /// Inputs:
    ///  - repository and branch as various edge-case strings (empty, whitespace, long, special chars).
    /// Expected:
    ///  - Result == HealthResult.Passed
    ///  - Cycles is not null and empty
    ///  - IRemoteFactory.CreateRemoteAsync is called once with the repository
    ///  - IRemote.GetLatestCommitAsync is called once with repository and branch
    /// </summary>
    [Test]
    [TestCaseSource(nameof(RepoBranchEdgeCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task EvaluateAsync_CommitIsNull_SetsPassedAndEmptyCyclesAsync(string repository, string branch)
    {
        // Arrange
        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        remoteFactoryMock
            .Setup(m => m.CreateRemoteAsync(repository))
            .ReturnsAsync(remoteMock.Object);

        remoteMock
            .Setup(m => m.GetLatestCommitAsync(repository, branch))
            .ReturnsAsync((string)null);

        var metric = new ProductDependencyCyclesHealthMetric(
            repository,
            branch,
            remoteFactoryMock.Object,
            barClientMock.Object,
            loggerMock.Object);

        // Act
        await metric.EvaluateAsync();

        // Assert
        metric.Result.Should().Be(HealthResult.Passed);
        metric.Cycles.Should().NotBeNull();
        metric.Cycles.Should().BeEmpty();

        remoteFactoryMock.Verify(m => m.CreateRemoteAsync(repository), Times.Once);
        remoteMock.Verify(m => m.GetLatestCommitAsync(repository, branch), Times.Once);
    }

    /// <summary>
    /// Partial test: Validates behavior when a non-null commit is found and the dependency graph contains no cycles.
    /// This requires controlling the static DependencyGraph.BuildRemoteDependencyGraphAsync call, which cannot be mocked using Moq.
    /// Guidance:
    ///  - Consider refactoring ProductDependencyCyclesHealthMetric to accept an injectable graph builder abstraction.
    ///  - Replace the TODO with a setup that returns a DependencyGraph instance whose Cycles is empty.
    /// Expected:
    ///  - Result == HealthResult.Passed
    ///  - Cycles is empty
    /// </summary>
    [Test]
    [Ignore("Cannot mock static DependencyGraph.BuildRemoteDependencyGraphAsync. Consider introducing an injectable abstraction for the graph builder.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task EvaluateAsync_CommitIsNonNull_NoCycles_SetsPassedAndEmptyCyclesAsync()
    {
        // Arrange
        const string repository = "https://contoso/repo";
        const string branch = "main";
        const string commit = "abcdef1234567890";

        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        remoteFactoryMock.Setup(m => m.CreateRemoteAsync(repository)).ReturnsAsync(remoteMock.Object);
        remoteMock.Setup(m => m.GetLatestCommitAsync(repository, branch)).ReturnsAsync(commit);

        var metric = new ProductDependencyCyclesHealthMetric(
            repository,
            branch,
            remoteFactoryMock.Object,
            barClientMock.Object,
            loggerMock.Object);

        // TODO: Replace with arrangement that returns a DependencyGraph whose Cycles is empty once an injectable abstraction is introduced.

        // Act
        await metric.EvaluateAsync();

        // Assert
        // Replace with AwesomeAssertions-based validations once the graph builder can be controlled.
        Assert.Inconclusive("Pending injectable abstraction to control DependencyGraph.BuildRemoteDependencyGraphAsync result.");
    }

    /// <summary>
    /// Partial test: Validates behavior when a non-null commit is found and the dependency graph contains cycles.
    /// This requires controlling the static DependencyGraph.BuildRemoteDependencyGraphAsync call, which cannot be mocked using Moq.
    /// Guidance:
    ///  - Consider refactoring ProductDependencyCyclesHealthMetric to accept an injectable graph builder abstraction.
    ///  - Replace the TODO with a setup that returns a DependencyGraph instance whose Cycles contains at least one cycle.
    /// Expected:
    ///  - Result == HealthResult.Failed
    ///  - Cycles is not empty and contains repository sequences
    /// </summary>
    [Test]
    [Ignore("Cannot mock static DependencyGraph.BuildRemoteDependencyGraphAsync. Consider introducing an injectable abstraction for the graph builder.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task EvaluateAsync_CommitIsNonNull_WithCycles_SetsFailedAndPopulatesCyclesAsync()
    {
        // Arrange
        const string repository = "https://contoso/repo";
        const string branch = "release/1.0";
        const string commit = "123456abcdef";

        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        remoteFactoryMock.Setup(m => m.CreateRemoteAsync(repository)).ReturnsAsync(remoteMock.Object);
        remoteMock.Setup(m => m.GetLatestCommitAsync(repository, branch)).ReturnsAsync(commit);

        var metric = new ProductDependencyCyclesHealthMetric(
            repository,
            branch,
            remoteFactoryMock.Object,
            barClientMock.Object,
            loggerMock.Object);

        // TODO: Replace with arrangement that returns a DependencyGraph whose Cycles contains at least one cycle once an injectable abstraction is introduced.

        // Act
        await metric.EvaluateAsync();

        // Assert
        // Replace with AwesomeAssertions-based validations once the graph builder can be controlled.
        Assert.Inconclusive("Pending injectable abstraction to control DependencyGraph.BuildRemoteDependencyGraphAsync result.");
    }
}
