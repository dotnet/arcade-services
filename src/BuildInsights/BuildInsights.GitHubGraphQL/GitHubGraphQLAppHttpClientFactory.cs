// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;
using System.Text;
using BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Options;

namespace BuildInsights.GitHubGraphQL;

public class GitHubGraphQLAppHttpClientFactory : IGitHubGraphQLHttpClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGitHubTokenProvider _tokenProvider;
    private readonly IOptions<GitHubGraphQLOptions> _options;

    public GitHubGraphQLAppHttpClientFactory(
        IHttpClientFactory httpClientFactory,
        IGitHubTokenProvider tokenProvider,
        IOptions<GitHubGraphQLOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options;
    }
    
    public async Task<HttpClient> GetClient()
    {
        HttpClient client = _httpClientFactory.CreateClient();
        string token = await _tokenProvider.GetTokenForRepository(_options.Value.InstallationRepository);
        client.BaseAddress = new Uri(_options.Value.Endpoint);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", 
            Convert.ToBase64String(Encoding.UTF8.GetBytes($":{token}")));

        return client;
    }
}
