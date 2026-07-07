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

public sealed class ServiceCommitSignatureVerifier : ICommitSignatureVerifier
{
    private readonly IProcessManager _processManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServiceCommitSignatureVerifier> _logger;

    public ServiceCommitSignatureVerifier(IProcessManager processManager, IConfiguration configuration, ILogger<ServiceCommitSignatureVerifier> logger)
    {
        _processManager = processManager;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> VerifyAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default)
    {
        string? publicKey = _configuration[global::ProductConstructionService.Api.PcsStartup.ConfigurationKeys.CodeFlowCommitSigningPublicKey];
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            _logger.LogWarning("Codeflow commit signing public key was not configured; refusing to approve PRs with unverified commits");
            return false;
        }

        string gnupgHome = Path.Combine(Path.GetTempPath(), $"arcade-services-gpg-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(gnupgHome);

        var environmentVariables = new Dictionary<string, string>
        {
            ["GNUPGHOME"] = gnupgHome,
            ["GIT_TERMINAL_PROMPT"] = "0",
        };

        string keyFile = Path.Combine(gnupgHome, "commit-signing-public.asc");
        await File.WriteAllTextAsync(keyFile, publicKey, cancellationToken);

        var importResult = await _processManager.Execute(
            "gpg",
            ["--batch", "--import", keyFile],
            workingDir: repoPath,
            envVariables: environmentVariables,
            cancellationToken: cancellationToken);

        if (!importResult.Succeeded)
        {
            _logger.LogWarning("Failed to import codeflow signing public key for verification: {stderr}", importResult.StandardError);
            return false;
        }

        var configureResult = await _processManager.ExecuteGit(
            repoPath,
            ["config", "gpg.format", "openpgp"],
            environmentVariables,
            cancellationToken);

        if (!configureResult.Succeeded)
        {
            _logger.LogWarning("Failed to configure git gpg.format for verification: {stderr}", configureResult.StandardError);
            return false;
        }

        var verifyResult = await _processManager.ExecuteGit(
            repoPath,
            ["verify-commit", commitSha],
            environmentVariables,
            cancellationToken);

        if (!verifyResult.Succeeded)
        {
            _logger.LogWarning("Commit {sha} could not be verified: {stderr}", commitSha, verifyResult.StandardError);
            return false;
        }

        return true;
    }
}
