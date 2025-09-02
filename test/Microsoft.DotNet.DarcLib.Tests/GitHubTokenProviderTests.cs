// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Maestro;
using Maestro.Common;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.UnitTests;

public class GitHubTokenProviderTests
{
    /// <summary>
    /// Verifies that GetTokenForRepository returns the token produced by the underlying IGitHubTokenProvider
    /// for a variety of repository URI inputs.
    /// Inputs:
    ///  - repoUri values including empty, whitespace, special characters, and very long strings.
    /// Expected:
    ///  - The returned token matches the mocked provider's token.
    ///  - The provider is invoked exactly once with the same repoUri.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(ValidRepoUris))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetTokenForRepository_VariousRepoUris_ReturnsProviderToken(string repoUri)
    {
        // Arrange
        var providerMock = new Mock<IGitHubTokenProvider>(MockBehavior.Strict);
        var expectedToken = "token-" + Guid.NewGuid().ToString("N");

        providerMock
            .Setup(p => p.GetTokenForRepository(repoUri))
            .Returns(Task.FromResult(expectedToken));

        var sut = new GitHubTokenProvider(providerMock.Object);

        // Act
        var actual = sut.GetTokenForRepository(repoUri);

        // Assert
        actual.Should().Be(expectedToken);
        providerMock.Verify(p => p.GetTokenForRepository(repoUri), Times.Once);
    }

    /// <summary>
    /// Ensures that any error from the underlying provider (synchronous throw, faulted task, or canceled task)
    /// is caught and results in a null return value from GetTokenForRepository.
    /// Inputs:
    ///  - failureMode: "sync-throw", "faulted", or "canceled".
    /// Expected:
    ///  - Method returns null and does not throw.
    ///  - Underlying provider is invoked exactly once.
    /// </summary>
    [Test]
    [TestCase("sync-throw")]
    [TestCase("faulted")]
    [TestCase("canceled")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetTokenForRepository_ProviderThrowsOrTaskFails_ReturnsNull(string failureMode)
    {
        // Arrange
        var repoUri = "https://github.com/org/repo";
        var providerMock = new Mock<IGitHubTokenProvider>(MockBehavior.Strict);

        if (failureMode == "sync-throw")
        {
            providerMock
                .Setup(p => p.GetTokenForRepository(repoUri))
                .Throws(new InvalidOperationException("boom"));
        }
        else if (failureMode == "faulted")
        {
            providerMock
                .Setup(p => p.GetTokenForRepository(repoUri))
                .Returns(Task.FromException<string>(new ApplicationException("fault")));
        }
        else if (failureMode == "canceled")
        {
            var canceledToken = new CancellationToken(true);
            providerMock
                .Setup(p => p.GetTokenForRepository(repoUri))
                .Returns(Task.FromCanceled<string>(canceledToken));
        }
        else
        {
            Assert.Inconclusive("Unknown failure mode provided to the test.");
        }

        var sut = new GitHubTokenProvider(providerMock.Object);

        // Act
        var actual = sut.GetTokenForRepository(repoUri);

        // Assert
        actual.Should().BeNull();
        providerMock.Verify(p => p.GetTokenForRepository(repoUri), Times.Once);
    }

    private static System.Collections.Generic.IEnumerable<string> ValidRepoUris()
    {
        yield return "https://github.com/org/repo";
        yield return "";
        yield return " ";
        yield return "\t\n";
        yield return "ssh://git@github.com/org/repo";
        yield return "file:///C:/repo/path";
        yield return "https://example.com/repo with spaces";
        yield return "特殊字符-repo";
        yield return new string('a', 4096);
        yield return "https://github.com/org/repo?query=param&another=%20value";
        yield return "git@github.com:org/repo.git";
    }

    /// <summary>
    /// Ensures that when the underlying IGitHubTokenProvider returns null, the method returns null.
    /// Inputs:
    ///  - repoUri values including typical, empty, and whitespace-only strings.
    ///  - The underlying provider returns null.
    /// Expected:
    ///  - GetTokenForRepositoryAsync returns null.
    ///  - The underlying provider is called exactly once with the same repoUri.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("https://github.com/dotnet/arcade")]
    [TestCase("")]
    [TestCase("   ")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetTokenForRepositoryAsync_ProviderReturnsNull_ReturnsNull(string repoUri)
    {
        // Arrange
        var providerMock = new Mock<IGitHubTokenProvider>(MockBehavior.Strict);
        providerMock
            .Setup(p => p.GetTokenForRepository(repoUri))
            .ReturnsAsync((string)null);

        var sut = new GitHubTokenProvider(providerMock.Object);

        // Act
        var result = await sut.GetTokenForRepositoryAsync(repoUri);

        // Assert
        result.Should().BeNull();
        providerMock.Verify(p => p.GetTokenForRepository(repoUri), Times.Once);
    }

    /// <summary>
    /// Validates that exceptions thrown by the underlying IGitHubTokenProvider are swallowed and null is returned.
    /// Inputs:
    ///  - repoUri value.
    ///  - The underlying provider throws different exception types.
    /// Expected:
    ///  - GetTokenForRepositoryAsync returns null without throwing.
    ///  - The underlying provider is called exactly once with the same repoUri.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(ExceptionCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetTokenForRepositoryAsync_ProviderThrows_ReturnsNull(string repoUri, Exception exceptionToThrow)
    {
        // Arrange
        var providerMock = new Mock<IGitHubTokenProvider>(MockBehavior.Strict);
        providerMock
            .Setup(p => p.GetTokenForRepository(repoUri))
            .ThrowsAsync(exceptionToThrow);

        var sut = new GitHubTokenProvider(providerMock.Object);

        // Act
        var result = await sut.GetTokenForRepositoryAsync(repoUri);

        // Assert
        result.Should().BeNull();
        providerMock.Verify(p => p.GetTokenForRepository(repoUri), Times.Once);
    }

    private static IEnumerable<TestCaseData> ExceptionCases
    {
        get
        {
            yield return new TestCaseData("https://github.com/org/repo", new ArgumentException("bad repo uri"))
                .SetName("GetTokenForRepositoryAsync_ProviderThrows_ReturnsNull_ArgumentException");
            yield return new TestCaseData("https://github.com/org/repo", new InvalidOperationException("invalid op"))
                .SetName("GetTokenForRepositoryAsync_ProviderThrows_ReturnsNull_InvalidOperationException");
            yield return new TestCaseData("https://github.com/org/repo", new Exception("generic"))
                .SetName("GetTokenForRepositoryAsync_ProviderThrows_ReturnsNull_GenericException");
        }
    }
}
