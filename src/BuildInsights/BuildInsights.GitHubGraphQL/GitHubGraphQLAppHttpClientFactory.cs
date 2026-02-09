// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;

namespace BuildInsights.GitHubGraphQL;

public class GitHubGraphQLAppHttpClientFactory : IGitHubGraphQLHttpClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<GitHubGraphQLOptions> _graphQLOptions;
    private readonly IGitHubTokenProvider _tokenProvider;

    public GitHubGraphQLAppHttpClientFactory(
        IHttpClientFactory httpClientFactory,
        IOptions<GitHubGraphQLOptions> gitHubGraphQLOptions,
        IGitHubTokenProvider tokenProvider)
    {
        _httpClientFactory = httpClientFactory;
        _graphQLOptions = gitHubGraphQLOptions;
        _tokenProvider = tokenProvider;
    }
    
    public async Task<HttpClient> GetClient()
    {
        HttpClient client = _httpClientFactory.CreateClient();
        string token = await _tokenProvider.GetTokenForRepository(_graphQLOptions.Value.InstallationRepository);
        client.BaseAddress = new Uri(_graphQLOptions.Value.Endpoint);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", 
            Convert.ToBase64String(Encoding.UTF8.GetBytes($":{token}")));

        return client;
    }
}
