// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading.Tasks;
using Maestro.Common;
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

    public RemoteFactory(
        ILoggerFactory loggerFactory,
        ICommandLineOptions options,
        IServiceProvider serviceProvider)
    {
        _loggerFactory = loggerFactory;
        _options = options;
        _serviceProvider = serviceProvider;
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
                    _loggerFactory.CreateLogger<GitHubClient>(),
                    temporaryRepositoryRoot,
                    // Caching not in use for Darc local client.
                    null),

            GitRepoType.AzureDevOps =>
                new AzureDevOpsClient(
                    options.GetAzdoTokenProvider(),
                    new ProcessManager(_loggerFactory.CreateLogger<IProcessManager>(), options.GitLocation),
                    _loggerFactory.CreateLogger<AzureDevOpsClient>(),
                    temporaryRepositoryRoot),

            _ => throw new InvalidOperationException($"Cannot create a remote of type {repoType}"),
        };
    }
}
