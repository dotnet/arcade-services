// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Helpers;

internal class RemoteFactory : IRemoteFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ICommandLineOptions _options;

    public RemoteFactory(ILoggerFactory loggerFactory, ICommandLineOptions options)
    {
        _loggerFactory = loggerFactory;
        _options = options;
    }

    public Task<IRemote> CreateRemoteAsync(string repoUrl)
    {
        IRemoteGitRepo gitClient = CreateRemoteGitClient(_options, repoUrl);
        return Task.FromResult<IRemote>(new Remote(gitClient, new VersionDetailsParser(), _loggerFactory.CreateLogger<IRemote>()));
    }

    public Task<IDependencyFileManager> CreateDependencyFileManagerAsync(string repoUrl)
    {
        IRemoteGitRepo gitClient = CreateRemoteGitClient(_options, repoUrl);
        var dfm = new DependencyFileManager(gitClient, new VersionDetailsParser(), _loggerFactory.CreateLogger<IDependencyFileManager>());
        return Task.FromResult<IDependencyFileManager>(dfm);
    }

    private IRemoteGitRepo CreateRemoteGitClient(ICommandLineOptions options, string repoUrl)
    {
        string temporaryRepositoryRoot = Path.GetTempPath();

        var repoType = GitRepoUrlParser.ParseTypeFromUri(repoUrl);

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

            _ => throw new System.InvalidOperationException($"Cannot create a remote of type {repoType}"),
        };
    }
}
