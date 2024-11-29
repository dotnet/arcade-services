// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    public static IBarApiClient GetBarClient(ICommandLineOptions options)
        => new BarApiClient(
            options.BuildAssetRegistryToken,
            managedIdentityId: null,
            options.IsCi,
            options.BuildAssetRegistryBaseUri);

    public Task<IRemote> CreateRemoteAsync(string repoUrl)
    {
        var logger = _loggerFactory.CreateLogger<IRemote>();
        IRemoteGitRepo gitClient = CreateRemoteGitClient(_options, repoUrl, logger);
        return Task.FromResult<IRemote>(new Remote(gitClient, new VersionDetailsParser(), logger));
    }

    public Task<IDependencyFileManager> CreateDependencyFileManagerAsync(string repoUrl)
    {
        IRemoteGitRepo gitClient = CreateRemoteGitClient(_options, repoUrl, _loggerFactory.CreateLogger<IRemote>());
        return Task.FromResult<IDependencyFileManager>(new DependencyFileManager(gitClient, new VersionDetailsParser(), _loggerFactory.CreateLogger<IDependencyFileManager>()));
    }

    private static IRemoteGitRepo CreateRemoteGitClient(ICommandLineOptions options, string repoUrl, ILogger logger)
    {
        string temporaryRepositoryRoot = Path.GetTempPath();

        var repoType = GitRepoUrlParser.ParseTypeFromUri(repoUrl);

        return repoType switch
        {
            GitRepoType.GitHub =>
                new GitHubClient(
                    options.GetGitHubTokenProvider(),
                    new ProcessManager(logger, options.GitLocation),
                    logger,
                    temporaryRepositoryRoot,
                    // Caching not in use for Darc local client.
                    null),

            GitRepoType.AzureDevOps =>
                new AzureDevOpsClient(
                    options.GetAzdoTokenProvider(),
                    new ProcessManager(logger, options.GitLocation),
                    logger,
                    temporaryRepositoryRoot),

            _ => throw new System.InvalidOperationException($"Cannot create a remote of type {repoType}"),
        };
    }
}
