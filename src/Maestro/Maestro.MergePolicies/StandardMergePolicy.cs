// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.MergePolicyEvaluation;
using Microsoft.VisualStudio.Services.Common;
using Newtonsoft.Json.Linq;

namespace Maestro.MergePolicies;

public class StandardMergePolicyBuilder : IMergePolicyBuilder
{
    private static readonly IReadOnlyList<string> s_standardGitHubIgnoreChecks = [
            "WIP",
            "license/cla",
            "auto-merge.config.enforce",
            "Build Analysis"
        ];
    private static readonly IReadOnlyList<string> s_standardAzureDevOpsIgnoreChecks = [
            "Comment requirements",
            "Minimum number of reviewers",
            "auto-merge.config.enforce",
            "Work item linking"
        ];

    public string Name => MergePolicyConstants.StandardMergePolicyName;

    private static IEnumerable<string> GetStandardIgnoreChecks(string prUrl)
    {
        if (prUrl.Contains("github.com"))
        {
            return s_standardGitHubIgnoreChecks;
        }
        else if (prUrl.Contains("dev.azure.com"))
        {
            return s_standardAzureDevOpsIgnoreChecks;
        }
        throw new NotImplementedException("Unknown pr repo url");
    }

    public async Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
    {
        string prUrl = pr.Url;
        MergePolicyProperties standardProperties = new(new Dictionary<string, JToken>
        {
            {
                MergePolicyConstants.IgnoreChecksMergePolicyPropertyName,
                new JArray(GetStandardIgnoreChecks(prUrl)
                    .Concat(properties.Get<IEnumerable<string>>(MergePolicyConstants.IgnoreChecksMergePolicyPropertyName) ?? [])
                    .Distinct())
            }
        });

        var policies = new List<IMergePolicy>();
        policies.AddRange(await new AllChecksSuccessfulMergePolicyBuilder().BuildMergePoliciesAsync(standardProperties, pr));
        policies.AddRange(await new NoRequestedChangesMergePolicyBuilder().BuildMergePoliciesAsync(standardProperties, pr));
        policies.AddRange(await new DontAutomergeDowngradesMergePolicyBuilder().BuildMergePoliciesAsync(standardProperties, pr));
        policies.AddRange(await new ValidateCoherencyMergePolicyBuilder().BuildMergePoliciesAsync(standardProperties, pr));

        if (pr.CodeFlowDirection == CodeFlowDirection.ForwardFlow)
        {
            policies.AddRange(await new ForwardFlowMergePolicyBuilder().BuildMergePoliciesAsync(standardProperties, pr));
        }

        return policies;
    }
}
