// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Models.PopUps;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using IConfigurationRepositoryManager = Microsoft.DotNet.MaestroConfiguration.Client.IConfigurationRepositoryManager;

namespace Microsoft.DotNet.Darc.Operations;

internal class AddSubscriptionOperation : SubscriptionOperationBase
{
    private readonly AddSubscriptionCommandLineOptions _options;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly IRemoteFactory _remoteFactory;

    public AddSubscriptionOperation(
        AddSubscriptionCommandLineOptions options,
        ILogger<AddSubscriptionOperation> logger,
        IBarApiClient barClient,
        IRemoteFactory remoteFactory,
        IGitRepoFactory gitRepoFactory,
        IConfigurationRepositoryManager configRepoManager)
        : base(barClient, configRepoManager, logger, options)
    {
        _options = options;
        _gitRepoFactory = gitRepoFactory;
        _remoteFactory = remoteFactory; 
    }

    /// <summary>
    /// Implements the 'add-subscription' operation
    /// </summary>
    protected override async Task<int> ExecuteInternalAsync()
    {
        if (_options.Batchable && _options.SourceEnabled)
        {
            _logger.LogError("Batched codeflow subscriptions are not supported.");
            return Constants.ErrorCode;
        }

        // If --subscription parameter is provided, copy settings from the existing subscription
        Subscription copyFromSubscription = null;
        if (!string.IsNullOrEmpty(_options.CopyFromSubscription))
        {
            try
            {
                copyFromSubscription = await _barClient.GetSubscriptionAsync(_options.CopyFromSubscription);
                _logger.LogInformation("Copying settings from subscription '{SubscriptionId}'", copyFromSubscription.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve subscription '{SubscriptionId}'", _options.CopyFromSubscription);
                return Constants.ErrorCode;
            }
        }

        // Initialize variables - if copying from a subscription, use its values as defaults
        // Command-line options always override copied values for string parameters
        // For boolean parameters (enabled, batchable, sourceEnabled), they are copied when NO 
        // merge policies are specified via command-line, as we cannot distinguish between explicit 
        // false and default false values
        bool enabled = _options.Enabled;
        string channel = _options.Channel;
        string sourceRepository = _options.SourceRepository;
        string targetRepository = _options.TargetRepository;
        string targetBranch = GitHelpers.NormalizeBranchName(_options.TargetBranch);
        string updateFrequency = _options.UpdateFrequency;
        bool batchable = _options.Batchable;
        bool sourceEnabled = _options.SourceEnabled;
        bool autoApprove = _options.AutoApprove;
        bool mergePrs = _options.MergePrs;
        List<string> ignoredChecks = [.. _options.IgnoreChecks];
        string sourceDirectory = _options.SourceDirectory;
        string targetDirectory = NormalizeTargetDirectory(_options.TargetDirectory);
        string failureNotificationTags = _options.FailureNotificationTags;
        List<string> excludedAssets = _options.ExcludedAssets != null ? [.._options.ExcludedAssets.Split(';', StringSplitOptions.RemoveEmptyEntries)] : [];

        // Copy values from the source subscription where not explicitly provided via command-line
        if (copyFromSubscription != null)
        {
            // For string values, use copied value if command-line option was not provided
            if (string.IsNullOrEmpty(channel))
            {
                channel = copyFromSubscription.Channel.Name;
            }
            if (string.IsNullOrEmpty(sourceRepository))
            {
                sourceRepository = copyFromSubscription.SourceRepository;
            }
            if (string.IsNullOrEmpty(targetRepository))
            {
                targetRepository = copyFromSubscription.TargetRepository;
            }
            if (string.IsNullOrEmpty(targetBranch))
            {
                targetBranch = copyFromSubscription.TargetBranch;
            }
            if (string.IsNullOrEmpty(updateFrequency))
            {
                updateFrequency = copyFromSubscription.Policy.UpdateFrequency.ToString();
            }
            if (string.IsNullOrEmpty(sourceDirectory))
            {
                sourceDirectory = copyFromSubscription.SourceDirectory;
            }
            if (string.IsNullOrEmpty(targetDirectory))
            {
                targetDirectory = copyFromSubscription.TargetDirectory;
            }
            if (string.IsNullOrEmpty(failureNotificationTags))
            {
                failureNotificationTags = copyFromSubscription.PullRequestFailureNotificationTags;
            }
            if (_options.ExcludedAssets == null && copyFromSubscription.ExcludedAssets != null)
            {
                excludedAssets = [..copyFromSubscription.ExcludedAssets];
            }
            
            // For boolean values, we copy them from the source subscription only if no merge settings
            // were specified via command-line (which would indicate the user is making intentional changes).
            // Note: Due to limitations in CommandLine library, we cannot distinguish between explicit 
            // false values and default false values, so copied boolean values take precedence.
            // Users must explicitly specify boolean flags to override them when using --subscription.
            if (!HasUserSpecifiedMergeSettings())
            {
                enabled = copyFromSubscription.Enabled;
                batchable = copyFromSubscription.Policy.Batchable;
                sourceEnabled = copyFromSubscription.SourceEnabled;
                autoApprove = copyFromSubscription.AutoApprove;
                mergePrs = copyFromSubscription.MergePrs;
                ignoredChecks = [.. copyFromSubscription.IgnoredChecks];
            }
        }

        if (!string.IsNullOrEmpty(sourceDirectory) && !string.IsNullOrEmpty(targetDirectory))
        {
            _logger.LogError("Only one of source or target directory can be specified for source-enabled subscriptions.");
            return Constants.ErrorCode;
        }

        if (batchable && mergePrs)
        {
            _logger.LogError("Batchable subscriptions cannot enable Merge PRs. Configure merging for the repository and branch instead.");
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
                mergePrs,
                ignoredChecks,
                (await suggestedChannels).Select(suggestedChannel => suggestedChannel.Name),
                (await suggestedRepos).SelectMany(subscription => new List<string> { subscription.SourceRepository, subscription.TargetRepository }).ToHashSet(),
                Constants.AvailableFrequencies,
                failureNotificationTags,
                sourceEnabled,
                autoApprove,
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
            batchable = addSubscriptionPopup.Batchable;
            mergePrs = addSubscriptionPopup.MergePrs;
            ignoredChecks = [.. addSubscriptionPopup.IgnoredChecks];
            failureNotificationTags = addSubscriptionPopup.FailureNotificationTags;
            sourceEnabled = addSubscriptionPopup.SourceEnabled;
            autoApprove = addSubscriptionPopup.AutoApprove;
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
                RepositoryBranch existingRepositoryBranch = (await _barClient.GetRepositoriesAsync(targetRepository, targetBranch))
                    .FirstOrDefault(repositoryBranch =>
                        repositoryBranch.Repository.Equals(targetRepository, StringComparison.OrdinalIgnoreCase) &&
                        repositoryBranch.Branch.Equals(targetBranch, StringComparison.OrdinalIgnoreCase));
                if (existingRepositoryBranch?.MergePrs != true)
                {
                    Console.WriteLine("Warning: Batchable subscription doesn't have Merge PRs enabled for its repository and branch. " +
                                      "PRs will not be auto-merged.");
                    Console.WriteLine($"Please use 'darc set-repository-policies --repo {targetRepository} --branch {targetBranch}' " +
                                      $"to configure merging.{Environment.NewLine}");
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

            if (!ValidateAutoApprove(autoApprove, sourceEnabled, targetDirectory))
            {
                return Constants.ErrorCode;
            }

            SubscriptionYamlParameters subscriptionYamlParameters = new()
            {
                Enabled = enabled,
                Channel = channel,
                SourceRepository = sourceRepository,
                TargetRepository = targetRepository,
                TargetBranch = targetBranch,
                UpdateFrequency = (UpdateFrequency)Enum.Parse(typeof(UpdateFrequency), updateFrequency, ignoreCase: true),
                Batchable = batchable,
                MergePolicies = [],
                MergePrs = mergePrs,
                IgnoredChecks = ignoredChecks,
                FailureNotificationTags = failureNotificationTags,
                SourceEnabled = sourceEnabled,
                AutoApprove = autoApprove,
                SourceDirectory = sourceDirectory,
                TargetDirectory = targetDirectory,
                ExcludedAssets = excludedAssets
            };

            await ValidateNoEquivalentSubscription(subscriptionYamlParameters);

            await _configurationRepositoryManager.AddSubscriptionAsync(
                _options.ToConfigurationRepositoryOperationParameters(),
                subscriptionYamlParameters);

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (MaestroConfiguration.Client.DuplicateConfigurationObjectException ex)
        {
            _logger.LogError("Subscription with equivalent parameters already exists in '{filePath}' in repo {repo} on branch {branch}.",
                ex.FilePath,
                ex.Repository,
                ex.Branch);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Failed to create subscription.");
            return Constants.ErrorCode;
        }
    }

    private bool HasUserSpecifiedMergeSettings() => _options.MergePrs || _options.IgnoreChecks.Any();

    private async Task ValidateNoEquivalentSubscription(SubscriptionYamlParameters subscriptionYamlParameters)
    {
        var equivalentSub = await TryGetEquivalentSubscription(subscriptionYamlParameters);

        if (equivalentSub != null)
        {
            throw new ArgumentException($"An equivalent subscription '{equivalentSub.Id}' already exists.");
        }
    }
}
