// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Maestro.DataProviders.ConfigurationIngestor.Validations;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestor;

public class ConfigurationIngestor
{
    public async Task<bool> TryIngestConfigurationAsync(ConfigurationData configurationData)
    {
        ValidateEntityFields(configurationData);
        await SaveConfigurationData(configurationData);
        return true;
    }

    public static void ValidateEntityFields(ConfigurationData newConfigurationData)
    {
        foreach (var sub in newConfigurationData.Subscriptions)
        {
            SubscriptionValidator.ValidateSubscription(sub);
        }

        foreach (var channel in newConfigurationData.Channels)
        {
            ChannelValidator.ValidateChannel(channel);
        }

        foreach (var defaultChannel in newConfigurationData.DefaultChannels)
        {
            DefaultChannelValidator.ValidateDefaultChannel(defaultChannel);
        }

        foreach (var branchMergePolicy in newConfigurationData.BranchMergePolicies)
        {
            BranchMergePolicyValidator.ValidateBranchMergePolicies(branchMergePolicy);
        }
    }

    public async Task SaveConfigurationData(ConfigurationData newConfigurationData)
    {
        await Task.FromResult(newConfigurationData);
    }
}
