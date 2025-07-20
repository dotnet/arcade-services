// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
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
    private readonly IRemoteTokenProvider _remoteTokenProvider;
    private readonly IAzureDevOpsTokenProvider _azdoTokenProvider;
    private readonly ITelemetryRecorder _telemetryRecorder;
    private readonly IProcessManager _processManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string? _temporaryPath = null;
    private readonly IRedisCacheClient _redisCacheClient;

    public GitRepoFactory(
        IRemoteTokenProvider remoteTokenProvider,
        IAzureDevOpsTokenProvider azdoTokenProvider,
        ITelemetryRecorder telemetryRecorder,
        IProcessManager processManager,
        IFileSystem fileSystem,
        ILoggerFactory loggerFactory,
        string temporaryPath,
        IRedisCacheClient redisCacheClient)
    {
        _remoteTokenProvider = remoteTokenProvider;
        _azdoTokenProvider = azdoTokenProvider;
        _telemetryRecorder = telemetryRecorder;
        _processManager = processManager;
        _fileSystem = fileSystem;
        _loggerFactory = loggerFactory;
        _temporaryPath = temporaryPath;
        _redisCacheClient = redisCacheClient;
    }

    public IGitRepo CreateClient(string repoUri) => GitRepoUrlUtils.ParseTypeFromUri(repoUri) switch
    {
        GitRepoType.AzureDevOps => new AzureDevOpsClient(
            _azdoTokenProvider,
            _processManager,
            _loggerFactory.CreateLogger<AzureDevOpsClient>(),
            _temporaryPath),

        GitRepoType.GitHub => new GitHubClient(
            _remoteTokenProvider,
            _processManager,
            _temporaryPath,
            // Caching not in use for Darc local client.
            null,
            _redisCacheClient,
            _loggerFactory.CreateLogger<GitHubClient>()),

        GitRepoType.Local => new LocalLibGit2Client(
            _remoteTokenProvider,
            _telemetryRecorder,
            _processManager,
            _fileSystem,
            _loggerFactory.CreateLogger<LocalGitClient>()),

        _ => throw new ArgumentException("Unknown git repository type", nameof(repoUri)),
    };
}
