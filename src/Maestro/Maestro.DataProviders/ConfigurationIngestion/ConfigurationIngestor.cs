// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders.ConfigurationIngestion.Model;
using Maestro.DataProviders.ConfigurationIngestion.Validations;
using Maestro.DataProviders.Exceptions;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.DarcLib;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion;

internal partial class ConfigurationIngestor(
        BuildAssetRegistryContext context,
        ISqlBarClient sqlBarClient,
        IGitHubInstallationIdResolver installationIdResolver,
        IGitHubTagValidator gitHubTagValidator)
    : IConfigurationIngestor
{
    private readonly BuildAssetRegistryContext _context = context;
    private readonly ISqlBarClient _sqlBarClient = sqlBarClient;
    private readonly IGitHubInstallationIdResolver _installationIdResolver = installationIdResolver;
    private readonly IGitHubTagValidator _gitHubTagValidator = gitHubTagValidator;

    public async Task<ConfigurationUpdates> IngestConfigurationAsync(
        ConfigurationData configurationData,
        string configurationNamespace,
        bool saveChanges = true)
    {
        var ingestionData = IngestedConfigurationData.FromYamls(configurationData);
        ValidateEntityFields(ingestionData);

        var namespaceEntity = await FetchOrCreateNamespaceAsync(configurationNamespace);

        var existingConfigurationData =
            CreateConfigurationDataObject(namespaceEntity);

        // save the old failure notification tags before applying the subscription updates
        var oldFailureNotificationTags = existingConfigurationData.Subscriptions.ToDictionary(s => s.Values.Id, s => s.Values.FailureNotificationTags);

        var configurationDataUpdate = ComputeEntityUpdates(
            ingestionData,
            existingConfigurationData);

        await PerformEntityChangesAsync(configurationDataUpdate, namespaceEntity);

        var finalUpdates = FilterNonUpdates(configurationDataUpdate.ToYamls());

        await ValidateNotificationTags(finalUpdates.Subscriptions, oldFailureNotificationTags);

        if (saveChanges)
        {
            await _context.SaveChangesAsync();
        }

        return finalUpdates;
    }

    private async Task ValidateNotificationTags(EntityChanges<SubscriptionYaml> subscriptionChanges, Dictionary<Guid, string?> oldFailureNotificationTags)
    {
        var subscriptionsToValidate = subscriptionChanges.Updates
            .Where(s => s.FailureNotificationTags != oldFailureNotificationTags[s.Id])
            .Concat(subscriptionChanges.Creations)
            .Where(s => !string.IsNullOrEmpty(s.FailureNotificationTags))
            .ToList();

        if (subscriptionsToValidate.Count == 0)
        {
            return;
        }

        // Group subscriptions by their tags to deduplicate API calls
        var subscriptionsByTag = subscriptionsToValidate
            .SelectMany(s => s.FailureNotificationTags!
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => (Tag: t.TrimStart('@'), Subscription: s)))
            .GroupBy(x => x.Tag, x => x.Subscription, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Validate each unique tag once and collect subscriptions with invalid tags
        var subscriptionsWithInvalidTags = new HashSet<Guid>();
        foreach (var (tag, subscriptions) in subscriptionsByTag)
        {
            if (!await _gitHubTagValidator.IsNotificationTagValidAsync(tag))
            {
                foreach (var subscription in subscriptions)
                {
                    subscriptionsWithInvalidTags.Add(subscription.Id);
                }
            }
        }

        if (subscriptionsWithInvalidTags.Count > 0)
        {
            throw new EntityIngestionValidationException(
                $"The following subscriptions have invalid Pull Request Failure Notification Tags: {string.Join(", ", subscriptionsWithInvalidTags)}."
                + " Is everyone listed publicly a member of the Microsoft github org?");
        }
    }

    private static void ValidateEntityFields(IngestedConfigurationData newConfigurationData)
    {
        SubscriptionValidator.ValidateSubscriptions(newConfigurationData.Subscriptions);
        ChannelValidator.ValidateChannels(newConfigurationData.Channels);
        DefaultChannelValidator.ValidateDefaultChannels(newConfigurationData.DefaultChannels);
        BranchMergePolicyValidator.ValidateBranchMergePolicies(newConfigurationData.BranchMergePolicies);
    }

    private async Task PerformEntityChangesAsync(
        IngestedConfigurationUpdates configurationDataUpdate,
        Namespace namespaceEntity)
    {
        // Deletions
        await DeleteSubscriptions(configurationDataUpdate.Subscriptions.Removals);
        await DeleteDefaultChannels(configurationDataUpdate.DefaultChannels.Removals, namespaceEntity);
        await DeleteRepositoryBranches(configurationDataUpdate.RepositoryBranches.Removals, namespaceEntity);
        await DeleteChannels(configurationDataUpdate.Channels.Removals);

        var existingChannels = _context.Channels.ToDictionary(c => c.Name);

        // Channels must be updated first due to entity relationships
        CreateChannels(configurationDataUpdate.Channels.Creations, namespaceEntity);
        UpdateChannels(configurationDataUpdate.Channels.Updates, [.. existingChannels.Values]);

        // We fetch the channels again including newly created ones
        existingChannels = _context.Channels
            .Local
            .ToDictionary(c => c.Name);

        // Before creating subscriptions and branch policies, ensure that all referenced repositories have a Repository row in the DB
        await EnsureRepositoryRegistrationAsync(configurationDataUpdate.Subscriptions.Creations
            .Select(s => s.Values.TargetRepository)
            .Concat(configurationDataUpdate.RepositoryBranches.Creations
                .Select(rb => rb.Values.Repository))
            .Distinct()
            .ToList());

        // Update the rest of the entities
        await CreateSubscriptions(configurationDataUpdate.Subscriptions.Creations, namespaceEntity, existingChannels);

        await UpdateSubscriptions(configurationDataUpdate.Subscriptions.Updates, namespaceEntity, existingChannels);

        CreateDefaultChannels(configurationDataUpdate.DefaultChannels.Creations, namespaceEntity, existingChannels);
        await UpdateDefaultChannels(configurationDataUpdate.DefaultChannels.Updates, namespaceEntity);

        CreateBranchRepositories(configurationDataUpdate.RepositoryBranches.Creations, namespaceEntity);
        await UpdateRepositoryBranches(configurationDataUpdate.RepositoryBranches.Updates, namespaceEntity);
    }

    private async Task<Namespace> FetchOrCreateNamespaceAsync(string configurationNamespace)
    {
        var namespaceEntity = await _context.Namespaces
            .Include(ns => ns.Subscriptions)
            .Include(ns => ns.Channels)
            .Include(ns => ns.DefaultChannels)
                .ThenInclude(dc => dc.Channel)
            .Include(ns => ns.RepositoryBranches)
            .Where(ns => ns.Name == configurationNamespace)
            .AsSplitQuery()
            .FirstOrDefaultAsync();

        if (namespaceEntity is null)
        {
            namespaceEntity = new Namespace
            {
                Name = configurationNamespace,
                Subscriptions = [],
                Channels = [],
                DefaultChannels = [],
                RepositoryBranches = [],
            };
            _context.Namespaces.Add(namespaceEntity);
        }

        return namespaceEntity;
    }

    private async Task CreateSubscriptions(
        IEnumerable<IngestedSubscription> newSubscriptions,
        Namespace namespaceEntity,
        Dictionary<string, Channel> existingChannelsByName)
    {
        List<Subscription> subscriptionDaos = [.. newSubscriptions
            .Select(sub => ConvertIngestedSubscriptionToDao(
                sub,
                namespaceEntity,
                existingChannelsByName))];

        await _sqlBarClient.CreateSubscriptionsAsync(subscriptionDaos, false);
    }

    private async Task UpdateSubscriptions(
        IEnumerable<IngestedSubscription> updatedSubscriptions,
        Namespace namespaceEntity,
        Dictionary<string, Channel> existingChannelsByName)
    {
        List<Subscription> subscriptionDaos = [.. updatedSubscriptions
            .Select(sub => ConvertIngestedSubscriptionToDao(
                sub,
                namespaceEntity,
                existingChannelsByName))];

        await _sqlBarClient.UpdateSubscriptionsAsync(subscriptionDaos, false);
    }

    private async Task DeleteSubscriptions(IEnumerable<IngestedSubscription> subscriptionRemovals)
    {
        var subscriptionIds = subscriptionRemovals.Select(sub => sub.Values.Id).ToHashSet();

        _context.SubscriptionUpdates.RemoveRange(
            _context.SubscriptionUpdates
                .Where(s => subscriptionIds.Contains(s.SubscriptionId)));

        var subscriptionDaos = await _context.Subscriptions
            .Where(sub => subscriptionIds.Contains(sub.Id))
            .ToListAsync();

        _context.Subscriptions.RemoveRange(subscriptionDaos);
    }

    private void CreateChannels(
        IEnumerable<IngestedChannel> newChannels,
        Namespace namespaceEntity)
    {
        var channelDaos = newChannels
            .Select(ch => ConvertIngestedChannelToDao(ch, namespaceEntity));

        _context.Channels.AddRange(channelDaos);
    }

    private void UpdateChannels(
        IEnumerable<IngestedChannel> updatedChannels,
        List<Channel> dbChannels)
    {
        var dbChannelsByName = dbChannels.ToDictionary(c => c.Name);

        foreach (var channel in updatedChannels)
        {
            var dbChannel = dbChannelsByName[channel.Values.Name];
            dbChannel!.Classification = channel.Values.Classification;
        }
    }

    private async Task DeleteChannels(IEnumerable<IngestedChannel> removedChannels)
    {

        var channelNames = removedChannels.Select(c => c.Values.Name);

        var channelRemovals = await _context.Channels
            .Where(channel => channelNames.Contains(channel.Name))
            .ToListAsync();

        var channelRemovalIds = channelRemovals.Select(c => c.Id);

        var linkedSubscriptions = _context.Subscriptions
            .Local
            .Where(s => channelRemovalIds.Contains(s.ChannelId))
            .ToList();

        if (linkedSubscriptions.Any())
        {
            throw new InvalidOperationException(
                $"Cannot delete or update channels because the following subscriptions still reference old channel names: {string.Join(", ", linkedSubscriptions.Select(s => s.Id))}");
        }

        _context.Channels.RemoveRange(channelRemovals);
    }

    private void CreateDefaultChannels(
        IEnumerable<IngestedDefaultChannel> newDefaultChannels,
        Namespace namespaceEntity,
        Dictionary<string, Channel> dbChannelsByName)
    {
        var defaultChannelDaos = newDefaultChannels
            .Select(dc => ConvertIngestedDefaultChannelToDao(dc, namespaceEntity, dbChannelsByName));

        _context.DefaultChannels.AddRange(defaultChannelDaos);
    }

    private async Task UpdateDefaultChannels(
        IEnumerable<IngestedDefaultChannel> updatedDefaultChannels,
        Namespace namespaceEntity)
    {
        var dbDefaultChannels = await _context.DefaultChannels
            .Where(dc => dc.Namespace == namespaceEntity)
            .ToDictionaryAsync(dc => (dc.Repository, dc.Branch, dc.Channel.Name));

        foreach (var defaultChannel in updatedDefaultChannels)
        {
            var key = (defaultChannel.Values.Repository,
                defaultChannel.Values.Branch,
                defaultChannel.Values.Channel);

            var dbDefaultChannel = dbDefaultChannels[key];

            dbDefaultChannel.Enabled = defaultChannel.Values.Enabled;
        }
    }

    private async Task DeleteDefaultChannels(
        IEnumerable<IngestedDefaultChannel> removedDefaultChannels,
        Namespace namespaceEntity)
    {
        var dbDefaultChannels = await _context.DefaultChannels
            .Where(dc => dc.Namespace == namespaceEntity)
            .ToDictionaryAsync(dc => (dc.Repository, dc.Branch, dc.Channel.Name));

        var defaultChannelRemovals = new List<DefaultChannel>();

        foreach (var dc in removedDefaultChannels)
        {
            var key = (dc.Values.Repository, dc.Values.Branch, dc.Values.Channel);
            if (dbDefaultChannels.TryGetValue(key, out DefaultChannel? dbDefaultChannel))
            {
                defaultChannelRemovals.Add(dbDefaultChannel);
            }
        }

        _context.DefaultChannels.RemoveRange(defaultChannelRemovals);
    }

    private void CreateBranchRepositories(
        IEnumerable<IngestedBranchMergePolicies> newBranchMergePolicies,
        Namespace namespaceEntity)
    {
        var branchMergePolicyDaos = newBranchMergePolicies
            .Select(bmp => ConvertIngestedBranchMergePoliciesToDao(bmp, namespaceEntity));

        _context.RepositoryBranches.AddRange(branchMergePolicyDaos);
    }

    private async Task UpdateRepositoryBranches(
        IEnumerable<IngestedBranchMergePolicies> updatedBranchMergePolicies,
        Namespace namespaceEntity)
    {
        var dbRepositoryBranches = await _context.RepositoryBranches
            .Where(rb => rb.Namespace.Name == namespaceEntity.Name)
            .ToDictionaryAsync(rb => (rb.RepositoryName, rb.BranchName));

        foreach (var bmp in updatedBranchMergePolicies)
        {
            var dbRepositoryBranch = dbRepositoryBranches[(bmp.Values.Repository, bmp.Values.Branch)];

            var updatedBranchMergePoliciesDao =
                ConvertIngestedBranchMergePoliciesToDao(bmp, namespaceEntity);

            dbRepositoryBranch.PolicyString = updatedBranchMergePoliciesDao.PolicyString;
        }
    }

    private async Task DeleteRepositoryBranches(
        IEnumerable<IngestedBranchMergePolicies> removedBRanchMergePolicies,
        Namespace namespaceEntity)
    {
        var branchRemovals = new List<RepositoryBranch>();

        var dbRepositoryBranches = await _context.RepositoryBranches
            .Where(rb => rb.Namespace == namespaceEntity)
            .ToDictionaryAsync(rb => rb.RepositoryName + "|" + rb.BranchName);

        foreach (var bmp in removedBRanchMergePolicies)
        {
            if (dbRepositoryBranches.TryGetValue(bmp.Values.Repository + "|" + bmp.Values.Branch, out RepositoryBranch? dbRepositoryBranch))
            {
                branchRemovals.Add(dbRepositoryBranch);
            }
        }

        _context.RepositoryBranches.RemoveRange(branchRemovals);
    }

    private ConfigurationUpdates FilterNonUpdates(ConfigurationUpdates update)
    {
        // Find subscription IDs that have ExcludedAssets added or removed
        // For deleted AssetFilters, we need to get the SubscriptionId from the original values
        // since the entity is no longer in the subscription's collection
        var subscriptionIdsWithAssetFilterChanges = _context.ChangeTracker.Entries<AssetFilter>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Deleted)
            .Select(e => e.State == EntityState.Deleted
                ? e.OriginalValues.GetValue<Guid?>("SubscriptionId")
                : e.CurrentValues.GetValue<Guid?>("SubscriptionId"))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        var subscriptionUpdates = _context.ChangeTracker.Entries<Subscription>()
            .Where(e => e.State == EntityState.Modified || subscriptionIdsWithAssetFilterChanges.Contains(e.Entity.Id))
            .Select(e => e.Entity)
            .Select(sub => SqlBarClient.ToClientModelSubscription(sub))
            .Select(SubscriptionYaml.FromClientModel)
            .ToList();

        var channelUpdates = _context.ChangeTracker.Entries<Channel>()
            .Where(e => e.State == EntityState.Modified)
            .Select(e => e.Entity)
            .Select(ch => SqlBarClient.ToClientModelChannel(ch))
            .Select(ChannelYaml.FromClientModel)
            .ToList();

        var defaultChannelUpdates = _context.ChangeTracker.Entries<DefaultChannel>()
            .Where(e => e.State == EntityState.Modified)
            .Select(e => e.Entity)
            .Select(dc => SqlBarClient.ToClientModelDefaultChannel(dc))
            .Select(DefaultChannelYaml.FromClientModel)
            .ToList();

        var repositoryBranchUpdates = _context.ChangeTracker.Entries<RepositoryBranch>()
            .Where(e => e.State == EntityState.Modified)
            .Select(e => e.Entity)
            .Select(rb => SqlBarClient.ToClientModelRepositoryBranch(rb))
            .Select(BranchMergePoliciesYaml.FromClientModel)
            .ToList();

        return new ConfigurationUpdates(
            update.Subscriptions with
            {
                Updates = subscriptionUpdates
            },
            update.Channels with
            {
                Updates = channelUpdates
            },
            update.DefaultChannels with
            {
                Updates = defaultChannelUpdates
            },
            update.RepositoryBranches with
            {
                Updates = repositoryBranchUpdates
            }
        );
    }

    /// <summary>
    /// Verifies that the repositories are registered in the database (and that they have a valid installation ID).
    /// </summary>
    private async Task EnsureRepositoryRegistrationAsync(IReadOnlyList<string> targetRepositories)
    {
        List<Repository> existing = await _context.Repositories
            .Where(r => targetRepositories.Contains(r.RepositoryName))
            .ToListAsync();

        async Task<long> GetInstallationId(string repoUri)
        {
            if (repoUri.Contains("github.com"))
            {
                var installationId = await _installationIdResolver.GetInstallationIdForRepository(repoUri);

                if (!installationId.HasValue)
                {
                    throw new EntityIngestionValidationException($"No Maestro GitHub application installation found for repository '{repoUri}'. " +
                        "The Maestro github application must be installed by the repository's owner and given access to the repository.");
                }

                return installationId.Value;
            }
            else
            {
                // In the case of a non github repository, we don't have an app installation,
                // but we should add an entry in the repositories table, as this is required when
                // adding a new subscription policy.
                return default;
            }
        }

        List<Repository> newRepositories = [];
        foreach (var newRepositoryUri in targetRepositories.Except(
            existing.Select(r => r.RepositoryName),
            StringComparer.OrdinalIgnoreCase))
        {
            var installationId = await GetInstallationId(newRepositoryUri);

            newRepositories.Add(new Repository
            {
                RepositoryName = newRepositoryUri,
                InstallationId = installationId
            });
        }
        if (newRepositories.Count > 0)
        {
            _context.Repositories.AddRange(newRepositories);
        }

        // we don't need to call context.UpdateRange since these area already tracked by EF
        foreach (var existingRepo in existing)
        {
            if (existingRepo.InstallationId > 0 || existingRepo.RepositoryName.Contains("dev.azure.com"))
            {
                continue;
            }

            existingRepo.InstallationId = await GetInstallationId(existingRepo.RepositoryName);
        }
    }
}
