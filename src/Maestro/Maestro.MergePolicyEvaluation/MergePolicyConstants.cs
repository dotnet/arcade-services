// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.MergePolicyEvaluation;

public class MergePolicyConstants
{
    public const string AllCheckSuccessfulMergePolicyName = "AllChecksSuccessful";
    public const string StandardMergePolicyName = "Standard";
    public const string NoRequestedChangesMergePolicyName = "NoRequestedChanges";
    public const string DontAutomergeDowngradesPolicyName = "DontAutomergeDowngrades";
    public const string ValidateCoherencyMergePolicyName = "ValidateCoherency";

    public const string IgnoreChecksMergePolicyPropertyName = "ignoreChecks";

    public const string MaestroMergePolicyCheckRunPrefix = "maestro-policy-";

    public const string MaestroMergePolicyDisplayName = "Maestro auto-merge";
}

