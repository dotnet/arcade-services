// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProductConstructionService.Common;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders.ConfigurationIngestion.Model;
using Maestro.DataProviders.ConfigurationIngestion.Validations;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.EntityFrameworkCore;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion;

internal partial class ConfigurationIngestor(
        BuildAssetRegistryContext context,
        ISqlBarClient sqlBarClient,
        IDistributedLock distributedLock)
    : IConfigurationIngestor
{
    private readonly BuildAssetRegistryContext _context = context;
    private readonly ISqlBarClient _sqlBarClient = sqlBarClient;
    private readonly IDistributedLock _distributedLock = distributedLock;

    public async Task<ConfigurationUpdates> IngestConfigurationAsync(
        ConfigurationData configurationData,
        string configurationNamespace,
        bool saveChanges)
    {
        var ingestionResult =
            await _distributedLock.ExecuteWithLockAsync("ConfigurationIngestion", async () =>
            {
                return await IngestConfigurationInternalAsync(configurationData, configurationNamespace, saveChanges);
            });

        return ingestionResult;
    }

    private async Task<ConfigurationUpdates> IngestConfigurationInternalAsync(
        ConfigurationData configurationData,
        string configurationNamespace,
        bool saveChanges = true)
    {
        var ingestionData = IngestedConfigurationData.FromYamls(configurationData);
        ValidateEntityFields(ingestionData);

        var namespaceEntity = await FetchOrCreateNamespaceAsync(configurationNamespace);

        var existingConfigurationData =
            CreateConfigurationDataObject(namespaceEntity);

        var configurationDataUpdate = ComputeEntityUpdates(
            ingestionData,
            existingConfigurationData);

        await PerformEntityChangesAsync(configurationDataUpdate, namespaceEntity, saveChanges);

        return configurationDataUpdate.ToYamls();
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
        Namespace namespaceEntity,
        bool saveChanges)
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

        // Update the rest of the entities
        await CreateSubscriptions(configurationDataUpdate.Subscriptions.Creations, namespaceEntity, existingChannels);
        await UpdateSubscriptions(configurationDataUpdate.Subscriptions.Updates, namespaceEntity, existingChannels);

        CreateDefaultChannels(configurationDataUpdate.DefaultChannels.Creations, namespaceEntity, existingChannels);
        await UpdateDefaultChannels(configurationDataUpdate.DefaultChannels.Updates, namespaceEntity);

        CreateBranchRepositories(configurationDataUpdate.RepositoryBranches.Creations, namespaceEntity);
        await UpdateRepositoryBranches(configurationDataUpdate.RepositoryBranches.Updates, namespaceEntity);

        if (saveChanges)
        {
            await _context.SaveChangesAsync();
        }
    }

    private async Task<Namespace> FetchOrCreateNamespaceAsync(string configurationNamespace)
    {
        var namespaceEntity = await _context.Namespaces
            .Include(ns => ns.Subscriptions)
            .Include(ns => ns.Channels)
            .Include(ns => ns.DefaultChannels)
            .Include(ns => ns.RepositoryBranches)
            .Where(ns => ns.Name == configurationNamespace)
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

            _context.Channels.Update(dbChannel);
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

            _context.DefaultChannels.Update(dbDefaultChannel);
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

            _context.RepositoryBranches.Update(dbRepositoryBranch);
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
}
