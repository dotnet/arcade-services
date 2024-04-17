// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public interface IGitRepoFactory
{
    IGitRepo CreateClient(string repoUri);
}
public class GitRepoFactory : IGitRepoFactory
{
    private readonly RemoteConfiguration _remoteConfiguration;
    private readonly ITelemetryRecorder _telemetryRecorder;
    private readonly IProcessManager _processManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string? _temporaryPath = null;

    public GitRepoFactory(
        RemoteConfiguration remoteConfiguration,
        ITelemetryRecorder telemetryRecorder,
        IProcessManager processManager,
        IFileSystem fileSystem,
        ILoggerFactory loggerFactory,
        string temporaryPath)
    {
        _remoteConfiguration = remoteConfiguration;
        _telemetryRecorder = telemetryRecorder;
        _processManager = processManager;
        _fileSystem = fileSystem;
        _loggerFactory = loggerFactory;
        _temporaryPath = temporaryPath;
    }

    public IGitRepo CreateClient(string repoUri) => GitRepoUrlParser.ParseTypeFromUri(repoUri) switch
    {
        GitRepoType.AzureDevOps => new AzureDevOpsClient(
            _processManager.GitExecutable,
            _remoteConfiguration.AzureDevOpsToken,
            _loggerFactory.CreateLogger<AzureDevOpsClient>(),
            _temporaryPath),

        GitRepoType.GitHub => new GitHubClient(
            _processManager.GitExecutable,
            _remoteConfiguration.GitHubToken,
            _loggerFactory.CreateLogger<GitHubClient>(),
            _temporaryPath,
            // Caching not in use for Darc local client.
            null),

        GitRepoType.Local => new LocalLibGit2Client(
            _remoteConfiguration,
            _telemetryRecorder,
            _processManager,
            _fileSystem,
            _loggerFactory.CreateLogger<LocalGitClient>()),

        _ => throw new ArgumentException("Unknown git repository type", nameof(repoUri)),
    };
}
