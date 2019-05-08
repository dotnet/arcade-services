// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System;
using Newtonsoft.Json.Linq;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.Darc.Models.PopUps
{
    public abstract class SubscriptionPopUp : EditorPopUp
    {
        private readonly ILogger _logger;

        public SubscriptionPopUp(string path, ILogger logger)
            : base(path)
        {
            Path = path;
            _logger = logger;
        }

        /// <summary>
        /// Validate the merge policies specified in YAML
        /// </summary>
        /// <returns>True if the merge policies are valid, false otherwise.</returns>
        internal bool ValidateMergePolicies(List<MergePolicy> mergePolicies)
        {
            if (mergePolicies != null)
            {
                foreach (MergePolicy policy in mergePolicies)
                {
                    switch (policy.Name)
                    {
                        case "AllChecksSuccessful":
                            // Should either have no properties, or one called "ignoreChecks"
                            if (policy.Properties != null &&
                                (policy.Properties.Count > 1 ||
                                (policy.Properties.Count == 1 &&
                                !policy.Properties.TryGetValue("ignoreChecks", out _))))
                            {
                                Console.WriteLine($"AllChecksSuccessful merge policy should have no properties, or an 'ignoreChecks' property. See help.");
                                return false;
                            }
                            break;
                        case "Standard":
                            break;
                        case "NoRequestedChanges":
                            break;
                        case "NoExtraCommits":
                            break;
                        default:
                            _logger.LogError($"Unknown merge policy {policy.Name}");
                            return false;
                    }
                }
            }

            return true;
        }

        protected List<MergePolicy> ConvertMergePolicies(List<MergePolicyData> mergePolicies)
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

        protected List<MergePolicyData> ConvertMergePolicies(IEnumerable<MergePolicy> value)
        {
            return value.Select(
                    d => new MergePolicyData
                    {
                        Name = d.Name,
                        Properties = d.Properties.ToDictionary(p => p.Key, p =>
                            {
                                switch (p.Value.Type)
                                {
                                    case JTokenType.Array:
                                        return (object) p.Value.ToObject<List<object>>();
                                    default:
                                        throw new NotImplementedException($"Unexpected property value type {p.Value.Type}");
                                }
                            })
                    })
                .ToList();
        }

        public class MergePolicyData
        {
            [YamlMember(Alias = "Name")]
            public string Name { get; set; }
            [YamlMember(Alias = "Properties")]
            public Dictionary<string, object> Properties { get; set; }
        }
    }
}
