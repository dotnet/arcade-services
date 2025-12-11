// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Models.PopUps;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using IConfigurationRepositoryManager = Microsoft.DotNet.MaestroConfiguration.Client.IConfigurationRepositoryManager;

namespace Microsoft.DotNet.Darc.Operations;

internal class AddSubscriptionOperation : SubscriptionOperationBase
{
    private readonly AddSubscriptionCommandLineOptions _options;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IConfigurationRepositoryManager _configRepoManager;

    public AddSubscriptionOperation(
        AddSubscriptionCommandLineOptions options,
        ILogger<AddSubscriptionOperation> logger,
        IBarApiClient barClient,
        IRemoteFactory remoteFactory,
        IGitRepoFactory gitRepoFactory,
        IConfigurationRepositoryManager configRepoManager)
        : base(barClient, logger)
    {
        _options = options;
        _configRepoManager = configRepoManager;
        _gitRepoFactory = gitRepoFactory;
        _remoteFactory = remoteFactory; 
    }

    /// <summary>
    /// Implements the 'add-subscription' operation
    /// </summary>
    public override async Task<int> ExecuteAsync()
    {
        if (_options.IgnoreChecks.Any() && !_options.AllChecksSuccessfulMergePolicy && !_options.StandardAutoMergePolicies)
        {
            _logger.LogError("--ignore-checks must be combined with --all-checks-passed or --standard-automerge");
            return Constants.ErrorCode;
        }
        if (_options.CodeFlowCheckMergePolicy && !_options.SourceEnabled)
        {
            _logger.LogError("--code-flow-check can only be used with --source-enabled subscriptions");
            return Constants.ErrorCode;
        }

        // Parse the merge policies
        List<MergePolicy> mergePolicies = [];

        if (_options.AllChecksSuccessfulMergePolicy)
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.AllCheckSuccessfulMergePolicyName,
                    Properties = new() { [MergePolicyConstants.IgnoreChecksMergePolicyPropertyName] = JToken.FromObject(_options.IgnoreChecks) }
                });
        }

        if (_options.NoRequestedChangesMergePolicy)
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.NoRequestedChangesMergePolicyName,
                    Properties = []
                });
        }

        if (_options.DontAutomergeDowngradesMergePolicy)
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.DontAutomergeDowngradesPolicyName,
                    Properties = []
                });
        }

        if (_options.StandardAutoMergePolicies)
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.StandardMergePolicyName,
                    Properties = new() { [MergePolicyConstants.IgnoreChecksMergePolicyPropertyName] = JToken.FromObject(_options.IgnoreChecks) }
                });
        }

        if (_options.ValidateCoherencyCheckMergePolicy)
        {
            mergePolicies.Add(
                new MergePolicy {
                    Name = MergePolicyConstants.ValidateCoherencyMergePolicyName,
                    Properties = []
                });
        }

        if (_options.CodeFlowCheckMergePolicy)
        {
            if (_options.StandardAutoMergePolicies)
            {
                _logger.LogInformation("Code flow check merge policy is already included in standard auto-merge policies. Skipping");
            }
            else
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = MergePolicyConstants.CodeflowMergePolicyName,
                        Properties = []
                    });
            }
        }

        if (_options.VersionDetailsPropsMergePolicy)
        {
            if (_options.StandardAutoMergePolicies)
            {
                _logger.LogError("Version Details Props merge policy cannot be combined with standard auto-merge policies. " +
                               "The Version Details Props policy is already included in standard auto-merge policies.");
                return Constants.ErrorCode;
            }
            
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.VersionDetailsPropsMergePolicyName,
                    Properties = []
                });
        }

        if (_options.Batchable && _options.SourceEnabled)
        {
            _logger.LogError("Batched codeflow subscriptions are not supported.");
            return Constants.ErrorCode;
        }

        if (_options.Batchable && mergePolicies.Count > 0)
        {
            Console.WriteLine("Batchable subscriptions cannot be combined with merge policies. " +
                              "Merge policies are specified at a repository+branch level.");
            return Constants.ErrorCode;
        }

        bool enabled = _options.Enabled;
        string channel = _options.Channel;
        string sourceRepository = _options.SourceRepository;
        string targetRepository = _options.TargetRepository;
        string targetBranch = GitHelpers.NormalizeBranchName(_options.TargetBranch);
        string updateFrequency = _options.UpdateFrequency;
        bool batchable = _options.Batchable;
        bool sourceEnabled = _options.SourceEnabled;
        string sourceDirectory = _options.SourceDirectory;
        string targetDirectory = NormalizeTargetDirectory(_options.TargetDirectory);
        string failureNotificationTags = _options.FailureNotificationTags;
        List<string> excludedAssets = _options.ExcludedAssets != null ? [.._options.ExcludedAssets.Split(';', StringSplitOptions.RemoveEmptyEntries)] : [];

        if (!string.IsNullOrEmpty(sourceDirectory) && !string.IsNullOrEmpty(targetDirectory))
        {
            _logger.LogError("Only one of source or target directory can be specified for source-enabled subscriptions.");
            return Constants.ErrorCode;
        }

        // If in quiet (non-interactive mode), ensure that all options were passed, then
        // just call the remote API
        if (_options.Quiet && !_options.ReadStandardIn)
        {
            if (string.IsNullOrEmpty(channel) ||
                string.IsNullOrEmpty(sourceRepository) ||
                string.IsNullOrEmpty(targetRepository) ||
                string.IsNullOrEmpty(targetBranch) ||
                string.IsNullOrEmpty(updateFrequency) ||
                !Constants.AvailableFrequencies.Contains(updateFrequency, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogError($"Missing input parameters for the subscription. Please see command help or remove --quiet/-q for interactive mode");
                return Constants.ErrorCode;
            }

            if (sourceEnabled && string.IsNullOrEmpty(sourceDirectory) && string.IsNullOrEmpty(targetDirectory))
            {
                _logger.LogError("One of source or target directory is required for source-enabled subscriptions.");
                return Constants.ErrorCode;
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(failureNotificationTags) && batchable)
            {
                _logger.LogWarning("Failure Notification Tags may be set, but will not be used while in batched mode.");
            }

            // Grab existing subscriptions to get suggested values.
            var suggestedRepos = _barClient.GetSubscriptionsAsync();
            var suggestedChannels = _barClient.GetChannelsAsync();

            // Help the user along with a form.  We'll use the API to gather suggested values
            // from existing subscriptions based on the input parameters.
            var addSubscriptionPopup = new AddSubscriptionPopUp("add-subscription/add-subscription-todo",
                _options.ForceCreation,
                _gitRepoFactory,
                _logger,
                channel,
                sourceRepository,
                targetRepository,
                targetBranch,
                updateFrequency,
                batchable,
                mergePolicies,
                (await suggestedChannels).Select(suggestedChannel => suggestedChannel.Name),
                (await suggestedRepos).SelectMany(subscription => new List<string> { subscription.SourceRepository, subscription.TargetRepository }).ToHashSet(),
                Constants.AvailableFrequencies,
                Constants.AvailableMergePolicyYamlHelp,
                failureNotificationTags,
                sourceEnabled,
                sourceDirectory,
                targetDirectory,
                excludedAssets);

            var uxManager = new UxManager(_options.GitLocation, _logger);
            int exitCode = _options.ReadStandardIn
                ? await uxManager.ReadFromStdIn(addSubscriptionPopup)
                : uxManager.PopUp(addSubscriptionPopup);

            if (exitCode != Constants.SuccessCode)
            {
                return exitCode;
            }

            channel = addSubscriptionPopup.Channel;
            sourceRepository = addSubscriptionPopup.SourceRepository;
            targetRepository = addSubscriptionPopup.TargetRepository;
            targetBranch = addSubscriptionPopup.TargetBranch;
            updateFrequency = addSubscriptionPopup.UpdateFrequency;
            mergePolicies = addSubscriptionPopup.MergePolicies;
            batchable = addSubscriptionPopup.Batchable;
            failureNotificationTags = addSubscriptionPopup.FailureNotificationTags;
            sourceEnabled = addSubscriptionPopup.SourceEnabled;
            sourceDirectory = addSubscriptionPopup.SourceDirectory;
            targetDirectory = addSubscriptionPopup.TargetDirectory;
            excludedAssets = [..addSubscriptionPopup.ExcludedAssets];
        }



        try
        {
            // If we are about to add a batchable subscription and the merge policies are empty for the
            // target repo/branch, warn the user.
            if (batchable)
            {
                var existingMergePolicies = await _barClient.GetRepositoryMergePoliciesAsync(targetRepository, targetBranch);
                if (!existingMergePolicies.Any())
                {
                    Console.WriteLine("Warning: Batchable subscription doesn't have any repository merge policies. " +
                                      "PRs will not be auto-merged.");
                    Console.WriteLine($"Please use 'darc set-repository-policies --repo {targetRepository} --branch {targetBranch}' " +
                                      $"to set policies.{Environment.NewLine}");
                }

                if (!string.IsNullOrEmpty(failureNotificationTags))
                {
                    Console.WriteLine("Warning: Failure notification tags may be set, but are ignored on batched subscriptions.");
                }
            }

            // Verify the target
            IRemote targetVerifyRemote = await _remoteFactory.CreateRemoteAsync(targetRepository);

            bool onlyCheckBranch = sourceEnabled && !string.IsNullOrEmpty(targetDirectory); 
            bool targetBranchExists = await UxHelpers.VerifyAndConfirmBranchExistsAsync(targetVerifyRemote, targetRepository, targetBranch, !_options.Quiet, onlyCheckBranch);

            if (!targetBranchExists)
            {
                Console.WriteLine("Aborting subscription creation.");
                return Constants.ErrorCode;
            }

            // Verify the source.
            IRemote sourceVerifyRemote = await _remoteFactory.CreateRemoteAsync(sourceRepository);

            bool sourceRepositoryExists = await UxHelpers.VerifyAndConfirmRepositoryExistsAsync(sourceVerifyRemote, sourceRepository, !_options.Quiet);

            if (!sourceRepositoryExists)
            {
                Console.WriteLine("Aborting subscription creation.");
                return Constants.ErrorCode;
            }

            // Check for codeflow subscription conflicts (source-enabled subscriptions)
            if (sourceEnabled)
            {
                try
                {
                    await ValidateCodeflowSubscriptionConflicts(
                        targetRepository,
                        targetBranch,
                        sourceDirectory,
                        targetDirectory,
                        existingSubscriptionId: null); // null for create (no existing subscription id)
                }
                catch (ArgumentException)
                {
                    Console.WriteLine("Aborting subscription creation.");
                    return Constants.ErrorCode;
                }
            }

            if (_options.ShouldUseConfigurationRepository)
            {
                SubscriptionYaml subscriptionYaml = new()
                {
                    Id = Guid.NewGuid(),
                    Enabled = enabled,
                    Channel = channel,
                    SourceRepository = sourceRepository,
                    TargetRepository = targetRepository,
                    TargetBranch = targetBranch,
                    UpdateFrequency = (UpdateFrequency)Enum.Parse(typeof(UpdateFrequency), updateFrequency, ignoreCase: true),
                    Batchable = batchable,
                    MergePolicies = MergePolicyYaml.FromClientModels(mergePolicies),
                    FailureNotificationTags = failureNotificationTags,
                    SourceEnabled = sourceEnabled,
                    SourceDirectory = sourceDirectory,
                    TargetDirectory = targetDirectory,
                    ExcludedAssets = excludedAssets
                };

                var equivalentSubscriptionId = await FindEquivalentSubscriptionAsync(subscriptionYaml);
                if (equivalentSubscriptionId != null)
                {
                    throw new ArgumentException($"Subscription {equivalentSubscriptionId} with equivalent parameters already exists.");
                }

                await _configRepoManager.AddSubscriptionAsync(
                    _options.ToConfigurationRepositoryOperationParameters(),
                    subscriptionYaml);
            }
            else
            {
                Subscription newSubscription = await _barClient.CreateSubscriptionAsync(
                    enabled,
                    channel,
                    sourceRepository,
                    targetRepository,
                    targetBranch,
                    updateFrequency,
                    batchable,
                    mergePolicies,
                    failureNotificationTags,
                    sourceEnabled,
                    sourceDirectory,
                    targetDirectory,
                    excludedAssets);

                Console.WriteLine($"Successfully created new subscription with id '{newSubscription.Id}'.");

                // Prompt the user to trigger the subscription unless they have explicitly disallowed it
                if (!_options.NoTriggerOnCreate)
                {
                    bool triggerAutomatically = _options.TriggerOnCreate || UxHelpers.PromptForYesNo("Trigger this subscription immediately?");
                    if (triggerAutomatically)
                    {
                        await _barClient.TriggerSubscriptionAsync(newSubscription.Id);
                        Console.WriteLine($"Subscription '{newSubscription.Id}' triggered.");
                    }
                }
            }
            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (RestApiException e) when (e.Response.Status == (int) System.Net.HttpStatusCode.BadRequest)
        {
            // Could have been some kind of validation error (e.g. channel doesn't exist)
            _logger.LogError($"Failed to create subscription: {e.Response.Content}");
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Failed to create subscription.");
            return Constants.ErrorCode;
        }
    }

    private async Task<string> FindEquivalentSubscriptionAsync(SubscriptionYaml subscriptionYaml)
    {
        var channel = await _barClient.GetChannelAsync(subscriptionYaml.Channel);
        if (channel == null)
        {
            throw new ArgumentException($"Channel '{subscriptionYaml.Channel}' does not exist.");
        }

        var equivalentSub = (await _barClient.GetSubscriptionsAsync(
                sourceRepo: subscriptionYaml.SourceRepository,
                channelId: channel.Id,
                targetRepo: subscriptionYaml.TargetRepository,
                sourceEnabled: subscriptionYaml.SourceEnabled,
                sourceDirectory: subscriptionYaml.SourceDirectory,
                targetDirectory: subscriptionYaml.TargetDirectory))
            .FirstOrDefault(s => s.TargetBranch == subscriptionYaml.TargetBranch);

        return equivalentSub?.Id.ToString();
    }
}
