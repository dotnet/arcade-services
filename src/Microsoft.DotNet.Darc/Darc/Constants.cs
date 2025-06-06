// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.MergePolicyEvaluation;
using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.DotNet.Darc;

public class Constants
{
    public const string SettingsFileName = "settings";
    public const int ErrorCode = 42;
    public const int SuccessCode = 0;
    public const int MaxPopupTries = 3;
    public static readonly string DarcDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".darc");

    /// <summary>
    /// Available update frequencies for subscriptions.  Currently the enumeration values aren't available
    /// through the generated API client.  When/if they ever are, this can be removed.
    /// </summary>
    public static readonly List<string> AvailableFrequencies =
    [
        "none",
        "everyDay",
        "everyBuild",
        "twiceDaily",
        "everyWeek",
        "everyTwoWeeks",
        "everyMonth",
    ];

    /// <summary>
    /// This maybe should be implemented in the API in the future, help info for the available merge policies.  For now,
    /// this is just generic help for available merge policies
    /// </summary>
    public static readonly List<string> AvailableMergePolicyYamlHelp =
    [
        "Merge policies are an optional set of rules that, if satisfied, mean that an",
        "auto-update PR will be automatically merged. A PR is only merged automatically if policies",
        "exist and all are satisfied.",
        "In YAML, policies are specified in a list using the following form:",
        "- Name: <name of policy>",
        "  Properties:",
        "  - <property set>",
        "Each policy may have a set of required properties." +
        "See below for available merge policies:",
        "",
        $"- Name: {MergePolicyConstants.StandardMergePolicyName} - Corresponds to either standard GitHub or AzureDevOps depending on the target repo type",
        "The standard GitHub merge policy is:",
        $"- Name: {MergePolicyConstants.AllCheckSuccessfulMergePolicyName}",
        $"  Properties:",
        $"    ignoreCheckes:",
        $"    - WIP",
        $"    - license/cla",
        $"- Name: {MergePolicyConstants.NoRequestedChangesMergePolicyName}",
        $"- Name: {MergePolicyConstants.DontAutomergeDowngradesPolicyName}",
        $"- Name: {MergePolicyConstants.ValidateCoherencyMergePolicyName}",
        $"- Name: {MergePolicyConstants.CodeflowMergePolicyName} # for source-enabled subscriptions",
        "The standard Azure DevOps merge policy is:",
        $"- Name: {MergePolicyConstants.AllCheckSuccessfulMergePolicyName}",
        $"  Properties:",
        $"    ignoreCheckes:",
        $"    - Comment requirements",
        $"    - Minimum number of reviewers",
        $"    - Required reviewers",
        $"    - Work item linking",
        $"- Name: {MergePolicyConstants.NoRequestedChangesMergePolicyName}",
        $"- Name: {MergePolicyConstants.DontAutomergeDowngradesPolicyName}",
        $"- Name: {MergePolicyConstants.CodeflowMergePolicyName} # for source-enabled subscriptions",
        "YAML format:",
        $"- Name: {MergePolicyConstants.StandardMergePolicyName}",
        "",
        "Explanation for every check:",
        $"{MergePolicyConstants.AllCheckSuccessfulMergePolicyName} - All PR checks must be successful, potentially ignoring a specified set of checks.",
        "Checks might be ignored if they are unrelated to PR validation. The check name corresponds to the string that shows up",
        "in GitHub/Azure DevOps.",
        "YAML format:",
        $"- Name: {MergePolicyConstants.AllCheckSuccessfulMergePolicyName}",
        "  Properties:",
        $"    {MergePolicyConstants.IgnoreChecksMergePolicyPropertyName}:",
        "    - WIP",
        "    - license/cla",
        "    - <other check names>",
        "",
        $"{MergePolicyConstants.NoRequestedChangesMergePolicyName} - If changes are requested on the PR (or the PR is rejected), it will not be merged.",
        "YAML format:",
        $"- Name: {MergePolicyConstants.NoRequestedChangesMergePolicyName}",
        "",
        $"{MergePolicyConstants.DontAutomergeDowngradesPolicyName} - If any version change is a downgrade, it will not be merged.",
        "YAML format:",
        $"- Name: {MergePolicyConstants.DontAutomergeDowngradesPolicyName}",
        "",
        $"{MergePolicyConstants.ValidateCoherencyMergePolicyName} - If coherency check failed for the PR, it will not be merged.",
        "YAML format:",
        $"- Name: {MergePolicyConstants.ValidateCoherencyMergePolicyName}",
        "",
        $"{MergePolicyConstants.CodeflowMergePolicyName} - If code flow metadata have been corrupted, it will not be merged.",
        "YAML format:",
        $"- Name: {MergePolicyConstants.CodeflowMergePolicyName}",
    ];
}
