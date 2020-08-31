// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Contracts;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Maestro.MergePolicies
{
    public class StandardMergePolicyBuilder : IMergePolicyBuilder
    {
        private static readonly MergePolicyProperties s_standardGitHubProperties;
        private static readonly MergePolicyProperties s_standardAzureDevOpsProperties;

        public string Name => MergePolicyConstants.StandardMergePolicyName;

        static StandardMergePolicyBuilder()
        {
            s_standardGitHubProperties = new MergePolicyProperties(new Dictionary<string, JToken>
            {
                { 
                    MergePolicyConstants.IgnoreChecksMergePolicyPropertyName, 
                    new JArray(
                        "WIP",
                        "license/cla",
                        "auto-merge.config.enforce"
                    )
                },
            });

            s_standardAzureDevOpsProperties = new MergePolicyProperties(new Dictionary<string, JToken>
            {
                {
                    MergePolicyConstants.IgnoreChecksMergePolicyPropertyName,
                    new JArray(
                        "Comment requirements",
                        "Minimum number of reviewers",
                        "auto-merge.config.enforce",
                        "Work item linking"
                    )
                },
            });
        }

        public async Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, IPullRequest pr)
        {
            string prUrl = pr.Url;
            MergePolicyProperties standardProperties;
            if (prUrl.Contains("github.com"))
            {
                standardProperties = s_standardGitHubProperties;
            }
            else if (prUrl.Contains("dev.azure.com"))
            {
                standardProperties = s_standardAzureDevOpsProperties;
            }
            else
            {
                throw new NotImplementedException("Unknown pr repo url");
            }

            var policies = new List<IMergePolicy>();
            policies.AddRange(await new AllChecksSuccessfulMergePolicyBuilder().BuildMergePoliciesAsync(standardProperties, pr));
            policies.AddRange(await new NoRequestedChangesMergePolicyBuilder().BuildMergePoliciesAsync(standardProperties, pr));
            policies.AddRange(await new DontAutomergeDowngradesMergePolicyBuilder().BuildMergePoliciesAsync(standardProperties, pr));
            return policies;
        }
    }
}
