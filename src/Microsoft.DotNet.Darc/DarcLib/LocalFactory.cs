// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Maestro.Common.Telemetry;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib;

public interface ILocalFactory
{
    ILocal CreateLocalGitClient(string repoPath);
}

public class LocalFactory(
    IDependencyFileManagerFactory dependencyFileManagerFactory,
    IRemoteTokenProvider tokenProvider,
    ILoggerFactory loggerFactory)
    : ILocalFactory
{
    private readonly IDependencyFileManagerFactory _dependencyFileManagerFactory = dependencyFileManagerFactory;
    private readonly IRemoteTokenProvider _tokenProvider = tokenProvider;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public ILocal CreateLocalGitClient(string repoPath)
    {
        var gitClient =  new LocalLibGit2Client(
            _tokenProvider,
            new NoTelemetryRecorder(),
            new ProcessManager(_loggerFactory.CreateLogger<ProcessManager>(), "git"),
            new FileSystem(),
            _loggerFactory.CreateLogger<LocalLibGit2Client>());

        var dependencyFileManager = _dependencyFileManagerFactory.CreateDependencyFileManager(gitClient);

        return new Local(
            dependencyFileManager,
            gitClient,
            _loggerFactory.CreateLogger<Local>(),
            repoPath);
    }
}
