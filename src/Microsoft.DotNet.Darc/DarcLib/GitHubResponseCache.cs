// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.Extensions.Logging;
using Octokit;
using Octokit.Caching;
using Octokit.Internal;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

internal sealed class GitHubResponseCache(ICache cache, ILogger logger) : IResponseCache
{
    private const string CacheKeyPrefix = "GitHubConditionalResponse:v1:";
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);
    // Query parameters (for example, ?page=2) are not part of the matched AbsolutePath.
    private static readonly Regex[] AllowedPathRegexes =
    [
        // PR details: /repos/dotnet/runtime/pulls/123
        new(@"^/repos/[^/]+/[^/]+/pulls/[0-9]+$", RegexOptions.CultureInvariant),
        // PR commits: /repos/dotnet/runtime/pulls/123/commits
        new(@"^/repos/[^/]+/[^/]+/pulls/[0-9]+/commits$", RegexOptions.CultureInvariant),
        // Combined statuses: /repos/dotnet/runtime/commits/abc123/status
        new(@"^/repos/[^/]+/[^/]+/commits/[^/]+/status$", RegexOptions.CultureInvariant),
        // Check runs: /repos/dotnet/runtime/commits/abc123/check-runs
        new(@"^/repos/[^/]+/[^/]+/commits/[^/]+/check-runs$", RegexOptions.CultureInvariant),
    ];

    public async Task<CachedResponse.V1?> GetAsync(IRequest request)
    {
        if (!IsEligible(request))
        {
            return null;
        }

        try
        {
            var response = await cache.TryGetAsync<GitHubCachedResponse>(GetKey(request));
            logger.LogDebug("GitHub conditional response cache lookup for {endpoint}: {cacheHit}", request.Endpoint, response != null);
            if (response == null || string.IsNullOrEmpty(response.Etag))
            {
                return null;
            }

            return new CachedResponse.V1(
                response.Body,
                response.Headers,
                new ApiInfo(response.Links, response.OauthScopes, response.AcceptedOauthScopes,
                    response.Etag, new RateLimit(response.Headers), response.ServerTimeDifference),
                HttpStatusCode.OK,
                response.ContentType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read GitHub conditional response cache for {endpoint}", request.Endpoint);
            throw;
        }
    }

    public async Task SetAsync(IRequest request, CachedResponse.V1 response)
    {
        if (!IsEligible(request))
        {
            return;
        }

        try
        {
            var key = GetKey(request);
            if (response.StatusCode == HttpStatusCode.OK && response.Body is string body)
            {
                // A response without an ETag replaces any previous entry, but cannot be reused.
                var entry = new GitHubCachedResponse(
                    string.IsNullOrEmpty(response.ApiInfo.Etag) ? string.Empty : body,
                    new Dictionary<string, string>(response.Headers),
                    response.ContentType,
                    response.ApiInfo.Etag,
                    new Dictionary<string, Uri>(response.ApiInfo.Links),
                    response.ApiInfo.OauthScopes.ToArray(),
                    response.ApiInfo.AcceptedOauthScopes.ToArray(),
                    response.ApiInfo.ServerTimeDifference);
                if (await cache.TrySetAsync(key, entry, Retention))
                {
                    logger.LogDebug("Stored GitHub conditional response for {endpoint}", request.Endpoint);
                }
                else
                {
                    logger.LogWarning("Failed to store GitHub conditional response for {endpoint}", request.Endpoint);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to store GitHub conditional response cache for {endpoint}", request.Endpoint);
            throw;
        }
    }

    private static bool IsEligible(IRequest request)
    {
        var uri = new Uri(request.BaseAddress, request.Endpoint);
        return request.Method == HttpMethod.Get &&
            uri.Scheme == Uri.UriSchemeHttps &&
            uri.Host == "api.github.com" &&
            uri.IsDefaultPort &&
            AllowedPathRegexes.Any(regex => regex.IsMatch(uri.AbsolutePath));
    }

    private static string GetKey(IRequest request) =>
        CacheKeyPrefix + new Uri(request.BaseAddress, request.Endpoint).AbsoluteUri;

    internal sealed record GitHubCachedResponse(
        string Body,
        Dictionary<string, string> Headers,
        string ContentType,
        string? Etag,
        Dictionary<string, Uri> Links,
        string[] OauthScopes,
        string[] AcceptedOauthScopes,
        TimeSpan ServerTimeDifference);
}
