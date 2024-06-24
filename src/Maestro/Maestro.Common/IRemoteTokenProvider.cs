// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.Common;

public interface IRemoteTokenProvider
{
    string? GetTokenForRepository(string repoUri);

    Task<string?> GetTokenForRepositoryAsync(string repoUri);
}

public class ResolvedTokenProvider : IRemoteTokenProvider
{
    private readonly string? _token;

    public ResolvedTokenProvider(string? token)
    {
        _token = token;
    }

    public string? GetTokenForRepository(string repoUri)
    {
        return _token;
    }

    public Task<string?> GetTokenForRepositoryAsync(string repoUri)
    {
        return Task.FromResult(_token);
    }
}
