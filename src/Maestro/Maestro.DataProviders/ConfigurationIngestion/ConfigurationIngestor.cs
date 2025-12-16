// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders.ConfigurationIngestion.Helpers;
using Maestro.DataProviders.ConfigurationIngestion.Validations;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.EntityFrameworkCore;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion;

internal class ConfigurationIngestor(
    BuildAssetRegistryContext context,
    ISqlBarClient sqlBarClient) : IConfigurationIngestor
{
    private readonly BuildAssetRegistryContext _context = context;
    private readonly ISqlBarClient _sqlBarClient = sqlBarClient;

    public async Task<ConfigurationDataUpdate> IngestConfigurationAsync(
        ConfigurationData configurationData,
        string configurationNamespace)
    {
        ValidateEntityFields(configurationData);

        var namespaceEntity = await FetchOrCreateNamespace(configurationNamespace);

        var existingConfigurationData =
            ConfigurationDataHelper.CreateConfigurationDataObject(namespaceEntity);

        var configurationDataUpdate = ConfigurationDataHelper.ComputeEntityUpdates(
            configurationData,
            existingConfigurationData);

        await SaveConfigurationData(configurationDataUpdate, namespaceEntity);

        return configurationDataUpdate;
    }

    private static void ValidateEntityFields(ConfigurationData newConfigurationData)
    {
        SubscriptionValidator.ValidateSubscriptions(newConfigurationData.Subscriptions);
        ChannelValidator.ValidateChannels(newConfigurationData.Channels);
        DefaultChannelValidator.ValidateDefaultChannels(newConfigurationData.DefaultChannels);
        BranchMergePolicyValidator.ValidateBranchMergePolicies(newConfigurationData.BranchMergePolicies);
    }

    private async Task SaveConfigurationData(ConfigurationDataUpdate configurationDataUpdate, Namespace namespaceEntity)
    {
        // Deletions
        await DeleteSubscriptions(configurationDataUpdate.Subscriptions.Removals);
        await DeleteDefaultChannels(
            configurationDataUpdate.DefaultChannels.Removals,
            namespaceEntity);
        await DeleteRepositoryBranches(
            configurationDataUpdate.RepositoryBranches.Removals,
            namespaceEntity);
        await DeleteChannels(configurationDataUpdate.Channels.Removals,
            configurationDataUpdate.Subscriptions.Creations,
            configurationDataUpdate.Subscriptions.Updates);

        var existingChannels = _context.Channels.ToDictionary(c => c.Name);

        // Channels must be updated first due to entity relationships
        CreateChannels(
            configurationDataUpdate.Channels.Creations,
            namespaceEntity);

        UpdateChannels(
            configurationDataUpdate.Channels.Updates,
            [.. existingChannels.Values],
            namespaceEntity);

        // We fetch the channels again including newly created ones
        existingChannels = _context.Channels
            .Local
            .ToDictionary(c => c.Name);

        // Update the rest of the entities
        await CreateSubscriptions(
            configurationDataUpdate.Subscriptions.Creations,
            namespaceEntity,
            existingChannels);

        await UpdateSubscriptions(
            configurationDataUpdate.Subscriptions.Updates,
            namespaceEntity,
            existingChannels);

        CreateDefaultChannels(
            configurationDataUpdate.DefaultChannels.Creations,
            namespaceEntity,
            existingChannels);

        await UpdateDefaultChannels(
            configurationDataUpdate.DefaultChannels.Updates,
            namespaceEntity);

        CreateBranchRepositories(
            configurationDataUpdate.RepositoryBranches.Creations,
            namespaceEntity);

        await UpdateRepositoryBranches(
            configurationDataUpdate.RepositoryBranches.Updates,
            namespaceEntity);

        await _context.SaveChangesAsync();
    }

    private async Task<Namespace> FetchOrCreateNamespace(string configurationNamespace)
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
            await _context.SaveChangesAsync();
        }

        return namespaceEntity;
    }

    private async Task CreateSubscriptions(
        IEnumerable<IngestedSubscription> newSubscriptions,
        Namespace namespaceEntity,
        Dictionary<string, Channel> existingChannelsByName)
    {
        List<Subscription> subscriptionDaos = [.. newSubscriptions
            .Select(sub => ConfigurationDataHelper.ConvertIngestedSubscriptionToDao(
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
            .Select(sub => ConfigurationDataHelper.ConvertIngestedSubscriptionToDao(
                sub,
                namespaceEntity,
                existingChannelsByName))];

        await _sqlBarClient.UpdateSubscriptionsAsync(subscriptionDaos, false);
    }

    private async Task DeleteSubscriptions(IEnumerable<IngestedSubscription> subscriptionRemovals)
    {
        var subscriptionIds = subscriptionRemovals.Select(sub => sub.Values.Id).ToList();

        var subscriptionDaos = await _context.Subscriptions
            .Where(sub => subscriptionIds.Contains(sub.Id))
            .ToListAsync();

        _context.Subscriptions.RemoveRange(subscriptionDaos);
    }

    private void CreateChannels(
        IEnumerable<IngestedChannel> newChannels,
        Namespace namespaceEntity)
    {
        List<Channel> channelDaos = [.. newChannels
            .Select(ch => ConfigurationDataHelper.ConvertIngestedChannelToDao(ch, namespaceEntity))];

        _context.Channels.AddRange(channelDaos);
    }

    private void UpdateChannels(
        IEnumerable<IngestedChannel> updatedChannels,
        List<Channel> dbChannels,
        Namespace namespaceEntity)
    {
        var dbChannelsByName = dbChannels.ToDictionary(c => c.Name);

        foreach (var channel in updatedChannels)
        {
            var dbChannel = dbChannelsByName[channel.Values.Name];
            dbChannel!.Classification = channel.Values.Classification;

            _context.Channels.Update(dbChannel);
        }
    }

    private async Task DeleteChannels(
        IEnumerable<IngestedChannel> removedChannels,
        IEnumerable<IngestedSubscription> addedSubscriptions,
        IEnumerable<IngestedSubscription> updatedSubscriptions)
    {

        var channelNames = removedChannels.Select(c => c.Values.Name);

        var channelRemovals = await _context.Channels
            .Where(channel => channelNames.Contains(channel.Name))
            .ToListAsync();

        var channelRemovalIds = channelRemovals.Select(c => c.Id);

        var linkedSubscriptions = await _context.Subscriptions
            .Where(s => channelRemovalIds.Contains(s.ChannelId))
            .ToListAsync();

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
        List<DefaultChannel> defaultChannelDaos = [.. newDefaultChannels
            .Select(dc => ConfigurationDataHelper.ConvertIngestedDefaultChannelToDao(
                dc,
                namespaceEntity,
                dbChannelsByName))];

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
        List<RepositoryBranch> branchMergePolicyDaos = [.. newBranchMergePolicies
            .Select(bmp => ConfigurationDataHelper.ConvertIngestedBranchMergePoliciesToDao(
                bmp,
                namespaceEntity))];

        _context.RepositoryBranches.AddRange(branchMergePolicyDaos);
    }

    private async Task UpdateRepositoryBranches(
        IEnumerable<IngestedBranchMergePolicies> updatedBranchMergePolicies,
        Namespace namespaceEntity)
    {
        var dbRepositoryBranches = await _context.RepositoryBranches
            .Where(rb => rb.Namespace.Name == namespaceEntity.Name)
            .ToDictionaryAsync(rb => (rb.Repository.RepositoryName, rb.BranchName));

        foreach (var bmp in updatedBranchMergePolicies)
        {
            var dbRepositoryBranch = dbRepositoryBranches[(bmp.Values.Repository, bmp.Values.Branch)];

            var updatedBranchMergePoliciesDao =
                ConfigurationDataHelper.ConvertIngestedBranchMergePoliciesToDao(bmp, namespaceEntity);

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
