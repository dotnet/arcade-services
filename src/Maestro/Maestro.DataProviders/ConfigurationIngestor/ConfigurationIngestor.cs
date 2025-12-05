// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders.ConfigurationIngestor.Validations;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestor;

public class ConfigurationIngestor(
    BuildAssetRegistryContext context,
    SqlBarClient sqlBarClient) : IConfigurationIngestor
{
    private readonly BuildAssetRegistryContext _context = context;
    private readonly SqlBarClient _sqlBarClient = sqlBarClient;

    public async Task IngestConfigurationAsync(
        ConfigurationData configurationData,
        string configurationNamespace)
    {
        ValidateEntityFields(configurationData);

        var existingConfigurationData =
            await _sqlBarClient.FetchExistingConfigurationDataAsync(configurationNamespace);

        var configurationDateUpdate = ComputeEntityUpdates(
            configurationData,
            existingConfigurationData);

        await SaveConfigurationData(configurationDateUpdate);
    }

    private static void ValidateEntityFields(ConfigurationData newConfigurationData)
    {
        SubscriptionValidator.ValidateSubscriptions(newConfigurationData.Subscriptions);
        ChannelValidator.ValidateChannels(newConfigurationData.Channels);
        DefaultChannelValidator.ValidateDefaultChannels(newConfigurationData.DefaultChannels);
        BranchMergePolicyValidator.ValidateBranchMergePolicies(newConfigurationData.BranchMergePolicies);
    }

    private static ConfigurationDataUpdate ComputeEntityUpdates(
    ConfigurationData configurationData,
    ConfigurationData existingConfigurationData)
    {
        EntityChanges<Subscription> subscriptionChanges =
            ConfigurationDataHelper.ComputeUpdatesForEntity<Subscription, Guid>(
                existingConfigurationData.Subscriptions,
                configurationData.Subscriptions);

        EntityChanges<Channel> channelChanges =
            ConfigurationDataHelper.ComputeUpdatesForEntity<Channel, string>(
                existingConfigurationData.Channels,
                configurationData.Channels);

        EntityChanges<DefaultChannel> defaultChannelChanges =
            ConfigurationDataHelper.ComputeUpdatesForEntity<DefaultChannel, (string, string, int)>(
                existingConfigurationData.DefaultChannels,
                configurationData.DefaultChannels);

        EntityChanges<RepositoryBranch> branchMergePolicyChanges =
            ConfigurationDataHelper.ComputeUpdatesForEntity<RepositoryBranch, (string, string)>(
                existingConfigurationData.BranchMergePolicies,
                configurationData.BranchMergePolicies);

        return new ConfigurationDataUpdate(
            subscriptionChanges,
            channelChanges,
            defaultChannelChanges,
            branchMergePolicyChanges);
    }

    public async Task SaveConfigurationData(ConfigurationDataUpdate configurationDataUpdate)
    {
        // Deletions
        await _sqlBarClient.DeleteSubscriptionsAsync(configurationDataUpdate.SubscriptionChanges.EntityRemovals, false);
        _context.DefaultChannels.RemoveRange(configurationDataUpdate.DefaultChannelChanges.EntityRemovals);
        _context.Channels.RemoveRange(configurationDataUpdate.ChannelChanges.EntityRemovals);
        _context.RepositoryBranches.RemoveRange(configurationDataUpdate.RepositoryBranchChanges.EntityRemovals);

        // Updates
        await _sqlBarClient.UpdateChannelsAsync(configurationDataUpdate.ChannelChanges.EntityUpdates, false);
        await _sqlBarClient.UpdateDefaultChannelsAsync(configurationDataUpdate.DefaultChannelChanges.EntityUpdates, false);
        await _sqlBarClient.UpdateSubscriptionsAsync(configurationDataUpdate.SubscriptionChanges.EntityUpdates, false);
        _context.RepositoryBranches.RemoveRange(configurationDataUpdate.RepositoryBranchChanges.EntityRemovals);

        // Creations
        _context.AddRange(configurationDataUpdate.ChannelChanges.EntityCreations);
        _context.AddRange(configurationDataUpdate.DefaultChannelChanges.EntityCreations);
        await _sqlBarClient.CreateSubscriptionsAsync(configurationDataUpdate.SubscriptionChanges.EntityCreations, false);

        await _context.SaveChangesAsync();
    }
}
