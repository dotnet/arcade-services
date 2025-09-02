// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LibGit2Sharp;
using Maestro;
using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;


[TestFixture]
public class VmrPusherTests
{
    private readonly Mock<ISourceManifest> _sourceManifest = new();
    private readonly Mock<IVmrInfo> _vmrInfo = new();
    private readonly Mock<ILocalLibGit2Client> _localGitRepo = new();
    private const string GraphQLUri = "https://api.github.com/graphql";
    private const string Sha = "7cf329817c862c15f9a4e5849b2268d801cb1078";
    private const string VmrUrl = "https://github.com/org/vmr";

    [SetUp]
    public void SetUp()
    {
        var repo = new RepositoryRecord(
            "some-repo",
            "https://github.com/org/some-repo",
            Sha,
            barId: null);

        _sourceManifest.Reset();
        _sourceManifest.SetupGet(s => s.Repositories).Returns(new List<RepositoryRecord>() { repo });
    }

    [Test]
    public async Task PushingUnexistingCommitThrowsExceptionTest()
    {
        var mockHttpClientFactory = new MockHttpClientFactory();

        var responseMsg = """{"data":{"somerepo":{"object":null}}}""";
        mockHttpClientFactory.AddCannedResponse(
            GraphQLUri,
            responseMsg,
            HttpStatusCode.OK,
            "application/json",
            HttpMethod.Post);

        var vmrPusher = new VmrPusher(
            _vmrInfo.Object,
            new NullLogger<VmrPusher>(),
            _sourceManifest.Object,
            mockHttpClientFactory,
            _localGitRepo.Object);

        await vmrPusher.Awaiting(p => p.Push(VmrUrl, "branch", false, "public-github-pat", CancellationToken.None))
            .Should()
            .ThrowAsync<Exception>()
            .WithMessage("Not all pushed commits are publicly available");
    }

    [Test]
    public async Task PublicCommitsArePushedTest()
    {
        var vmrPath = new NativePath("vmr");

        _vmrInfo.Reset();
        _vmrInfo.SetupGet(i => i.VmrPath).Returns(vmrPath);

        var mockHttpClientFactory = new MockHttpClientFactory();

        var responseMsg = """{"data":{"somerepo":{"object": {"id": "C_kwDOBjr6NNoAKGNjYjQ2YWU5M2E4MjhkYjE4MWIzMTBkZTBkMmIwNTI1MWQ0ZDcxNDA"}}}}""";
        mockHttpClientFactory.AddCannedResponse(
            GraphQLUri,
            responseMsg,
            HttpStatusCode.OK,
            "application/json",
            HttpMethod.Post);

        var vmrPusher = new VmrPusher(
            _vmrInfo.Object,
            new NullLogger<VmrPusher>(),
            _sourceManifest.Object,
            mockHttpClientFactory,
            _localGitRepo.Object);

        await vmrPusher.Push(VmrUrl, "branch", false, "public-github-pat", CancellationToken.None);

        _localGitRepo.Verify(
            x => x.Push(
                vmrPath,
                "branch",
                VmrUrl,
                It.Is<LibGit2Sharp.Identity>(x => x.Name == Constants.DarcBotName && x.Email == Constants.DarcBotEmail)),
            Times.Once());
    }

    /// <summary>
    /// Verifies that when skipCommitVerification is true, Push forwards the provided branch and remoteUrl
    /// directly to ILocalLibGit2Client.Push with the DarcBot identity, regardless of string edge cases.
    /// Inputs:
    ///  - skipCommitVerification: true
    ///  - gitHubNoScopePat: null (ignored)
    ///  - Various branch and remoteUrl edge cases (empty, whitespace, unicode, long, special chars)
    /// Expected:
    ///  - No exception thrown
    ///  - ILocalLibGit2Client.Push invoked exactly once with forwarded parameters and DarcBot identity.
    /// </summary>
    [Test]
    [TestCase("", "")]
    [TestCase("", " ")]
    [TestCase(" ", "https://github.com/org/vmr")]
    [TestCase("feature", "ssh://git@github.com:org/v.git")]
    [TestCase("feature/sub", "file:///C:/repo")]
    [TestCase("中文🚀", "https://example.com/x")]
    [TestCase("branch-" + "a", "http://example.com/" + "x")]
    [TestCase("very-" + "long-" + "branch-" + "name-" + "with-" + "many-" + "segments-" + "and-" + "unicode-🚀",
              "https://example.com/" + "path/" + "to/" + "remote/" + "with/" + "segments")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task Push_SkipVerificationTrue_ForwardsParametersToGitPush(string branch, string remoteUrl)
    {
        // Arrange
        var vmrPath = new NativePath("vmr");

        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfoMock.SetupGet(i => i.VmrPath).Returns(vmrPath);

        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var httpClient = new HttpClient(handlerMock.Object);
        var httpClientFactoryMock = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        httpClientFactoryMock.Setup(f => f.CreateClient("GraphQL")).Returns(httpClient);

        var gitClientMock = new Mock<ILocalLibGit2Client>(MockBehavior.Strict);
        gitClientMock
            .Setup(x => x.Push(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Identity>()))
            .Returns(Task.CompletedTask);

        var sut = new VmrPusher(
            vmrInfoMock.Object,
            loggerMock.Object,
            sourceManifestMock.Object,
            httpClientFactoryMock.Object,
            gitClientMock.Object);

        // Act
        await sut.Push(remoteUrl, branch, true, null, CancellationToken.None);

        // Assert
        gitClientMock.Verify(
            x => x.Push(
                vmrPath,
                branch,
                remoteUrl,
                It.Is<Identity>(id => id.Name == Constants.DarcBotName && id.Email == Constants.DarcBotEmail)),
            Times.Once);
    }

    /// <summary>
    /// Ensures that when skipCommitVerification is false and gitHubNoScopePat is null,
    /// the method throws with the expected message and does not push.
    /// Inputs:
    ///  - skipCommitVerification: false
    ///  - gitHubNoScopePat: null
    /// Expected:
    ///  - Exception thrown with the token-required message.
    ///  - ILocalLibGit2Client.Push is never called.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Push_SkipVerificationFalse_NullToken_ThrowsAndDoesNotPush()
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);
        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var httpClient = new HttpClient(handlerMock.Object);
        var httpClientFactoryMock = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        httpClientFactoryMock.Setup(f => f.CreateClient("GraphQL")).Returns(httpClient);

        var gitClientMock = new Mock<ILocalLibGit2Client>(MockBehavior.Strict);

        var sut = new VmrPusher(
            vmrInfoMock.Object,
            loggerMock.Object,
            sourceManifestMock.Object,
            httpClientFactoryMock.Object,
            gitClientMock.Object);

        // Act & Assert
        var ex = Assert.ThrowsAsync<Exception>(async () =>
            await sut.Push("https://example.com/repo", "branch", false, null, CancellationToken.None));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Is.EqualTo("Please specify a GitHub token with basic scope to be used for authenticating to GitHub GraphQL API."));

        gitClientMock.Verify(
            x => x.Push(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Identity>()),
            Times.Never);
    }

    /// <summary>
    /// Validates that when commit verification is enabled and the GitHub GraphQL response indicates the commit
    /// is not found (object null), Push throws and no push to remote occurs.
    /// Inputs:
    ///  - skipCommitVerification: false
    ///  - gitHubNoScopePat: empty string (non-null)
    ///  - Source manifest with one repository "some-repo" at specific SHA.
    ///  - GraphQL response: {"data":{"somerepo":{"object":null}}}
    /// Expected:
    ///  - Exception thrown with message "Not all pushed commits are publicly available".
    ///  - ILocalLibGit2Client.Push is never called.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Push_SkipVerificationFalse_CommitsUnavailable_ThrowsAndDoesNotPush()
    {
        // Arrange
        const string sha = "7cf329817c862c15f9a4e5849b2268d801cb1078";
        var repo = new RepositoryRecord("some-repo", "https://github.com/org/some-repo", sha, null);

        var vmrPath = new NativePath("vmr");
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfoMock.SetupGet(i => i.VmrPath).Returns(vmrPath);

        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        sourceManifestMock.SetupGet(s => s.Repositories).Returns(new List<RepositoryRecord> { repo });

        var responseJson = "{\"data\":{\"somerepo\":{\"object\":null}}}";
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(m =>
                    m.RequestUri != null &&
                    m.RequestUri.AbsoluteUri == "https://api.github.com/graphql" &&
                    m.Method == HttpMethod.Post),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                });

        var httpClient = new HttpClient(handlerMock.Object);
        var httpClientFactoryMock = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        httpClientFactoryMock.Setup(f => f.CreateClient("GraphQL")).Returns(httpClient);

        var gitClientMock = new Mock<ILocalLibGit2Client>(MockBehavior.Strict);

        var sut = new VmrPusher(
            vmrInfoMock.Object,
            loggerMock.Object,
            sourceManifestMock.Object,
            httpClientFactoryMock.Object,
            gitClientMock.Object);

        // Act & Assert
        var ex = Assert.ThrowsAsync<Exception>(async () =>
            await sut.Push("https://github.com/org/vmr", "branch", false, string.Empty, CancellationToken.None));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Is.EqualTo("Not all pushed commits are publicly available"));

        gitClientMock.Verify(
            x => x.Push(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Identity>()),
            Times.Never);
    }

    /// <summary>
    /// Ensures that when commit verification is enabled and the GraphQL response indicates the commit exists,
    /// Push completes successfully and pushes with the DarcBot identity.
    /// Inputs:
    ///  - skipCommitVerification: false
    ///  - gitHubNoScopePat: whitespace string (non-null)
    ///  - Source manifest with one repository "some-repo" at specific SHA.
    ///  - GraphQL response: {"data":{"somerepo":{"object":{"id":"..."}}}}
    /// Expected:
    ///  - No exception thrown.
    ///  - ILocalLibGit2Client.Push invoked exactly once with correct parameters and DarcBot identity.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task Push_SkipVerificationFalse_CommitsAvailable_PushesOnceWithDarcBotIdentity()
    {
        // Arrange
        const string sha = "7cf329817c862c15f9a4e5849b2268d801cb1078";
        var repo = new RepositoryRecord("some-repo", "https://github.com/org/some-repo", sha, null);

        var vmrPath = new NativePath("vmr");
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfoMock.SetupGet(i => i.VmrPath).Returns(vmrPath);

        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        sourceManifestMock.SetupGet(s => s.Repositories).Returns(new List<RepositoryRecord> { repo });

        var responseJson = "{\"data\":{\"somerepo\":{\"object\":{\"id\":\"C_kwDOBjr6NNoAKGNjYjQ2YWU5M2E4MjhkYjE4MWIzMTBkZTBkMmIwNTI1MWQ0ZDcxNDA\"}}}}";
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(m =>
                    m.RequestUri != null &&
                    m.RequestUri.AbsoluteUri == "https://api.github.com/graphql" &&
                    m.Method == HttpMethod.Post),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                });

        var httpClient = new HttpClient(handlerMock.Object);
        var httpClientFactoryMock = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        httpClientFactoryMock.Setup(f => f.CreateClient("GraphQL")).Returns(httpClient);

        var gitClientMock = new Mock<ILocalLibGit2Client>(MockBehavior.Strict);
        gitClientMock
            .Setup(x => x.Push(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Identity>()))
            .Returns(Task.CompletedTask);

        var sut = new VmrPusher(
            vmrInfoMock.Object,
            loggerMock.Object,
            sourceManifestMock.Object,
            httpClientFactoryMock.Object,
            gitClientMock.Object);

        // Act
        await sut.Push("https://github.com/org/vmr", "branch", false, " ", CancellationToken.None);

        // Assert
        gitClientMock.Verify(
            x => x.Push(
                vmrPath,
                "branch",
                "https://github.com/org/vmr",
                It.Is<Identity>(id => id.Name == Constants.DarcBotName && id.Email == Constants.DarcBotEmail)),
            Times.Once);
    }
}

/// <summary>
/// Unit tests for VmrPusher constructor behavior.
/// Focuses on verifying correct interaction with IHttpClientFactory and
/// ensuring that failures from the factory are propagated.
/// </summary>
[TestFixture]
public class VmrPusherConstructorTests
{
    /// <summary>
    /// Validates that the constructor calls IHttpClientFactory.CreateClient with the logical name "GraphQL",
    /// and that if the factory throws, the exception is propagated; otherwise, construction succeeds.
    /// Inputs:
    ///  - factoryThrows: when true, the mock IHttpClientFactory throws InvalidOperationException("boom").
    /// Expected:
    ///  - CreateClient("GraphQL") is invoked exactly once.
    ///  - When factoryThrows == true: constructor throws InvalidOperationException with message "boom".
    ///  - When factoryThrows == false: constructor completes without throwing.
    /// </summary>
    [TestCase(false, TestName = "Ctor_WhenFactorySucceeds_CreatesClientWithGraphQLName")]
    [TestCase(true, TestName = "Ctor_WhenFactoryThrows_PropagatesException")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Ctor_FactoryBehavior_ExpectedOutcome(bool factoryThrows)
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Strict);
        var sourceManifestMock = new Mock<ISourceManifest>(MockBehavior.Strict);
        var gitClientMock = new Mock<ILocalLibGit2Client>(MockBehavior.Strict);

        var httpClientFactoryMock = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        if (factoryThrows)
        {
            httpClientFactoryMock
                .Setup(f => f.CreateClient("GraphQL"))
                .Throws(new InvalidOperationException("boom"));
        }
        else
        {
            httpClientFactoryMock
                .Setup(f => f.CreateClient("GraphQL"))
                .Returns(new HttpClient());
        }

        // Act
        if (factoryThrows)
        {
            Assert.That(
                () => new VmrPusher(
                    vmrInfoMock.Object,
                    loggerMock.Object,
                    sourceManifestMock.Object,
                    httpClientFactoryMock.Object,
                    gitClientMock.Object),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("boom"));
        }
        else
        {
            var instance = new VmrPusher(
                vmrInfoMock.Object,
                loggerMock.Object,
                sourceManifestMock.Object,
                httpClientFactoryMock.Object,
                gitClientMock.Object);

            // Using assertion library minimally to validate the instance is created.
            instance.Should().NotBeNull();
        }

        // Assert
        httpClientFactoryMock.Verify(f => f.CreateClient("GraphQL"), Times.Once);
        httpClientFactoryMock.VerifyNoOtherCalls();
        vmrInfoMock.VerifyNoOtherCalls();
        loggerMock.VerifyNoOtherCalls();
        sourceManifestMock.VerifyNoOtherCalls();
        gitClientMock.VerifyNoOtherCalls();
    }
}
