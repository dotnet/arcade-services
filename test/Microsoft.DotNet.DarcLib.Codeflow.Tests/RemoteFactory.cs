// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading.Tasks;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

internal class RemoteFactory : IRemoteFactory
{
    private readonly IProcessManager _processManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRedisCacheClient _redisCacheClient;

    public RemoteFactory(
        IProcessManager processManager,
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        IRedisCacheClient redisCacheClient)
    {
        _processManager = processManager;
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
        _redisCacheClient = redisCacheClient;
    }


    public Task<IRemote> CreateRemoteAsync(string repoUrl)
    {
        IRemoteGitRepo gitClient = CreateRemoteGitClient(repoUrl);
        return Task.FromResult<IRemote>(ActivatorUtilities.CreateInstance<Remote>(_serviceProvider, gitClient));
    }

    public Task<IDependencyFileManager> CreateDependencyFileManagerAsync(string repoUrl)
    {
        IRemoteGitRepo gitClient = CreateRemoteGitClient(repoUrl);
        return Task.FromResult<IDependencyFileManager>(ActivatorUtilities.CreateInstance<DependencyFileManager>(_serviceProvider, gitClient));
    }

    private IRemoteGitRepo CreateRemoteGitClient(string repoUrl)
    {
        var temporaryRepositoryRoot = Path.GetTempPath();

        var repoType = GitRepoUrlUtils.ParseTypeFromUri(repoUrl);

        return repoType switch
        {
            GitRepoType.GitHub =>
                new GitHubClient(
                    new ResolvedTokenProvider(null),
                    _processManager,
                    temporaryRepositoryRoot,
                    null, // Caching not in use for Darc local client.
                    _redisCacheClient,
                    _loggerFactory.CreateLogger<GitHubClient>()),

            GitRepoType.AzureDevOps =>
                new AzureDevOpsClient(
                    AzureDevOpsTokenProvider.FromStaticOptions([]),
                    _processManager,
                    _loggerFactory.CreateLogger<AzureDevOpsClient>(),
                    temporaryRepositoryRoot),

            _ => throw new NotSupportedException($"Unsupported repo type: {repoType}"),
        };
    }
}
