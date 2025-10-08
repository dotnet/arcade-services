// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Models.PopUps;

internal class UpdateSubscriptionPopUp : SubscriptionPopUp<SubscriptionUpdateData>
{
    private readonly ILogger _logger;
    private readonly Subscription _originalSubscription;

    public bool Enabled => bool.Parse(_data.Enabled);

    private UpdateSubscriptionPopUp(
        string path,
        bool forceCreation,
        IGitRepoFactory gitRepoFactory,
        ILogger logger,
        Subscription subscription,
        IEnumerable<string> suggestedChannels,
        IEnumerable<string> suggestedRepositories,
        IEnumerable<string> availableUpdateFrequencies,
        IEnumerable<string> availableMergePolicyHelp,
        SubscriptionUpdateData data)
        : base(path, forceCreation, suggestedChannels, suggestedRepositories, availableUpdateFrequencies, availableMergePolicyHelp, logger, gitRepoFactory, data,
            header: [
                new Line($"Use this form to update the values of subscription '{subscription.Id}'.", true),
                new Line($"Note that if you are setting 'Is batchable' to true you need to remove all Merge Policies.", true),
                Line.Empty,
                new("Excluded assets is a list of package names to be ignored during dependency updates. ", true),
                new("Asterisks can be used to filter whole namespaces, e.g. - Microsoft.DotNet.Arcade.*", true),
                new("When used with non-source-enabled subscriptions which target directories, it is possible to exclude assets in specified directories", true),
                new("e.g. - src/sdk/System.Text.json, or use globbing e.g. - src/*/System.Text.* ", true),
                Line.Empty,
                new("In source-enabled (VMR code flow subscriptions) subscriptions, source and target directories define which directory of the VMR (under src/) are the sources synchronized with.", true),
                new("Only one of those needs to be set based on whether the source or the target repo is the VMR.", true),
                new("In dependency flow subscriptions only target directory is supported and defines a comma separated list of paths ('.' for repo root) where the dependency updates are applied.", true),
                new("These paths support globbing, but only at the end of the path, e.g src/*", true),
                new("Source directory is not supported in dependency flow subscriptions.", true),
                Line.Empty,
            ])
    {
        _logger = logger;
        _originalSubscription = subscription;
    }

    public UpdateSubscriptionPopUp(
        string path,
        bool forceCreation,
        IGitRepoFactory gitRepoFactory,
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
        : this(path, forceCreation, gitRepoFactory, logger, subscription, suggestedChannels, suggestedRepositories, availableUpdateFrequencies, availableMergePolicyHelp,
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

    protected override async Task<int> ParseAndValidateData(SubscriptionUpdateData data)
    {
        int result = await base.ParseAndValidateData(data);
        if (result != Constants.SuccessCode)
        {
            return result;
        }

        if (!bool.TryParse(data.Enabled, out _))
        {
            _logger.LogError("Enabled is not a valid boolean value.");
            return Constants.ErrorCode;
        }

        _data.Enabled = ParseSetting(data.Enabled, _data.Enabled, false)!;

        // Validate that immutable fields have not been changed
        var immutableFieldErrors = new List<string>();

        // Check if Id has changed
        string? parsedId = ParseSetting(data.Id, _originalSubscription.Id.ToString(), false);
        if (!string.IsNullOrEmpty(parsedId) && parsedId != _originalSubscription.Id.ToString())
        {
            immutableFieldErrors.Add($"Id (cannot be changed from '{_originalSubscription.Id}')");
        }

        // Check if TargetRepository has changed
        string? parsedTargetRepo = ParseSetting(data.TargetRepository, _originalSubscription.TargetRepository, false);
        if (!string.IsNullOrEmpty(parsedTargetRepo) && parsedTargetRepo != _originalSubscription.TargetRepository)
        {
            immutableFieldErrors.Add($"Target Repository URL (cannot be changed from '{_originalSubscription.TargetRepository}')");
        }

        // Check if TargetBranch has changed
        string? parsedTargetBranch = ParseSetting(data.TargetBranch, _originalSubscription.TargetBranch, false);
        if (!string.IsNullOrEmpty(parsedTargetBranch) && parsedTargetBranch != _originalSubscription.TargetBranch)
        {
            immutableFieldErrors.Add($"Target Branch (cannot be changed from '{_originalSubscription.TargetBranch}')");
        }

        // Check if SourceEnabled has changed
        string? parsedSourceEnabled = ParseSetting(data.SourceEnabled, _originalSubscription.SourceEnabled.ToString(), false);
        if (!string.IsNullOrEmpty(parsedSourceEnabled) && parsedSourceEnabled != _originalSubscription.SourceEnabled.ToString())
        {
            immutableFieldErrors.Add($"Source Enabled (cannot be changed from '{_originalSubscription.SourceEnabled}')");
        }

        if (immutableFieldErrors.Any())
        {
            _logger.LogError("The following immutable fields cannot be modified:");
            foreach (var error in immutableFieldErrors)
            {
                _logger.LogError($"  - {error}");
            }
            return Constants.ErrorCode;
        }

        return Constants.SuccessCode;
    }
}
