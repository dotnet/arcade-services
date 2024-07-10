// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
