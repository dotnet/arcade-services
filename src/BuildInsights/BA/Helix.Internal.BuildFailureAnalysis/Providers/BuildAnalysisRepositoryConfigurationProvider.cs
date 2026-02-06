// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Azure;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Services;
using Microsoft.Extensions.Options;
using Microsoft.Internal.Helix.Utility.Azure;
using Microsoft.Extensions.Logging;
using System;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Providers
{
    public class BuildAnalysisRepositoryConfigurationProvider : IBuildAnalysisRepositoryConfigurationService
    {
        private readonly BuildAnalysisRepositoryConfigurationTableConnectionSettings _repoOptionsTable;
        private readonly ILogger<BuildAnalysisRepositoryConfigurationProvider> _logger;
        private readonly ITableClientFactory _tableClientFactory;
        private const string branchWildCard = "*";

        public BuildAnalysisRepositoryConfigurationProvider(
            IOptions<BuildAnalysisRepositoryConfigurationTableConnectionSettings> repoOptionsTable,
            ITableClientFactory tableClientFactory,
            ILogger<BuildAnalysisRepositoryConfigurationProvider> logger)
        {
            _repoOptionsTable = repoOptionsTable.Value;
            _tableClientFactory = tableClientFactory;
            _logger = logger;
        }

        public async Task<BuildAnalysisRepositoryConfiguration> GetRepositoryConfiguration(string repository, string branch, CancellationToken cancellationToken)
        {
            string normalizedRepository = NormalizeString(repository);
            string normalizedBranch = NormalizeString(branch);
            TableClient tableClient = _tableClientFactory.GetTableClient(_repoOptionsTable.Name, _repoOptionsTable.Endpoint);

            AsyncPageable<BuildAnalysisRepositoryConfiguration> results = tableClient.QueryAsync<BuildAnalysisRepositoryConfiguration>(
                o => o.PartitionKey == normalizedRepository &&
                (o.RowKey.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase) || o.RowKey == branchWildCard),
                cancellationToken: cancellationToken);

            List<BuildAnalysisRepositoryConfiguration> repoConfigs = new();

            await foreach (Page<BuildAnalysisRepositoryConfiguration> page in results.AsPages().WithCancellation(cancellationToken))
            {
                repoConfigs.AddRange(page.Values);
            }

            // We only expect a single entry per repo for a specific branch.
            // If there are multiple, log as this is an unexpected scenario.
            if (repoConfigs.Count(c => c.RowKey.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase)) > 1)
            {
                _logger.LogWarning("Found multiple Build Analysis configurations for repo {repo} at branch {branch}.", repository, branch);
            }

            // Attempt to return the branch specific configuration if it exists,
            // or the repo global configuration using the wildcard if it doesn't
            return repoConfigs.FirstOrDefault(c => c.RowKey.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase)) ??
                repoConfigs.FirstOrDefault(c => c.RowKey == branchWildCard);
        }

        private static string NormalizeString(string value)
        {
            return $"{value?.Replace('/', '-')}";
        }
    }
}
