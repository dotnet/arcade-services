// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace Maestro.AzureDevOps
{
    public class AzureDevOpsTokenProvider : IAzureDevOpsTokenProvider
    {
        private readonly IOptionsMonitor<AzureDevOpsTokenProviderOptions> _options;

        public AzureDevOpsTokenProvider(IOptionsMonitor<AzureDevOpsTokenProviderOptions> options)
        {
            _options = options;
        }

        public Task<string> GetTokenForAccount(string account)
        {
            var options = _options.CurrentValue;
            if (!options.Tokens.TryGetValue(account, out string pat) || string.IsNullOrEmpty(pat))
            {
                throw new ArgumentOutOfRangeException($"Azure DevOps account {account} does not have a configured PAT. " +
                    $"Please ensure the 'Tokens' array in the 'AzureDevOps' section of settings.json contains a PAT for {account}");
            }

            return Task.FromResult<string>(pat);
        }
    }
}
