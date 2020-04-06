// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Contracts;
using NuGet.Versioning;
using System;
using System.Threading.Tasks;

namespace Maestro.MergePolicies
{
    public class DontAutomergeDowngradesMergePolicy : MergePolicy
    {
        public override string DisplayName => "Do not automerge downgrades";

        public override async Task EvaluateAsync(IMergePolicyEvaluationContext context, MergePolicyProperties properties)
        {
            EvaluateDowngrades(context);
            await Task.CompletedTask;
        }

        internal static void EvaluateDowngrades(IMergePolicyEvaluationContext context)
        {
            try
            {
                if (HasAnyDowngrade(context.PullRequest))
                {
                    context.Fail("Some dependency updates are downgrades. Aborting auto-merge.");
                }
                else
                {
                    context.Succeed("No version downgrade detected.");
                }
            }
            catch (Exception e)
            {
                context.Fail($"Failed to check version downgrades. Aborting auto-merge. {e.Message}");
            }
        }

        private static bool HasAnyDowngrade(IPullRequest pr)
        {
            foreach (var dependency in pr.RequiredUpdates)
            {
                if (!SemanticVersion.TryParse(dependency.FromVersion, out var fromVersion))
                {
                    throw new ArgumentException($"Could not parse '{dependency.FromVersion}' as a Semantic Version string.");
                }

                if (!SemanticVersion.TryParse(dependency.ToVersion, out var toVersion))
                {
                    throw new ArgumentException($"Could not parse '{dependency.ToVersion}' as a Semantic Version string.");
                }

                if (fromVersion.CompareTo(toVersion) > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
