// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;
using System.Text;
using Microsoft.DotNet.GitHub.Authentication;

namespace BuildInsights.GitHubGraphQL;

public class GitHubGraphQLAppHttpClientFactory : IGitHubGraphQLHttpClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGitHubTokenProvider _tokenProvider;

    public GitHubGraphQLAppHttpClientFactory(
        IHttpClientFactory httpClientFactory,
        IGitHubTokenProvider tokenProvider)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
    }
    
    public HttpClient GetClient()
    {
        HttpClient client = _httpClientFactory.CreateClient();
        string token = _tokenProvider.GetTokenForApp();
        client.BaseAddress = new Uri("https://api.github.com/graphql");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", 
            Convert.ToBase64String(Encoding.UTF8.GetBytes($":{token}")));

        return client;
    }
}
