// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.Darc.Models.PopUps;

public class AddSubscriptionPopUp : SubscriptionPopUp
{
    private readonly ILogger _logger;

    public AddSubscriptionPopUp(
        string path,
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
        : base(path, suggestedChannels, suggestedRepositories, availableMergePolicyHelp, logger,
            new SubscriptionData
            {
                Channel = GetCurrentSettingForDisplay(channel, "<required>", false),
                SourceRepository = GetCurrentSettingForDisplay(sourceRepository, "<required>", false),
                TargetRepository = GetCurrentSettingForDisplay(targetRepository, "<required>", false),
                TargetBranch = GetCurrentSettingForDisplay(targetBranch, "<required>", false),
                UpdateFrequency = GetCurrentSettingForDisplay(updateFrequency, $"<'{string.Join("', '", Constants.AvailableFrequencies)}'>", false),
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
                new("Source and target directories only apply to source-enabled subscription (VMR code flow subscriptions).", true),
                new("They define which directory of the VMR (under src/) are the sources synchronized with.", true),
                new("Only one of those needs to be set based on whether the source or the target repo is the VMR.", true),
                Line.Empty,
                new("Excluded assets is a list of package names to be ignored during source-enabled subscriptions (VMR code flow). ", true),
                new("Asterisks can be used to filter whole namespaces, e.g. - Microsoft.DotNet.Arcade.*", true),
                Line.Empty,
                new("For additional information about subscriptions, please see", true),
                new("https://github.com/dotnet/arcade/blob/main/Documentation/BranchesChannelsAndSubscriptions.md", true),
                Line.Empty,
                new("Fill out the following form.  Suggested values for fields are shown below.", true),
                new()
            ])
    {
        _logger = logger;
    }

    public override int ProcessContents(IList<Line> contents)
    {
        SubscriptionData outputYamlData;

        try
        {
            // Join the lines back into a string and deserialize as YAML.
            // TODO: Alter the popup/ux manager to pass along the raw file to avoid this unnecessary
            // operation once authenticate ends up as YAML.
            string yamlString = contents.Aggregate("", (current, line) => $"{current}{Environment.NewLine}{line.Text}");
            IDeserializer serializer = new DeserializerBuilder().Build();
            outputYamlData = serializer.Deserialize<SubscriptionData>(yamlString);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to parse input yaml.  Please see help for correct format.");
            return Constants.ErrorCode;
        }

        var result = ParseAndValidateData(outputYamlData);

        _data.TargetRepository = ParseSetting(outputYamlData.TargetRepository, _data.TargetRepository, false);
        if (string.IsNullOrEmpty(_data.TargetRepository))
        {
            _logger.LogError("Target repository URL must be non-empty");
            return Constants.ErrorCode;
        }

        _data.TargetBranch = ParseSetting(outputYamlData.TargetBranch, _data.TargetBranch, false);
        if (string.IsNullOrEmpty(_data.TargetBranch))
        {
            _logger.LogError("Target branch must be non-empty");
            return Constants.ErrorCode;
        }

        return result;
    }
}
