// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using ProductConstructionService.Api.VirtualMonoRepo;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
public class ServiceCommitSignatureVerifierTests
{
    [Test]
    public async Task VerifyAsync_UsesConfiguredPublicKeyAndGitVerifyCommit()
    {
        var processManager = new Mock<IProcessManager>();
        processManager
            .Setup(p => p.Execute(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });
        processManager
            .Setup(p => p.ExecuteGit(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });
        processManager
            .Setup(p => p.ExecuteGit(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 0 });

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KeyVaultSecrets:codeflow-commit-signing-public-key"] = "public-key",
            })
            .Build();

        var verifier = new ServiceCommitSignatureVerifier(
            processManager.Object,
            configuration,
            NullLogger<ServiceCommitSignatureVerifier>.Instance);

        bool verified = await verifier.VerifyAsync("/repo", "abc123");

        Assert.That(verified, Is.True);
        processManager.Verify(
            p => p.Execute(
                "gpg",
                It.Is<IEnumerable<string>>(args => args.Contains("--import")),
                It.IsAny<TimeSpan?>(),
                It.IsAny<string?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        processManager.Verify(
            p => p.ExecuteGit(
                "/repo",
                It.Is<string[]>(args => args.Any(arg => arg == "verify-commit") && args.Any(arg => arg == "abc123")),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
