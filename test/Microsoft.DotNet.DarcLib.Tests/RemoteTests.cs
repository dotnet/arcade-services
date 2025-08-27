// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using FluentAssertions;
using Maestro;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet;
using NuGet.Versioning;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;


[TestFixture]
public class RemoteTests
{
    /// <summary>
    /// Verifies that the Remote constructor succeeds when all dependencies are provided.
    /// Inputs:
    ///  - All constructor parameters are valid non-null mocks.
    /// Expected:
    ///  - No exception is thrown during construction.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_AllValidDependencies_DoesNotThrow()
    {
        // Arrange
        var remoteGitClient = new Mock<IRemoteGitRepo>().Object;
        var versionDetailsParser = new Mock<IVersionDetailsParser>().Object;
        var sourceMappingParser = new Mock<ISourceMappingParser>().Object;
        var remoteFactory = new Mock<IRemoteFactory>().Object;
        var assetLocationResolver = new Mock<IAssetLocationResolver>().Object;
        var cacheClient = new Mock<IRedisCacheClient>().Object;
        var logger = new Mock<ILogger>().Object;

        // Act
        var act = () => new Remote(
            remoteGitClient,
            versionDetailsParser,
            sourceMappingParser,
            remoteFactory,
            assetLocationResolver,
            cacheClient,
            logger);

        // Assert
        // Intentionally no explicit assertion: test passes if no exception is thrown.
        act();
    }

    /// <summary>
    /// Verifies that the Remote constructor does not validate null inputs and succeeds
    /// when any single dependency is null.
    /// Inputs:
    ///  - Exactly one of the constructor parameters is null as specified by paramName.
    /// Expected:
    ///  - No exception is thrown during construction.
    /// </summary>
    [TestCase("remoteGitClient")]
    [TestCase("versionDetailsParser")]
    [TestCase("sourceMappingParser")]
    [TestCase("remoteFactory")]
    [TestCase("assetLocationResolver")]
    [TestCase("cacheClient")]
    [TestCase("logger")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_SingleNullDependency_DoesNotThrow(string paramName)
    {
        // Arrange
        var remoteGitClient = new Mock<IRemoteGitRepo>().Object;
        var versionDetailsParser = new Mock<IVersionDetailsParser>().Object;
        var sourceMappingParser = new Mock<ISourceMappingParser>().Object;
        var remoteFactory = new Mock<IRemoteFactory>().Object;
        var assetLocationResolver = new Mock<IAssetLocationResolver>().Object;
        var cacheClient = new Mock<IRedisCacheClient>().Object;
        var logger = new Mock<ILogger>().Object;

        switch (paramName)
        {
            case "remoteGitClient":
                remoteGitClient = null;
                break;
            case "versionDetailsParser":
                versionDetailsParser = null;
                break;
            case "sourceMappingParser":
                sourceMappingParser = null;
                break;
            case "remoteFactory":
                remoteFactory = null;
                break;
            case "assetLocationResolver":
                assetLocationResolver = null;
                break;
            case "cacheClient":
                cacheClient = null;
                break;
            case "logger":
                logger = null;
                break;
        }

        // Act
        var act = () => new Remote(
            remoteGitClient,
            versionDetailsParser,
            sourceMappingParser,
            remoteFactory,
            assetLocationResolver,
            cacheClient,
            logger);

        // Assert
        // Intentionally no explicit assertion: test passes if no exception is thrown.
        act();
    }

    /// <summary>
    /// Verifies that the Remote constructor still succeeds when multiple dependencies are null,
    /// including those directly passed to the internal DependencyFileManager constructor.
    /// Inputs:
    ///  - remoteGitClient, versionDetailsParser, and logger are null simultaneously.
    /// Expected:
    ///  - No exception is thrown during construction.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_MultipleNullsIncludingFileManagerInputs_DoesNotThrow()
    {
        // Arrange
        IRemoteGitRepo remoteGitClient = null;
        IVersionDetailsParser versionDetailsParser = null;
        var sourceMappingParser = new Mock<ISourceMappingParser>().Object;
        var remoteFactory = new Mock<IRemoteFactory>().Object;
        var assetLocationResolver = new Mock<IAssetLocationResolver>().Object;
        var cacheClient = new Mock<IRedisCacheClient>().Object;
        ILogger logger = null;

        // Act
        var act = () => new Remote(
            remoteGitClient,
            versionDetailsParser,
            sourceMappingParser,
            remoteFactory,
            assetLocationResolver,
            cacheClient,
            logger);

        // Assert
        // Intentionally no explicit assertion: test passes if no exception is thrown.
        act();
    }

    private static IEnumerable<TestCaseData> CreateBranch_Cases()
    {
        // Standard input
        yield return new TestCaseData(
            "https://github.com/org/repo",
            "main",
            "feature/new")
            .SetName("CreateNewBranchAsync_StandardNames");

        // Empty strings
        yield return new TestCaseData(
            string.Empty,
            string.Empty,
            string.Empty)
            .SetName("CreateNewBranchAsync_EmptyStrings");

        // Whitespace-only values
        yield return new TestCaseData(
            "   ",
            "\t ",
            " \n ")
            .SetName("CreateNewBranchAsync_WhitespaceOnly");

        // Very long strings
        var longRepo = "https://example.com/" + new string('r', 500);
        var longBase = "base/" + new string('b', 500);
        var longNew = "feature/" + new string('n', 500);
        yield return new TestCaseData(
            longRepo,
            longBase,
            longNew)
            .SetName("CreateNewBranchAsync_VeryLongStrings");

        // Special characters
        yield return new TestCaseData(
            "git@github.com:org/repo.git",
            "base-branch🔥",
            "feature/#weird?branch")
            .SetName("CreateNewBranchAsync_SpecialCharacters");
    }

    /// <summary>
    /// Ensures CreateNewBranchAsync forwards parameters to the IRemoteGitRepo in the correct order.
    /// Inputs:
    ///  - repoUri, baseBranch, newBranch including standard, empty, whitespace, long, and special-character cases.
    /// Expected:
    ///  - IRemoteGitRepo.CreateBranchAsync is called exactly once with (repoUri, newBranch, baseBranch).
    /// </summary>
    [Test]
    [TestCaseSource(nameof(CreateBranch_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CreateNewBranchAsync_ForwardsParametersInCorrectOrder_CallsClientOnce(string repoUri, string baseBranch, string newBranch)
    {
        // Arrange
        var remoteGitRepo = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitRepo
            .Setup(m => m.CreateBranchAsync(repoUri, newBranch, baseBranch))
            .Returns(Task.CompletedTask);

        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Loose);
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Loose);
        var locationResolver = new Mock<IAssetLocationResolver>(MockBehavior.Loose);
        var cacheClient = new Mock<IRedisCacheClient>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var remote = new Remote(
            remoteGitRepo.Object,
            versionDetailsParser.Object,
            sourceMappingParser.Object,
            remoteFactory.Object,
            locationResolver.Object,
            cacheClient.Object,
            logger.Object);

        // Act
        await remote.CreateNewBranchAsync(repoUri, baseBranch, newBranch);

        // Assert
        remoteGitRepo.Verify(m => m.CreateBranchAsync(repoUri, newBranch, baseBranch), Times.Once);
        remoteGitRepo.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that CreateOrUpdatePullRequestMergeStatusInfoAsync forwards the exact same parameters
    /// (including object identity) to the underlying IRemoteGitRepo implementation.
    /// Inputs:
    ///  - Various pullRequestUrl values (empty, whitespace, long, special characters) and evaluation list sizes.
    /// Expected:
    ///  - IRemoteGitRepo.CreateOrUpdatePullRequestMergeStatusInfoAsync is invoked exactly once with the same instances.
    /// </summary>
    [TestCaseSource(nameof(DelegationCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CreateOrUpdatePullRequestMergeStatusInfoAsync_ValidInputs_DelegatesToRemoteWithSameArguments(string pullRequestUrl, int evaluationCount)
    {
        // Arrange
        var remoteGitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitRepoMock
            .Setup(m => m.CreateOrUpdatePullRequestMergeStatusInfoAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<MergePolicyEvaluationResult>>()))
            .Returns(Task.CompletedTask);

        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var locationResolver = new Mock<IAssetLocationResolver>(MockBehavior.Strict);
        var cacheClient = new Mock<IRedisCacheClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var remote = new Remote(
            remoteGitRepoMock.Object,
            versionDetailsParser.Object,
            sourceMappingParser.Object,
            remoteFactory.Object,
            locationResolver.Object,
            cacheClient.Object,
            logger.Object);

        var evaluations = CreateEvaluations(evaluationCount);

        // Act
        await remote.CreateOrUpdatePullRequestMergeStatusInfoAsync(pullRequestUrl, evaluations);

        // Assert
        remoteGitRepoMock.Verify(m => m.CreateOrUpdatePullRequestMergeStatusInfoAsync(
                It.Is<string>(s => object.ReferenceEquals(s, pullRequestUrl)),
                It.Is<IReadOnlyCollection<MergePolicyEvaluationResult>>(e => object.ReferenceEquals(e, evaluations))),
            Times.Once);
        remoteGitRepoMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures that exceptions thrown by the underlying IRemoteGitRepo are propagated unchanged.
    /// Inputs:
    ///  - A valid pullRequestUrl and a non-empty evaluations collection.
    /// Expected:
    ///  - The same exception (InvalidOperationException) is thrown by Remote.CreateOrUpdatePullRequestMergeStatusInfoAsync.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CreateOrUpdatePullRequestMergeStatusInfoAsync_RemoteThrows_PropagatesException()
    {
        // Arrange
        var remoteGitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitRepoMock
            .Setup(m => m.CreateOrUpdatePullRequestMergeStatusInfoAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<MergePolicyEvaluationResult>>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var locationResolver = new Mock<IAssetLocationResolver>(MockBehavior.Strict);
        var cacheClient = new Mock<IRedisCacheClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var remote = new Remote(
            remoteGitRepoMock.Object,
            versionDetailsParser.Object,
            sourceMappingParser.Object,
            remoteFactory.Object,
            locationResolver.Object,
            cacheClient.Object,
            logger.Object);

        var url = "https://github.com/org/repo/pull/123";
        var evaluations = CreateEvaluations(2);

        // Act + Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await remote.CreateOrUpdatePullRequestMergeStatusInfoAsync(url, evaluations));

        remoteGitRepoMock.Verify(m => m.CreateOrUpdatePullRequestMergeStatusInfoAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<MergePolicyEvaluationResult>>()),
            Times.Once);
        remoteGitRepoMock.VerifyNoOtherCalls();
    }

    private static IEnumerable<TestCaseData> DelegationCases()
    {
        yield return new TestCaseData("https://github.com/org/repo/pull/1", 0).SetName("Url_Normal_Evals_Empty");
        yield return new TestCaseData("", 1).SetName("Url_Empty_Evals_Single");
        yield return new TestCaseData("   ", 2).SetName("Url_Whitespace_Evals_Two");
        yield return new TestCaseData(new string('x', 1024), 3).SetName("Url_VeryLong_Evals_Three");
        yield return new TestCaseData("https://example.com/pr?query=1&name=value#frag", 4).SetName("Url_SpecialCharacters_Evals_Four");
    }

    private static IReadOnlyCollection<MergePolicyEvaluationResult> CreateEvaluations(int count)
    {
        var list = new List<MergePolicyEvaluationResult>(capacity: Math.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            // Use cast to avoid assuming enum members; values outside defined range are acceptable for construction.
            var status = (MergePolicyEvaluationStatus)(i % 4);
            list.Add(new MergePolicyEvaluationResult(
                status: status,
                title: $"Policy {i}",
                message: $"Result {i}",
                mergePolicyName: $"PolicyName{i}",
                mergePolicyDisplayName: $"PolicyDisplay{i}"));
        }
        return list;
    }

    /// <summary>
    /// Verifies that UpdatePullRequestAsync forwards the exact parameters to the underlying IRemoteGitRepo.
    /// Inputs:
    ///  - A variety of pullRequestUri values including empty, whitespace, long, and special-character URIs.
    ///  - A PullRequest instance with simple Title/Description to ensure the same reference is passed through.
    /// Expected:
    ///  - IRemoteGitRepo.UpdatePullRequestAsync is invoked exactly once with the same uri string and the same PullRequest instance.
    ///  - No exception is thrown when the underlying client returns successfully.
    /// </summary>
    [Test]
    [TestCase("https://host/repo/pull/1")]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" +
              "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 200 'a'
    [TestCase("https://host/repo/pull/✓?q=∆&x=©")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task UpdatePullRequestAsync_ForwardsParametersToClient(string pullRequestUri)
    {
        // Arrange
        var repoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var versionParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Loose);
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Loose);
        var locationResolver = new Mock<IAssetLocationResolver>(MockBehavior.Loose);
        var cache = new Mock<IRedisCacheClient>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var pr = new PullRequest
        {
            Title = $"Title-{pullRequestUri.Length}",
            Description = "Desc"
        };

        repoMock
            .Setup(m => m.UpdatePullRequestAsync(
                It.Is<string>(u => u == pullRequestUri),
                It.Is<PullRequest>(p => ReferenceEquals(p, pr))))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var remote = new Remote(
            repoMock.Object,
            versionParser.Object,
            sourceMappingParser.Object,
            remoteFactory.Object,
            locationResolver.Object,
            cache.Object,
            logger.Object);

        // Act
        await remote.UpdatePullRequestAsync(pullRequestUri, pr);

        // Assert
        repoMock.Verify(m => m.UpdatePullRequestAsync(
                It.Is<string>(u => u == pullRequestUri),
                It.Is<PullRequest>(p => ReferenceEquals(p, pr))),
            Times.Once);
        repoMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures that exceptions thrown by the underlying IRemoteGitRepo.UpdatePullRequestAsync are propagated.
    /// Inputs:
    ///  - A valid pullRequestUri and a simple PullRequest object.
    ///  - The client mock is configured to throw InvalidOperationException with a custom message.
    /// Expected:
    ///  - Remote.UpdatePullRequestAsync throws the same InvalidOperationException with the same message.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task UpdatePullRequestAsync_WhenClientThrows_ExceptionIsPropagated()
    {
        // Arrange
        var repoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var versionParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Loose);
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Loose);
        var locationResolver = new Mock<IAssetLocationResolver>(MockBehavior.Loose);
        var cache = new Mock<IRedisCacheClient>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var uri = "https://host/repo/pull/2";
        var pr = new PullRequest { Title = "T", Description = "D" };

        repoMock
            .Setup(m => m.UpdatePullRequestAsync(
                It.Is<string>(u => u == uri),
                It.Is<PullRequest>(p => ReferenceEquals(p, pr))))
            .ThrowsAsync(new InvalidOperationException("boom"))
            .Verifiable();

        var remote = new Remote(
            repoMock.Object,
            versionParser.Object,
            sourceMappingParser.Object,
            remoteFactory.Object,
            locationResolver.Object,
            cache.Object,
            logger.Object);

        // Act
        Func<Task> act = () => remote.UpdatePullRequestAsync(uri, pr);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        repoMock.Verify(m => m.UpdatePullRequestAsync(
                It.Is<string>(u => u == uri),
                It.Is<PullRequest>(p => ReferenceEquals(p, pr))),
            Times.Once);
        repoMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that DeletePullRequestBranchAsync delegates to the underlying IRemoteGitRepo without throwing.
    /// Inputs:
    ///  - Various forms of pull request URIs including typical URL, empty, whitespace, file-URI-like, unicode/special chars, and very long strings.
    /// Expected:
    ///  - The call completes successfully without exceptions.
    ///  - The underlying client's DeletePullRequestBranchAsync is invoked exactly once with the same URI.
    /// </summary>
    [TestCase("https://github.com/owner/repo/pull/123")]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("file://C:/path/pr/1")]
    [TestCase("特殊字符://路径?查询=✓&🚀")]
    [TestCaseSource(nameof(GetVeryLongUri))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task DeletePullRequestBranchAsync_ValidUris_CallsClientAndDoesNotThrow(string pullRequestUri)
    {
        // Arrange
        var remoteGitRepo = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitRepo
            .Setup(m => m.DeletePullRequestBranchAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var remote = new Remote(
            remoteGitRepo.Object,
            new Mock<IVersionDetailsParser>().Object,
            new Mock<ISourceMappingParser>().Object,
            new Mock<IRemoteFactory>().Object,
            new Mock<IAssetLocationResolver>().Object,
            new Mock<IRedisCacheClient>().Object,
            new Mock<ILogger>().Object);

        // Act
        await remote.DeletePullRequestBranchAsync(pullRequestUri);

        // Assert
        remoteGitRepo.Verify(m => m.DeletePullRequestBranchAsync(pullRequestUri), Times.Once);
        remoteGitRepo.VerifyNoOtherCalls();
    }

    private static IEnumerable<string> GetVeryLongUri()
    {
        yield return new string('a', 1024);
    }

    /// <summary>
    /// Verifies that GetPullRequestAsync forwards the provided pullRequestUri to the underlying IRemoteGitRepo
    /// and returns exactly the same PullRequest instance.
    /// Inputs:
    ///  - Various pull request URIs, including empty, whitespace, long, and special-character-containing strings.
    /// Expected:
    ///  - The underlying IRemoteGitRepo.GetPullRequestAsync is invoked exactly once with the same string.
    ///  - The returned value is the very same PullRequest instance.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(GetPullRequestAsync_ValidUri_DelegatesToClientAndReturnsResult_TestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetPullRequestAsync_ValidUri_DelegatesToClientAndReturnsResult(string pullRequestUri)
    {
        // Arrange
        var remoteGitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var versionDetailsParserMock = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var assetLocationResolverMock = new Mock<IAssetLocationResolver>(MockBehavior.Strict);
        var cacheClientMock = new Mock<IRedisCacheClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var expected = new PullRequest();

        remoteGitRepoMock
            .Setup(m => m.GetPullRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(expected);

        var remote = new Remote(
            remoteGitRepoMock.Object,
            versionDetailsParserMock.Object,
            sourceMappingParserMock.Object,
            remoteFactoryMock.Object,
            assetLocationResolverMock.Object,
            cacheClientMock.Object,
            loggerMock.Object);

        // Act
        var result = await remote.GetPullRequestAsync(pullRequestUri);

        // Assert
        result.Should().BeSameAs(expected);
        remoteGitRepoMock.Verify(m => m.GetPullRequestAsync(It.Is<string>(s => s == pullRequestUri)), Times.Once);
        remoteGitRepoMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that GetPullRequestAsync returns null when the underlying IRemoteGitRepo returns null.
    /// Inputs:
    ///  - A representative pull request URI.
    /// Expected:
    ///  - The returned value is null and the underlying method is called exactly once.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetPullRequestAsync_ClientReturnsNull_ReturnsNull()
    {
        // Arrange
        const string pullRequestUri = "https://example.com/owner/repo/pull/123";
        var remoteGitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var versionDetailsParserMock = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var assetLocationResolverMock = new Mock<IAssetLocationResolver>(MockBehavior.Strict);
        var cacheClientMock = new Mock<IRedisCacheClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        remoteGitRepoMock
            .Setup(m => m.GetPullRequestAsync(It.IsAny<string>()))
            .ReturnsAsync((PullRequest)null);

        var remote = new Remote(
            remoteGitRepoMock.Object,
            versionDetailsParserMock.Object,
            sourceMappingParserMock.Object,
            remoteFactoryMock.Object,
            assetLocationResolverMock.Object,
            cacheClientMock.Object,
            loggerMock.Object);

        // Act
        var result = await remote.GetPullRequestAsync(pullRequestUri);

        // Assert
        result.Should().BeNull();
        remoteGitRepoMock.Verify(m => m.GetPullRequestAsync(It.Is<string>(s => s == pullRequestUri)), Times.Once);
        remoteGitRepoMock.VerifyNoOtherCalls();
    }

    private static IEnumerable GetPullRequestAsync_ValidUri_DelegatesToClientAndReturnsResult_TestCases()
    {
        yield return new TestCaseData("https://github.com/org/repo/pull/1").SetName("TypicalUrl");
        yield return new TestCaseData(string.Empty).SetName("EmptyString");
        yield return new TestCaseData("   ").SetName("WhitespaceOnly");
        yield return new TestCaseData(new string('a', 8192)).SetName("VeryLongString");
        yield return new TestCaseData("https://host/αβγ/δ?x=1&y=2%20#frag\t\n").SetName("SpecialCharacters");
    }

    /// <summary>
    /// Verifies that CreatePullRequestAsync forwards the exact parameters to the underlying IRemoteGitRepo
    /// and returns the string value provided by the dependency.
    /// Inputs:
    ///  - Various repoUri values, including empty, whitespace, URLs with special characters, and a very long string.
    ///  - A non-null PullRequest instance.
    /// Expected:
    ///  - The returned value equals the value from IRemoteGitRepo.CreatePullRequestAsync.
    ///  - The underlying method is called exactly once with the same PullRequest instance.
    /// </summary>
    [TestCaseSource(nameof(CreatePullRequestAsync_RepoUris))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CreatePullRequestAsync_ForwardsParametersAndReturnsValue(string repoUri)
    {
        // Arrange
        var remoteGitClient = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var locationResolver = new Mock<IAssetLocationResolver>(MockBehavior.Strict);
        var cacheClient = new Mock<IRedisCacheClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var pr = new PullRequest
        {
            Title = "Title",
            Description = "Description",
            BaseBranch = "base",
            HeadBranch = "head",
            Status = PrStatus.Open,
            UpdatedAt = DateTimeOffset.UtcNow,
            TargetBranchCommitSha = "abc123"
        };

        var expectedUrl = "created-pr-url";
        remoteGitClient
            .Setup(m => m.CreatePullRequestAsync(repoUri, pr))
            .ReturnsAsync(expectedUrl);

        var remote = new Remote(
            remoteGitClient.Object,
            versionDetailsParser.Object,
            sourceMappingParser.Object,
            remoteFactory.Object,
            locationResolver.Object,
            cacheClient.Object,
            logger.Object);

        // Act
        var result = await remote.CreatePullRequestAsync(repoUri, pr);

        // Assert
        result.Should().Be(expectedUrl);
        remoteGitClient.Verify(m => m.CreatePullRequestAsync(repoUri, pr), Times.Once);
        remoteGitClient.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures that exceptions thrown by the underlying IRemoteGitRepo.CreatePullRequestAsync
    /// are propagated by Remote.CreatePullRequestAsync without alteration.
    /// Inputs:
    ///  - A valid repoUri.
    ///  - A non-null PullRequest instance.
    ///  - The IRemoteGitRepo mock configured to throw InvalidOperationException.
    /// Expected:
    ///  - The method throws InvalidOperationException.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CreatePullRequestAsync_WhenClientThrows_ExceptionIsPropagated()
    {
        // Arrange
        var remoteGitClient = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var locationResolver = new Mock<IAssetLocationResolver>(MockBehavior.Strict);
        var cacheClient = new Mock<IRedisCacheClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var repoUri = "https://github.com/org/repo";
        var pr = new PullRequest
        {
            Title = "T",
            Description = "D",
            BaseBranch = "b",
            HeadBranch = "h",
            Status = PrStatus.Open,
            UpdatedAt = DateTimeOffset.UtcNow,
            TargetBranchCommitSha = "sha"
        };

        remoteGitClient
            .Setup(m => m.CreatePullRequestAsync(repoUri, pr))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var remote = new Remote(
            remoteGitClient.Object,
            versionDetailsParser.Object,
            sourceMappingParser.Object,
            remoteFactory.Object,
            locationResolver.Object,
            cacheClient.Object,
            logger.Object);

        // Act
        Func<Task> act = () => remote.CreatePullRequestAsync(repoUri, pr);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        remoteGitClient.Verify(m => m.CreatePullRequestAsync(repoUri, pr), Times.Once);
        remoteGitClient.VerifyNoOtherCalls();
    }

    private static IEnumerable<string> CreatePullRequestAsync_RepoUris()
    {
        yield return "";
        yield return " ";
        yield return "https://github.com/org/repo";
        yield return "ssh://git@github.com:org/repo.git";
        yield return "https://example.com/repo path?query=param#fragment";
        yield return new string('a', 1024);
    }

    /// <summary>
    /// Verifies that when the repository URI is empty or whitespace-only,
    /// RepositoryExistsAsync returns false and does not call the underlying IRemoteGitRepo.RepoExistsAsync.
    /// Inputs:
    ///  - repoUri: "", " ", "\t", " \r\n ".
    /// Expected:
    ///  - The method returns false.
    ///  - IRemoteGitRepo.RepoExistsAsync is never invoked.
    /// </summary>
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\t")]
    [TestCase(" \r\n ")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task RepositoryExistsAsync_WhitespaceOrEmptyRepoUri_ReturnsFalseAndDoesNotCallClient(string repoUri)
    {
        // Arrange
        var remoteGitClient = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var assetLocationResolver = new Mock<IAssetLocationResolver>(MockBehavior.Strict);
        var cacheClient = new Mock<IRedisCacheClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var remote = new Remote(
            remoteGitClient.Object,
            new VersionDetailsParser(),
            sourceMappingParser.Object,
            remoteFactory.Object,
            assetLocationResolver.Object,
            cacheClient.Object,
            logger.Object);

        // Act
        var result = await remote.RepositoryExistsAsync(repoUri);

        // Assert
        result.Should().BeFalse();
        remoteGitClient.Verify(x => x.RepoExistsAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a valid repository URI is delegated to IRemoteGitRepo.RepoExistsAsync,
    /// and the result is returned unchanged.
    /// Inputs:
    ///  - repoUri set to common forms (HTTPS, SSH) and mocked client responses (true/false).
    /// Expected:
    ///  - The method returns the same boolean as provided by IRemoteGitRepo.RepoExistsAsync.
    ///  - IRemoteGitRepo.RepoExistsAsync is called exactly once with the provided repoUri.
    /// </summary>
    [TestCase("https://github.com/dotnet/arcade", true)]
    [TestCase("ssh://git@github.com:dotnet/arcade.git", false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task RepositoryExistsAsync_ValidRepoUri_DelegatesToClientAndReturnsResult(string repoUri, bool clientResult)
    {
        // Arrange
        var remoteGitClient = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitClient
            .Setup(x => x.RepoExistsAsync(repoUri))
            .ReturnsAsync(clientResult);

        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var assetLocationResolver = new Mock<IAssetLocationResolver>(MockBehavior.Strict);
        var cacheClient = new Mock<IRedisCacheClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var remote = new Remote(
            remoteGitClient.Object,
            new VersionDetailsParser(),
            sourceMappingParser.Object,
            remoteFactory.Object,
            assetLocationResolver.Object,
            cacheClient.Object,
            logger.Object);

        // Act
        var result = await remote.RepositoryExistsAsync(repoUri);

        // Assert
        result.Should().Be(clientResult);
        remoteGitClient.Verify(x => x.RepoExistsAsync(repoUri), Times.Once);
    }

    /// <summary>
    /// Verifies that GetLatestCommitAsync delegates to IRemoteGitRepo.GetLastCommitShaAsync with the exact inputs
    /// and returns the same SHA string result.
    /// Inputs:
    ///  - Various repoUri and branch combinations including empty, whitespace, unicode, and very long strings.
    /// Expected:
    ///  - The method returns the exact string produced by the underlying client and the dependency is called once with the same parameters.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(GetLatestCommitAsync_ValidInputs))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetLatestCommitAsync_DelegatesToRemoteGitRepo_ExpectedShaReturned(string repoUri, string branch, string expectedSha)
    {
        // Arrange
        var remoteGitRepo = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitRepo
            .Setup(x => x.GetLastCommitShaAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(expectedSha);

        var remote = new Remote(
            remoteGitRepo.Object,
            Mock.Of<IVersionDetailsParser>(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        var result = await remote.GetLatestCommitAsync(repoUri, branch);

        // Assert
        NUnit.Framework.Assert.That(result, Is.EqualTo(expectedSha));
        remoteGitRepo.Verify(x => x.GetLastCommitShaAsync(repoUri, branch), Times.Once);
    }

    /// <summary>
    /// Ensures that exceptions thrown by the underlying IRemoteGitRepo are propagated by GetLatestCommitAsync.
    /// Inputs:
    ///  - A tuple with repoUri and branch strings, and an exception instance thrown by the dependency.
    /// Expected:
    ///  - The same exception type is thrown by GetLatestCommitAsync.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(GetLatestCommitAsync_Exceptions))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetLatestCommitAsync_WhenRemoteGitRepoThrows_ExceptionIsPropagated(string repoUri, string branch, Exception exceptionToThrow)
    {
        // Arrange
        var remoteGitRepo = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitRepo
            .Setup(x => x.GetLastCommitShaAsync(repoUri, branch))
            .ThrowsAsync(exceptionToThrow);

        var remote = new Remote(
            remoteGitRepo.Object,
            Mock.Of<IVersionDetailsParser>(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act + Assert
        NUnit.Framework.Assert.ThrowsAsync(exceptionToThrow.GetType(), async () => await remote.GetLatestCommitAsync(repoUri, branch));
        remoteGitRepo.Verify(x => x.GetLastCommitShaAsync(repoUri, branch), Times.Once);
    }

    private static IEnumerable<object[]> GetLatestCommitAsync_ValidInputs()
    {
        yield return new object[] { "https://github.com/org/repo", "main", "abc123" };
        yield return new object[] { "", " ", "sha-!@#$%^&*()_+" };
        yield return new object[] { "ssh://git@github.com:org/repo.git", "release/1.0", "deadbeef" };
        yield return new object[] { "https://example.com/repo?query=param&x=1#frag", "feature/äöü-测试-🚀", "0123456789abcdef" };
        yield return new object[] { new string('a', 2048), new string('b', 1024), "ffffffffffffffff" };
    }

    private static IEnumerable<object[]> GetLatestCommitAsync_Exceptions()
    {
        yield return new object[] { "https://github.com/org/repo", "main", new ArgumentException("bad input") };
        yield return new object[] { "https://github.com/org/repo", "dev", new InvalidOperationException("invalid op") };
    }

    /// <summary>
    /// Verifies that Remote.GetCommitAsync forwards the exact repoUri and sha to IRemoteGitRepo.GetCommitAsync
    /// and returns the same Commit instance from the underlying client.
    /// Inputs:
    ///  - Various repoUri and sha combinations including normal, empty, whitespace, and special-character strings.
    /// Expected:
    ///  - IRemoteGitRepo.GetCommitAsync is invoked once with the exact parameters.
    ///  - The returned Commit instance is the same instance provided by the mock (pass-through).
    /// </summary>
    [Test]
    [TestCase("https://github.com/org/repo", "abcdef0123456789")]
    [TestCase("", "sha")]
    [TestCase("   ", "deadbeef")]
    [TestCase("https://example.com/repo-with-specials_%2F?x=y", "abc123~!@#$%^&*()_+-=[]{}|;':,.<>/?")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetCommitAsync_ForwardsParametersAndReturnsResult(string repoUri, string sha)
    {
        // Arrange
        var expectedCommit = new Commit("author", "sha", "message");

        var remoteGitClient = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitClient
            .Setup(m => m.GetCommitAsync(repoUri, sha))
            .ReturnsAsync(expectedCommit);

        var remote = new Remote(
            remoteGitClient.Object,
            Mock.Of<IVersionDetailsParser>(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        var result = await remote.GetCommitAsync(repoUri, sha);

        // Assert
        result.Should().BeSameAs(expectedCommit);
        remoteGitClient.Verify(m => m.GetCommitAsync(repoUri, sha), Times.Once);
        remoteGitClient.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that Remote.GetCommitAsync returns null when the underlying IRemoteGitRepo returns null,
    /// indicating that no commit was found for the given repoUri and sha.
    /// Inputs:
    ///  - A valid-looking repoUri and sha for which the client returns null.
    /// Expected:
    ///  - The method returns null.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetCommitAsync_ReturnsNull_WhenClientReturnsNull()
    {
        // Arrange
        const string repoUri = "https://github.com/org/repo";
        const string sha = "missing-commit";

        var remoteGitClient = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitClient
            .Setup(m => m.GetCommitAsync(repoUri, sha))
            .ReturnsAsync((Commit)null);

        var remote = new Remote(
            remoteGitClient.Object,
            Mock.Of<IVersionDetailsParser>(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        var result = await remote.GetCommitAsync(repoUri, sha);

        // Assert
        result.Should().BeNull();
        remoteGitClient.Verify(m => m.GetCommitAsync(repoUri, sha), Times.Once);
        remoteGitClient.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that Remote.GetCommitAsync propagates exceptions thrown by the underlying IRemoteGitRepo.GetCommitAsync.
    /// Inputs:
    ///  - repoUri and sha that cause the client to throw an InvalidOperationException.
    /// Expected:
    ///  - The same exception type is thrown by Remote.GetCommitAsync (no swallowing or wrapping).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetCommitAsync_PropagatesException_WhenClientThrows()
    {
        // Arrange
        const string repoUri = "https://github.com/org/repo";
        const string sha = "faulty-sha";

        var remoteGitClient = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitClient
            .Setup(m => m.GetCommitAsync(repoUri, sha))
            .Throws(new InvalidOperationException("Simulated failure"));

        var remote = new Remote(
            remoteGitClient.Object,
            Mock.Of<IVersionDetailsParser>(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        Func<Task> act = () => remote.GetCommitAsync(repoUri, sha);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        remoteGitClient.Verify(m => m.GetCommitAsync(repoUri, sha), Times.Once);
        remoteGitClient.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that GetDependenciesAsync correctly filters dependencies by name using case-insensitive comparison.
    /// Inputs:
    ///  - Version.Details.xml containing 3 dependencies: Foo, BAR, and bar (mixed casing).
    ///  - Various 'name' filters including null, empty, whitespace, exact, different casing, and non-existing names.
    /// Expected:
    ///  - When 'name' is null or empty, all dependencies are returned.
    ///  - When 'name' is whitespace-only or non-existing, an empty sequence is returned.
    ///  - When 'name' matches ignoring case, only dependencies with that name (case-insensitively) are returned.
    /// </summary>
    [TestCaseSource(nameof(GetDependenciesAsync_NameFilter_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetDependenciesAsync_NameFilter_FiltersExpected(string nameFilter, string[] expectedNames)
    {
        // Arrange
        var versionDetailsXml = @"
<Dependencies>
  <ProductDependencies>
    <Dependency Name=""Foo"" Version=""1.1.0"">
      <Uri>https://repo/foo</Uri>
      <Sha>sha-foo</Sha>
    </Dependency>
    <Dependency Name=""BAR"" Version=""3.0.0"">
      <Uri>https://repo/bar1</Uri>
      <Sha>sha-bar1</Sha>
    </Dependency>
  </ProductDependencies>
  <ToolsetDependencies>
    <Dependency Name=""bar"" Version=""2.2.0"">
      <Uri>https://repo/bar2</Uri>
      <Sha>sha-bar2</Sha>
    </Dependency>
  </ToolsetDependencies>
</Dependencies>";

        var gitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        gitRepoMock
            .Setup(m => m.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(versionDetailsXml);

        var remote = new Remote(
            gitRepoMock.Object,
            new VersionDetailsParser(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        var result = await remote.GetDependenciesAsync("https://repo/any", "any-branch", nameFilter);
        var actualNames = result.Select(d => d.Name).ToArray();

        // Assert
        // Validate cardinality
        if (actualNames.Length != expectedNames.Length)
        {
            throw new AssertionException($"Expected {expectedNames.Length} dependencies but got {actualNames.Length}. Actual: [{string.Join(", ", actualNames)}]");
        }

        // Validate content (order-insensitive)
        foreach (var expected in expectedNames)
        {
            if (!actualNames.Contains(expected))
            {
                throw new AssertionException($"Expected dependency '{expected}' not found. Actual: [{string.Join(", ", actualNames)}]");
            }
        }

        // Ensure no extras
        foreach (var actual in actualNames)
        {
            if (!expectedNames.Contains(actual))
            {
                throw new AssertionException($"Unexpected dependency '{actual}' returned. Expected: [{string.Join(", ", expectedNames)}]");
            }
        }

        gitRepoMock.Verify(m => m.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.AtLeastOnce);
    }

    /// <summary>
    /// Ensures GetDependenciesAsync operates correctly across edge-case repoUri and branchOrCommit values.
    /// Inputs:
    ///  - Several repoUri and branchOrCommit values including empty, whitespace, typical URL, and long strings.
    ///  - Null 'name' filter, meaning all dependencies should be returned.
    /// Expected:
    ///  - No exception is thrown and all dependencies parsed from Version.Details.xml are returned.
    /// </summary>
    [TestCaseSource(nameof(GetDependenciesAsync_RepoBranchEdge_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetDependenciesAsync_RepoUriAndBranchEdgeValues_ReturnsAllDependencies(string repoUri, string branchOrCommit)
    {
        // Arrange
        var versionDetailsXml = @"
<Dependencies>
  <ProductDependencies>
    <Dependency Name=""A"" Version=""1.0.0""><Uri>u1</Uri><Sha>s1</Sha></Dependency>
  </ProductDependencies>
  <ToolsetDependencies>
    <Dependency Name=""B"" Version=""2.0.0""><Uri>u2</Uri><Sha>s2</Sha></Dependency>
    <Dependency Name=""C"" Version=""3.0.0""><Uri>u3</Uri><Sha>s3</Sha></Dependency>
  </ToolsetDependencies>
</Dependencies>";

        var gitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        gitRepoMock
            .Setup(m => m.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(versionDetailsXml);

        var remote = new Remote(
            gitRepoMock.Object,
            new VersionDetailsParser(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        var result = await remote.GetDependenciesAsync(repoUri, branchOrCommit, null);
        var list = result.ToList();

        // Assert
        if (list.Count != 3)
        {
            throw new AssertionException($"Expected 3 dependencies but got {list.Count}.");
        }

        var names = list.Select(d => d.Name).OrderBy(n => n).ToArray();
        var expected = new[] { "A", "B", "C" };
        for (int i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(names[i], expected[i], StringComparison.Ordinal))
            {
                throw new AssertionException($"Expected names [{string.Join(", ", expected)}] but got [{string.Join(", ", names)}].");
            }
        }

        gitRepoMock.Verify(m => m.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.AtLeastOnce);
    }

    private static IEnumerable GetDependenciesAsync_NameFilter_Cases()
    {
        yield return new TestCaseData(null, new[] { "Foo", "BAR", "bar" })
            .SetName("GetDependenciesAsync_NameNull_ReturnsAll");
        yield return new TestCaseData(string.Empty, new[] { "Foo", "BAR", "bar" })
            .SetName("GetDependenciesAsync_NameEmpty_ReturnsAll");
        yield return new TestCaseData("foo", new[] { "Foo" })
            .SetName("GetDependenciesAsync_NameExactLower_ReturnsFooOnly");
        yield return new TestCaseData("BAR", new[] { "BAR", "bar" })
            .SetName("GetDependenciesAsync_NameUpper_ReturnsAllCaseVariants");
        yield return new TestCaseData("   ", Array.Empty<string>())
            .SetName("GetDependenciesAsync_NameWhitespace_ReturnsNone");
        yield return new TestCaseData("does-not-exist", Array.Empty<string>())
            .SetName("GetDependenciesAsync_NameNotFound_ReturnsNone");
    }

    private static IEnumerable GetDependenciesAsync_RepoBranchEdge_Cases()
    {
        yield return new TestCaseData(string.Empty, string.Empty)
            .SetName("GetDependenciesAsync_EdgeRepoAndBranch_EmptyStrings");
        yield return new TestCaseData("   ", "   ")
            .SetName("GetDependenciesAsync_EdgeRepoAndBranch_Whitespace");
        yield return new TestCaseData("https://example.com/org/repo", "main")
            .SetName("GetDependenciesAsync_EdgeRepoAndBranch_TypicalValues");
        yield return new TestCaseData(new string('r', 256), new string('b', 512))
            .SetName("GetDependenciesAsync_EdgeRepoAndBranch_VeryLongStrings");
        yield return new TestCaseData("file://C:/path/to/repo", "feature/αβγδ")
            .SetName("GetDependenciesAsync_EdgeRepoAndBranch_SpecialCharacters");
    }

    /// <summary>
    /// Verifies GetSourceDependencyAsync returns a fully parsed SourceDependency when the Version.Details.xml
    /// contains a valid <Source> element with all required attributes.
    /// Inputs:
    ///  - repoUri and branch strings.
    ///  - Version.Details.xml content including a Source element with Uri, Mapping, Sha, and BarId.
    /// Expected:
    ///  - A non-null SourceDependency with properties equal to the XML attributes.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetSourceDependencyAsync_SourceElementPresent_ReturnsParsedSource()
    {
        // Arrange
        var repoUri = "https://example.com/repo.git";
        var branch = "main";
        var expectedUri = "https://github.com/dotnet/arcade";
        var expectedMapping = "src/arcade";
        var expectedSha = "abc123def456";
        var expectedBarId = 123;

        var versionDetailsXml =
            $@"<Dependencies>
                   <Source Uri=""{expectedUri}"" Mapping=""{expectedMapping}"" Sha=""{expectedSha}"" BarId=""{expectedBarId}"" />
               </Dependencies>";

        var gitClient = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        gitClient
            .Setup(m => m.GetFileContentsAsync(It.IsAny<string>(), repoUri, branch))
            .ReturnsAsync(versionDetailsXml);

        var remote = new Remote(
            gitClient.Object,
            new VersionDetailsParser(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        var result = await remote.GetSourceDependencyAsync(repoUri, branch);

        // Assert
        result.Should().NotBeNull();
        result.Uri.Should().Be(expectedUri);
        result.Mapping.Should().Be(expectedMapping);
        result.Sha.Should().Be(expectedSha);
        result.BarId.Should().Be(expectedBarId);
    }

    /// <summary>
    /// Verifies GetSourceDependencyAsync returns null when the Version.Details.xml does not contain a <Source> element.
    /// Inputs:
    ///  - repoUri and branch strings.
    ///  - Version.Details.xml content without a Source element.
    /// Expected:
    ///  - Null result, indicating no source information present.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetSourceDependencyAsync_SourceElementMissing_ReturnsNull()
    {
        // Arrange
        var repoUri = "https://example.com/repo.git";
        var branch = "release/1.0";
        var versionDetailsXml = @"<Dependencies></Dependencies>";

        var gitClient = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        gitClient
            .Setup(m => m.GetFileContentsAsync(It.IsAny<string>(), repoUri, branch))
            .ReturnsAsync(versionDetailsXml);

        var remote = new Remote(
            gitClient.Object,
            new VersionDetailsParser(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        var result = await remote.GetSourceDependencyAsync(repoUri, branch);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies GetSourceDependencyAsync propagates parsing errors when the <Source> element is malformed
    /// (e.g., missing required attributes like Uri).
    /// Inputs:
    ///  - repoUri and branch strings.
    ///  - Version.Details.xml content with a Source element missing the Uri attribute.
    /// Expected:
    ///  - DarcException is thrown during parsing.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetSourceDependencyAsync_SourceElementMissingUri_ThrowsDarcException()
    {
        // Arrange
        var repoUri = "https://example.com/repo.git";
        var branch = "feature/x";
        var malformedXml =
            @"<Dependencies>
                  <Source Mapping=""map"" Sha=""deadbeef"" />
              </Dependencies>";

        var gitClient = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        gitClient
            .Setup(m => m.GetFileContentsAsync(It.IsAny<string>(), repoUri, branch))
            .ReturnsAsync(malformedXml);

        var remote = new Remote(
            gitClient.Object,
            new VersionDetailsParser(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        Func<Task> act = () => remote.GetSourceDependencyAsync(repoUri, branch);

        // Assert
        await act.Should().ThrowAsync<DarcException>();
    }

    /// <summary>
    /// Provides a diverse set of arguments (including empty strings, whitespace, special characters,
    /// long strings, different path styles, and null gitDirectory) to verify that CloneAsync forwards
    /// them exactly as provided to the underlying IRemoteGitRepo.CloneAsync call.
    /// </summary>
    public static IEnumerable<object[]> ValidCloneArgs()
    {
        yield return new object[] { "https://github.com/org/repo", "main", "/tmp/dir", true, null };
        yield return new object[] { "ssh://git@github.com/org/repo.git", "feature/xyz", "C:\\work\\repo", false, "C:\\work\\repo\\.git" };
        yield return new object[] { "", "", "", false, null };
        yield return new object[] { "https://example.com/r", "☃", " /weird path/with spaces ", true, "/custom/.git" };
        yield return new object[] { new string('r', 256), new string('c', 512), new string('t', 128), false, new string('g', 64) };
    }

    /// <summary>
    /// Verifies that CloneAsync delegates parameters to the underlying git client exactly as provided.
    /// Inputs:
    ///  - repoUri, commit, targetDirectory: various strings including empty, whitespace, special, and long values.
    ///  - checkoutSubmodules: both true and false.
    ///  - gitDirectory: either a path or null.
    /// Expected:
    ///  - IRemoteGitRepo.CloneAsync is invoked once with exactly the same parameters.
    ///  - No exception is thrown when the underlying call completes successfully.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(ValidCloneArgs))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CloneAsync_ValidInputs_DelegatesToRemoteGitClient(
        string repoUri,
        string commit,
        string targetDirectory,
        bool checkoutSubmodules,
        string gitDirectory)
    {
        // Arrange
        var remoteGitMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitMock
            .Setup(m => m.CloneAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Loose);
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Loose);
        var assetLocationResolver = new Mock<IAssetLocationResolver>(MockBehavior.Loose);
        var redisCacheClient = new Mock<IRedisCacheClient>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var remote = new Remote(
            remoteGitMock.Object,
            versionDetailsParser.Object,
            sourceMappingParser.Object,
            remoteFactory.Object,
            assetLocationResolver.Object,
            redisCacheClient.Object,
            logger.Object);

        // Act
        await remote.CloneAsync(repoUri, commit, targetDirectory, checkoutSubmodules, gitDirectory);

        // Assert
        remoteGitMock.Verify(
            m => m.CloneAsync(repoUri, commit, targetDirectory, checkoutSubmodules, gitDirectory),
            Times.Once);
        remoteGitMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Validates that CloneAsync propagates exceptions thrown by the underlying git client.
    /// Inputs:
    ///  - A set of parameters that causes the mocked IRemoteGitRepo.CloneAsync to throw InvalidOperationException.
    /// Expected:
    ///  - The same InvalidOperationException is observed by the caller.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CloneAsync_UnderlyingThrows_ExceptionIsPropagated()
    {
        // Arrange
        var repoUri = "https://example.com/repo";
        var commit = "commit-sha";
        var targetDirectory = "/tmp/dir";
        var checkoutSubmodules = true;
        var gitDirectory = (string)null;

        var remoteGitMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitMock
            .Setup(m => m.CloneAsync(repoUri, commit, targetDirectory, checkoutSubmodules, gitDirectory))
            .ThrowsAsync(new InvalidOperationException("Clone failed"));

        var versionDetailsParser = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var sourceMappingParser = new Mock<ISourceMappingParser>(MockBehavior.Loose);
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Loose);
        var assetLocationResolver = new Mock<IAssetLocationResolver>(MockBehavior.Loose);
        var redisCacheClient = new Mock<IRedisCacheClient>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var remote = new Remote(
            remoteGitMock.Object,
            versionDetailsParser.Object,
            sourceMappingParser.Object,
            remoteFactory.Object,
            assetLocationResolver.Object,
            redisCacheClient.Object,
            logger.Object);

        // Act + Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await remote.CloneAsync(repoUri, commit, targetDirectory, checkoutSubmodules, gitDirectory));

        remoteGitMock.Verify(
            m => m.CloneAsync(repoUri, commit, targetDirectory, checkoutSubmodules, gitDirectory),
            Times.Once);
        remoteGitMock.VerifyNoOtherCalls();
    }

    private static IEnumerable<TestCaseData> GetCommonScriptFilesAsync_PathCases()
    {
        // Case 1: relativeBasePath is null -> uses Constants.CommonScriptFilesPath
        yield return new TestCaseData(
            null,
            Constants.CommonScriptFilesPath
        ).SetName("GetCommonScriptFilesAsync_RelativeBasePathNull_UsesDefaultEngCommonPath");

        // Case 2: relativeBasePath provided -> combines base with Constants.CommonScriptFilesPath
        var basePath = new NativePath("repoRoot");
        string combined = basePath / Constants.CommonScriptFilesPath;
        yield return new TestCaseData(
            (LocalPath)basePath,
            combined
        ).SetName("GetCommonScriptFilesAsync_RelativeBasePathProvided_CombinesWithEngCommon");
    }

    /// <summary>
    /// Verifies that GetCommonScriptFilesAsync computes the correct path and forwards the call to IRemoteGitRepo,
    /// returning exactly the files provided by the client.
    /// Inputs:
    ///  - repoUri and commit arbitrary strings.
    ///  - relativeBasePath either null (default path) or a LocalPath (combined path).
    /// Expected:
    ///  - IRemoteGitRepo.GetFilesAtCommitAsync is invoked once with the computed path.
    ///  - The returned List<GitFile> is the same instance as provided by the mock.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(GetCommonScriptFilesAsync_PathCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetCommonScriptFilesAsync_PathComputationAndReturn_ExpectedBehavior(LocalPath relativeBasePath, string expectedPath)
    {
        // Arrange
        var repoUri = "https://example/repo";
        var commit = "abc123";

        var remoteGitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var versionDetailsParserMock = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Loose);
        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Loose);
        var assetLocationResolverMock = new Mock<IAssetLocationResolver>(MockBehavior.Loose);
        var redisCacheClientMock = new Mock<IRedisCacheClient>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var filesFromClient = new List<GitFile>
        {
            new GitFile($"{Constants.EngFolderName}/common/file1.ps1", "echo 1"),
            new GitFile($"{Constants.EngFolderName}/common/file2.sh", "echo 2")
        };

        remoteGitRepoMock
            .Setup(m => m.GetFilesAtCommitAsync(
                repoUri,
                commit,
                expectedPath))
            .ReturnsAsync(filesFromClient);

        var remote = new Remote(
            remoteGitRepoMock.Object,
            versionDetailsParserMock.Object,
            sourceMappingParserMock.Object,
            remoteFactoryMock.Object,
            assetLocationResolverMock.Object,
            redisCacheClientMock.Object,
            loggerMock.Object);

        // Act
        var result = await remote.GetCommonScriptFilesAsync(repoUri, commit, relativeBasePath);

        // Assert
        remoteGitRepoMock.Verify(m => m.GetFilesAtCommitAsync(repoUri, commit, expectedPath), Times.Once);
        remoteGitRepoMock.VerifyNoOtherCalls();

        if (!object.ReferenceEquals(filesFromClient, result))
        {
            throw new AssertionException("The returned list instance is not the same as provided by the client.");
        }
    }

    /// <summary>
    /// Ensures that if the underlying IRemoteGitRepo throws during GetFilesAtCommitAsync,
    /// the exception is not swallowed and is propagated to the caller.
    /// Inputs:
    ///  - A valid repoUri, commit, and null relativeBasePath.
    /// Expected:
    ///  - An InvalidOperationException is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetCommonScriptFilesAsync_RemoteThrows_PropagatesException()
    {
        // Arrange
        var repoUri = "https://example/repo";
        var commit = "deadbeef";
        var relativeBasePath = (LocalPath)null;

        var remoteGitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var versionDetailsParserMock = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Loose);
        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Loose);
        var assetLocationResolverMock = new Mock<IAssetLocationResolver>(MockBehavior.Loose);
        var redisCacheClientMock = new Mock<IRedisCacheClient>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        remoteGitRepoMock
            .Setup(m => m.GetFilesAtCommitAsync(
                repoUri,
                commit,
                Constants.CommonScriptFilesPath))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var remote = new Remote(
            remoteGitRepoMock.Object,
            versionDetailsParserMock.Object,
            sourceMappingParserMock.Object,
            remoteFactoryMock.Object,
            assetLocationResolverMock.Object,
            redisCacheClientMock.Object,
            loggerMock.Object);

        // Act + Assert
        try
        {
            await remote.GetCommonScriptFilesAsync(repoUri, commit, relativeBasePath);
            throw new AssertionException("Expected InvalidOperationException to be thrown, but no exception was thrown.");
        }
        catch (InvalidOperationException)
        {
            // Expected path
        }

        remoteGitRepoMock.Verify(m => m.GetFilesAtCommitAsync(repoUri, commit, Constants.CommonScriptFilesPath), Times.Once);
        remoteGitRepoMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that GetPullRequestCommentsAsync forwards the input URL to the underlying IRemoteGitRepo
    /// and returns exactly the collection provided by the dependency.
    /// Inputs:
    ///  - pullRequestUrl: null, empty, whitespace, very long, special/unicode, typical GitHub URL.
    ///  - clientResult: null, empty list, single-item, multi-item with duplicates.
    /// Expected:
    ///  - The underlying client's GetPullRequestCommentsAsync is invoked exactly once with the same URL.
    ///  - The returned value equals the dependency's result (including null).
    /// </summary>
    [Test]
    [TestCaseSource(nameof(GetPullRequestCommentsAsync_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetPullRequestCommentsAsync_ForwardsInputAndReturnsClientResult(string pullRequestUrl, List<string> clientResult)
    {
        // Arrange
        var remoteGitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitRepoMock
            .Setup(m => m.GetPullRequestCommentsAsync(pullRequestUrl))
            .ReturnsAsync(clientResult);

        var remote = new Remote(
            remoteGitRepoMock.Object,
            Mock.Of<IVersionDetailsParser>(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        var result = await remote.GetPullRequestCommentsAsync(pullRequestUrl);

        // Assert
        remoteGitRepoMock.Verify(m => m.GetPullRequestCommentsAsync(pullRequestUrl), Times.Once);

        if (!ReferenceEquals(result, clientResult))
        {
            if (clientResult == null || result == null)
            {
                throw new Exception("Expected null result to propagate from dependency.");
            }

            if (result.Count != clientResult.Count || !result.SequenceEqual(clientResult))
            {
                throw new Exception("Result sequence does not match dependency output.");
            }
        }
    }

    /// <summary>
    /// Ensures that GetPullRequestCommentsAsync propagates exceptions thrown by the underlying IRemoteGitRepo
    /// without swallowing or altering them.
    /// Inputs:
    ///  - pullRequestUrl that causes the dependency to throw.
    /// Expected:
    ///  - The same exception type and message are observed by the caller.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetPullRequestCommentsAsync_PropagatesException()
    {
        // Arrange
        const string url = "https://github.com/org/repo/pull/999";
        const string expectedMessage = "Dependency failure";
        var remoteGitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitRepoMock
            .Setup(m => m.GetPullRequestCommentsAsync(url))
            .ThrowsAsync(new InvalidOperationException(expectedMessage));

        var remote = new Remote(
            remoteGitRepoMock.Object,
            Mock.Of<IVersionDetailsParser>(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        try
        {
            await remote.GetPullRequestCommentsAsync(url);
            throw new Exception("Expected exception was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            // Assert
            if (!string.Equals(ex.Message, expectedMessage, StringComparison.Ordinal))
            {
                throw new Exception("Exception message mismatch.");
            }
        }
    }

    private static IEnumerable GetPullRequestCommentsAsync_Cases()
    {
        // null URL with null result
        yield return new TestCaseData(null, null);

        // empty URL with empty result
        yield return new TestCaseData(string.Empty, new List<string>());

        // whitespace URL with single-item result
        yield return new TestCaseData("   ", new List<string> { "one" });

        // typical GitHub URL with multi-item result
        yield return new TestCaseData(
            "https://github.com/org/repo/pull/123",
            new List<string> { "LGTM", "Looks good", "Thanks!" });

        // URL with special/unicode characters
        yield return new TestCaseData(
            "https://example.com/pr/%F0%9F%98%80\t\n",
            new List<string> { "🚀", "", "duplicate", "duplicate" });

        // very long URL
        var veryLong = "https://example.com/" + new string('a', 8192);
        yield return new TestCaseData(veryLong, new List<string>());
    }

    /// <summary>
    /// Validates that GetFileContentsAsync forwards the provided parameters (filePath, repoUri, branch)
    /// to the underlying IRemoteGitRepo and returns exactly the content returned by that client.
    /// Inputs are parameterized to cover typical, empty, whitespace, special characters, and long strings.
    /// Expected: The result equals the client-returned content and the client is invoked exactly once with the same parameters.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(GetFileContentsAsync_ValidInputCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetFileContentsAsync_ValidInputs_ForwardsAndReturnsResult(string filePath, string repoUri, string branch)
    {
        // Arrange
        var gitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var versionParserMock = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Loose);
        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Loose);
        var locationResolverMock = new Mock<IAssetLocationResolver>(MockBehavior.Loose);
        var cacheClientMock = new Mock<IRedisCacheClient>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var expectedContent = $"content::{filePath}::{repoUri}::{branch}";

        gitRepoMock
            .Setup(m => m.GetFileContentsAsync(filePath, repoUri, branch))
            .ReturnsAsync(expectedContent);

        var remote = new Remote(
            gitRepoMock.Object,
            versionParserMock.Object,
            sourceMappingParserMock.Object,
            remoteFactoryMock.Object,
            locationResolverMock.Object,
            cacheClientMock.Object,
            loggerMock.Object);

        // Act
        var result = await remote.GetFileContentsAsync(filePath, repoUri, branch);

        // Assert
        result.Should().Be(expectedContent);
        gitRepoMock.Verify(m => m.GetFileContentsAsync(filePath, repoUri, branch), Times.Once);
    }

    /// <summary>
    /// Ensures that exceptions thrown by the underlying IRemoteGitRepo.GetFileContentsAsync
    /// are not swallowed by Remote.GetFileContentsAsync and are propagated to the caller.
    /// Inputs:
    ///  - Valid strings for filePath, repoUri, and branch.
    /// Expected:
    ///  - The same exception type (InvalidOperationException) is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetFileContentsAsync_WhenClientThrows_ExceptionIsPropagated()
    {
        // Arrange
        var filePath = "src/dir/file.txt";
        var repoUri = "https://github.com/org/repo";
        var branch = "feature/branch-α";

        var gitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var versionParserMock = new Mock<IVersionDetailsParser>(MockBehavior.Loose);
        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Loose);
        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Loose);
        var locationResolverMock = new Mock<IAssetLocationResolver>(MockBehavior.Loose);
        var cacheClientMock = new Mock<IRedisCacheClient>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        gitRepoMock
            .Setup(m => m.GetFileContentsAsync(filePath, repoUri, branch))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var remote = new Remote(
            gitRepoMock.Object,
            versionParserMock.Object,
            sourceMappingParserMock.Object,
            remoteFactoryMock.Object,
            locationResolverMock.Object,
            cacheClientMock.Object,
            loggerMock.Object);

        // Act
        Func<Task> act = () => remote.GetFileContentsAsync(filePath, repoUri, branch);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        gitRepoMock.Verify(m => m.GetFileContentsAsync(filePath, repoUri, branch), Times.Once);
    }

    private static IEnumerable<TestCaseData> GetFileContentsAsync_ValidInputCases()
    {
        yield return new TestCaseData("dir/file.txt", "https://github.com/org/repo", "main")
            .SetName("TypicalInputs");
        yield return new TestCaseData(string.Empty, string.Empty, string.Empty)
            .SetName("EmptyStrings");
        yield return new TestCaseData(" path with spaces/üñí©ødé.txt ", "ssh://git@github.com/org/repo.git?x=1&y=ä", "feature/issue-123_α")
            .SetName("WhitespaceAndUnicodeAndSpecialUri");
        yield return new TestCaseData(new string('a', 2048), "https://example.com/" + new string('b', 256), new string('c', 128))
            .SetName("VeryLongStrings");
        yield return new TestCaseData("weird/:*?<>|chars.txt", "file:///C:/temp/repo", "rel/1.0")
            .SetName("SpecialCharactersInPathAndBranch");
    }
}



[TestFixture]
public class RemoteDeleteBranchAsyncTests
{
    /// <summary>
    /// Verifies that DeleteBranchAsync logs an informational message and delegates to IRemoteGitRepo.DeleteBranchAsync
    /// for a variety of string inputs, including empty, whitespace, special characters, and long values.
    /// Inputs:
    ///  - repoUri and branch with diverse string cases (non-null): normal, empty, whitespace-only, special characters, long strings.
    /// Expected:
    ///  - ILogger.Log called once with LogLevel.Information and a message containing the repoUri and branch.
    ///  - IRemoteGitRepo.DeleteBranchAsync invoked once with the exact repoUri and branch.
    /// </summary>
    [TestCaseSource(nameof(DeleteBranchAsync_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task DeleteBranchAsync_VariousInputs_DelegatesAndLogs(string repoUri, string branch)
    {
        // Arrange
        var gitClientMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);

        var loggerMock = new Mock<ILogger>(MockBehavior.Strict);
        loggerMock
            .Setup(l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()));

        gitClientMock
            .Setup(m => m.DeleteBranchAsync(repoUri, branch))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var remote = CreateRemote(gitClientMock.Object, loggerMock.Object);

        // Act
        await remote.DeleteBranchAsync(repoUri, branch);

        // Assert
        gitClientMock.Verify(m => m.DeleteBranchAsync(repoUri, branch), Times.Once);

        loggerMock.Verify(l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString().Contains("Deleting branch", StringComparison.Ordinal) &&
                    v.ToString().Contains(branch, StringComparison.Ordinal) &&
                    v.ToString().Contains(repoUri, StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()),
            Times.Once);
    }

    private static Remote CreateRemote(IRemoteGitRepo client, ILogger logger)
    {
        return new Remote(
            client,
            Mock.Of<IVersionDetailsParser>(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            logger);
    }

    private static IEnumerable<TestCaseData> DeleteBranchAsync_Cases()
    {
        var veryLong = new string('r', 2048);
        var veryLongBranch = new string('b', 2048);

        yield return new TestCaseData("https://github.com/org/repo", "main").SetName("DeleteBranchAsync_NormalInputs_DelegatesAndLogs");
        yield return new TestCaseData("", "").SetName("DeleteBranchAsync_EmptyStrings_DelegatesAndLogs");
        yield return new TestCaseData("   ", "   ").SetName("DeleteBranchAsync_WhitespaceOnly_DelegatesAndLogs");
        yield return new TestCaseData("ssh://git@github.com:org/repo.git", "refs/heads/release/8.0").SetName("DeleteBranchAsync_SshUriAndRefHeads_DelegatesAndLogs");
        yield return new TestCaseData("https://example.com/repo?x=1&y=2", "feature/awesome-branch").SetName("DeleteBranchAsync_QueryStringInUri_DelegatesAndLogs");
        yield return new TestCaseData("/path/with/special/!@#$%^&*()", "branch-with-üŋïçødê").SetName("DeleteBranchAsync_SpecialCharacters_DelegatesAndLogs");
        yield return new TestCaseData(veryLong, veryLongBranch).SetName("DeleteBranchAsync_VeryLongStrings_DelegatesAndLogs");
    }
}



[TestFixture]
public class RemoteBranchExistsAsyncTests
{
    private static IEnumerable<TestCaseData> BranchExistsAsync_ValidCases()
    {
        yield return new TestCaseData("https://github.com/org/repo", "main", true)
            .SetName("BranchExistsAsync_StandardInputs_ReturnsTrue");
        yield return new TestCaseData("ssh://git@github.com:org/repo.git", "refs/heads/release/1.0", false)
            .SetName("BranchExistsAsync_RefsHeadsNotation_ReturnsFalse");
        yield return new TestCaseData("", "", true)
            .SetName("BranchExistsAsync_EmptyRepoAndBranch_ReturnsTrue");
        yield return new TestCaseData("https://example.com/with space", "feature/foo-bar", false)
            .SetName("BranchExistsAsync_UriWithSpaceAndFeatureBranch_ReturnsFalse");
        yield return new TestCaseData("https://github.com/org/repo", "hotfix/bug#123?*", true)
            .SetName("BranchExistsAsync_SpecialCharactersInBranch_ReturnsTrue");

        var longUri = "https://host/" + new string('a', 2050);
        var longBranch = new string('b', 1024);
        yield return new TestCaseData(longUri, longBranch, false)
            .SetName("BranchExistsAsync_VeryLongInputs_ReturnsFalse");
    }

    /// <summary>
    /// Verifies that BranchExistsAsync forwards the provided repoUri and branch to IRemoteGitRepo.DoesBranchExistAsync
    /// and returns the exact result from the dependency.
    /// Inputs:
    ///  - Diverse combinations of repoUri and branch including empty strings, special characters, and very long strings.
    /// Expected:
    ///  - The returned boolean equals the mocked dependency's result.
    ///  - IRemoteGitRepo.DoesBranchExistAsync is invoked exactly once with the same repoUri and branch.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(BranchExistsAsync_ValidCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task BranchExistsAsync_VariousInputs_ForwardsParametersAndReturnsExpected(string repoUri, string branch, bool expected)
    {
        // Arrange
        var remoteGitRepo = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitRepo
            .Setup(m => m.DoesBranchExistAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(expected);

        var remote = new Remote(
            remoteGitRepo.Object,
            new VersionDetailsParser(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        var result = await remote.BranchExistsAsync(repoUri, branch);

        // Assert
        result.Should().Be(expected);
        remoteGitRepo.Verify(m => m.DoesBranchExistAsync(repoUri, branch), Times.Once);
        remoteGitRepo.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures that BranchExistsAsync propagates exceptions thrown by the underlying IRemoteGitRepo.
    /// Inputs:
    ///  - A valid repoUri and branch for which the dependency throws InvalidOperationException.
    /// Expected:
    ///  - The same InvalidOperationException is thrown by BranchExistsAsync.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task BranchExistsAsync_RemoteThrows_ExceptionIsPropagated()
    {
        // Arrange
        var repoUri = "https://github.com/org/repo";
        var branch = "bugfix/#123";
        var remoteGitRepo = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        remoteGitRepo
            .Setup(m => m.DoesBranchExistAsync(repoUri, branch))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var remote = new Remote(
            remoteGitRepo.Object,
            new VersionDetailsParser(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        Func<Task> act = async () => await remote.BranchExistsAsync(repoUri, branch);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        remoteGitRepo.Verify(m => m.DoesBranchExistAsync(repoUri, branch), Times.Once);
        remoteGitRepo.VerifyNoOtherCalls();
    }
}



[TestFixture]
public class Remote_GetPullRequestChecksAsync_Tests
{
    private static IEnumerable<TestCaseData> PullRequestUrlCases()
    {
        yield return new TestCaseData("https://github.com/owner/repo/pull/1")
            .SetName("GetPullRequestChecksAsync_ValidUrl_ForwardsCallAndReturnsResult");
        yield return new TestCaseData("")
            .SetName("GetPullRequestChecksAsync_EmptyString_ForwardsCallAndReturnsResult");
        yield return new TestCaseData("   ")
            .SetName("GetPullRequestChecksAsync_WhitespaceString_ForwardsCallAndReturnsResult");
        yield return new TestCaseData(new string('a', 2048))
            .SetName("GetPullRequestChecksAsync_VeryLongString_ForwardsCallAndReturnsResult");
        yield return new TestCaseData("http://example.com/pull/1?x=1&y=2#frag_%$!*()[]{}")
            .SetName("GetPullRequestChecksAsync_SpecialCharacters_ForwardsCallAndReturnsResult");
    }

    /// <summary>
    /// Verifies that GetPullRequestChecksAsync logs an informational message, forwards the exact URL
    /// to the underlying IRemoteGitRepo.GetPullRequestChecksAsync, and returns the same result instance.
    /// Inputs:
    ///  - pullRequestUrl: exercised with typical, empty, whitespace-only, very long, and special-character strings.
    /// Expected:
    ///  - ILogger.Log is called once with LogLevel.Information and the formatted message containing the URL.
    ///  - IRemoteGitRepo.GetPullRequestChecksAsync is called once with the exact same URL.
    ///  - The returned IEnumerable{Check} instance is the same as the mock's return value.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(PullRequestUrlCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetPullRequestChecksAsync_VariousInputs_ForwardsAndReturns(string pullRequestUrl)
    {
        // Arrange
        var remoteGitClientMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var expected = new List<Check>();
        remoteGitClientMock
            .Setup(m => m.GetPullRequestChecksAsync(pullRequestUrl))
            .ReturnsAsync(expected);

        var remote = new Remote(
            remoteGitClientMock.Object,
            Mock.Of<IVersionDetailsParser>(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            loggerMock.Object);

        // Act
        var result = await remote.GetPullRequestChecksAsync(pullRequestUrl);

        // Assert
        remoteGitClientMock.Verify(m => m.GetPullRequestChecksAsync(pullRequestUrl), Times.Once);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString() == $"Getting status checks for pull request '{pullRequestUrl}'..."),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);

        if (!object.ReferenceEquals(result, expected))
        {
            throw new Exception("The method did not return the exact IEnumerable<Check> instance provided by IRemoteGitRepo.");
        }
    }

    /// <summary>
    /// Ensures that exceptions thrown by the underlying IRemoteGitRepo.GetPullRequestChecksAsync are not swallowed
    /// and are propagated by Remote.GetPullRequestChecksAsync, while still logging the informational message.
    /// Inputs:
    ///  - pullRequestUrl: a typical URL.
    /// Expected:
    ///  - InvalidOperationException is thrown with the original message.
    ///  - ILogger.Log is called once with LogLevel.Information.
    ///  - IRemoteGitRepo.GetPullRequestChecksAsync is invoked once with the same URL.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetPullRequestChecksAsync_RemoteThrows_ExceptionIsPropagated()
    {
        // Arrange
        var pullRequestUrl = "https://github.com/owner/repo/pull/42";

        var remoteGitClientMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var boom = new InvalidOperationException("boom");
        remoteGitClientMock
            .Setup(m => m.GetPullRequestChecksAsync(pullRequestUrl))
            .ThrowsAsync(boom);

        var remote = new Remote(
            remoteGitClientMock.Object,
            Mock.Of<IVersionDetailsParser>(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            loggerMock.Object);

        // Act
        bool threw = false;
        try
        {
            await remote.GetPullRequestChecksAsync(pullRequestUrl);
        }
        catch (InvalidOperationException ex)
        {
            threw = true;
            if (ex.Message != "boom")
            {
                throw;
            }
        }

        // Assert
        if (!threw)
        {
            throw new Exception("Expected InvalidOperationException was not thrown.");
        }

        remoteGitClientMock.Verify(m => m.GetPullRequestChecksAsync(pullRequestUrl), Times.Once);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString() == $"Getting status checks for pull request '{pullRequestUrl}'..."),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }
}



/// <summary>
/// Tests for Remote.GetPullRequestReviewsAsync, validating logging, pass-through behavior, and error propagation.
/// </summary>
[TestFixture]
public class Remote_GetPullRequestReviewsAsync_Tests
{
    /// <summary>
    /// Provides diverse input scenarios for GetPullRequestReviewsAsync:
    ///  - pullRequestUrl: null, empty, whitespace, long, typical URL, and special characters.
    ///  - expectedReviews: collections with 0..N items across different ReviewState values.
    /// </summary>
    public static IEnumerable GetPullRequestReviewsAsync_ReturnsUnderlyingResult_Cases
    {
        get
        {
            yield return new TestCaseData(
                null,
                new List<Review> { new Review(ReviewState.Approved, "u1") })
                .SetName("GetPullRequestReviewsAsync_NullUrl_PassesThroughAndReturnsClientResult");

            yield return new TestCaseData(
                string.Empty,
                new List<Review>())
                .SetName("GetPullRequestReviewsAsync_EmptyUrl_PassesThroughAndReturnsClientResult");

            yield return new TestCaseData(
                " ",
                new List<Review> { new Review(ReviewState.Commented, "u2") })
                .SetName("GetPullRequestReviewsAsync_WhitespaceUrl_PassesThroughAndReturnsClientResult");

            yield return new TestCaseData(
                new string('a', 2048),
                new List<Review>
                {
                    new Review(ReviewState.ChangesRequested, "u3"),
                    new Review(ReviewState.Pending, "u4")
                })
                .SetName("GetPullRequestReviewsAsync_VeryLongUrl_PassesThroughAndReturnsClientResult");

            yield return new TestCaseData(
                "https://github.com/org/repo/pull/123",
                new List<Review>
                {
                    new Review(ReviewState.Approved, "https://rev/1"),
                    new Review(ReviewState.Rejected, "https://rev/2"),
                    new Review(ReviewState.Commented, "https://rev/3")
                })
                .SetName("GetPullRequestReviewsAsync_TypicalUrl_PassesThroughAndReturnsClientResult");

            yield return new TestCaseData(
                "https://host/路径?x=ß&y=ℝ",
                new List<Review> { new Review(ReviewState.Pending, "https://rev/special") })
                .SetName("GetPullRequestReviewsAsync_SpecialCharactersUrl_PassesThroughAndReturnsClientResult");
        }
    }

}



[TestFixture]
public class RemoteGetPackageSourcesAsyncTests
{
    /// <summary>
    /// Ensures that when nuget.config contains a mix of valid and invalid packageSources entries,
    /// only entries with both 'key' and 'value' attributes are returned, preserving the original order.
    /// Inputs:
    ///  - repoUri and commit values (including empty/whitespace variations).
    ///  - nuget.config XML with valid entries, missing key/value attributes, and special characters.
    /// Expected:
    ///  - The returned sequence contains only the 'value' attributes from valid entries, in order.
    /// </summary>
    [TestCase("https://github.com/org/repo", "abc123")]
    [TestCase("", " ")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetPackageSourcesAsync_NugetConfigContainsValidAndInvalidEntries_ReturnsOnlyFeedsInOrder(string repoUri, string commit)
    {
        // Arrange
        var nugetConfigXml =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
              <configuration>
                <packageSources>
                  <add key=""nuget.org"" value=""https://api.nuget.org/v3/index.json"" />
                  <add key=""local"" value=""C:\pkgs"" />
                  <add key=""missingvalue"" keyx=""abc"" />
                  <add value=""https://contoso.invalid"" />
                  <add key=""myfeed"" value=""https://example.com/feed?param=1&amp;x=a%20b"" />
                  <add key=""dup"" value=""v1"" />
                  <add key=""dup"" value=""v2"" />
                  <add key=""spaced"" value=""  https://space  "" />
                  <add key=""unicode"" value=""https://例子.测试/包"" />
                </packageSources>
              </configuration>";

        var gitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        gitRepoMock
            .Setup(x => x.GetFileContentsAsync(It.IsAny<string>(), repoUri, commit))
            .ReturnsAsync(nugetConfigXml);

        var remote = new Remote(
            gitRepoMock.Object,
            new VersionDetailsParser(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        var expected =
            new List<string>
            {
                "https://api.nuget.org/v3/index.json",
                @"C:\pkgs",
                "https://example.com/feed?param=1&x=a%20b",
                "v1",
                "v2",
                "  https://space  ",
                "https://例子.测试/包"
            };

        // Act
        var result = await remote.GetPackageSourcesAsync(repoUri, commit);
        var feeds = result.ToList();

        // Assert
        feeds.Should().Equal(expected);
    }

    /// <summary>
    /// Validates that when nuget.config contains no valid packageSources entries,
    /// the method returns an empty sequence.
    /// Inputs:
    ///  - A nuget.config XML with no 'add' elements containing both 'key' and 'value' attributes.
    /// Expected:
    ///  - An empty IEnumerable<string> is returned.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetPackageSourcesAsync_NoValidEntries_ReturnsEmpty()
    {
        // Arrange
        var repoUri = "https://github.com/org/repo";
        var commit = "def456";

        var nugetConfigXml =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
              <configuration>
                <packageSources>
                  <add value=""https://only-value.invalid"" />
                  <add key=""only-key"" />
                  <packageSources /> <!-- empty -->
                </packageSources>
              </configuration>";

        var gitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        gitRepoMock
            .Setup(x => x.GetFileContentsAsync(It.IsAny<string>(), repoUri, commit))
            .ReturnsAsync(nugetConfigXml);

        var remote = new Remote(
            gitRepoMock.Object,
            new VersionDetailsParser(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        var result = await remote.GetPackageSourcesAsync(repoUri, commit);
        var feeds = result.ToList();

        // Assert
        feeds.Should().BeEmpty();
    }

    /// <summary>
    /// Ensures that when nuget.config cannot be found in the repository (all lookups fail),
    /// the method throws DependencyFileNotFoundException.
    /// Inputs:
    ///  - Any repoUri and commit.
    ///  - IRemoteGitRepo.GetFileContentsAsync throws DependencyFileNotFoundException for all file paths.
    /// Expected:
    ///  - DependencyFileNotFoundException is thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetPackageSourcesAsync_NugetConfigMissing_ThrowsDependencyFileNotFoundException()
    {
        // Arrange
        var repoUri = "https://github.com/org/repo";
        var commit = "missing-config-sha";

        var gitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        gitRepoMock
            .Setup(x => x.GetFileContentsAsync(It.IsAny<string>(), repoUri, commit))
            .ThrowsAsync(new DependencyFileNotFoundException("not found"));

        var remote = new Remote(
            gitRepoMock.Object,
            new VersionDetailsParser(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        Func<Task> act = () => remote.GetPackageSourcesAsync(repoUri, commit);

        // Assert
        await act.Should().ThrowAsync<DependencyFileNotFoundException>();
    }
}



[TestFixture]
public class RemoteCommentPullRequestAsyncTests
{
    private static IEnumerable<TestCaseData> ForwardingCases()
    {
        yield return new TestCaseData("https://example.com/repo/pull/1", "LGTM");
        yield return new TestCaseData("", "");
        yield return new TestCaseData("   ", "   ");
        yield return new TestCaseData("https://github.com/org/repo/pull/123?query=%20%26", "Emoji 🚀🔥 and newline\nand tab\t");
        yield return new TestCaseData("ssh://git@github.com/org/repo/pull/456", new string('x', 5000));
    }

    /// <summary>
    /// Ensures that CommentPullRequestAsync forwards the exact parameters to IRemoteGitRepo.CommentPullRequestAsync
    /// and awaits the call successfully.
    /// Inputs:
    ///  - Various pullRequestUri and comment combinations including empty, whitespace, special characters, and very long strings.
    /// Expected:
    ///  - The underlying IRemoteGitRepo.CommentPullRequestAsync is invoked exactly once with the same parameters.
    ///  - No exception is thrown for these inputs.
    /// </summary>
    [TestCaseSource(nameof(ForwardingCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CommentPullRequestAsync_ParametersForwarded_CallsClientExactlyOnce(string pullRequestUri, string comment)
    {
        // Arrange
        var gitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var versionDetailsParserMock = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var assetLocationResolverMock = new Mock<IAssetLocationResolver>(MockBehavior.Strict);
        var cacheMock = new Mock<IRedisCacheClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        gitRepoMock
            .Setup(m => m.CommentPullRequestAsync(pullRequestUri, comment))
            .Returns(Task.CompletedTask);

        var remote = new Remote(
            gitRepoMock.Object,
            versionDetailsParserMock.Object,
            sourceMappingParserMock.Object,
            remoteFactoryMock.Object,
            assetLocationResolverMock.Object,
            cacheMock.Object,
            loggerMock.Object);

        // Act
        await remote.CommentPullRequestAsync(pullRequestUri, comment);

        // Assert
        gitRepoMock.Verify(m => m.CommentPullRequestAsync(pullRequestUri, comment), Times.Once);
        gitRepoMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that exceptions thrown by the underlying IRemoteGitRepo.CommentPullRequestAsync
    /// are propagated by Remote.CommentPullRequestAsync without being swallowed.
    /// Inputs:
    ///  - Valid pullRequestUri and comment causing downstream to throw.
    /// Expected:
    ///  - The same exception type is thrown with the same message.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CommentPullRequestAsync_DownstreamThrows_ExceptionIsPropagated()
    {
        // Arrange
        var gitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        var versionDetailsParserMock = new Mock<IVersionDetailsParser>(MockBehavior.Strict);
        var sourceMappingParserMock = new Mock<ISourceMappingParser>(MockBehavior.Strict);
        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var assetLocationResolverMock = new Mock<IAssetLocationResolver>(MockBehavior.Strict);
        var cacheMock = new Mock<IRedisCacheClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var pullRequestUri = "https://example.com/repo/pull/42";
        var comment = "trigger error";
        var expected = new InvalidOperationException("boom");

        gitRepoMock
            .Setup(m => m.CommentPullRequestAsync(pullRequestUri, comment))
            .ThrowsAsync(expected);

        var remote = new Remote(
            gitRepoMock.Object,
            versionDetailsParserMock.Object,
            sourceMappingParserMock.Object,
            remoteFactoryMock.Object,
            assetLocationResolverMock.Object,
            cacheMock.Object,
            loggerMock.Object);

        // Act + Assert (manual to avoid non-specified assertion frameworks)
        try
        {
            await remote.CommentPullRequestAsync(pullRequestUri, comment);
            throw new Exception("Expected exception was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            if (!string.Equals(ex.Message, expected.Message, StringComparison.Ordinal))
            {
                throw;
            }
        }

        gitRepoMock.Verify(m => m.CommentPullRequestAsync(pullRequestUri, comment), Times.Once);
        gitRepoMock.VerifyNoOtherCalls();
    }
}



[TestFixture]
public class RemoteGetSourceManifestAsyncTests
{
    /// <summary>
    /// Ensures that GetSourceManifestAsync forwards the correct default manifest path and parameters to the git client
    /// and properly parses a minimal valid JSON payload into an empty SourceManifest.
    /// Inputs:
    ///  - vmrUri and branchOrCommit combinations including normal, empty, and special-character inputs.
    /// Expected:
    ///  - _remoteGitClient.GetFileContentsAsync is called once with VmrInfo.DefaultRelativeSourceManifestPath, vmrUri, branchOrCommit.
    ///  - The returned SourceManifest is not null and contains empty Repositories and Submodules collections.
    /// </summary>
    [TestCase("https://github.com/org/repo", "main")]
    [TestCase("", "")]
    [TestCase("ssh://git@example.com:repo.git", "feature/Ä-测试")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetSourceManifestAsync_ValidJson_ParsesAndUsesDefaultPath(string vmrUri, string branchOrCommit)
    {
        // Arrange
        var gitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        gitRepoMock
            .Setup(m => m.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("{}");

        var remote = new Remote(
            gitRepoMock.Object,
            Mock.Of<IVersionDetailsParser>(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act
        var manifest = await remote.GetSourceManifestAsync(vmrUri, branchOrCommit);

        // Assert
        gitRepoMock.Verify(
            m => m.GetFileContentsAsync(
                It.Is<string>(p => p == VmrInfo.DefaultRelativeSourceManifestPath.ToString()),
                vmrUri,
                branchOrCommit),
            Times.Once);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest.Repositories.Count, Is.EqualTo(0));
        Assert.That(manifest.Submodules.Count, Is.EqualTo(0));
    }

    /// <summary>
    /// Validates that GetSourceManifestAsync throws a JsonException when the retrieved file content is invalid JSON.
    /// Inputs:
    ///  - Arbitrary vmrUri and branchOrCommit values.
    /// Expected:
    ///  - A JsonException is thrown due to invalid source-manifest JSON payload.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetSourceManifestAsync_InvalidJson_ThrowsJsonException()
    {
        // Arrange
        var gitRepoMock = new Mock<IRemoteGitRepo>(MockBehavior.Strict);
        gitRepoMock
            .Setup(m => m.GetFileContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("not valid json");

        var remote = new Remote(
            gitRepoMock.Object,
            Mock.Of<IVersionDetailsParser>(),
            Mock.Of<ISourceMappingParser>(),
            Mock.Of<IRemoteFactory>(),
            Mock.Of<IAssetLocationResolver>(),
            Mock.Of<IRedisCacheClient>(),
            Mock.Of<ILogger>());

        // Act + Assert
        Assert.ThrowsAsync<JsonException>(() => remote.GetSourceManifestAsync("repo", "branch"));
    }
}



[TestFixture]
public class RemoteGetSourceMappingsAsyncTests
{
    /// <summary>
    /// Provides diverse inputs for GetSourceMappingsAsync:
    ///  - Typical URL/branch with minimal JSON.
    ///  - Empty vmrUri/branch and whitespace file content.
    ///  - Long and special-character inputs to exercise boundary handling and pass-through behavior.
    /// </summary>
    public static IEnumerable<TestCaseData> GetSourceMappingsAsync_TestCases()
    {
        yield return new TestCaseData(
            "https://example.com/vmr.git",
            "main",
            "{ }",
            2
        ).SetName("TypicalInputs_MinimalJson_ReturnsTwoMappings");

        yield return new TestCaseData(
            string.Empty,
            string.Empty,
            "   ",
            0
        ).SetName("EmptyRepoAndBranch_WhitespaceContent_ReturnsEmpty");

        yield return new TestCaseData(
            "ssh://git@host:2222/repo/vmr",
            "feature/special-ß-µ-漢字",
            "{ \"m\": [{\"a\":1}]\n}",
            1
        ).SetName("SshUri_SpecialCharsInBranch_ReturnsOneMapping");

        yield return new TestCaseData(
            new string('u', 1024),
            new string('b', 512),
            "{\"a\":\"" + new string('x', 256) + "\"}",
            3
        ).SetName("VeryLongInputs_LongJson_ReturnsThreeMappings");

        yield return new TestCaseData(
            "file:///C:/vmr",
            "release/1.0",
            "{\n\t\"mappings\":[{\"name\":\"A\\u0001\"}]\n}",
            1
        ).SetName("FileUri_ControlCharInJson_ReturnsOneMapping");
    }

}
