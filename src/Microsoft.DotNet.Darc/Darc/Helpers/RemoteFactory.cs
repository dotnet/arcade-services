// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Helpers;

internal class RemoteFactory : IRemoteFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ICommandLineOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRedisCacheClient _redisCacheClient;

    public RemoteFactory(
        ILoggerFactory loggerFactory,
        ICommandLineOptions options,
        IServiceProvider serviceProvider,
        IRedisCacheClient redisCacheClient)
    {
        _loggerFactory = loggerFactory;
        _options = options;
        _serviceProvider = serviceProvider;
        _redisCacheClient = redisCacheClient;
    }

    public Task<IRemote> CreateRemoteAsync(string repoUrl)
    {
        IRemoteGitRepo gitClient = CreateRemoteGitClient(_options, repoUrl);
        return Task.FromResult<IRemote>(ActivatorUtilities.CreateInstance<Remote>(_serviceProvider, gitClient));
    }

    public Task<IDependencyFileManager> CreateDependencyFileManagerAsync(string repoUrl)
    {
        IRemoteGitRepo gitClient = CreateRemoteGitClient(_options, repoUrl);
        return Task.FromResult<IDependencyFileManager>(ActivatorUtilities.CreateInstance<DependencyFileManager>(_serviceProvider, gitClient));
    }

    private IRemoteGitRepo CreateRemoteGitClient(ICommandLineOptions options, string repoUrl)
    {
        string temporaryRepositoryRoot = Path.GetTempPath();

        var repoType = GitRepoUrlUtils.ParseTypeFromUri(repoUrl);

        return repoType switch
        {
            GitRepoType.GitHub =>
                new GitHubClient(
                    options.GetGitHubTokenProvider(),
                    new ProcessManager(_loggerFactory.CreateLogger<IProcessManager>(), options.GitLocation),
                    temporaryRepositoryRoot,
                    null, // Memory Caching not in use for Darc local client.
                    _redisCacheClient,
                    _loggerFactory.CreateLogger<GitHubClient>()),

            GitRepoType.AzureDevOps =>
                new AzureDevOpsClient(
                    options.GetAzdoTokenProvider(),
                    new ProcessManager(_loggerFactory.CreateLogger<IProcessManager>(), options.GitLocation),
                    _loggerFactory.CreateLogger<AzureDevOpsClient>(),
                    temporaryRepositoryRoot),

            _ => throw new System.InvalidOperationException($"Cannot create a remote of type {repoType}"),
        };
    }
}
