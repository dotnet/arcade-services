// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Microsoft.DotNet.Darc.Models.PopUps
{
    public static class MergePoliciesPopUpHelpers
    {
        /// <summary>
        /// Validate the merge policies specified in YAML
        /// </summary>
        /// <returns>True if the merge policies are valid, false otherwise.</returns>
        public static bool ValidateMergePolicies(List<MergePolicy> mergePolicies, ILogger logger)
        {
            if (mergePolicies != null)
            {
                foreach (MergePolicy policy in mergePolicies)
                {
                    if (policy.Name.Equals(Constants.AllCheckSuccessfulMergePolicyName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Should either have no properties, or one called "ignoreChecks"
                        if (policy.Properties != null &&
                            (policy.Properties.Count > 1 ||
                            (policy.Properties.Count == 1 &&
                            !policy.Properties.TryGetValue(Constants.IgnoreChecksMergePolicyPropertyName, out _))))
                        {
                            logger.LogError($"{Constants.AllCheckSuccessfulMergePolicyName} merge policy should have no properties, or an '{Constants.IgnoreChecksMergePolicyPropertyName}' property. See help.");
                            return false;
                        }
                    }
                    else if (policy.Name.Equals(Constants.StandardMergePolicyName, StringComparison.OrdinalIgnoreCase) ||
                             policy.Name.Equals(Constants.NoExtraCommitsMergePolicyName, StringComparison.OrdinalIgnoreCase) ||
                             policy.Name.Equals(Constants.NoRequestedChangesMergePolicyName, StringComparison.OrdinalIgnoreCase))
                    {
                        // All good
                    }
                    else
                    {
                        logger.LogError($"Unknown merge policy '{policy.Name}'");
                        return false;
                    }
                }
            }

            return true;
        }

        public static List<MergePolicy> ConvertMergePolicies(List<MergePolicyData> mergePolicies)
        {
            return mergePolicies?.Select(
                    d => 
                    new MergePolicy
                    {
                        Name = d.Name,
                        Properties =
                            d.Properties != null ? 
                                d.Properties.ToImmutableDictionary(p => p.Key, p => JToken.FromObject(p.Value)) :
                                ImmutableDictionary.Create<string, JToken>()
                    })
                .ToList();
        }

        public static List<MergePolicyData> ConvertMergePolicies(IEnumerable<MergePolicy> value)
        {
            return value.Select(
                    d => new MergePolicyData
                    {
                        Name = d.Name,
                        Properties = d.Properties != null ?
                            (d.Properties.ToDictionary(p => p.Key, p =>
                            {
                                switch (p.Value.Type)
                                {
                                    case JTokenType.Array:
                                        return (object)p.Value.ToObject<List<object>>();
                                    default:
                                        throw new NotImplementedException($"Unexpected property value type {p.Value.Type}");
                                }
                            })) : new Dictionary<string, object>()
                    })
                .ToList();
        }
    }
}
