// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Caching.Memory;

namespace Maestro.DataProviders
{
    public class DarcRemoteMemoryCache
    {
        public MemoryCache Cache { get; set; }
        public DarcRemoteMemoryCache()
        {
            Cache = new MemoryCache(new MemoryCacheOptions
            {
                // The cache is generally targeted towards small objects, like
                // files returned from GitHub or Azure DevOps, to reduce API calls.
                // Limit the cache size to 64MB to avoid
                // large amounts of growth if the service is alive for long periods of time.
                SizeLimit = 1024 * 1024 * 64
            });
        }
    }
}
