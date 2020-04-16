// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.Dotnet.GitHub.Authentication;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Maestro.DataProviders
{
    public class DarcRemoteFactory : IRemoteFactory
    {
        public DarcRemoteFactory(
            BuildAssetRegistryContext context,
            IGitHubTokenProvider githubTokenProvider,
            IKustoClientProvider kustoClientProvider)
        {
            Context = context;
            GHProvider = githubTokenProvider;
            KustoClientProvider = (KustoClientProvider) kustoClientProvider;
        }
        
        private IGitHubTokenProvider GHProvider { get; set; }

        public BuildAssetRegistryContext Context { get; }

        private readonly KustoClientProvider KustoClientProvider;

        public Task<IRemote> GetBarOnlyRemoteAsync(ILogger logger)
        {
            return Task.FromResult((IRemote)new Remote(null, new MaestroBarClient(Context, KustoClientProvider), logger));
        }

        public Task<IRemote> GetRemoteAsync(string repoUrl, ILogger logger)
        {
            return Task.FromResult((IRemote)new Remote(null, new MaestroBarClient(Context, KustoClientProvider), logger));
        }
    }
}
