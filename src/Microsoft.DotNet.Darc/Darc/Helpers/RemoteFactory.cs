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
    private readonly ICommandLineOptions _options;

    public RemoteFactory(ICommandLineOptions options)
    {
        _options = options;
    }

    public static IRemote GetRemote(ICommandLineOptions options, string repoUrl, ILogger logger)
    {
        IRemoteGitRepo gitClient = GetRemoteGitClient(options, repoUrl, logger);
        return new Remote(gitClient, new VersionDetailsParser(), logger);
    }

    public static IBarApiClient GetBarClient(ICommandLineOptions options, ILogger logger)
    {
        DarcSettings darcSettings = LocalSettings.GetDarcSettings(options, logger);
        return new BarApiClient(
            darcSettings?.BuildAssetRegistryPassword,
            darcSettings?.BuildAssetRegistryBaseUri);
    }

    public Task<IRemote> GetRemoteAsync(string repoUrl, ILogger logger)
        => Task.FromResult(GetRemote(_options, repoUrl, logger));

    public Task<IDependencyFileManager> GetDependencyFileManagerAsync(string repoUrl, ILogger logger)
    {
        IRemoteGitRepo gitClient = GetRemoteGitClient(_options, repoUrl, logger);
        return Task.FromResult<IDependencyFileManager>(new DependencyFileManager(gitClient, new VersionDetailsParser(), logger));
    }

    private static IRemoteGitRepo GetRemoteGitClient(ICommandLineOptions options, string repoUrl, ILogger logger)
    {
        DarcSettings darcSettings = LocalSettings.GetDarcSettings(options, logger, repoUrl);

        if (darcSettings.GitType != GitRepoType.None &&
            string.IsNullOrEmpty(darcSettings.GitRepoPersonalAccessToken))
        {
            throw new DarcException($"No personal access token was provided for repo type '{darcSettings.GitType}'");
        }

        // If a temporary repository root was not provided, use the environment
        // provided temp directory.
        string temporaryRepositoryRoot = darcSettings.TemporaryRepositoryRoot;
        if (string.IsNullOrEmpty(temporaryRepositoryRoot))
        {
            temporaryRepositoryRoot = Path.GetTempPath();
        }

        return darcSettings.GitType switch
        {
            GitRepoType.GitHub =>
                new GitHubClient(
                    options.GitLocation,
                    darcSettings.GitRepoPersonalAccessToken,
                    logger,
                    temporaryRepositoryRoot,
                    // Caching not in use for Darc local client.
                    null),

            GitRepoType.AzureDevOps =>
                new AzureDevOpsClient(
                    options.GitLocation,
                    darcSettings.GitRepoPersonalAccessToken,
                    logger,
                    temporaryRepositoryRoot),

            _ => throw new System.InvalidOperationException($"Cannot create a remote of type {darcSettings.GitType}"),
        };
    }
}
