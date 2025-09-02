// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
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

namespace Microsoft.DotNet.DarcLib.Tests.Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class VmrManagerBaseTests
{
    /// <summary>
    /// Ensures GetLocalVmr returns the ILocalGitRepo instance created by ILocalGitRepoFactory
    /// and that the factory is invoked with the exact VMR path provided by IVmrInfo.
    /// Inputs:
    ///  - Various VMR path representations (absolute, relative, mixed separators, special characters).
    /// Expected:
    ///  - Returned instance is exactly the one produced by the factory.
    ///  - IVmrInfo.VmrPath getter is called once.
    ///  - ILocalGitRepoFactory.Create is called once with the same NativePath value.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(PathCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetLocalVmr_PassesVmrPathToFactoryAndReturnsInstance(string vmrPathString)
    {
        // Arrange
        var expectedPath = new NativePath(vmrPathString);

        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupGet(v => v.VmrPath).Returns(expectedPath);

        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var patchHandler = new Mock<IVmrPatchHandler>(MockBehavior.Strict);
        var tpnGenerator = new Mock<IThirdPartyNoticesGenerator>(MockBehavior.Strict);
        var codeownersGenerator = new Mock<ICodeownersGenerator>(MockBehavior.Strict);
        var credScanGenerator = new Mock<ICredScanSuppressionsGenerator>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrUpdater>>(MockBehavior.Loose);

        var repoInstance = new Mock<ILocalGitRepo>(MockBehavior.Strict).Object;
        var repoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        repoFactory
            .Setup(f => f.Create(It.Is<NativePath>(p => p.Equals(expectedPath))))
            .Returns(repoInstance);

        var sut = new TestableVmrManager(
            vmrInfo.Object,
            dependencyTracker.Object,
            patchHandler.Object,
            tpnGenerator.Object,
            codeownersGenerator.Object,
            credScanGenerator.Object,
            localGitClient.Object,
            repoFactory.Object,
            logger.Object);

        // Act
        var result = sut.CallGetLocalVmr();

        // Assert
        result.Should().BeSameAs(repoInstance);
        vmrInfo.VerifyGet(v => v.VmrPath, Times.Once);
        repoFactory.Verify(f => f.Create(It.Is<NativePath>(p => p.Equals(expectedPath))), Times.Once);
        repoFactory.VerifyNoOtherCalls();
    }

    private static IEnumerable PathCases()
    {
        yield return "/opt/vmr";
        yield return "C:\\vmr\\repo";
        yield return "relative/path/with/mixed\\seps";
        yield return "C:\\répo\\vmr-测试🚀";
    }

    private sealed class TestableVmrManager : VmrManagerBase
    {
        public TestableVmrManager(
            IVmrInfo vmrInfo,
            IVmrDependencyTracker dependencyInfo,
            IVmrPatchHandler vmrPatchHandler,
            IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
            ICodeownersGenerator codeownersGenerator,
            ICredScanSuppressionsGenerator credScanSuppressionsGenerator,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            ILogger<VmrUpdater> logger)
            : base(vmrInfo, dependencyInfo, vmrPatchHandler, thirdPartyNoticesGenerator, codeownersGenerator, credScanSuppressionsGenerator, localGitClient, localGitRepoFactory, logger)
        {
        }

        public ILocalGitRepo CallGetLocalVmr() => GetLocalVmr();
    }

    /// <summary>
    /// Verifies that the constructor correctly wires the injected IVmrInfo and ILocalGitRepoFactory
    /// so that GetLocalVmr() calls the factory with the IVmrInfo.VmrPath and returns the created repo.
    /// Inputs:
    ///  - Different VMR root path strings, including absolute, relative, with spaces, and special characters.
    /// Expected:
    ///  - ILocalGitRepoFactory.Create is called once with the exact NativePath provided via IVmrInfo.VmrPath.
    ///  - The returned ILocalGitRepo instance from the factory is returned by GetLocalVmr().
    /// </summary>
    [Test]
    [TestCase(@"C:\vmr\root")]
    [TestCase("/home/user/vmr")]
    [TestCase("relative path with spaces")]
    [TestCase("weird-!@#$%^&*()-path")]
    [TestCase("")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Ctor_GetLocalVmrUsesInjectedFactoryAndVmrPath_ReturnsRepo(string vmrPathString)
    {
        // Arrange
        var vmrInfoMock = new Mock<IVmrInfo>(MockBehavior.Strict);
        var dependencyInfoMock = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var patchHandlerMock = new Mock<IVmrPatchHandler>(MockBehavior.Strict);
        var tpnGeneratorMock = new Mock<IThirdPartyNoticesGenerator>(MockBehavior.Strict);
        var codeownersGeneratorMock = new Mock<ICodeownersGenerator>(MockBehavior.Strict);
        var credScanSuppressionsGeneratorMock = new Mock<ICredScanSuppressionsGenerator>(MockBehavior.Strict);
        var localGitClientMock = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactoryMock = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<VmrUpdater>>(MockBehavior.Loose);

        var expectedNativePath = new NativePath(vmrPathString);
        vmrInfoMock.SetupGet(v => v.VmrPath).Returns(expectedNativePath);

        var repoMock = new Mock<ILocalGitRepo>(MockBehavior.Strict);
        NativePath capturedPath = default;
        localGitRepoFactoryMock
            .Setup(f => f.Create(It.IsAny<NativePath>()))
            .Callback<NativePath>(p => capturedPath = p)
            .Returns(repoMock.Object);

        var sut = new TestableVmrManager(
            vmrInfoMock.Object,
            dependencyInfoMock.Object,
            patchHandlerMock.Object,
            tpnGeneratorMock.Object,
            codeownersGeneratorMock.Object,
            credScanSuppressionsGeneratorMock.Object,
            localGitClientMock.Object,
            localGitRepoFactoryMock.Object,
            loggerMock.Object);

        // Act
        var result = sut.GetLocalVmrPublic();

        // Assert
        result.Should().BeSameAs(repoMock.Object);
        capturedPath.ToString().Should().Be(expectedNativePath.ToString());
        localGitRepoFactoryMock.Verify(f => f.Create(It.IsAny<NativePath>()), Times.Once);
    }

    /// <summary>
    /// Verifies that CommitAsync forwards the VMR path, commit message (including edge cases),
    /// sets allowEmpty to true, and passes through the author (when provided) to ILocalGitClient.CommitAsync.
    /// Also verifies that informational logs are emitted before and after the git commit invocation.
    /// Inputs:
    ///  - Various commit messages (normal, empty, whitespace, multi-line/unicode, very long).
    ///  - Author either omitted (null) or provided (name, email).
    /// Expected:
    ///  - ILocalGitClient.CommitAsync is called once with:
    ///      path = IVmrInfo.VmrPath, message = input commitMessage, allowEmpty = true,
    ///      author = null or provided tuple, cancellationToken = default.
    ///  - ILogger logs "Committing.." and "Committed in {duration} seconds".
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(Commit_ForwardsParameters_TestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task CommitAsync_ForwardsParameters_And_Logs(string commitMessage, bool includeAuthor, string authorName, string authorEmail)
    {
        // Arrange
        var vmrPath = "/vmr/repo";
        var vmrInfo = new Mock<IVmrInfo>(MockBehavior.Strict);
        vmrInfo.SetupGet(v => v.VmrPath).Returns(vmrPath);

        var dependencyTracker = new Mock<IVmrDependencyTracker>(MockBehavior.Strict);
        var patchHandler = new Mock<IVmrPatchHandler>(MockBehavior.Strict);
        var tpnGenerator = new Mock<IThirdPartyNoticesGenerator>(MockBehavior.Strict);
        var codeownersGenerator = new Mock<ICodeownersGenerator>(MockBehavior.Strict);
        var credScanSuppressionsGenerator = new Mock<ICredScanSuppressionsGenerator>(MockBehavior.Strict);
        var localGitClient = new Mock<ILocalGitClient>(MockBehavior.Strict);
        var localGitRepoFactory = new Mock<ILocalGitRepoFactory>(MockBehavior.Strict);
        var logger = new Mock<ILogger<VmrUpdater>>(MockBehavior.Loose);

        localGitClient
            .Setup(c => c.CommitAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<(string, string)?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var manager = new TestableVmrManager(
            vmrInfo.Object,
            dependencyTracker.Object,
            patchHandler.Object,
            tpnGenerator.Object,
            codeownersGenerator.Object,
            credScanSuppressionsGenerator.Object,
            localGitClient.Object,
            localGitRepoFactory.Object,
            logger.Object);

        // Act
        if (includeAuthor)
        {
            await manager.CommitPublicAsync(commitMessage, (authorName, authorEmail));
        }
        else
        {
            await manager.CommitPublicAsync(commitMessage);
        }

        // Assert
        if (includeAuthor)
        {
            localGitClient.Verify(c => c.CommitAsync(
                    vmrPath,
                    commitMessage,
                    true,
                    It.Is<(string, string)?>(a => a.HasValue && a.Value.Item1 == authorName && a.Value.Item2 == authorEmail),
                    It.Is<CancellationToken>(t => t == default)),
                Times.Once);
        }
        else
        {
            localGitClient.Verify(c => c.CommitAsync(
                    vmrPath,
                    commitMessage,
                    true,
                    It.Is<(string, string)?>(a => a == null),
                    It.Is<CancellationToken>(t => t == default)),
                Times.Once);
        }

        logger.Verify(l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString() == "Committing.."),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((_, __) => true)),
            Times.Once);

        logger.Verify(l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                {
                    var s = v.ToString();
                    return s != null && s.StartsWith("Committed in ") && s.EndsWith(" seconds");
                }),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((_, __) => true)),
            Times.Once);
    }

    public static IEnumerable Commit_ForwardsParameters_TestCases
    {
        get
        {
            yield return new TestCaseData("regular message", false, "", "");
            yield return new TestCaseData("", false, "", "");
            yield return new TestCaseData(" \t", true, "Alice", "alice@example.com");
            yield return new TestCaseData("line1\nline2\u263A", true, "Bob O'Connor", "b.o'c@example.org");
            yield return new TestCaseData(new string('x', 4096), false, "", "");
        }
    }

    /// <summary>
    /// Ensures that all supported placeholders are replaced correctly when all inputs are provided.
    /// Inputs:
    ///  - template contains {name}, {remote}, {oldSha}, {newSha}, {oldShaShort}, {newShaShort}, {commitMessage}.
    ///  - non-null name, remote, oldSha (>= 7 chars), newSha (>= 7 chars), and additionalMessage.
    /// Expected:
    ///  - Returned string has all placeholders replaced with the provided values, including short SHAs (first 7 chars).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void PrepareCommitMessage_AllPlaceholders_PopulatedCorrectly()
    {
        // Arrange
        var template =
            "Update {name} from {oldShaShort} to {newShaShort} at {remote}\n" +
            "Old: {oldSha}\n" +
            "New: {newSha}\n\n" +
            "{commitMessage}";

        var name = "RepoA";
        var remote = "https://example/remote.git";
        var oldSha = "abcdef1234567890";
        var newSha = "1234567abcdef";
        var additionalMessage = "Extra details.";

        // Act
        var result = Exposer.Prepare(template, name, remote, oldSha, newSha, additionalMessage);

        // Assert
        var expected =
            "Update RepoA from abcdef1 to 1234567 at https://example/remote.git\n" +
            "Old: abcdef1234567890\n" +
            "New: 1234567abcdef\n\n" +
            "Extra details.";
        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that nullable inputs (oldSha, newSha, additionalMessage) are replaced with empty strings.
    /// Inputs:
    ///  - template contains placeholders for SHAs and commitMessage.
    ///  - oldSha = null, newSha = null, additionalMessage = null.
    /// Expected:
    ///  - {oldSha}, {newSha}, {oldShaShort}, {newShaShort}, and {commitMessage} are replaced with empty strings.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void PrepareCommitMessage_NullShasAndMessage_ReplacedWithEmptyStrings()
    {
        // Arrange
        var template =
            "Update {name} from {oldShaShort} to {newShaShort} at {remote}\n" +
            "Old: {oldSha}\n" +
            "New: {newSha}\n\n" +
            "{commitMessage}";

        var name = "RepoA";
        var remote = "https://example/remote.git";
        string oldSha = null;
        string newSha = null;
        string additionalMessage = null;

        // Act
        var result = Exposer.Prepare(template, name, remote, oldSha, newSha, additionalMessage);

        // Assert
        var expected =
            "Update RepoA from  to  at https://example/remote.git\n" +
            "Old: \n" +
            "New: \n\n";
        result.Should().Be(expected);
    }

    /// <summary>
    /// Ensures an ArgumentOutOfRangeException is thrown when a provided SHA is shorter than 7 characters,
    /// because short SHA derivation slices the first 7 characters.
    /// Inputs:
    ///  - shortShaAsNew controls whether the invalid SHA is passed as newSha (true) or oldSha (false).
    ///  - The other SHA is valid (>= 7 characters).
    /// Expected:
    ///  - ArgumentOutOfRangeException is thrown.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void PrepareCommitMessage_ShortSha_ThrowsArgumentOutOfRangeException(bool shortShaAsNew)
    {
        // Arrange
        var template = "Message {oldShaShort} -> {newShaShort}";
        var name = "RepoA";
        var remote = "https://example/remote.git";
        var invalidShort = "12345";
        var valid = "0123456789abcdef";

        var oldSha = shortShaAsNew ? valid : invalidShort;
        var newSha = shortShaAsNew ? invalidShort : valid;

        // Act
        Action act = () => Exposer.Prepare(template, name, remote, oldSha, newSha, null);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class Exposer : VmrManagerBase
    {
        // This private constructor ensures no parameterless constructor is emitted.
        // We never instantiate this type in tests because we only need access to the protected static method.
        private Exposer(
            IVmrInfo vmrInfo,
            IVmrDependencyTracker dependencyInfo,
            IVmrPatchHandler vmrPatchHandler,
            IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
            ICodeownersGenerator codeownersGenerator,
            ICredScanSuppressionsGenerator credScanSuppressionsGenerator,
            ILocalGitClient localGitClient,
            ILocalGitRepoFactory localGitRepoFactory,
            ILogger<VmrUpdater> logger)
            : base(
                  vmrInfo,
                  dependencyInfo,
                  vmrPatchHandler,
                  thirdPartyNoticesGenerator,
                  codeownersGenerator,
                  credScanSuppressionsGenerator,
                  localGitClient,
                  localGitRepoFactory,
                  logger)
        {
        }

        public static string Prepare(string template, string name, string remote, string oldSha = null, string newSha = null, string additionalMessage = null)
            => PrepareCommitMessage(template, name, remote, oldSha, newSha, additionalMessage);
    }
}
