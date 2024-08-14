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
        => new BarApiClient(
            options.BuildAssetRegistryToken,
            managedIdentityId: null,
            options.IsCi,
            options.BuildAssetRegistryBaseUri);

    public Task<IRemote> GetRemoteAsync(string repoUrl, ILogger logger)
        => Task.FromResult(GetRemote(_options, repoUrl, logger));

    public Task<IDependencyFileManager> GetDependencyFileManagerAsync(string repoUrl, ILogger logger)
    {
        IRemoteGitRepo gitClient = GetRemoteGitClient(_options, repoUrl, logger);
        return Task.FromResult<IDependencyFileManager>(new DependencyFileManager(gitClient, new VersionDetailsParser(), logger));
    }

    private static IRemoteGitRepo GetRemoteGitClient(ICommandLineOptions options, string repoUrl, ILogger logger)
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
