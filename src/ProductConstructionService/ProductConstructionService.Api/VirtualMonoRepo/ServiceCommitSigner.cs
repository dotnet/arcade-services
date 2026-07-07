// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.Api.VirtualMonoRepo;

public sealed class ServiceCommitSigner : ICommitSigner
{
    private readonly IProcessManager _processManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServiceCommitSigner> _logger;

    public ServiceCommitSigner(IProcessManager processManager, IConfiguration configuration, ILogger<ServiceCommitSigner> logger)
    {
        _processManager = processManager;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<CommitSigningConfiguration> GetConfigurationAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        string? privateKey = _configuration[PcsStartup.ConfigurationKeys.CodeFlowCommitSigningPrivateKey];
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            return new CommitSigningConfiguration();
        }

        string gnupgHome = Path.Combine(Path.GetTempPath(), $"arcade-services-gpg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(gnupgHome);

        var environmentVariables = new Dictionary<string, string>
        {
            ["GNUPGHOME"] = gnupgHome,
            ["GIT_TERMINAL_PROMPT"] = "0",
        };

        string keyFile = Path.Combine(gnupgHome, "commit-signing.asc");
        await File.WriteAllTextAsync(keyFile, privateKey, cancellationToken);

        var importResult = await _processManager.Execute(
            "gpg",
            ["--batch", "--import", keyFile],
            workingDir: repoPath,
            envVariables: environmentVariables,
            cancellationToken: cancellationToken);

        if (!importResult.Succeeded)
        {
            _logger.LogWarning("Failed to import codeflow signing key: {stderr}", importResult.StandardError);
            return new CommitSigningConfiguration();
        }

        var configureResult = await _processManager.ExecuteGit(
            repoPath,
            ["config", "gpg.format", "openpgp"],
            environmentVariables,
            cancellationToken);

        if (!configureResult.Succeeded)
        {
            _logger.LogWarning("Failed to configure gpg.format for codeflow signing: {stderr}", configureResult.StandardError);
            return new CommitSigningConfiguration();
        }

        var programResult = await _processManager.ExecuteGit(
            repoPath,
            ["config", "gpg.program", "gpg"],
            environmentVariables,
            cancellationToken);

        if (!programResult.Succeeded)
        {
            _logger.LogWarning("Failed to configure gpg.program for codeflow signing: {stderr}", programResult.StandardError);
            return new CommitSigningConfiguration();
        }

        return new CommitSigningConfiguration
        {
            Enabled = true,
            EnvironmentVariables = environmentVariables,
        };
    }
}
