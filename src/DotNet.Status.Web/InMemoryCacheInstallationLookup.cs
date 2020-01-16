// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using GitHubJwt;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace DotNet.Status.Web
{
    public class InMemoryCacheInstallationLookup : IInstallationLookup
    {
        private readonly GitHubAppTokenProvider _tokens;
        private readonly IOptions<GitHubClientOptions> _options;
        private readonly ILogger<InMemoryCacheInstallationLookup> _log;
        private readonly SemaphoreSlim _sem = new SemaphoreSlim(1, 1);

        private ImmutableDictionary<string, long> _cache = ImmutableDictionary<string, long>.Empty;
        private DateTimeOffset _lastCached = DateTimeOffset.MinValue;

        public InMemoryCacheInstallationLookup(
            GitHubAppTokenProvider tokens,
            IOptions<GitHubClientOptions> options,
            ILogger<InMemoryCacheInstallationLookup> log)
        {
            _tokens = tokens;
            _options = options;
            _log = log;
        }

        public async Task<long> GetInstallationId(string repositoryUrl)
        {
            string[] segments = new Uri(repositoryUrl, UriKind.Absolute).Segments;
            string org = segments[segments.Length - 2].TrimEnd('/').ToLowerInvariant();

            if (HasCachedValue(org, out long installation))
            {
                return installation;
            }

            await _sem.WaitAsync();
            try
            {
                if (HasCachedValue(org, out installation))
                {
                    return installation;
                }

                _log.LogInformation("No cached installation token found for {org}", org);

                ImmutableDictionary<string, long>.Builder newCache = ImmutableDictionary.CreateBuilder<string, long>();
                string appToken = _tokens.GetAppToken();
                var client = new GitHubAppsClient(
                    new ApiConnection(
                        new Connection(_options.Value.ProductHeader)
                        {
                            Credentials = new Credentials(appToken, AuthenticationType.Bearer)
                        }
                    )
                );

                foreach (Installation i in await client.GetAllInstallationsForCurrent())
                {
                    string installedOrg = i.Account.Login.ToLowerInvariant();
                    _log.LogInformation("Found installation token for {org}, with id {id}", installedOrg, i.Id);
                    newCache.Add(installedOrg, i.Id);
                }

                foreach (string key in _cache.Keys)
                {
                    // Anything we had before but don't have now has been uninstalled, remove it
                    if (newCache.TryAdd(key, 0))
                    {
                        _log.LogInformation("Removed uninstalled org {org}", key);
                    }
                }

                // If the current thing wasn't in this list, it's not installed, record that so when they ask again in
                // a few seconds, we don't have to re-query GitHub
                if (newCache.TryAdd(org, 0))
                {
                    _log.LogInformation("Removed uninstalled, but requested org {org}", org);
                }

                Interlocked.Exchange(ref _cache, newCache.ToImmutable());
                _lastCached = DateTimeOffset.UtcNow;

                return _cache[org];
            }
            finally
            {
                _sem.Release();
            }
        }

        private bool HasCachedValue(string repositoryUrl, out long installation)
        {
            if (_lastCached + TimeSpan.FromMinutes(15) < DateTimeOffset.UtcNow)
            {
                installation = 0;
                return false;
            }

            return _cache.TryGetValue(repositoryUrl, out installation);
        }
    }
}
