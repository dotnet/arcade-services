// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders.ConfigurationIngestion.Validations;
using Microsoft.DotNet.DarcLib.Models.Yaml;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.EntityFrameworkCore;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion;

public class ConfigurationIngestor(
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
            await FetchExistingConfigurationDataAsync(namespaceEntity);

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

    public async Task SaveConfigurationData(ConfigurationDataUpdate configurationDataUpdate, Namespace namespaceEntity)
    {
        // Deletions
        await DeleteSubscriptions([.. configurationDataUpdate.Subscriptions.Removals.Select(sub => sub.Id)]);
        await DeleteDefaultChannels(configurationDataUpdate.DefaultChannels.Removals);
        await DeleteRepositoryBranches(configurationDataUpdate.RepositoryBranches.Removals);
        await DeleteChannels(configurationDataUpdate.Channels.Removals);

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

        await CreateBranchRepositories(
            configurationDataUpdate.RepositoryBranches.Creations,
            namespaceEntity);

        await UpdateRepositoryBranches(
            configurationDataUpdate.RepositoryBranches.Updates,
            namespaceEntity);

        await _context.SaveChangesAsync();
    }

    public async Task<ConfigurationData> FetchExistingConfigurationDataAsync(Namespace namespaceEntity)
    {
        var subscriptions = await _context.Subscriptions
            .Where(sub => sub.Namespace == namespaceEntity)
            .Select(sub => SqlBarClient.ToClientModelSubscription(sub))
            .Select(clientSub => SubscriptionYaml.FromClientModel(clientSub))
            .ToListAsync();

        var channels = await _context.Channels
            .Where(c => c.Namespace == namespaceEntity)
            .Select(channel => SqlBarClient.ToClientModelChannel(channel))
            .Select(clientChannel => ChannelYaml.FromClientModel(clientChannel))
            .ToListAsync();

        var defaultChannels = _context.DefaultChannels
            .Where(dc => dc.Namespace == namespaceEntity)
            .Select(dc => new
            {
                DaoId = dc.Id,
                ClientModel = SqlBarClient.ToClientModelDefaultChannel(dc),
            })
            .AsEnumerable()
            .Select(x =>
            {
                var yaml = DefaultChannelYaml.FromClientModel(x.ClientModel);
                yaml.Id = x.DaoId; // Assign the DAO ID to the YAML object for ingestion purposes
                return yaml;
            });

        var branchMergePolicies = await _context.RepositoryBranches
            .Where(rb => rb.Namespace == namespaceEntity)
            .Select(rb => SqlBarClient.ToClientModelRepositoryBranch(rb))
            .Select(clientRb => BranchMergePoliciesYaml.FromClientModel(clientRb))
            .ToListAsync();

        return new ConfigurationData(
            subscriptions,
            channels,
            defaultChannels,
            branchMergePolicies);
    }

    private async Task<Namespace> FetchOrCreateNamespace(string configurationNamespace)
    {
        var namespaceEntity = await _context.Namespaces
            .FirstOrDefaultAsync(ns => ns.Name == configurationNamespace);

        if (namespaceEntity is null)
        {
            namespaceEntity = new Namespace
            {
                Name = configurationNamespace,
            };
            _context.Namespaces.Add(namespaceEntity);
            await _context.SaveChangesAsync();
        }

        return namespaceEntity;
    }

    private async Task CreateSubscriptions(
        IEnumerable<SubscriptionYaml> subscriptions,
        Namespace namespaceEntity,
        Dictionary<string, Channel> existingChannelsByName)
    {
        List<Subscription> subscriptionDaos = [.. subscriptions
            .Select(sub => ConfigurationDataHelper.ConvertSubscriptionYamlToDao(
                sub,
                namespaceEntity,
                existingChannelsByName))];

        await _sqlBarClient.CreateSubscriptionsAsync(subscriptionDaos, false);
    }

    private async Task UpdateSubscriptions(
        IEnumerable<SubscriptionYaml> subscriptions,
        Namespace namespaceEntity,
        Dictionary<string, Channel> existingChannelsByName)
    {
        List<Subscription> subscriptionDaos = [.. subscriptions
            .Select(sub => ConfigurationDataHelper.ConvertSubscriptionYamlToDao(
                sub,
                namespaceEntity,
                existingChannelsByName))];

        await _sqlBarClient.UpdateSubscriptionsAsync(subscriptionDaos, false);
    }

    private async Task DeleteSubscriptions(IEnumerable<Guid> subscriptionsIds)
    {
        var subscriptionRemovals = await _context.Subscriptions
            .Where(sub => subscriptionsIds.Contains(sub.Id))
            .ToListAsync();

        _context.Subscriptions.RemoveRange(subscriptionRemovals);
    }

    private void CreateChannels(
        IEnumerable<ChannelYaml> channels,
        Namespace namespaceEntity)
    {
        List<Channel> channelDaos = [.. channels
            .Select(ch => ConfigurationDataHelper.ConvertChannelYamlToDao(ch, namespaceEntity))];

        _context.Channels.AddRange(channelDaos);
    }

    private void UpdateChannels(
        IEnumerable<ChannelYaml> externalChannels,
        List<Channel> dbChannels,
        Namespace namespaceEntity)
    {
        var dbChannelsByName = dbChannels.ToDictionary(c => c.Name);

        foreach (var channel in externalChannels)
        {
            dbChannelsByName.TryGetValue(channel.Name, out Channel? dbChannel);

            dbChannel!.Classification = channel.Classification;

            _context.Channels.Update(dbChannel);
        }
    }

    private async Task DeleteChannels(IEnumerable<ChannelYaml> channels)
    {
        var channelNames = channels.Select(c => c.Name);

        var channelRemovals = await _context.Channels
            .Where(channel => channelNames.Contains(channel.Name))
            .ToListAsync();

        _context.Channels.RemoveRange(channelRemovals);
    }

    private void CreateDefaultChannels(
        IEnumerable<DefaultChannelYaml> defaultChannels,
        Namespace namespaceEntity,
        Dictionary<string, Channel> dbChannelsByName)
    {
        List<DefaultChannel> defaultChannelDaos = [.. defaultChannels
            .Select(dc => ConfigurationDataHelper.ConvertDefaultChannelYamlToDao(
                dc,
                namespaceEntity,
                dbChannelsByName,
                null))];

        _context.DefaultChannels.AddRange(defaultChannelDaos);
    }

    private async Task UpdateDefaultChannels(
        IEnumerable<DefaultChannelYaml> externalDefaultChannels,
        Namespace namespaceEntity)
    {
        var dcLookups = externalDefaultChannels.ToDictionary(dc => dc.Id);

        var dbDefaultChannels = await _context.DefaultChannels
            .Where(dc => dcLookups.Keys.Contains(dc.Id))
            .ToListAsync();

        foreach (var dbDefaultChannel in dbDefaultChannels)
        {
            dcLookups.TryGetValue(dbDefaultChannel.Id, out DefaultChannelYaml? defaultChannel);

            dbDefaultChannel.Enabled = defaultChannel!.Enabled;

            _context.DefaultChannels.Update(dbDefaultChannel);
        }
    }

    private async Task DeleteDefaultChannels(IEnumerable<DefaultChannelYaml> defaultChannels)
    {
        var defaultChannelIds = defaultChannels.Select(dc => dc.Id);

        var defaultChannelRemovals = await _context.DefaultChannels
            .Where(dc => defaultChannelIds.Contains(dc.Id))
            .ToListAsync();

        _context.DefaultChannels.RemoveRange(defaultChannelRemovals);
    }

    private async Task CreateBranchRepositories(
        IEnumerable<BranchMergePoliciesYaml> branchMergePolicies,
        Namespace namespaceEntity)
    {
        List<RepositoryBranch> branchMergePolicyDaos = [.. branchMergePolicies
            .Select(bmp => ConfigurationDataHelper.ConvertBranchMergePoliciesYamlToDao(
                bmp,
                namespaceEntity))];

        _context.RepositoryBranches.AddRange(branchMergePolicyDaos);
    }

    private async Task UpdateRepositoryBranches(
        IEnumerable<BranchMergePoliciesYaml> branchMergePolicies,
        Namespace namespaceEntity)
    {
        var dbRepositoryBranches = await _context.RepositoryBranches
            .Where(rb => rb.Namespace.Name == namespaceEntity.Name)
            .ToDictionaryAsync(rb => (rb.Repository.RepositoryName, rb.BranchName));

        foreach (var bmp in branchMergePolicies)
        {
            dbRepositoryBranches.TryGetValue((bmp.Repository, bmp.Branch), out RepositoryBranch? dbRepositoryBranch);

            var externalBranchMergePoliciesDao =
                ConfigurationDataHelper.ConvertBranchMergePoliciesYamlToDao(bmp, namespaceEntity);

            dbRepositoryBranch!.PolicyString = externalBranchMergePoliciesDao.PolicyString;

            _context.RepositoryBranches.Update(dbRepositoryBranch);
        }
    }
    
    private async Task DeleteRepositoryBranches(IEnumerable<BranchMergePoliciesYaml> branchMergePolicies)
    {
        var branchRemovals = new List<RepositoryBranch>();

        var dbRepositoryBranches = await _context.RepositoryBranches
            .ToDictionaryAsync(rb => rb.RepositoryName + "|" + rb.BranchName);

        foreach(var bmp in branchMergePolicies)
        {
            dbRepositoryBranches.TryGetValue(bmp.Repository + "|" + bmp.Branch, out RepositoryBranch? dbRepositoryBranch);
            if (dbRepositoryBranch != null)
            {
                branchRemovals.Add(dbRepositoryBranch);
            }
        }

        _context.RepositoryBranches.RemoveRange(branchRemovals);
    }
}
