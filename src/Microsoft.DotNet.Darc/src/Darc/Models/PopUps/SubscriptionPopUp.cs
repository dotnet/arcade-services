// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

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
            foreach (MergePolicy policy in mergePolicies)
            {
                switch (policy.Name)
                {
                    case "AllChecksSuccessful":
                        // Should either have no properties, or one called "ignoreChecks"
                        object ignoreChecksProperty = null;
                        if (policy.Properties != null &&
                            (policy.Properties.Count > 1 ||
                            (policy.Properties.Count == 1 &&
                            !policy.Properties.TryGetValue("ignoreChecks", out ignoreChecksProperty))))
                        {
                            _logger.LogError($"AllChecksSuccessful merge policy should have no properties, or an 'ignoreChecks' property. See help.");
                            return false;
                        }
                        break;
                    case "RequireChecks":
                        // Should have 'checks' property.
                        object checksProperty = null;
                        if (policy.Properties != null &&
                            (policy.Properties.Count != 1 ||
                            !policy.Properties.TryGetValue("checks", out checksProperty)))
                        {
                            _logger.LogError($"RequireChecks merge policy should have a list of required checks specified with 'checks'. See help.");
                            return false;
                        }
                        break;
                    case "NoExtraCommits":
                        break;
                    default:
                        _logger.LogError($"Unknown merge policy {policy.Name}");
                        return false;
                }
            }

            return true;
        }
    }
}
