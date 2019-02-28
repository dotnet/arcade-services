// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Options;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Maestro.AzureDevOps
{
    public class AzureDevOpsTokenProvider : IAzureDevOpsTokenProvider
    {
        private readonly IOptions<AzureDevOpsTokenProviderOptions> _options;
        private readonly Regex accountNameRegex = new Regex(@"^https://dev\.azure\.com/(?<account>[a-zA-Z0-9]+)/");

        public AzureDevOpsTokenProvider(IOptions<AzureDevOpsTokenProviderOptions> options)
        {
            _options = options;
        }

        public AzureDevOpsTokenProviderOptions Options => _options.Value;

        public Task<string> GetTokenForRepository(string repositoryUrl)
        {
            // Identify the instance name, then look up in 
            Match m = accountNameRegex.Match(repositoryUrl);
            if (!m.Success)
            {
                throw new ArgumentException($"{repositoryUrl} is not a valid Azure DevOps repository URL");
            }

            return GetTokenForAccount(m.Groups["account"].Value);
        }

        public Task<string> GetTokenForAccount(string accountName)
        {
            if (!Options.Tokens.TryGetValue(accountName, out string pat) || string.IsNullOrEmpty(pat))
            {
                throw new ArgumentOutOfRangeException($"Account {accountName} does not have a configured PAT. " +
                    $"Please ensure the 'Tokens' array in the 'AzureDevOps' section of settings.json contains a PAT for {accountName}");
            }

            return Task.FromResult<string>(pat);
        }
    }
}
