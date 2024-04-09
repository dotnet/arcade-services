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

public class UpdateSubscriptionPopUp : SubscriptionPopUp
{
    public const string EnabledElement = "Enabled";

    private readonly ILogger _logger;
    private readonly SubscriptionUpdateData _yamlData;

    public bool Enabled => bool.Parse(_yamlData.Enabled);

    private UpdateSubscriptionPopUp(
        string path,
        ILogger logger,
        Subscription subscription,
        IEnumerable<string> suggestedChannels,
        IEnumerable<string> suggestedRepositories,
        IEnumerable<string> availableMergePolicyHelp,
        SubscriptionUpdateData data)
        : base(path, suggestedChannels, suggestedRepositories, availableMergePolicyHelp, logger, data,
            header: [
                new Line($"Use this form to update the values of subscription '{subscription.Id}'.", true),
                new Line($"Note that if you are setting 'Is batchable' to true you need to remove all Merge Policies.", true),
                Line.Empty,
                new("Source and target directories only apply to source-enabled subscriptions (VMR code flow subscriptions).", true),
                new("They define which directory of the VMR (under src/) are the sources synchronized with.", true),
                new("Only one of those must be set based on whether the source or the target repo is the VMR.", true),
                Line.Empty,
                new("Excluded assets is a list of package names to be ignored during source-enabled subscriptions (VMR code flow). ", true),
                new("Asterisks can be used to filter whole namespaces, e.g. - Microsoft.DotNet.Arcade.*", true),
                Line.Empty,
            ])
    {
        _logger = logger;
        _yamlData = data;
    }

    public UpdateSubscriptionPopUp(
        string path,
        ILogger logger,
        Subscription subscription,
        IEnumerable<string> suggestedChannels,
        IEnumerable<string> suggestedRepositories,
        IEnumerable<string> availableUpdateFrequencies,
        IEnumerable<string> availableMergePolicyHelp,
        string failureNotificationTags,
        bool? sourceEnabled,
        string sourceDirectory,
        string targetDirectory,
        List<string> excludedAssets)
        : this(path, logger, subscription, suggestedChannels, suggestedRepositories, availableMergePolicyHelp,
              new SubscriptionUpdateData
              {
                  Id = GetCurrentSettingForDisplay(subscription.Id.ToString(), subscription.Id.ToString(), false),
                  Channel = GetCurrentSettingForDisplay(subscription.Channel.Name, subscription.Channel.Name, false),
                  SourceRepository = GetCurrentSettingForDisplay(subscription.SourceRepository, subscription.SourceRepository, false),
                  TargetRepository = GetCurrentSettingForDisplay(subscription.TargetRepository, subscription.TargetRepository, false),
                  TargetBranch = GetCurrentSettingForDisplay(subscription.TargetBranch, subscription.TargetBranch, false),
                  Batchable = GetCurrentSettingForDisplay(subscription.Policy.Batchable.ToString(), subscription.Policy.Batchable.ToString(), false),
                  UpdateFrequency = GetCurrentSettingForDisplay(subscription.Policy.UpdateFrequency.ToString(), subscription.Policy.UpdateFrequency.ToString(), false),
                  Enabled = GetCurrentSettingForDisplay(subscription.Enabled.ToString(), subscription.Enabled.ToString(), false),
                  FailureNotificationTags = GetCurrentSettingForDisplay(failureNotificationTags, failureNotificationTags, false),
                  MergePolicies = MergePoliciesPopUpHelpers.ConvertMergePolicies(subscription.Policy.MergePolicies),
                  SourceEnabled = GetCurrentSettingForDisplay(sourceEnabled?.ToString(), false.ToString(), false),
                  SourceDirectory = GetCurrentSettingForDisplay(sourceDirectory, subscription.SourceDirectory, false),
                  TargetDirectory = GetCurrentSettingForDisplay(targetDirectory, subscription.TargetDirectory, false),
                  ExcludedAssets = excludedAssets,
              })
    {
    }

    public override int ProcessContents(IList<Line> contents)
    {
        SubscriptionUpdateData outputYamlData;

        try
        {
            string yamlString = contents.Aggregate(string.Empty, (current, line) => $"{current}{Environment.NewLine}{line.Text}");
            IDeserializer serializer = new DeserializerBuilder().Build();
            outputYamlData = serializer.Deserialize<SubscriptionUpdateData>(yamlString);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to parse input yaml.  Please see help for correct format.");
            return Constants.ErrorCode;
        }

        var result = ParseAndValidateData(outputYamlData);

        if (!bool.TryParse(outputYamlData.Enabled, out bool enabled))
        {
            _logger.LogError("Enabled is not a valid boolean value.");
            return Constants.ErrorCode;
        }

        _yamlData.Enabled = ParseSetting(outputYamlData.Enabled, _yamlData.Enabled, false)!;

        return result;
    }

    /// <summary>
    /// Helper class for YAML encoding/decoding purposes.
    /// This is used so that we can have friendly alias names for elements.
    /// </summary>
    #nullable disable
    private class SubscriptionUpdateData : SubscriptionData
    {
        [YamlMember(ApplyNamingConventions = false)]
        public string Id { get; set; }

        [YamlMember(Alias = EnabledElement, ApplyNamingConventions = false)]
        public string Enabled { get; set; }
    }
}
