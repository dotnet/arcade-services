// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Contracts;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Maestro.MergePolicies;

public class DontAutomergeDowngradesMergePolicy : MergePolicy
{
    public override string DisplayName => "Do not automerge downgrades";

    public override Task<MergePolicyEvaluationResult> EvaluateAsync(IPullRequest pr, IRemote darc)
    {
        try
        {
            List<string> versionCheckMessages = GetDowngradeOrInvalidVersionMessages(pr);

            if (versionCheckMessages.Count > 0)
            {
                // It'd be great if this could be markdown, but checks as implemented today are just a big run-on sentence with no special formatting characters.
                string errorMessage = @$"
The following dependency updates appear to be downgrades or invalid versions: {string.Join(',', versionCheckMessages)}. Aborting auto-merge.
 Note that manual commits pushed to fix up the pull request won't cause the downgrade check to be re-evaluated, 
 you can ignore the check in this case.
 If you think this PR should merge but lack permission to override this check, consider finding an admin or recreating the pull request manually.
 If you feel you are seeing this message in error, please contact the dnceng team.";
                return Task.FromResult(Fail(errorMessage));
            }
            else
            {
                return Task.FromResult(
                    Succeed("No version downgrade detected and all specified versions semantically valid."));
            }
        }
        catch (Exception e)
        {
            return Task.FromResult(
                Fail($"Failed to check version downgrades. Aborting auto-merge. {e.Message}"));
        }
    }

    private static List<string> GetDowngradeOrInvalidVersionMessages(IPullRequest pr)
    {
        List<string> messages = new List<string>();

        foreach (var dependency in pr.RequiredUpdates)
        {
            bool gotValidVersions = true;
            if (!SemanticVersion.TryParse(dependency.FromVersion, out var fromVersion))
            {
                messages.Add($" Could not parse the 'from' version '{dependency.FromVersion}' of {dependency.DependencyName} as a Semantic Version string");
                gotValidVersions = false;
            }

            if (!SemanticVersion.TryParse(dependency.ToVersion, out var toVersion))
            {
                messages.Add($" Could not parse the 'to' version '{dependency.ToVersion}' of {dependency.DependencyName} as a Semantic Version string");
                gotValidVersions = false;
            }

            if (gotValidVersions && fromVersion.CompareTo(toVersion) > 0)
            {
                messages.Add($" Dependency {dependency.DependencyName} was downgraded from {fromVersion} to {toVersion}");
            }
        }

        return messages;
    }
}

public class DontAutomergeDowngradesMergePolicyBuilder : IMergePolicyBuilder
{
    public string Name => MergePolicyConstants.DontAutomergeDowngradesPolicyName;
    public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, IPullRequest pr)
    {
        IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new DontAutomergeDowngradesMergePolicy() };
        return Task.FromResult(policies);
    }
}
