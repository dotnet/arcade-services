// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Maestro.AzureDevOps
{
    public interface IAzureDevOpsTokenProvider
    {
        Task<string> GetTokenForAccount(string account);
    }

    public static class AzureDevOpsTokenProviderExtensions
    {
        private static readonly Regex AccountNameRegex = new Regex(@"^https://dev\.azure\.com/(?<account>[a-zA-Z0-9]+)/");

        public static Task<string> GetTokenForRepository(this IAzureDevOpsTokenProvider that, string repositoryUrl)
        {
            Match m = AccountNameRegex.Match(repositoryUrl);
            if (!m.Success)
            {
                throw new ArgumentException($"{repositoryUrl} is not a valid Azure DevOps repository URL");
            }
            string account = m.Groups["account"].Value;
            return that.GetTokenForAccount(account);
        }
    }
}
