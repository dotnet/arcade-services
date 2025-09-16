// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Microsoft.DotNet.DarcLib.Models.Darc.Yaml;

#nullable enable
namespace Microsoft.DotNet.Darc.Models.PopUps;

internal class AddSubscriptionPopUp : SubscriptionPopUp<SubscriptionYamlData>
{
    public AddSubscriptionPopUp(
        string path,
        bool forceCreation,
        IGitRepoFactory gitRepoFactory,
        ILogger logger,
        string channel,
        string sourceRepository,
        string targetRepository,
        string targetBranch,
        string updateFrequency,
        bool batchable,
        List<MergePolicy> mergePolicies,
        IEnumerable<string> suggestedChannels,
        IEnumerable<string> suggestedRepositories,
        IEnumerable<string> availableUpdateFrequencies,
        IEnumerable<string> availableMergePolicyHelp,
        string failureNotificationTags,
        bool? sourceEnabled,
        string? sourceDirectory,
        string? targetDirectory,
        List<string> excludedAssets)
        : base(path, forceCreation, suggestedChannels, suggestedRepositories, availableUpdateFrequencies, availableMergePolicyHelp, logger, gitRepoFactory,
            new SubscriptionYamlData
            {
                Channel = GetCurrentSettingForDisplay(channel, "<required>", false),
                SourceRepository = GetCurrentSettingForDisplay(sourceRepository, "<required>", false),
                TargetRepository = GetCurrentSettingForDisplay(targetRepository, "<required>", false),
                TargetBranch = GetCurrentSettingForDisplay(targetBranch, "<required>", false),
                UpdateFrequency = GetCurrentSettingForDisplay(updateFrequency, $"<'{string.Join("', '", availableUpdateFrequencies)}'>", false),
                Batchable = GetCurrentSettingForDisplay(batchable.ToString(), batchable.ToString(), false),
                MergePolicies = MergePoliciesPopUpHelpers.ConvertMergePolicies(mergePolicies),
                FailureNotificationTags = failureNotificationTags,
                SourceEnabled = GetCurrentSettingForDisplay(sourceEnabled?.ToString(), false.ToString(), false),
                SourceDirectory = GetCurrentSettingForDisplay(sourceDirectory, string.Empty, false),
                TargetDirectory = GetCurrentSettingForDisplay(targetDirectory, string.Empty, false),
                ExcludedAssets = excludedAssets,
            },
            header: [
                new("Use this form to create a new subscription.", true),
                new("A subscription maps a build of a source repository that has been applied to a specific channel", true),
                new("onto a specific branch in a target repository.  The subscription has a trigger (update frequency)", true),
                new("and merge policy. If a subscription is batchable, no merge policy should be provided, and the", true),
                new("set-repository-policies command should be used instead to set policies at the repository and branch level. ", true),
                new("For non-batched subscriptions, providing a list of semicolon-delineated GitHub tags will tag these", true),
                new("logins when monitoring the pull requests, once one or more policy checks fail.", true),
                Line.Empty,
                new("Excluded assets is a list of package names to be ignored during dependency updates. ", true),
                new("Asterisks can be used to filter whole namespaces, e.g. - Microsoft.DotNet.Arcade.*", true),
                new("When used with non-source-enabled subscriptions which target directories, it is possible to exclude assets in specified directories", true),
                new("e.g. - src/sdk/System.Text.json, or use globbing e.g. - src/**/System.Text.* ", true),
                Line.Empty,
                new("In source-enabled (VMR code flow subscriptions) subscriptions, source and target directories define which directory of the VMR (under src/) are the sources synchronized with.", true),
                new("Only one of those needs to be set based on whether the source or the target repo is the VMR.", true),
                new("In dependency flow subscriptions only target directory is supported and defines a comma separated list of paths ('.' for repo root) where the dependency updates are applied.", true),
                new("These paths support matching by prefix e.g. src/*", true),
                new("Source directory is not supported in dependency flow subscriptions.", true),
                Line.Empty,
                new("For additional information about subscriptions, please see", true),
                new("https://github.com/dotnet/arcade/blob/main/Documentation/BranchesChannelsAndSubscriptions.md", true),
                Line.Empty,
                new("Fill out the following form.  Suggested values for fields are shown below.", true),
                new()
            ])
    {
    }
}
