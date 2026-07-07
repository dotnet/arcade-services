// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Common.Telemetry;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Internal.Credentials;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests;

[TestFixture]
public class LocalGitClientCommitSigningTests
{
    [Test]
    public async Task CommitAsync_UsesNoGpgSignWhenSigningDisabled()
    {
        var processManager = new Mock<IProcessManager>();
        processManager
            .Setup(p => p.ExecuteGit(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult());

        var commitSigner = new Mock<ICommitSigner>();
        commitSigner
            .Setup(s => s.GetConfigurationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommitSigningConfiguration());

        var gitClient = new LocalGitClient(
            Mock.Of<IRemoteTokenProvider>(),
            new NoTelemetryRecorder(),
            processManager.Object,
            new FileSystem(),
            NullLogger<LocalGitClient>.Instance,
            commitSigner.Object);

        await gitClient.CommitAsync("/repo", "message", allowEmpty: false);

        processManager.Verify(
            p => p.ExecuteGit(
                "/repo",
                It.Is<IEnumerable<string>>(args => args.Contains("--no-gpg-sign") && !args.Contains("--gpg-sign")),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task CommitAsync_UsesGpgSignWhenSigningEnabled()
    {
        var processManager = new Mock<IProcessManager>();
        processManager
            .Setup(p => p.ExecuteGit(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult());

        var commitSigner = new Mock<ICommitSigner>();
        commitSigner
            .Setup(s => s.GetConfigurationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommitSigningConfiguration
            {
                Enabled = true,
                EnvironmentVariables = new Dictionary<string, string> { ["GNUPGHOME"] = "/tmp/gnupg" },
            });

        var gitClient = new LocalGitClient(
            Mock.Of<IRemoteTokenProvider>(),
            new NoTelemetryRecorder(),
            processManager.Object,
            new FileSystem(),
            NullLogger<LocalGitClient>.Instance,
            commitSigner.Object);

        await gitClient.CommitAsync("/repo", "message", allowEmpty: false);

        processManager.Verify(
            p => p.ExecuteGit(
                "/repo",
                It.Is<IEnumerable<string>>(args => args.Contains("--gpg-sign") && !args.Contains("--no-gpg-sign")),
                It.Is<Dictionary<string, string>?>(env => env != null && env["GNUPGHOME"] == "/tmp/gnupg"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
