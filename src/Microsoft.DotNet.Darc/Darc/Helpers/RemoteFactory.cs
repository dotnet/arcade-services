// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Helpers;

internal class RemoteFactory : IRemoteFactory
{
    CommandLineOptions _options;

    public RemoteFactory(CommandLineOptions options)
    {
        _options = options;
    }

    /// <summary>
    ///     Get a remote for a specific repo.
    /// </summary>
    /// <param name="options">Command line options</param>
    /// <param name="repoUrl">Repository url</param>
    /// <param name="logger">Logger</param>
    /// <returns>New remote</returns>
    public static IRemote GetRemote(CommandLineOptions options, string repoUrl, ILogger logger)
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

        IRemoteGitRepo gitClient = null;
        if (darcSettings.GitType == GitRepoType.GitHub)
        {
            gitClient = new GitHubClient(options.GitLocation, darcSettings.GitRepoPersonalAccessToken,
                logger,
                temporaryRepositoryRoot,
                // Caching not in use for Darc local client.
                null);
        }
        else if (darcSettings.GitType == GitRepoType.AzureDevOps)
        {
            gitClient = new AzureDevOpsClient(options.GitLocation, darcSettings.GitRepoPersonalAccessToken,
                logger,
                temporaryRepositoryRoot);
        }

        IBarClient barClient = null;
        if (!string.IsNullOrEmpty(darcSettings.BuildAssetRegistryPassword))
        {
            barClient = new MaestroApiBarClient(darcSettings.BuildAssetRegistryPassword,
                darcSettings.BuildAssetRegistryBaseUri);
        }

        return new Remote(gitClient, barClient, new VersionDetailsParser(), logger);
    }

    /// <summary>
    ///     Get a build asset registry only remote for a specific repo.
    /// </summary>
    /// <param name="options">Command line options</param>
    /// <param name="logger">Logger</param>
    /// <returns>New remote</returns>
    public static IBarRemote GetBarOnlyRemote(CommandLineOptions options, ILogger logger)
    {
        DarcSettings darcSettings = LocalSettings.GetDarcSettings(options, logger);

        if (darcSettings.GitType != GitRepoType.None &&
            string.IsNullOrEmpty(darcSettings.GitRepoPersonalAccessToken))
        {
            throw new DarcException($"No personal access token was provided for repo type '{darcSettings.GitType}'");
        }

        IBarClient barClient = null;
        if (!string.IsNullOrEmpty(darcSettings.BuildAssetRegistryPassword))
        {
            barClient = new MaestroApiBarClient(darcSettings.BuildAssetRegistryPassword,
            darcSettings.BuildAssetRegistryBaseUri);
        }

        return new BarRemote(barClient, logger);
    }

    /// <summary>
    ///     Retrieve a remote based on repository URI, which sets up the git client.
    ///     Also sets up the BAR client.
    /// </summary>
    /// <param name="repoUrl">Repository url to get a remote for</param>
    /// <param name="logger">Logger</param>
    /// <returns>New remote</returns>
    public Task<IRemote> GetRemoteAsync(string repoUrl, ILogger logger)
        => Task.FromResult(GetRemote(_options, repoUrl, logger));

    public Task<IBarRemote> GetBarOnlyRemoteAsync(ILogger logger)
        => Task.FromResult(GetBarOnlyRemote(_options, logger));
}
