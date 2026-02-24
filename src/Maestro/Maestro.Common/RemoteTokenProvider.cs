// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common.AzureDevOpsTokens;

namespace Maestro.Common;

public interface IRemoteTokenProvider
{
    string? GetTokenForRepository(string repoUri);

    Task<string?> GetTokenForRepositoryAsync(string repoUri);
}

public class ResolvedTokenProvider(string? token) : IRemoteTokenProvider
{
    public string? GetTokenForRepository(string repoUri) => token;

    public Task<string?> GetTokenForRepositoryAsync(string repoUri) => Task.FromResult(token);
}

public class RemoteTokenProvider : IRemoteTokenProvider
{
    private readonly IRemoteTokenProvider _azdoTokenProvider;
    private readonly IRemoteTokenProvider _gitHubTokenProvider;

    public RemoteTokenProvider()
    {
        _azdoTokenProvider = new ResolvedTokenProvider(null);
        _gitHubTokenProvider = new ResolvedTokenProvider(null);
    }

    public RemoteTokenProvider(
        IRemoteTokenProvider azdoTokenProvider,
        IRemoteTokenProvider gitHubTokenProvider)
    {
        _azdoTokenProvider = azdoTokenProvider;
        _gitHubTokenProvider = gitHubTokenProvider;
    }

    public RemoteTokenProvider(
        IAzureDevOpsTokenProvider azdoTokenProvider,
        string? gitHubToken)
    {
        _azdoTokenProvider = azdoTokenProvider;
        _gitHubTokenProvider = new ResolvedTokenProvider(gitHubToken);
    }

    public RemoteTokenProvider(
        string? azdoToken,
        string? gitHubToken)
    {
        _azdoTokenProvider = new ResolvedTokenProvider(azdoToken);
        _gitHubTokenProvider = new ResolvedTokenProvider(gitHubToken);
    }

    public async Task<string?> GetTokenForRepositoryAsync(string repoUri)
    {
        var repoType = GitRepoUrlUtils.ParseTypeFromUri(repoUri);

        return repoType switch
        {
            GitRepoType.GitHub => _gitHubTokenProvider.GetTokenForRepository(repoUri),
            GitRepoType.AzureDevOps => await _azdoTokenProvider.GetTokenForRepositoryAsync(repoUri),
            GitRepoType.Local => null,
            _ => throw new NotImplementedException($"Unsupported repository remote {repoUri}"),
        };
    }

    public string? GetTokenForRepository(string repoUri)
    {
        var repoType = GitRepoUrlUtils.ParseTypeFromUri(repoUri);

        return repoType switch
        {
            GitRepoType.GitHub => _gitHubTokenProvider.GetTokenForRepository(repoUri),
            GitRepoType.AzureDevOps => _azdoTokenProvider.GetTokenForRepositoryAsync(repoUri).GetAwaiter().GetResult(),
            GitRepoType.Local => null,
            _ => throw new NotImplementedException($"Unsupported repository remote {repoUri}"),
        };
    }
}
