// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GitHubJwt;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Options;
using Octokit;

namespace DotNet.Status.Web
{
    public class InMemoryCacheInstallationLookup : IInstallationLookup
    {
        private readonly GitHubJwtFactory _jwtFactory;
        private readonly IOptions<GitHubClientOptions> _options;

        public InMemoryCacheInstallationLookup(
            GitHubJwtFactory jwtFactory,
            IOptions<GitHubClientOptions> options)
        {
            _jwtFactory = jwtFactory;
            _options = options;
        }

        private Dictionary<string, long> _cache = new Dictionary<string,long>();
        private DateTimeOffset _lastCached = DateTimeOffset.MinValue;
        private readonly SemaphoreSlim _sem = new SemaphoreSlim(1, 1);

        public async Task<long> GetInstallationId(string repositoryUrl)
        {
            var segments = new Uri(repositoryUrl, UriKind.Absolute).Segments;
            var org = segments[segments.Length - 2].TrimEnd('/');

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

                var newCache = new Dictionary<string, long>();
                string appToken = _jwtFactory.CreateEncodedJwtToken();
                GitHubAppsClient client = new GitHubAppsClient(
                    new ApiConnection(
                        new Connection(_options.Value.ProductHeader)
                        {
                            Credentials = new Credentials(appToken, AuthenticationType.Bearer)
                        }
                    )
                );

                foreach (Installation i in await client.GetAllInstallationsForCurrent())
                {
                    newCache[org] = i.Id;
                }

                Interlocked.Exchange(ref _cache, newCache);
                _lastCached = DateTimeOffset.UtcNow;
                
                if (_cache.TryGetValue(org, out installation))
                {
                    return installation;
                }
                else
                {
                    _cache[org] = 0;
                    return 0;
                }
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
