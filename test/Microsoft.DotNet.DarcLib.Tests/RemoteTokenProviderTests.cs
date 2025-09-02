// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Maestro;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;


public class RemoteTokenProviderTests
{
    /// <summary>
    /// Validates that the parameterless constructor completes without throwing,
    /// ensuring that default token providers are instantiated safely.
    /// Inputs:
    ///  - No inputs.
    /// Expected:
    ///  - No exception is thrown during construction.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_Default_DoesNotThrow()
    {
        // Arrange
        // No arrangement needed.

        // Act
        var instance = new RemoteTokenProvider();

        // Assert
        // No exception thrown is the assertion.
        _ = instance;
    }

    /// <summary>
    /// Partial test: Intended to verify that the parameterless constructor initializes
    /// both internal token providers as ResolvedTokenProvider instances with null tokens.
    /// Inputs:
    ///  - No inputs.
    /// Expected:
    ///  - _azdoTokenProvider and _gitHubTokenProvider are instances of ResolvedTokenProvider configured with null tokens.
    /// Notes:
    ///  - This verification requires access to private fields or exercising other class members
    ///    (e.g., GetTokenForRepository/GetTokenForRepositoryAsync) which are outside the requested test scope,
    ///    and reflection is prohibited by the testing rules. To complete this test, either:
    ///    1) Allow use of the class's public methods to indirectly validate constructor effects, or
    ///    2) Permit reflection-based inspection of private fields.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_Default_InitializesResolvedTokenProviders_Partial()
    {
        // Arrange
        var instance = new RemoteTokenProvider();

        // Act
        var gitHubToken = instance.GetTokenForRepository("https://github.com/dotnet/arcade");
        var azdoToken = instance.GetTokenForRepository("https://dev.azure.com/dnceng/internal/_git/arcade");

        // Assert
        Assert.That(gitHubToken, Is.Null, "GitHub token should be null when default constructor is used.");
        Assert.That(azdoToken, Is.Null, "Azure DevOps token should be null when default constructor is used.");
    }

    private static System.Collections.Generic.IEnumerable<TestCaseData> TokenPairs()
    {
        yield return new TestCaseData(null, null).SetName("Tokens_Null_Null");
        yield return new TestCaseData(string.Empty, string.Empty).SetName("Tokens_Empty_Empty");
        yield return new TestCaseData(" ", "\t\r\n").SetName("Tokens_Whitespace_VariedWhitespace");
        yield return new TestCaseData(new string('a', 1024), new string('b', 2048)).SetName("Tokens_VeryLongStrings");
        yield return new TestCaseData(@"p@$$w0rd!:/\?%#中文🙂", "gh-Τόκεν-✓").SetName("Tokens_SpecialAndUnicode");
        yield return new TestCaseData("azdoTokenValue", "ghTokenValue").SetName("Tokens_NormalValues");
    }


    /// <summary>
    /// Validates that the parameterless constructor completes without throwing
    /// and yields a usable instance.
    /// Inputs:
    ///  - No inputs.
    /// Expected:
    ///  - No exception is thrown and the created instance is not null and implements IRemoteTokenProvider.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_Default_DoesNotThrow_CreatesInstance()
    {
        // Arrange

        // Act
        var instance = new RemoteTokenProvider();

        // Assert
        instance.Should().NotBeNull();
        instance.Should().BeAssignableTo<IRemoteTokenProvider>();
    }

    /// <summary>
    /// Verifies that the two-parameter constructor (IRemoteTokenProvider azdoTokenProvider, IRemoteTokenProvider gitHubTokenProvider)
    /// wires dependencies correctly for the synchronous API.
    /// Inputs:
    ///  - repoUri: GitHub and Azure DevOps URIs.
    ///  - returnNull: whether the dependency should return null instead of a token value.
    /// Expected:
    ///  - For GitHub URIs, GetTokenForRepository routes to the provided GitHub provider's GetTokenForRepository and returns its value.
    ///  - For Azure DevOps URIs, GetTokenForRepository routes to the provided AzDO provider's GetTokenForRepositoryAsync and returns its value.
    ///  - The non-relevant provider is not called.
    /// </summary>
    [TestCase("https://github.com/dotnet/runtime", false)]
    [TestCase("https://github.com/dotnet/runtime", true)]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/arcade", false)]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/arcade", true)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithProviders_SynchronousApiRoutesToCorrectProvider(string repoUri, bool returnNull)
    {
        // Arrange
        var azdoProviderMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var gitHubProviderMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);

        // Configure expected provider based on repo type
        if (repoUri.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var gitHubTokenValue = "gh-token";
            if (returnNull)
            {
                gitHubProviderMock
                    .Setup(p => p.GetTokenForRepository(repoUri))
                    .Returns((string)null);
            }
            else
            {
                gitHubProviderMock
                    .Setup(p => p.GetTokenForRepository(repoUri))
                    .Returns(gitHubTokenValue);
            }
        }
        else
        {
            var azdoTokenValue = "ado-token";
            if (returnNull)
            {
                azdoProviderMock
                    .Setup(p => p.GetTokenForRepositoryAsync(repoUri))
                    .Returns(Task.FromResult<string>(null));
            }
            else
            {
                azdoProviderMock
                    .Setup(p => p.GetTokenForRepositoryAsync(repoUri))
                    .Returns(Task.FromResult(azdoTokenValue));
            }
        }

        var sut = new RemoteTokenProvider(azdoProviderMock.Object, gitHubProviderMock.Object);

        // Act
        var result = sut.GetTokenForRepository(repoUri);

        // Assert
        if (repoUri.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (returnNull)
            {
                result.Should().BeNull();
            }
            else
            {
                result.Should().Be("gh-token");
            }

            gitHubProviderMock.Verify(p => p.GetTokenForRepository(repoUri), Times.Once);
            azdoProviderMock.Verify(p => p.GetTokenForRepositoryAsync(repoUri), Times.Never);
            gitHubProviderMock.VerifyNoOtherCalls();
            azdoProviderMock.VerifyNoOtherCalls();
        }
        else
        {
            if (returnNull)
            {
                result.Should().BeNull();
            }
            else
            {
                result.Should().Be("ado-token");
            }

            azdoProviderMock.Verify(p => p.GetTokenForRepositoryAsync(repoUri), Times.Once);
            gitHubProviderMock.Verify(p => p.GetTokenForRepository(repoUri), Times.Never);
            gitHubProviderMock.VerifyNoOtherCalls();
            azdoProviderMock.VerifyNoOtherCalls();
        }
    }

    /// <summary>
    /// Verifies that the two-parameter constructor (IRemoteTokenProvider azdoTokenProvider, IRemoteTokenProvider gitHubTokenProvider)
    /// wires dependencies correctly for the asynchronous API.
    /// Inputs:
    ///  - repoUri: GitHub and Azure DevOps URIs.
    ///  - returnNull: whether the dependency should return null instead of a token value.
    /// Expected:
    ///  - For GitHub URIs, GetTokenForRepositoryAsync routes to the provided GitHub provider's GetTokenForRepository and returns its value.
    ///  - For Azure DevOps URIs, GetTokenForRepositoryAsync routes to the provided AzDO provider's GetTokenForRepositoryAsync and returns its value.
    ///  - The non-relevant provider is not called.
    /// </summary>
    [TestCase("https://github.com/dotnet/runtime", false)]
    [TestCase("https://github.com/dotnet/runtime", true)]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/arcade", false)]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/arcade", true)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task Constructor_WithProviders_AsynchronousApiRoutesToCorrectProvider(string repoUri, bool returnNull)
    {
        // Arrange
        var azdoProviderMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var gitHubProviderMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);

        if (repoUri.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var gitHubTokenValue = "gh-token-async";
            if (returnNull)
            {
                gitHubProviderMock
                    .Setup(p => p.GetTokenForRepository(repoUri))
                    .Returns((string)null);
            }
            else
            {
                gitHubProviderMock
                    .Setup(p => p.GetTokenForRepository(repoUri))
                    .Returns(gitHubTokenValue);
            }
        }
        else
        {
            var azdoTokenValue = "ado-token-async";
            if (returnNull)
            {
                azdoProviderMock
                    .Setup(p => p.GetTokenForRepositoryAsync(repoUri))
                    .Returns(Task.FromResult<string>(null));
            }
            else
            {
                azdoProviderMock
                    .Setup(p => p.GetTokenForRepositoryAsync(repoUri))
                    .Returns(Task.FromResult(azdoTokenValue));
            }
        }

        var sut = new RemoteTokenProvider(azdoProviderMock.Object, gitHubProviderMock.Object);

        // Act
        var result = await sut.GetTokenForRepositoryAsync(repoUri);

        // Assert
        if (repoUri.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (returnNull)
            {
                result.Should().BeNull();
            }
            else
            {
                result.Should().Be("gh-token-async");
            }

            gitHubProviderMock.Verify(p => p.GetTokenForRepository(repoUri), Times.Once);
            azdoProviderMock.Verify(p => p.GetTokenForRepositoryAsync(repoUri), Times.Never);
            gitHubProviderMock.VerifyNoOtherCalls();
            azdoProviderMock.VerifyNoOtherCalls();
        }
        else
        {
            if (returnNull)
            {
                result.Should().BeNull();
            }
            else
            {
                result.Should().Be("ado-token-async");
            }

            azdoProviderMock.Verify(p => p.GetTokenForRepositoryAsync(repoUri), Times.Once);
            gitHubProviderMock.Verify(p => p.GetTokenForRepository(repoUri), Times.Never);
            gitHubProviderMock.VerifyNoOtherCalls();
            azdoProviderMock.VerifyNoOtherCalls();
        }
    }

    /// <summary>
    /// Ensures that when constructed with an Azure DevOps token provider and a GitHub token,
    /// the GitHub token is returned as-is for GitHub repository URIs via the synchronous API.
    /// Inputs:
    ///  - azdoTokenProvider: strict mock (should not be invoked for GitHub URIs).
    ///  - gitHubToken: various inputs including null, empty, whitespace, long, and special characters.
    /// Expected:
    ///  - GetTokenForRepository returns exactly the provided gitHubToken.
    ///  - Azure DevOps provider is not called.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(typeof(TestData), nameof(TestData.GitHubTokens))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_AzdoProviderAndGitHubToken_GitHubRepo_SyncReturnsProvidedToken(string gitHubToken)
    {
        // Arrange
        var azdoProviderMock = new Mock<IAzureDevOpsTokenProvider>(MockBehavior.Strict);
        var sut = new RemoteTokenProvider(azdoProviderMock.Object, gitHubToken);
        var gitHubRepoUri = "https://github.com/dotnet/arcade";

        // Act
        var actual = sut.GetTokenForRepository(gitHubRepoUri);

        // Assert
        if (gitHubToken == null)
        {
            actual.Should().BeNull();
        }
        else
        {
            actual.Should().Be(gitHubToken);
        }
        azdoProviderMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures that when constructed with an Azure DevOps token provider and a GitHub token,
    /// the GitHub token is returned as-is for GitHub repository URIs via the asynchronous API.
    /// Inputs:
    ///  - azdoTokenProvider: strict mock (should not be invoked for GitHub URIs).
    ///  - gitHubToken: various inputs including null, empty, whitespace, long, and special characters.
    /// Expected:
    ///  - GetTokenForRepositoryAsync returns exactly the provided gitHubToken.
    ///  - Azure DevOps provider is not called.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(typeof(TestData), nameof(TestData.GitHubTokens))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task Constructor_AzdoProviderAndGitHubToken_GitHubRepo_AsyncReturnsProvidedToken(string gitHubToken)
    {
        // Arrange
        var azdoProviderMock = new Mock<IAzureDevOpsTokenProvider>(MockBehavior.Strict);
        var sut = new RemoteTokenProvider(azdoProviderMock.Object, gitHubToken);
        var gitHubRepoUri = "https://github.com/dotnet/arcade";

        // Act
        var actual = await sut.GetTokenForRepositoryAsync(gitHubRepoUri);

        // Assert
        if (gitHubToken == null)
        {
            actual.Should().BeNull();
        }
        else
        {
            actual.Should().Be(gitHubToken);
        }
        azdoProviderMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Validates that the Azure DevOps provider passed to the constructor is used for Azure DevOps repository URIs
    /// through the synchronous API, and its result is returned unmodified.
    /// Inputs:
    ///  - gitHubToken: arbitrary values (should not affect Azure DevOps path).
    ///  - azdoReturnedToken: values including null, empty, whitespace, long, and special characters.
    /// Expected:
    ///  - GetTokenForRepository returns exactly azdoReturnedToken.
    ///  - IAzureDevOpsTokenProvider.GetTokenForRepositoryAsync is called exactly once with the same URI.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(typeof(TestData), nameof(TestData.AzdoTokenMatrix))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_AzdoProviderAndGitHubToken_AzdoRepo_SyncReturnsProviderResult(string gitHubToken, string azdoReturnedToken)
    {
        // Arrange
        var azdoRepoUri = "https://dev.azure.com/dnceng/internal/_git/arcade";
        var azdoProviderMock = new Mock<IAzureDevOpsTokenProvider>(MockBehavior.Strict);
        azdoProviderMock
            .Setup(m => m.GetTokenForRepositoryAsync(azdoRepoUri))
            .ReturnsAsync(azdoReturnedToken);

        var sut = new RemoteTokenProvider(azdoProviderMock.Object, gitHubToken);

        // Act
        var actual = sut.GetTokenForRepository(azdoRepoUri);

        // Assert
        if (azdoReturnedToken == null)
        {
            actual.Should().BeNull();
        }
        else
        {
            actual.Should().Be(azdoReturnedToken);
        }

        azdoProviderMock.Verify(m => m.GetTokenForRepositoryAsync(azdoRepoUri), Times.Once);
        azdoProviderMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Validates that the Azure DevOps provider passed to the constructor is used for Azure DevOps repository URIs
    /// through the asynchronous API, and its result is returned unmodified.
    /// Inputs:
    ///  - gitHubToken: arbitrary values (should not affect Azure DevOps path).
    ///  - azdoReturnedToken: values including null, empty, whitespace, long, and special characters.
    /// Expected:
    ///  - GetTokenForRepositoryAsync returns exactly azdoReturnedToken.
    ///  - IAzureDevOpsTokenProvider.GetTokenForRepositoryAsync is called exactly once with the same URI.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(typeof(TestData), nameof(TestData.AzdoTokenMatrix))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task Constructor_AzdoProviderAndGitHubToken_AzdoRepo_AsyncReturnsProviderResult(string gitHubToken, string azdoReturnedToken)
    {
        // Arrange
        var azdoRepoUri = "https://dev.azure.com/dnceng/internal/_git/arcade";
        var azdoProviderMock = new Mock<IAzureDevOpsTokenProvider>(MockBehavior.Strict);
        azdoProviderMock
            .Setup(m => m.GetTokenForRepositoryAsync(azdoRepoUri))
            .ReturnsAsync(azdoReturnedToken);

        var sut = new RemoteTokenProvider(azdoProviderMock.Object, gitHubToken);

        // Act
        var actual = await sut.GetTokenForRepositoryAsync(azdoRepoUri);

        // Assert
        if (azdoReturnedToken == null)
        {
            actual.Should().BeNull();
        }
        else
        {
            actual.Should().Be(azdoReturnedToken);
        }

        azdoProviderMock.Verify(m => m.GetTokenForRepositoryAsync(azdoRepoUri), Times.Once);
        azdoProviderMock.VerifyNoOtherCalls();
    }

    private static class TestData
    {
        public static IEnumerable<TestCaseData> GitHubTokens()
        {
            yield return new TestCaseData(null).SetName("GitHubToken_null");
            yield return new TestCaseData(string.Empty).SetName("GitHubToken_empty");
            yield return new TestCaseData(" ").SetName("GitHubToken_whitespace");
            yield return new TestCaseData("ghp_example_token_123").SetName("GitHubToken_normal");
            yield return new TestCaseData(new string('a', 4096)).SetName("GitHubToken_very_long");
            yield return new TestCaseData("!@#$%^&*()🚀\r\n\t").SetName("GitHubToken_special_chars");
        }

        public static IEnumerable<TestCaseData> AzdoTokenMatrix()
        {
            // Combine diverse GitHub tokens with diverse Azure DevOps provider results
            var gitHubTokens = new string[]
            {
                null,
                string.Empty,
                " ",
                "ghp_example_token_123",
                new string('b', 1024),
                "!@#$%^&*()🛠️"
            };

            var azdoReturnedTokens = new string[]
            {
                null,
                string.Empty,
                " ",
                "azdo_pat_456",
                new string('c', 2048),
                "[]{}<>|`~\n\r"
            };

            foreach (var gh in gitHubTokens)
            {
                foreach (var az in azdoReturnedTokens)
                {
                    yield return new TestCaseData(gh, az)
                        .SetName($"GitHubToken_{Describe(gh)}__AzdoReturnedToken_{Describe(az)}");
                }
            }
        }

        private static string Describe(string value)
        {
            if (value == null) return "null";
            if (value.Length == 0) return "empty";
            if (string.IsNullOrWhiteSpace(value)) return "whitespace";
            if (value.Length > 32) return $"len{value.Length}";
            return value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }
    }

    /// <summary>
    /// Validates that GitRemoteUser returns the GitHub bot username defined by Constants.GitHubBotUserName.
    /// Inputs:
    ///  - None (static property).
    /// Expected:
    ///  - The returned value equals Constants.GitHubBotUserName.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GitRemoteUser_ReturnsGitHubBotUserName()
    {
        // Arrange
        var expected = Constants.GitHubBotUserName;

        // Act
        var actual = RemoteTokenProvider.GitRemoteUser;

        // Assert
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that for GitHub and Azure DevOps repository URIs, the async method returns the correct token
    /// and invokes the appropriate underlying provider:
    /// - GitHub: calls synchronous GetTokenForRepository on the GitHub provider.
    /// - Azure DevOps: calls asynchronous GetTokenForRepositoryAsync on the AzDO provider.
    /// Inputs:
    ///  - repoUri = "https://github.com/org/repo" or "https://dev.azure.com/org/project/_git/repo".
    /// Expected:
    ///  - Returns the configured token for the matching provider.
    ///  - Calls only the expected provider method for the detected repo type.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("https://github.com/dotnet/arcade", "gh-token", "GitHub")]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/arcade", "azdo-token", "AzureDevOps")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetTokenForRepositoryAsync_KnownRepoType_ReturnsExpectedTokenAndCallsCorrectProvider(
        string repoUri,
        string expectedToken,
        string expectedType)
    {
        // Arrange
        var azdoMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var githubMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);

        if (expectedType == "GitHub")
        {
            githubMock
                .Setup(m => m.GetTokenForRepository(repoUri))
                .Returns(expectedToken);
        }
        else if (expectedType == "AzureDevOps")
        {
            azdoMock
                .Setup(m => m.GetTokenForRepositoryAsync(repoUri))
                .ReturnsAsync(expectedToken);
        }

        var sut = new RemoteTokenProvider(azdoMock.Object, githubMock.Object);

        // Act
        var token = await sut.GetTokenForRepositoryAsync(repoUri);

        // Assert
        token.Should().Be(expectedToken);

        if (expectedType == "GitHub")
        {
            githubMock.Verify(m => m.GetTokenForRepository(repoUri), Times.Once);
            azdoMock.Verify(m => m.GetTokenForRepositoryAsync(It.IsAny<string>()), Times.Never);
        }
        else
        {
            azdoMock.Verify(m => m.GetTokenForRepositoryAsync(repoUri), Times.Once);
            githubMock.Verify(m => m.GetTokenForRepository(It.IsAny<string>()), Times.Never);
        }

        azdoMock.VerifyNoOtherCalls();
        githubMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that for local repository paths (relative or simple strings), the async method
    /// returns null and does not call any underlying providers.
    /// Inputs:
    ///  - repoUri = "", "relative/path", or "C:\repo".
    /// Expected:
    ///  - Result is null.
    ///  - No provider methods are invoked.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("")]
    [TestCase("relative/path")]
    [TestCase("C:\\repo")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetTokenForRepositoryAsync_LocalRepo_ReturnsNullAndSkipsProviders(string repoUri)
    {
        // Arrange
        var azdoMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var githubMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var sut = new RemoteTokenProvider(azdoMock.Object, githubMock.Object);

        // Act
        var token = await sut.GetTokenForRepositoryAsync(repoUri);

        // Assert
        token.Should().BeNull();

        azdoMock.VerifyNoOtherCalls();
        githubMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that an unsupported repository remote (unknown host) results in NotImplementedException
    /// whose message contains the original repository URI.
    /// Inputs:
    ///  - repoUri = "https://gitlab.com/org/repo".
    /// Expected:
    ///  - Throws NotImplementedException with message containing "Unsupported repository remote" and the repoUri.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task GetTokenForRepositoryAsync_UnsupportedRemote_ThrowsNotImplemented()
    {
        // Arrange
        var repoUri = "https://gitlab.com/org/repo";
        var azdoMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var githubMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var sut = new RemoteTokenProvider(azdoMock.Object, githubMock.Object);

        // Act
        Func<Task> act = () => sut.GetTokenForRepositoryAsync(repoUri);

        // Assert
        var ex = await act.Should().ThrowAsync<NotImplementedException>();
        ex.Message.Should().StartWith("Unsupported repository remote");
        ex.Message.Should().Contain(repoUri);

        azdoMock.VerifyNoOtherCalls();
        githubMock.VerifyNoOtherCalls();
    }
}
