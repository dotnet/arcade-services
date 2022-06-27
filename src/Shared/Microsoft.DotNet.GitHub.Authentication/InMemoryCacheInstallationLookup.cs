// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace Microsoft.DotNet.GitHub.Authentication
{
    public class InMemoryCacheInstallationLookup : IInstallationLookup
    {
        private readonly IGitHubAppTokenProvider _tokens;
        private readonly IOptions<GitHubClientOptions> _options;
        private readonly ILogger<InMemoryCacheInstallationLookup> _log;
        private readonly SemaphoreSlim _sem = new SemaphoreSlim(1, 1);

        private ImmutableDictionary<string, long> _cache = ImmutableDictionary<string, long>.Empty;
        private Dictionary<string, ImmutableDictionary<string, long>> _cacheForApp = new Dictionary<string, ImmutableDictionary<string, long>>();

        private DateTimeOffset _lastCached = DateTimeOffset.MinValue;
        private Dictionary<string, DateTimeOffset> _lastCachedForApp = new Dictionary<string, DateTimeOffset>();

        public InMemoryCacheInstallationLookup(
            IGitHubAppTokenProvider tokens,
            IOptions<GitHubClientOptions> options,
            ILogger<InMemoryCacheInstallationLookup> log)
        {
            _tokens = tokens;
            _options = options;
            _log = log;
        }

        public async Task<bool> IsOrganizationSupported(string org)
        {
            return await GetInstallationIdForOrg(org.ToLowerInvariant()) != 0;
        }

        public async Task<long> GetInstallationId(string repositoryUrl)
        {
            string[] segments = new Uri(repositoryUrl, UriKind.Absolute).Segments;
            string org = segments[^2].TrimEnd('/').ToLowerInvariant();
            
            return await GetInstallationIdForOrg(org);
        }

        public async Task<long> GetInstallationIdForApp(string appName, string repositoryUrl)
        {
            string[] segments = new Uri(repositoryUrl, UriKind.Absolute).Segments;
            string org = segments[^2].TrimEnd('/').ToLowerInvariant();

            return await GetInstallationIdForAppForOrg(appName, org);
        }

        public async Task<bool> IsOrganizationSupportedForApp(string appName, string org)
        {
            return await GetInstallationIdForAppForOrg(appName, org.ToLowerInvariant()) != 0;
        }

        private async Task<long> GetInstallationIdForOrg(string org)
        {
            string[] allowed = _options.Value.AllowOrgs;
            if (allowed != null && !allowed.Contains(org))
            {
                // We have an allow list, and this org isn't in it, bail
                return 0;
            }

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

        private async Task<long> GetInstallationIdForAppForOrg(string appName, string org)
        {
            string[] allowed = _options.Value.AllowOrgs;
            if (allowed != null && !allowed.Contains(org))
            {
                // We have an allow list, and this org isn't in it, bail
                return 0;
            }

            if (HasCachedValueForApp(appName, org, out long installation))
            {
                return installation;
            }

            await _sem.WaitAsync();
            try
            {
                if (HasCachedValueForApp(appName, org, out installation))
                {
                    return installation;
                }

                _log.LogInformation("No cached installation token found for {org} for app {app}", org, appName);

                string appToken = _tokens.GetAppToken(appName);
                var client = new GitHubAppsClient(
                    new ApiConnection(
                        new Connection(_options.Value.ProductHeader)
                        {
                            Credentials = new Credentials(appToken, AuthenticationType.Bearer)
                        }
                    )
                );

                ImmutableDictionary<string, long>.Builder newCache = ImmutableDictionary.CreateBuilder<string, long>();
                foreach (Installation i in await client.GetAllInstallationsForCurrent())
                {
                    string installedOrg = i.Account.Login.ToLowerInvariant();
                    _log.LogInformation("Found installation token for {org}, with id {id} for app {app}", installedOrg, i.Id, appName);
                    newCache.Add(installedOrg, i.Id);
                }

                _cacheForApp.TryGetValue(appName, out ImmutableDictionary<string, long> cacheForApp);
                cacheForApp ??= ImmutableDictionary<string, long>.Empty;
                foreach (string key in cacheForApp.Keys)
                {
                    // Anything we had before but don't have now has been uninstalled, remove it
                    if (newCache.TryAdd(key, 0))
                    {
                        _log.LogInformation("Removed uninstalled org {org} for app {app}", key, appName);
                    }
                }

                // If the current thing wasn't in this list, it's not installed, record that so when they ask again in
                // a few seconds, we don't have to re-query GitHub
                if (newCache.TryAdd(org, 0))
                {
                    _log.LogInformation("Removed uninstalled, but requested org {org} for app {app}", org, appName);
                }

                Interlocked.Exchange(ref cacheForApp, newCache.ToImmutable());
                _cacheForApp[appName] = cacheForApp;
                _lastCachedForApp[appName] = DateTimeOffset.UtcNow;

                return _cacheForApp[appName][org];
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

        private bool HasCachedValueForApp(string appName, string repositoryUrl, out long installation)
        {
            DateTimeOffset lastCached = _lastCachedForApp.GetValueOrDefault(appName, DateTimeOffset.MinValue);
            if (lastCached + TimeSpan.FromMinutes(15) < DateTimeOffset.UtcNow)
            {
                installation = 0;
                return false;
            }

            return _cacheForApp[appName].TryGetValue(repositoryUrl, out installation);
        }
    }
}
