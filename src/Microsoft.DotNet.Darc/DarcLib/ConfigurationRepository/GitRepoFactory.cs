// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Maestro.Common;

namespace Microsoft.DotNet.DarcLib.ConfigurationRepository;

public class GitRepoFactory : MaestroConfiguration.Client.IGitRepoFactory
{
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IRemoteFactory _remoteFactory;

    public GitRepoFactory(
        IGitRepoFactory gitRepoFactory,
        ILocalGitRepoFactory localGitRepoFactory,
        IRemoteFactory remoteFactory)
    {
        _gitRepoFactory = gitRepoFactory;
        _localGitRepoFactory = localGitRepoFactory;
        _remoteFactory = remoteFactory;
    }

    public async Task<MaestroConfiguration.Client.IGitRepo> CreateClient(string repoUri)
    {
        var gitRepo = _gitRepoFactory.CreateClient(repoUri);
        return GitRepoUrlUtils.ParseTypeFromUri(repoUri) switch
        {
            GitRepoType.Local => new LocalGitRepo(gitRepo, _localGitRepoFactory.Create(new Helpers.NativePath(repoUri))),
            GitRepoType.GitHub or GitRepoType.AzureDevOps => new RemoteGitRepo(gitRepo, await _remoteFactory.CreateRemoteAsync(repoUri)),
            _ => throw new NotSupportedException($"Git repository type for '{repoUri}' is not supported."),
        };
    }
}
