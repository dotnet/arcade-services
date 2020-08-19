// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Maestro.Contracts
{
    public static class MergePolicyConstants
    {
        public const string AllCheckSuccessfulMergePolicyName = "AllChecksSuccessful";
        public const string StandardMergePolicyName = "Standard";
        public const string NoExtraCommitsMergePolicyName = "NoExtraCommits";
        public const string NoRequestedChangesMergePolicyName = "NoRequestedChanges";
        public const string DontAutomergeDowngradesPolicyName = "DontAutomergeDowngrades";

        public const string IgnoreChecksMergePolicyPropertyName = "ignoreChecks";

        public const string MaestroMergePolicyCheckRunPrefix = "maestro-policy-";

        public const string MaestroMergePolicyDisplayName = "Maestro auto-merge";
    }
}
